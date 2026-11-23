#include "../../Include/URUI/Core/ElementText.h"
#include "../../Include/URUI/Core/Context.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/ElementUtilities.h"
#include "../../Include/URUI/Core/Event.h"
#include "../../Include/URUI/Core/FontEngineInterface.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/Property.h"
#include "../../Include/URUI/Core/RenderManager.h"
#include "../../Include/URUI/Core/TextShapingContext.h"
#include "ComputeProperty.h"
#include "ElementDefinition.h"
#include "ElementStyle.h"
#include "TransformState.h"
#include <limits>

namespace URUI
{

    static bool BuildToken(String& token, const char*& token_begin, const char* string_end, bool first_token,
                           bool collapse_white_space, bool break_at_endline, Style::TextTransform text_transformation,
                           bool decode_escape_characters);
    static bool LastToken(const char* token_begin, const char* string_end, bool collapse_white_space,
                          bool break_at_endline);

    static int RoundDownToIntegerClamped(float value)
    {
        constexpr int clamp = (1 << std::numeric_limits<float>::digits);
        constexpr float clamp_f = float{clamp};
        if (value >= clamp_f)
        {
            return clamp;
        }
        if (value <= -clamp_f)
        {
            return -clamp;
        }
        return static_cast<int>(value);
    }

    struct TextOverflowResolved
    {
        bool enabled = false;
        float overflow_width = 0.f;
        String overflow_text;
    };

    static TextOverflowResolved ResolveTextOverflow(Element* parent, FontFaceHandle font_face_handle)
    {
        if (!parent)
        {
            return {};
        }

        const auto& parent_computed = parent->GetComputedValues();
        if (parent_computed.overflow_x() == Style::Overflow::Visible &&
            parent_computed.overflow_y() == Style::Overflow::Visible)
        {
            return {};
        }

        const Style::TextOverflow text_overflow = parent_computed.text_overflow();
        if (text_overflow == Style::TextOverflow::Clip)
        {
            return {};
        }

        const Box& box = parent->GetBox();
        const BoxArea clip_area = parent->GetClipArea();

        auto AccumulateRightSideEdgesUpTo = [](const Box& box, BoxArea up_to_area) -> float
        {
            float result = 0;
            for (int i = (int)up_to_area; i < int(BoxArea::Content); i++)
            {
                result += box.GetEdge((BoxArea)i, BoxEdge::Right);
            }
            return result;
        };

        const float overflow_width =
            parent->GetScrollLeft() + box.GetSize().x + AccumulateRightSideEdgesUpTo(box, clip_area);

        // U+2026
        constexpr char ellipsis_chars[] = "\xE2\x80\xA6";
        constexpr char dots_chars[] = "...";

        String overflow_text =
            (text_overflow == Style::TextOverflow::String
                 ? parent->GetComputedValues().text_overflow_string()
                 : (GetFontEngineInterface()->GetFontMetrics(font_face_handle).has_ellipsis ? ellipsis_chars
                                                                                            : dots_chars));

        return TextOverflowResolved{
            true,
            overflow_width,
            std::move(overflow_text),
        };
    }

    void LogMissingFontFace(Element* element)
    {
        const String font_family_property = element->GetProperty<String>("font-family");
        if (font_family_property.empty())
        {
            Log::Message(Log::LT_WARNING, "No font face defined. Missing 'font-family' property. On element %s",
                         element->GetAddress().c_str());
        }
        else
        {
            const ComputedValues& computed = element->GetComputedValues();
            const String font_face_description =
                GetFontFaceDescription(font_family_property, computed.font_style(), computed.font_weight());
            Log::Message(
                Log::LT_WARNING,
                "No font face defined. Ensure (1) that Context::Update is run after new elements are constructed, before Context::Render, "
                "and (2) that the specified font face %s has been successfully loaded. "
                "Please see previous log messages for all successfully loaded fonts. On element %s",
                font_face_description.c_str(), element->GetAddress().c_str());
        }
    }

    URUI_RTTI_Define(ElementText)

