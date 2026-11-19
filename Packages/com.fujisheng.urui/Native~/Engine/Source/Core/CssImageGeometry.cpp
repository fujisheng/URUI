#include "CssImageGeometry.h"
#include "../../../graphic_tint.h"
#include "CssTokenizer.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/StyleSheetSpecification.h"
#include "../../Include/URUI/Core/StyleSheetTypes.h"
#include "../../Include/URUI/Core/PropertyDefinition.h"
#include <algorithm>

namespace URUI
{

    bool SplitCssImageComponents(const String& value, Vector<StringList>& layers)
    {
        Vector<Diagnostic> diagnostics;
        const auto tokens = TokenizeCss(value, "urui:image-geometry", diagnostics);
        if (!diagnostics.empty())
        {
            return false;
        }
        layers = {StringList{}};
        int depth = 0;
        String part;
        auto flush = [&]
        {
            if (!part.empty())
            {
                layers.back().push_back(std::move(part));
                part.clear();
            }
        };
        for (const auto& token : tokens)
        {
            if (token.kind == CssTokenKind::Whitespace && depth == 0)
            {
                flush();
                continue;
            }
            if (token.kind == CssTokenKind::Delimiter)
            {
                if (token.value == "(")
                {
                    ++depth;
                }
                else if (token.value == ")" && --depth < 0)
                {
                    return false;
                }
                else if (depth == 0 && (token.value == "," || token.value == "/"))
                {
                    flush();
                    if (token.value == ",")
                    {
                        if (layers.back().empty())
                        {
                            return false;
                        }
                        layers.emplace_back();
                    }
                    else
                    {
                        layers.back().push_back("/");
                    }
                    continue;
                }
            }
            part += token.raw;
        }
        flush();
        return depth == 0 && !layers.back().empty();
    }

    static String Keyword(const String& value)
    {
        Vector<Diagnostic> diagnostics;
        const auto tokens = TokenizeCss(value, "urui:image-keyword", diagnostics);
        return diagnostics.empty() && tokens.size() == 1 && tokens[0].kind == CssTokenKind::Ident
                   ? AsciiLowerCss(tokens[0].value)
                   : String{};
    }

    static bool Length(const String& value, NumericValue& numeric, bool nonnegative)
    {
        Property property;
        if (!StyleSheetSpecification::GetParser("length_percent")->ParseValue(property, value, {}))
        {
            return false;
        }
        numeric = property.GetNumericValue();
        return !nonnegative || numeric.number >= 0.f;
    }

    bool ParseCssImagePosition(const StringList& parts, Array<NumericValue, 2>& result)
    {
        if (parts.empty() || parts.size() > 2)
        {
            return false;
        }
        StringList values = parts;
        for (auto& value : values)
        {
            const auto keyword = Keyword(value);
            if (keyword == "left" || keyword == "right" || keyword == "top" || keyword == "bottom" ||
                keyword == "center")
            {
                value = keyword;
            }
        }
        auto horizontal = [](const String& value) { return value == "left" || value == "right"; };
        auto vertical = [](const String& value) { return value == "top" || value == "bottom"; };
        if (values.size() == 1)
        {
            values = vertical(values[0]) ? StringList{"center", values[0]} : StringList{values[0], "center"};
        }
        if (vertical(values[0]) || horizontal(values[1]))
        {
            if (!(vertical(values[0]) || values[0] == "center") || !(horizontal(values[1]) || values[1] == "center"))
            {
                return false;
            }
            std::swap(values[0], values[1]);
        }
        for (int axis = 0; axis < 2; ++axis)
        {
            const auto& value = values[axis];
            if ((axis == 0 && vertical(value)) || (axis == 1 && horizontal(value)))
            {
                return false;
            }
            if (value == "left" || value == "top")
            {
                result[axis] = {0, Unit::PERCENT};
            }
            else if (value == "right" || value == "bottom")
            {
                result[axis] = {100, Unit::PERCENT};
            }
            else if (value == "center")
            {
                result[axis] = {50, Unit::PERCENT};
            }
            else if (!Length(value, result[axis], false))
            {
                return false;
            }
        }
        return true;
    }