    ElementText::ElementText(const String& tag)
        : Element(tag), colour(255, 255, 255), opacity(1), font_handle_version(0), geometry_dirty(true),
          dirty_layout_on_change(true), generated_decoration(Style::TextDecoration::None),
          decoration_property(Style::TextDecoration::None), font_effects_dirty(true), font_effects_handle(0)
    {
    }

    ElementText::~ElementText()
    {
    }

    void ElementText::SetText(const String& _text)
    {
        if (text != _text)
        {
            text = _text;

            if (dirty_layout_on_change)
            {
                DirtyLayout();
            }
        }
    }

    const String& ElementText::GetText() const
    {
        return text;
    }

    void ElementText::OnRender()
    {
        URUI_ZoneScoped;

        FontFaceHandle font_face_handle = GetFontFaceHandle();
        if (font_face_handle == 0)
        {
            return;
        }

        RenderManager& render_manager = GetContext()->GetRenderManager();

        // If our font effects have potentially changed, update it and force a geometry generation if necessary.
        if (font_effects_dirty && UpdateFontEffects())
        {
            geometry_dirty = true;
        }

        // Dirty geometry if font version has changed.
        int new_version = GetFontEngineInterface()->GetVersion(font_face_handle);
        if (new_version != font_handle_version)
        {
            font_handle_version = new_version;
            geometry_dirty = true;
        }

        // Regenerate the geometry if the colour or font configuration has altered.
        if (geometry_dirty)
        {
            GenerateGeometry(render_manager, font_face_handle);
        }

        // Regenerate text decoration if necessary.
        if (decoration_property != generated_decoration)
        {
            if (decoration_property == Style::TextDecoration::None)
            {
                decoration.reset();
            }
            else
            {
                Mesh mesh;
                if (decoration)
                {
                    mesh = decoration->Release(Geometry::ReleaseMode::ClearMesh);
                }
                else
                {
                    decoration = MakeUnique<Geometry>();
                }

                GenerateDecoration(mesh, font_face_handle);
                *decoration = GetRenderManager()->MakeGeometry(std::move(mesh));
            }

            generated_decoration = decoration_property;
        }

        const Vector2f translation = GetAbsoluteOffset();

        bool render = true;

        // Do a visibility test against the scissor region to avoid unnecessary render calls. Instead of handling
        // culling in complicated transform cases, for simplicity we always proceed to render if one is detected.
        Rectanglei scissor_region = render_manager.GetScissorRegion();
        if (!scissor_region.Valid())
        {
            scissor_region = Rectanglei::FromSize(render_manager.GetViewport());
        }

        if (!GetTransformState() || !GetTransformState()->GetTransform())
        {
            const FontMetrics& font_metrics = GetFontEngineInterface()->GetFontMetrics(GetFontFaceHandle());
            const int ascent = Math::RoundUpToInteger(font_metrics.ascent);
            const int descent = Math::RoundUpToInteger(font_metrics.descent);

            render = false;
            for (const Line& line : lines)
            {
                const Vector2i baseline = Vector2i(translation + line.position);
                const Rectanglei line_region =
                    Rectanglei::FromCorners(baseline - Vector2i(0, ascent), baseline + Vector2i(line.width, descent));

                if (line_region.Valid() && scissor_region.Intersects(line_region))
                {
                    render = true;
                    break;
                }
            }
        }

        if (render)
        {
            for (size_t i = 0; i < geometry.size(); ++i)
            {
                geometry[i].geometry.Render(translation, geometry[i].texture);
            }
        }

        if (decoration)
        {
            decoration->Render(translation);
        }
    }

    bool ElementText::GenerateLine(String& line, int& line_length, float& line_width, int line_begin,
                                   float maximum_line_width, float right_spacing_width, bool trim_whitespace_prefix,
                                   bool decode_escape_characters, bool allow_empty)
    {
        URUI_ZoneScoped;
        URUI_ASSERT(maximum_line_width >= 0.f);

        FontFaceHandle font_face_handle = GetFontFaceHandle();

        // Initialise the output variables.
        line.clear();
        line_length = 0;
        line_width = 0;

        // Bail if we don't have a valid font face.
        if (font_face_handle == 0)
        {
            LogMissingFontFace(GetParentNode() ? GetParentNode() : this);
            return true;
        }

        // Determine how we are processing white-space while formatting the text.
        using namespace Style;
        const auto& computed = GetComputedValues();
        const WhiteSpace white_space_property = computed.white_space();
        const bool collapse_white_space =
            (white_space_property == WhiteSpace::Normal || white_space_property == WhiteSpace::Nowrap ||
             white_space_property == WhiteSpace::Preline);
        const bool break_at_line =
            (white_space_property == WhiteSpace::Normal || white_space_property == WhiteSpace::Prewrap ||
             white_space_property == WhiteSpace::Preline);
        const bool break_at_endline =
            (white_space_property == WhiteSpace::Pre || white_space_property == WhiteSpace::Prewrap ||
             white_space_property == WhiteSpace::Preline);

        const TextShapingContext text_shaping_context{computed.language(), computed.direction(),
                                                      computed.font_kerning(), computed.letter_spacing()};
        TextTransform text_transform_property = computed.text_transform();
        WordBreak word_break = computed.word_break();

        FontEngineInterface* font_engine_interface = GetFontEngineInterface();

        // Starting at the line_begin character, we generate sections of the text (we'll call them tokens) depending on the
        // white-space parsing parameters. Each section is then appended to the line if it can fit. If not, or if an
        // endline is found (and we're processing them), then the line is ended. kthxbai!
        const char* token_begin = text.c_str() + line_begin;
        const char* string_end = text.c_str() + text.size();
        String token;
        while (token_begin != string_end)
        {
            token.clear();
            const char* next_token_begin = token_begin;
            Character previous_codepoint = Character::Null;
            if (!line.empty())
            {
                previous_codepoint = StringUtilities::ToCharacter(
                    StringUtilities::SeekBackwardUTF8(&line.back(), line.data()), line.data() + line.size());
            }

            // Generate the next token and determine its pixel-length.
            bool break_line =
                BuildToken(token, next_token_begin, string_end, line.empty() && trim_whitespace_prefix,
                           collapse_white_space, break_at_endline, text_transform_property, decode_escape_characters);
            float token_width = font_engine_interface->GetStringWidth(font_face_handle, token, text_shaping_context,
                                                                      previous_codepoint);

            // If we're breaking to fit a line box, check if the token can fit on the line before we add it.
            if (break_at_line)
            {
                const bool is_last_token =
                    LastToken(next_token_begin, string_end, collapse_white_space, break_at_endline);
                float max_token_width =
                    maximum_line_width - (is_last_token ? line_width + right_spacing_width : line_width);

                if (token_width > max_token_width)
                {
                    if (word_break == WordBreak::BreakAll || (word_break == WordBreak::BreakWord && line.empty()))
                    {
                        // Try to break up the word via binary search over UTF-8 codepoint boundaries.
                        max_token_width = maximum_line_width - line_width;
                        const char* token_limit = next_token_begin;
                        Vector<const char*> boundaries;
                        for (const char* p = token_begin; p < token_limit;)
                        {
                            p = StringUtilities::SeekForwardUTF8(p + 1, token_limit);
                            boundaries.push_back(p);
                        }

                        // boundaries[0] = end of 1st codepoint, ..., boundaries.back() = token_limit.
                        // Full token (boundaries.size()) already failed > max_token_width, search [0, size - 1).
                        size_t lo = 0;
                        size_t hi = boundaries.size() > 1 ? boundaries.size() - 1 : 0;
                        size_t best = 0;
                        while (lo < hi)
                        {
                            const size_t mid = lo + (hi - lo) / 2;
                            token.clear();
                            next_token_begin = token_begin;
                            BuildToken(token, next_token_begin, boundaries[mid],
                                       line.empty() && trim_whitespace_prefix, collapse_white_space, break_at_endline,
                                       text_transform_property, decode_escape_characters);
                            const float w = font_engine_interface->GetStringWidth(
                                font_face_handle, token, text_shaping_context, previous_codepoint);
                            if (w <= max_token_width)
                            {
                                best = mid + 1;
                                lo = mid + 1;
                            }
                            else
                            {
                                hi = mid;
                            }
                        }

                        if (best == 0)
                        {
                            if (allow_empty || !line.empty())
                            {
                                return false;
                            }
                            // Force the first character to be consumed even though it overflows.
                            best = 1;
                        }

                        const char* partial_string_end = boundaries[best - 1];
                        token.clear();
                        next_token_begin = token_begin;
                        BuildToken(token, next_token_begin, partial_string_end,
                                   line.empty() && trim_whitespace_prefix, collapse_white_space, break_at_endline,
                                   text_transform_property, decode_escape_characters);
                        token_width = font_engine_interface->GetStringWidth(
                            font_face_handle, token, text_shaping_context, previous_codepoint);

                        break_line = true;
                    }
                    else if (allow_empty || !line.empty())
                    {
                        // Let the token overflow into the next line.
                        return false;
                    }
                }
            }

            // The token can fit on the end of the line, so add it onto the end and increment our width and length counters.
            line += token;
            line_length += (int)(next_token_begin - token_begin);
            line_width += token_width;

            // Break out of the loop if an endline was forced.
            if (break_line && (allow_empty || !line.empty()))
            {
                return false;
            }

            // Set the beginning of the next token.
            token_begin = next_token_begin;
        }

        return true;
    }