    static bool Size(const StringList& parts)
    {
        if (parts.empty() || parts.size() > 2)
        {
            return false;
        }
        if (parts.size() == 1 && (Keyword(parts[0]) == "contain" || Keyword(parts[0]) == "cover"))
        {
            return true;
        }
        for (const auto& value : parts)
        {
            NumericValue length;
            if (Keyword(value) != "auto" && !Length(value, length, true))
            {
                return false;
            }
        }
        return true;
    }
    static bool Repeat(const StringList& parts)
    {
        if (parts.empty() || parts.size() > 2)
        {
            return false;
        }
        if (parts.size() == 1 && (Keyword(parts[0]) == "repeat-x" || Keyword(parts[0]) == "repeat-y"))
        {
            return true;
        }
        for (const auto& value : parts)
        {
            if (Keyword(value) != "repeat" && Keyword(value) != "no-repeat")
            {
                return false;
            }
        }
        return true;
    }

    static bool BorderValues(const StringList& parts, const String& kind, Array<NumericValue, 4>* output = nullptr,
                             bool* has_fill = nullptr)
    {
        if (parts.empty())
        {
            return false;
        }
        Vector<NumericValue> numbers;
        bool fill = false;
        for (const auto& part : parts)
        {
            const auto keyword = Keyword(part);
            if (kind == "slice" && keyword == "fill")
            {
                if (fill)
                {
                    return false;
                }
                fill = true;
                continue;
            }
            if (kind == "stretch")
            {
                if (keyword != "stretch")
                {
                    return false;
                }
                numbers.emplace_back(0, Unit::NUMBER);
                continue;
            }
            if (kind == "width" && keyword == "auto")
            {
                numbers.emplace_back(0, Unit::KEYWORD);
                continue;
            }
            Property parsed;
            const auto parser = kind == "slice" ? "number_percent" : "number_length_percent";
            if (!StyleSheetSpecification::GetParser(parser)->ParseValue(parsed, part, {}))
            {
                return false;
            }
            if (parsed.Get<float>() < 0 || (kind == "outset" && parsed.unit == Unit::PERCENT))
            {
                return false;
            }
            numbers.push_back(parsed.GetNumericValue());
        }
        if (numbers.empty() || numbers.size() > (kind == "stretch" ? 2 : 4))
        {
            return false;
        }
        if (output)
        {
            (*output)[0] = numbers[0];
            (*output)[1] = numbers.size() > 1 ? numbers[1] : numbers[0];
            (*output)[2] = numbers.size() > 2 ? numbers[2] : numbers[0];
            (*output)[3] = numbers.size() > 3 ? numbers[3] : (*output)[1];
        }
        if (has_fill)
        {
            *has_fill = fill;
        }
        return true;
    }

    bool ParseCssBorderImageSlice(const String& value, Array<NumericValue, 4>& slices, bool& fill)
    {
        Vector<StringList> layers;
        return SplitCssImageComponents(value, layers) && layers.size() == 1 &&
               BorderValues(layers[0], "slice", &slices, &fill);
    }