    void ElementText::ClearLines()
    {
        DirtyNativeRendering();
        URUI_ZoneScoped;
        lines.clear();
        generated_decoration = Style::TextDecoration::None;
        geometry_dirty = true;
    }

    void ElementText::AddLine(Vector2f line_position, String line)
    {
        DirtyNativeRendering();
        if (font_effects_dirty)
        {
            UpdateFontEffects();
        }

        lines.emplace_back(std::move(line), line_position);

        geometry_dirty = true;
    }

    void ElementText::SuppressAutoLayout()
    {
        dirty_layout_on_change = false;
    }

    void ElementText::OnPropertyChange(const PropertyIdSet& changed_properties)
    {
        URUI_ZoneScoped;

        Element::OnPropertyChange(changed_properties);

        bool colour_changed = false;
        bool font_face_changed = false;
        auto& computed = GetComputedValues();

        if (changed_properties.Contains(PropertyId::Color) || changed_properties.Contains(PropertyId::Opacity))
        {
            const float new_opacity = 1.f;
            const bool opacity_changed = opacity != new_opacity;

            ColourbPremultiplied new_colour = computed.color().ToPremultiplied(new_opacity);
            colour_changed = colour != new_colour;

            if (colour_changed)
            {
                colour = new_colour;
            }
            if (opacity_changed)
            {
                opacity = new_opacity;
                font_effects_dirty = true;
                geometry_dirty = true;
            }
        }

        //
        if (changed_properties.Contains(PropertyId::FontFamily) ||
            //
            changed_properties.Contains(PropertyId::FontWeight) ||
            //
            changed_properties.Contains(PropertyId::FontStyle) ||
            //
            changed_properties.Contains(PropertyId::FontSize) ||
            //
            changed_properties.Contains(PropertyId::FontKerning) ||
            //
            changed_properties.Contains(PropertyId::LetterSpacing) ||
            //
            changed_properties.Contains(PropertyId::Language) ||
            //
            changed_properties.Contains(PropertyId::Direction) || changed_properties.Contains(PropertyId::TextOverflow))
        {
            font_face_changed = true;
            geometry_dirty = true;

            font_effects_handle = 0;
            font_effects_dirty = true;
            font_handle_version = 0;
        }

        if (changed_properties.Contains(PropertyId::FontEffect))
        {
            font_effects_dirty = true;
        }

        if (changed_properties.Contains(PropertyId::TextDecoration))
        {
            decoration_property = computed.text_decoration();
            if (decoration && decoration_property == Style::TextDecoration::None)
            {
                decoration.reset();
            }
        }

        if (font_face_changed)
        {
            // We have to let our document know we need to be regenerated.
            if (dirty_layout_on_change)
            {
                DirtyLayout();
            }
        }
        else if (colour_changed)
        {
            // Force the geometry to be regenerated.
            geometry_dirty = true;

            // Re-colour the decoration geometry.
            if (decoration)
            {
                Mesh mesh = decoration->Release();
                for (Vertex& vertex : mesh.vertices)
                {
                    vertex.colour = colour;
                }

                if (RenderManager* render_manager = GetRenderManager())
                {
                    *decoration = render_manager->MakeGeometry(std::move(mesh));
                }
            }
        }
    }

    void ElementText::GetHTML(String& content)
    {
        content += StringUtilities::EncodeHtml(text);
    }

    bool ElementText::UpdateFontEffects()
    {
        URUI_ZoneScoped;

        if (GetFontFaceHandle() == 0)
        {
            return false;
        }

        font_effects_dirty = false;

        static const FontEffectList empty_font_effects;

        // Fetch the --urui-font-effect for this text element
        const FontEffectList* font_effects = &empty_font_effects;
        if (GetComputedValues().has_font_effect())
        {
            if (const Property* p = GetProperty(PropertyId::FontEffect))
            {
                if (FontEffectsPtr effects = p->Get<FontEffectsPtr>())
                {
                    font_effects = &effects->list;
                }
            }
        }

        // Request a font layer configuration to match this set of effects. If this is different from
        // our old configuration, then return true to indicate we'll need to regenerate geometry.
        FontEffectsHandle new_font_effects_handle =
            GetFontEngineInterface()->PrepareFontEffects(GetFontFaceHandle(), *font_effects);
        if (new_font_effects_handle != font_effects_handle)
        {
            font_effects_handle = new_font_effects_handle;
            return true;
        }

        return false;
    }

    void ElementText::GenerateGeometry(RenderManager& render_manager, const FontFaceHandle font_face_handle)
    {
        URUI_ZoneScopedC(0xD2691E);

        const TextOverflowResolved text_overflow = ResolveTextOverflow(GetParentNode(), font_face_handle);

        const auto& computed = GetComputedValues();
        const TextShapingContext text_shaping_context{computed.language(), computed.direction(),
                                                      computed.font_kerning(), computed.letter_spacing()};

        TexturedMeshList mesh_list;
        mesh_list.reserve(geometry.size());

        for (Line& line : lines)
        {
            line.width = GetFontEngineInterface()->GenerateString(render_manager, font_face_handle, font_effects_handle,
                                                                  line.text, line.position, colour, opacity,
                                                                  text_shaping_context, mesh_list);
        }

        const auto text_overflows_on_line = [&](const Line& line)
        { return line.position.x + line.width > text_overflow.overflow_width; };
        if (text_overflow.enabled && std::any_of(lines.begin(), lines.end(), text_overflows_on_line))
        {
            mesh_list.clear();

            for (Line& line : lines)
            {
                if (line.text.empty())
                {
                    continue;
                }

                String abbreviated_text;
                StringView text_submit_view = line.text;

                if (text_overflows_on_line(line))
                {
                    const char* begin_ptr = line.text.c_str();
                    const char* end_ptr = begin_ptr + line.text.size();
                    Vector<const char*> prefixes;
                    for (const char* p = begin_ptr; p < end_ptr;)
                    {
                        const char* next = StringUtilities::SeekForwardUTF8(p + 1, end_ptr);
                        if (next < end_ptr)
                        {
                            prefixes.push_back(next);
                        }
                        p = next;
                    }

                    // Binary search for the longest prefix + overflow_text that does not overflow.
                    // Note: original loop stops at the first character boundary (prefixes[0]), never empty prefix.
                    if (!prefixes.empty())
                    {
                        abbreviated_text.reserve(line.text.size() + text_overflow.overflow_text.size());
                        size_t lo = 0;
                        size_t hi = prefixes.size();
                        size_t best = 0;
                        float best_width = line.width;
                        while (lo < hi)
                        {
                            const size_t mid = lo + (hi - lo) / 2;
                            abbreviated_text.assign(begin_ptr, prefixes[mid]);
                            abbreviated_text.append(text_overflow.overflow_text);
                            line.width = GetFontEngineInterface()->GetStringWidth(font_face_handle, abbreviated_text,
                                                                                  text_shaping_context);
                            if (!text_overflows_on_line(line))
                            {
                                best = mid;
                                best_width = line.width;
                                lo = mid + 1;
                            }
                            else
                            {
                                hi = mid;
                            }
                        }
                        abbreviated_text.assign(begin_ptr, prefixes[best]);
                        abbreviated_text.append(text_overflow.overflow_text);
                        if (lo == 0)
                        {
                            best_width = GetFontEngineInterface()->GetStringWidth(font_face_handle, abbreviated_text,
                                                                                  text_shaping_context);
                        }
                        line.width = best_width;
                        text_submit_view = abbreviated_text;
                    }
                }

                line.width = GetFontEngineInterface()->GenerateString(
                    render_manager, font_face_handle, font_effects_handle, text_submit_view, line.position, colour,
                    opacity, text_shaping_context, mesh_list);
            }
        }

        // Apply the new geometry and textures. Reuse the old geometry if the mesh matches, which can be relatively common
        // where the layout is changed in a way that does not visually affect this element.
        geometry.resize(mesh_list.size());
        for (size_t i = 0; i < mesh_list.size(); i++)
        {
            if (!geometry[i].geometry || geometry[i].geometry.GetMesh() != mesh_list[i].mesh)
            {
                geometry[i].geometry = render_manager.MakeGeometry(std::move(mesh_list[i].mesh));
            }

            geometry[i].texture = mesh_list[i].texture;
        }

        generated_decoration = Style::TextDecoration::None;
        geometry_dirty = false;
    }