    bool PropertyParserImageGeometry::ParseValue(Property& property, const String& value,
                                                 const ParameterMap& parameters) const
    {
        Vector<StringList> layers;
        if (!SplitCssImageComponents(value, layers))
        {
            return false;
        }
        for (const auto& parts : layers)
        {
            if (parameters.count("slice") || parameters.count("width") || parameters.count("outset") ||
                parameters.count("stretch"))
            {
                if (layers.size() != 1 || !BorderValues(parts, parameters.count("slice")    ? "slice"
                                                               : parameters.count("width")  ? "width"
                                                               : parameters.count("outset") ? "outset"
                                                                                            : "stretch"))
                {
                    return false;
                }
            }
            else if (parameters.count("size"))
            {
                if (!Size(parts))
                {
                    return false;
                }
            }
            else if (parameters.count("position"))
            {
                Array<NumericValue, 2> position;
                if (!ParseCssImagePosition(parts, position))
                {
                    return false;
                }
            }
            else if (parameters.count("repeat"))
            {
                if (!Repeat(parts))
                {
                    return false;
                }
            }
            else if (parameters.count("box"))
            {
                if (parts.size() != 1)
                {
                    return false;
                }
                const auto box = Keyword(parts[0]);
                if (box != "border-box" && box != "padding-box" && box != "content-box")
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        property = Property(value, Unit::STRING);
        return true;
    }

    bool ParseCssBorderImageShorthand(const PropertySpecification& specification, PropertyDictionary& dictionary,
                                      const String& value)
    {
        Vector<StringList> layers;
        if (!SplitCssImageComponents(value, layers) || layers.size() != 1)
        {
            return false;
        }
        String source;
        String repeat;
        Array<StringList, 3> parts;
        int segment = 0;
        for (const auto& part : layers[0])
        {
            if (part == "/")
            {
                if (++segment > 2)
                {
                    return false;
                }
                continue;
            }
            if (Keyword(part) == "stretch")
            {
                repeat += (repeat.empty() ? "" : " ") + part;
                continue;
            }
            Property parsed;
            if (specification.GetProperty(PropertyId::BorderImageSource)->ParseValue(parsed, part))
            {
                if (!source.empty() || segment != 0)
                {
                    return false;
                }
                source = part;
            }
            else
            {
                parts[segment].push_back(part);
            }
        }
        auto join = [](const StringList& parts)
        {
            String result;
            for (const auto& part : parts)
            {
                result += (result.empty() ? "" : " ") + part;
            }
            return result;
        };
        if (segment && parts[0].empty())
        {
            return false;
        }
        if (segment == 1 && parts[1].empty())
        {
            return false;
        }
        if (segment == 2 && parts[2].empty())
        {
            return false;
        }
        if (!source.empty() &&
            !specification.ParsePropertyDeclaration(dictionary, PropertyId::BorderImageSource, source))
        {
            return false;
        }
        if (!repeat.empty() &&
            !specification.ParsePropertyDeclaration(dictionary, PropertyId::BorderImageRepeat, repeat))
        {
            return false;
        }
        for (int index = 0; index < 3; ++index)
        {
            if (!parts[index].empty())
            {
                if (!specification.ParsePropertyDeclaration(
                        dictionary,
                        Array<PropertyId, 3>{{PropertyId::BorderImageSlice, PropertyId::BorderImageWidth,
                                              PropertyId::BorderImageOutset}}[index],
                        join(parts[index])))
                {
                    return false;
                }
            }
        }
        return !source.empty() || !repeat.empty() || !parts[0].empty();
    }

    bool ParseCssBackgroundShorthand(const PropertySpecification& specification, PropertyDictionary& dictionary,
                                     const String& value)
    {
        Vector<StringList> layers;
        if (!SplitCssImageComponents(value, layers))
        {
            return false;
        }
        const Array<PropertyId, 6> ids{{PropertyId::BackgroundImage, PropertyId::BackgroundPosition,
                                        PropertyId::BackgroundSize, PropertyId::BackgroundRepeat,
                                        PropertyId::BackgroundOrigin, PropertyId::BackgroundClip}};
        Array<String, 6> joined;
        for (size_t layer = 0; layer < layers.size(); ++layer)
        {
            Array<String, 6> values{{"none", "0% 0%", "auto", "repeat", "padding-box", "border-box"}};
            bool image = false;
            bool color = false;
            bool repeat = false;
            bool slash = false;
            StringList position;
            StringList boxes;
            const auto& parts = layers[layer];
            for (size_t index = 0; index < parts.size(); ++index)
            {
                const auto& part = parts[index];
                const auto keyword = Keyword(part);
                Property parsed;
                if (part == "/")
                {
                    if (slash || position.empty() || index + 1 == parts.size())
                    {
                        return false;
                    }
                    slash = true;
                    StringList size{parts[++index]};
                    if (!Size(size))
                    {
                        return false;
                    }
                    if (index + 1 < parts.size())
                    {
                        StringList two{size[0], parts[index + 1]};
                        if (Size(two))
                        {
                            size = std::move(two);
                            ++index;
                        }
                    }
                    values[2] = size[0] + (size.size() == 2 ? " " + size[1] : "");
                }
                else if (specification.GetProperty(PropertyId::BackgroundColor)->ParseValue(parsed, part))
                {
                    if (color || layer + 1 != layers.size())
                    {
                        return false;
                    }
                    color = true;
                    dictionary.SetProperty(PropertyId::BackgroundColor, parsed);
                }
                else if (specification.GetProperty(PropertyId::BackgroundImage)->ParseValue(parsed, part))
                {
                    if (image)
                    {
                        return false;
                    }
                    image = true;
                    values[0] = part;
                }
                else if (Repeat({part}))
                {
                    if (repeat)
                    {
                        return false;
                    }
                    repeat = true;
                    values[3] = part;
                    if (index + 1 < parts.size() && Repeat({part, parts[index + 1]}))
                    {
                        values[3] += " " + parts[++index];
                    }
                }
                else if (keyword == "border-box" || keyword == "padding-box" || keyword == "content-box")
                {
                    if (boxes.size() == 2)
                    {
                        return false;
                    }
                    boxes.push_back(part);
                }
                else
                {
                    if (slash)
                    {
                        return false;
                    }
                    position.push_back(part);
                }
            }
            if (!position.empty())
            {
                Array<NumericValue, 2> parsed;
                if (!ParseCssImagePosition(position, parsed))
                {
                    return false;
                }
                values[1] = position[0] + (position.size() == 2 ? " " + position[1] : "");
            }
            if (!boxes.empty())
            {
                values[4] = boxes[0];
                values[5] = boxes.back();
            }
            for (size_t index = 0; index < ids.size(); ++index)
            {
                joined[index] += (layer ? ", " : "") + values[index];
            }
        }
        for (size_t index = 0; index < ids.size(); ++index)
        {
            if (!specification.ParsePropertyDeclaration(dictionary, ids[index], joined[index]))
            {
                return false;
            }
        }
        return true;
    }

    static StringList GetParts(Element* element, PropertyId property, size_t layer)
    {
        Vector<StringList> layers;
        if (const auto* value = element->GetProperty(property))
        {
            if (value->unit == Unit::STRING && SplitCssImageComponents(value->Get<String>(), layers))
            {
                return layers[layer % layers.size()];
            }
        }
        return {};
    }

    BoxArea GetCssImageArea(Element* element, PropertyId property, size_t layer)
    {
        const auto parts = GetParts(element, property, layer);
        if (parts.empty())
        {
            return BoxArea::Border;
        }
        const auto name = Keyword(parts[0]);
        return name == "content-box" ? BoxArea::Content : name == "padding-box" ? BoxArea::Padding : BoxArea::Border;
    }
    size_t GetCssImageLayerCount(Element* element, PropertyId image_property)
    {
        const auto* property = element->GetProperty(image_property);
        if (property && property->unit == Unit::DECORATOR)
        {
            const auto& declarations = property->value.GetReference<DecoratorsPtr>();
            if (declarations)
            {
                return declarations->layer_count;
            }
        }
        return 1;
    }

    // 在圆角绘制网格上按非重复轴截断，保留 UV 插值；不改变共享纹理的采样状态。
    static void ClipAxis(Mesh& mesh, int axis, float limit, bool lower)
    {
        Mesh clipped;
        clipped.texture_sampling = mesh.texture_sampling;
        for (size_t i = 0; i + 2 < mesh.indices.size(); i += 3)
        {
            Vector<Vertex> polygon{mesh.vertices[mesh.indices[i]], mesh.vertices[mesh.indices[i + 1]],
                                   mesh.vertices[mesh.indices[i + 2]]};
            Vector<Vertex> output;
            for (size_t j = 0; j < polygon.size(); ++j)
            {
                const auto& from = polygon[j];
                const auto& to = polygon[(j + 1) % polygon.size()];
                const bool from_in = lower ? from.position[axis] >= limit : from.position[axis] <= limit;
                const bool to_in = lower ? to.position[axis] >= limit : to.position[axis] <= limit;
                if (from_in)
                {
                    output.push_back(from);
                }
                if (from_in != to_in)
                {
                    const float t = (limit - from.position[axis]) / (to.position[axis] - from.position[axis]);
                    Vertex cut = from;
                    cut.position += (to.position - from.position) * t;
                    cut.tex_coord += (to.tex_coord - from.tex_coord) * t;
                    output.push_back(cut);
                }
            }
            const int first = int(clipped.vertices.size());
            clipped.vertices.insert(clipped.vertices.end(), output.begin(), output.end());
            for (size_t j = 1; j + 1 < output.size(); ++j)
            {
                clipped.indices.insert(clipped.indices.end(), {first, first + int(j), first + int(j + 1)});
            }
        }
        mesh = std::move(clipped);
    }

    CssImageLayout ResolveCssImageLayout(Element* element, PropertyId image_property, size_t layer,
                                         Vector2f natural_size, bool intrinsic_ratio, int box)
    {
        const bool background = image_property == PropertyId::BackgroundImage;
        const auto size_parts =
            GetParts(element, background ? PropertyId::BackgroundSize : PropertyId::MaskSize, layer);
        const auto repeat_parts =
            GetParts(element, background ? PropertyId::BackgroundRepeat : PropertyId::MaskRepeat, layer);
        const auto position_parts =
            GetParts(element, background ? PropertyId::BackgroundPosition : PropertyId::MaskPosition, layer);
        const auto origin_area =
            GetCssImageArea(element, background ? PropertyId::BackgroundOrigin : PropertyId::MaskOrigin, layer);
        CssImageLayout result;
        result.clip = GetCssImageArea(element, background ? PropertyId::BackgroundClip : PropertyId::MaskClip, layer);
        bool repeat[2] = {true, true};
        if (!repeat_parts.empty())
        {
            const auto first = Keyword(repeat_parts[0]);
            repeat[0] = first == "repeat" || first == "repeat-x";
            repeat[1] = repeat_parts.size() == 2 ? Keyword(repeat_parts[1]) == "repeat"
                                                 : first == "repeat" || first == "repeat-y";
        }
        result.sampling = TextureSampling((repeat[0] ? 1 : 0) | (repeat[1] ? 2 : 0));
        const auto origin = element->GetRenderBox(origin_area, box);
        const Vector2f area_size = origin.GetFillSize();
        result.size = intrinsic_ratio ? natural_size : area_size;
        if (!size_parts.empty())
        {
            const auto first = Keyword(size_parts[0]);
            if (first == "contain" || first == "cover")
            {
                if (intrinsic_ratio && natural_size.x > 0 && natural_size.y > 0)
                {
                    const Vector2f scale = area_size / natural_size;
                    result.size *= first == "contain" ? std::min(scale.x, scale.y) : std::max(scale.x, scale.y);
                }
            }
            else
            {
                bool automatic[2] = {true, true};
                for (int axis = 0; axis < int(size_parts.size()); ++axis)
                {
                    if (Keyword(size_parts[axis]) != "auto")
                    {
                        NumericValue numeric;
                        Length(size_parts[axis], numeric, true);
                        result.size[axis] = element->ResolveNumericValue(numeric, area_size[axis]);
                        automatic[axis] = false;
                    }
                }
                if (intrinsic_ratio)
                {
                    if (automatic[0] && !automatic[1] && natural_size.y > 0)
                    {
                        result.size.x = result.size.y * natural_size.x / natural_size.y;
                    }
                    if (automatic[1] && !automatic[0] && natural_size.x > 0)
                    {
                        result.size.y = result.size.x * natural_size.y / natural_size.x;
                    }
                }
            }
        }
        Array<NumericValue, 2> position{{{0, Unit::PERCENT}, {0, Unit::PERCENT}}};
        ParseCssImagePosition(position_parts, position);
        result.origin = origin.GetBorderOffset() + origin.GetFillOffset();
        for (int axis = 0; axis < 2; ++axis)
        {
            result.origin[axis] += element->ResolveNumericValue(position[axis], area_size[axis] - result.size[axis]);
        }
        return result;
    }

    Mesh GenerateCssImageMesh(Element* element, PropertyId image_property, size_t layer, Vector2f natural_size,
                              bool intrinsic_ratio, int box_index)
    {
        Mesh result;
        for (int box = 0; box < element->GetNumBoxes(); ++box)
        {
            if (box_index >= 0 && box != box_index)
            {
                continue;
            }
            const auto layout =
                ResolveCssImageLayout(element, image_property, layer, natural_size, intrinsic_ratio, box);
            if (layout.size.x <= 0 || layout.size.y <= 0)
            {
                continue;
            }
            Mesh mesh;
            mesh.texture_sampling = result.texture_sampling = layout.sampling;
            MeshUtilities::GenerateBackground(
                mesh, element->GetRenderBox(layout.clip, box),
                (image_property == PropertyId::MaskImage
                     ? element->GetComputedValues().image_color()
                     : URUI::Graphics::Tint(*element, element->GetComputedValues().image_color()))
                    .ToPremultiplied());
            for (auto& vertex : mesh.vertices)
            {
                vertex.tex_coord = (vertex.position - layout.origin) / layout.size;
            }
            for (int axis = 0; axis < 2; ++axis)
            {
                if (!(int(layout.sampling) & (1 << axis)))
                {
                    ClipAxis(mesh, axis, layout.origin[axis], true);
                    ClipAxis(mesh, axis, layout.origin[axis] + layout.size[axis], false);
                }
            }
            const int offset = int(result.vertices.size());
            result.vertices.insert(result.vertices.end(), mesh.vertices.begin(), mesh.vertices.end());
            for (int index : mesh.indices)
            {
                result.indices.push_back(index + offset);
            }
        }
        return result;
    }

    Mesh GenerateCssBorderImageMesh(Element* element, Vector2f source_size)
    {
        Mesh mesh;
        if (source_size.x <= 0 || source_size.y <= 0)
        {
            return mesh;
        }
        Array<NumericValue, 4> slices, widths, outsets;
        bool fill = false;
        if (!BorderValues(GetParts(element, PropertyId::BorderImageSlice, 0), "slice", &slices, &fill) ||
            !BorderValues(GetParts(element, PropertyId::BorderImageWidth, 0), "width", &widths) ||
            !BorderValues(GetParts(element, PropertyId::BorderImageOutset, 0), "outset", &outsets))
        {
            return mesh;
        }
        Array<float, 4> source;
        for (int edge = 0; edge < 4; ++edge)
        {
            const float dimension = source_size[(edge + 1) % 2];
            source[edge] =
                std::min(dimension, slices[edge].unit == Unit::PERCENT ? slices[edge].number * .01f * dimension
                                                                       : slices[edge].number);
        }
        const Vector2f uv[4] = {{0, 0},
                                {source[3] / source_size.x, source[0] / source_size.y},
                                {1 - source[1] / source_size.x, 1 - source[2] / source_size.y},
                                {1, 1}};
        for (int box = 0; box < element->GetNumBoxes(); ++box)
        {
            const auto border = element->GetRenderBox(BoxArea::Padding, box);
            const auto borders = border.GetBorderWidths();
            Array<float, 4> outset, width;
            for (int edge = 0; edge < 4; ++edge)
            {
                outset[edge] = outsets[edge].unit == Unit::NUMBER ? outsets[edge].number * borders[edge]
                                                                  : element->ResolveLength(outsets[edge]);
            }
            const Vector2f size = element->GetRenderBox(BoxArea::Border, box).GetFillSize() +
                                  Vector2f(outset[3] + outset[1], outset[0] + outset[2]);
            const Vector2f offset = border.GetBorderOffset() - Vector2f(outset[3], outset[0]);
            for (int edge = 0; edge < 4; ++edge)
            {
                width[edge] = widths[edge].unit == Unit::KEYWORD ? source[edge]
                              : widths[edge].unit == Unit::NUMBER
                                  ? widths[edge].number * borders[edge]
                                  : element->ResolveNumericValue(widths[edge], size[(edge + 1) % 2]);
            }
            float factor = 1.f;
            if (width[3] + width[1] > 0)
            {
                factor = std::min(factor, size.x / (width[3] + width[1]));
            }
            if (width[0] + width[2] > 0)
            {
                factor = std::min(factor, size.y / (width[0] + width[2]));
            }
            for (auto& edge : width)
            {
                edge *= factor;
            }
            const Vector2f position[4] = {{0, 0}, {width[3], width[0]}, {size.x - width[1], size.y - width[2]}, size};
            for (int y = 0; y < 3; ++y)
            {
                for (int x = 0; x < 3; ++x)
                {
                    if ((x == 1 && y == 1 && !fill) || uv[x + 1].x <= uv[x].x || uv[y + 1].y <= uv[y].y)
                    {
                        continue;
                    }
                    const Vector2f extent{position[x + 1].x - position[x].x, position[y + 1].y - position[y].y};
                    if (extent.x <= 0 || extent.y <= 0)
                    {
                        continue;
                    }
                    MeshUtilities::GenerateQuad(
                        mesh, offset + Vector2f(position[x].x, position[y].y), extent,
                        URUI::Graphics::Tint(*element, element->GetComputedValues().image_color()).ToPremultiplied(),
                        {uv[x].x, uv[y].y}, {uv[x + 1].x, uv[y + 1].y});
                }
            }
        }
        return mesh;
    }

}