    void ElementText::GenerateDecoration(Mesh& mesh, const FontFaceHandle font_face_handle)
    {
        URUI_ZoneScopedC(0xA52A2A);
        URUI_ASSERT(decoration);

        const FontMetrics& metrics = GetFontEngineInterface()->GetFontMetrics(font_face_handle);

        float offset = 0.f;
        switch (decoration_property)
        {
        case Style::TextDecoration::Underline:
            offset = metrics.underline_position;
            break;
        case Style::TextDecoration::Overline:
            offset = -1.1f * metrics.ascent;
            break;
        case Style::TextDecoration::LineThrough:
            offset = -0.65f * metrics.x_height;
            break;
        case Style::TextDecoration::None:
            return;
        }

        for (const Line& line : lines)
        {
            const Vector2f position = {line.position.x, line.position.y + offset};
            const Vector2f size = {(float)line.width, metrics.underline_thickness};
            MeshUtilities::GenerateLine(mesh, position, size, colour);
        }
    }

    static bool BuildToken(String& token, const char*& token_begin, const char* string_end, bool first_token,
                           bool collapse_white_space, bool break_at_endline, Style::TextTransform text_transformation,
                           bool decode_escape_characters)
    {
        URUI_ASSERT(token_begin != string_end);

        const size_t remaining = size_t(string_end - token_begin);
        const size_t desired = token.size() + (remaining < 64u ? remaining : 64u);
        if (token.capacity() < desired)
        {
            token.reserve(desired);
        }

        // Check what the first character of the token is; all we need to know is if it is white-space or not.
        bool parsing_white_space = StringUtilities::IsWhitespace(*token_begin);

        // Loop through the string from the token's beginning until we find an end to the token. This can occur in various
        // places, depending on the white-space processing;
        //  - at the end of a section of non-white-space characters,
        //  - at the end of a section of white-space characters, if we're not collapsing white-space,
        //  - at an endline token, if we're breaking on endlines.
        while (token_begin != string_end)
        {
            bool force_non_whitespace = false;
            char character = *token_begin;

            const char* escape_begin = token_begin;

            // Check for an ampersand; if we find one, we've got an HTML escaped character.
            if (decode_escape_characters && character == '&')
            {
                // Find the terminating ';'.
                while (token_begin != string_end && *token_begin != ';')
                {
                    ++token_begin;
                }

                // If we couldn't find the ';', print the token like normal text.
                if (token_begin == string_end)
                {
                    token_begin = escape_begin;
                }
                // We could find a ';', parse the escape code. If the escape code is recognised, set the parsed character
                // to the appropriate one. If it is a non-breaking space, prevent it being picked up as whitespace. If it
                // is not recognised, print the token like normal text.
                else
                {
                    String escape_code(escape_begin + 1, token_begin);

                    if (escape_code == "lt")
                    {
                        character = '<';
                    }
                    else if (escape_code == "gt")
                    {
                        character = '>';
                    }
                    else if (escape_code == "amp")
                    {
                        character = '&';
                    }
                    else if (escape_code == "quot")
                    {
                        character = '"';
                    }
                    else if (escape_code == "nbsp")
                    {
                        character = ' ';
                        force_non_whitespace = true;
                    }
                    else
                    {
                        token_begin = escape_begin;
                    }
                }
            }

            // Check for an endline token; if we're breaking on endlines and we find one, then return true to indicate a
            // forced break.
            if (break_at_endline && character == '\n')
            {
                token += '\n';
                token_begin++;
                return true;
            }

            // If we've transitioned from white-space characters to non-white-space characters, or vice-versa, then check
            // if should terminate the token; if we're not collapsing white-space, then yes (as sections of white-space are
            // non-breaking), otherwise only if we've transitioned from characters to white-space.
            bool white_space = !force_non_whitespace && StringUtilities::IsWhitespace(character);
            if (white_space != parsing_white_space)
            {
                if (!collapse_white_space)
                {
                    // Restore pointer to the beginning of the escaped token, if we processed an escape code.
                    token_begin = escape_begin;
                    return false;
                }

                // We're collapsing white-space; we only tokenise words, not white-space, so we're only done tokenising
                // once we've begun parsing non-white-space and then found white-space.
                if (!parsing_white_space)
                {
                    // However, if we are the last non-whitespace character in the string, and there are trailing
                    // whitespace characters after this token, then we need to append a single space to the end of this
                    // token.
                    if (token_begin != string_end &&
                        LastToken(token_begin, string_end, collapse_white_space, break_at_endline))
                    {
                        token += ' ';
                    }

                    return false;
                }

                // We've transitioned from white-space to non-white-space, so we append a single white-space character.
                if (!first_token)
                {
                    token += ' ';
                }

                parsing_white_space = false;
            }

            // If the current character is white-space, we'll append a space character to the token if we're not collapsing
            // sections of white-space.
            if (white_space)
            {
                if (!collapse_white_space)
                {
                    token += ' ';
                }
            }
            else
            {
                if (text_transformation == Style::TextTransform::Uppercase)
                {
                    if (character >= 'a' && character <= 'z')
                    {
                        character += ('A' - 'a');
                    }
                }
                else if (text_transformation == Style::TextTransform::Lowercase)
                {
                    if (character >= 'A' && character <= 'Z')
                    {
                        character -= ('A' - 'a');
                    }
                }

                token += character;
            }

            ++token_begin;
        }

        return false;
    }

    static bool LastToken(const char* token_begin, const char* string_end, bool collapse_white_space,
                          bool break_at_endline)
    {
        bool last_token = (token_begin == string_end);
        if (collapse_white_space && !last_token)
        {
            last_token = true;
            const char* character = token_begin;

            while (character != string_end)
            {
                if (!StringUtilities::IsWhitespace(*character) || (break_at_endline && *character == '\n'))
                {
                    last_token = false;
                    break;
                }

                character++;
            }
        }

        return last_token;
    }

}
