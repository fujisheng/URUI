#include "bridge_runtime.h"
#include "Engine/Source/Core/ElementStyle.h"
#include <URUI/Core/Decorator.h>
#include <URUI/Core/PropertyDefinition.h>
#include <URUI/Core/PropertyParser.h>
#include <URUI/Core/StyleSheetTypes.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <unordered_map>
#include <vector>

namespace URUI::Bridge
{
    namespace
    {
        struct ProgramKeyword
        {
            URUI::String text;
            float value = 0.f;
            int native = 0;
        };

        struct ProgramResourceKey
        {
            URUI::String text;
            URUI::String source;
            bool operator==(const ProgramResourceKey& other) const noexcept
            {
                return text == other.text && source == other.source;
            }
        };

        struct ProgramResourceKeyHash
        {
            size_t operator()(const ProgramResourceKey& k) const noexcept
            {
                const size_t h1 = std::hash<URUI::String>{}(k.text);
                const size_t h2 = std::hash<URUI::String>{}(k.source);
                return h1 ^ (h2 + 0x9e3779b97f4a7c15ULL + (h1 << 6) + (h1 >> 2));
            }
        };

        struct RawParseCacheEntry
        {
            URUI::String text;
            URUI::Property property;
            uint32_t kind = 0;
            uint32_t resourceId = 0;
            bool valid = false;
        };

        struct ProgramField
        {
            URUI::String name;
            URUI::PropertyId property = URUI::PropertyId::Invalid;
            uint32_t offset = 0;
            uint32_t kind = 0;
            int32_t origin = -1;
            std::vector<ProgramKeyword> keywords;
            std::unordered_map<URUI::String, uint32_t> resourceKeywordIndex;
            std::unordered_map<ProgramResourceKey, uint32_t, ProgramResourceKeyHash> imageIndex;
            URUI::Property lastImage;
            URUI::String lastImageSource;
            bool imageCached = false;
            uint32_t resource_id = 0;
            bool raw = false;
            bool inherited = false;
            bool cached = false;
            bool optional = false;
            URUI::Property cachedProperty;
            std::unordered_map<URUI::String, RawParseCacheEntry> rawCache;
        };

        struct ProgramLayout
        {
            uint64_t owner = 0;
            uint64_t root = 0;
            uint64_t schema_revision = 0;
            URUI::String name;
            uint32_t rows = 0;
            const URUI::PropertySpecification* specification = nullptr;
            const URUI::ElementDocument* document = nullptr;
            std::vector<ProgramField> fields;
        };

        static_assert(sizeof(URUIProgramField) == (sizeof(void*) == 8 ? 32 : 24), "program field ABI");
        static_assert(sizeof(URUIProgramKeyword) == (sizeof(void*) == 8 ? 16 : 12), "program keyword ABI");

        float RelativeBase(URUI::Element* element, const URUI::PropertyDefinition* definition)
        {
            if (!definition)
            {
                return 0.f;
            }
            auto* parent = element->GetParentNode();
            switch (definition->GetRelativeTarget())
            {
            case URUI::RelativeTarget::ContainingBlockWidth:
                return parent ? parent->GetBox().GetSize(URUI::BoxArea::Content).x : 0.f;
            case URUI::RelativeTarget::ContainingBlockHeight:
                return parent ? parent->GetBox().GetSize(URUI::BoxArea::Content).y : 0.f;
            case URUI::RelativeTarget::FontSize:
                return element->GetComputedValues().font_size();
            case URUI::RelativeTarget::ParentFontSize:
                return parent ? parent->GetComputedValues().font_size() : element->GetComputedValues().font_size();
            case URUI::RelativeTarget::LineHeight:
                return element->GetComputedValues().line_height().value;
            default:
                return 0.f;
            }
        }

        int OriginFromPriority(URUI::CssOriginImportance importance) noexcept
        {
            switch (importance)
            {
            case URUI::CssOriginImportance::UserAgentNormal:
                return 1;
            case URUI::CssOriginImportance::AuthorNormal:
                return 2;
            case URUI::CssOriginImportance::Animation:
                return 3;
            case URUI::CssOriginImportance::AuthorImportant:
            case URUI::CssOriginImportance::UserAgentImportant:
                return 4;
            case URUI::CssOriginImportance::Transition:
                return 5;
            }
            return 0;
        }

        int ProgramOrigin(URUI::Element* element, const ProgramField& field)
        {
            if (field.raw)
            {
                for (auto* current = element; current; current = field.inherited ? current->GetParentNode() : nullptr)
                {
                    if (const auto* property = current->GetLocalProperty(field.name))
                    {
                        return OriginFromPriority(property->priority.originImportance);
                    }
                }
                return 0;
            }
            for (auto* current = element; current; current = field.inherited ? current->GetParentNode() : nullptr)
            {
                if (const auto* property = current->GetLocalProperty(field.property))
                {
                    return OriginFromPriority(property->priority.originImportance);
                }
            }
            return 0;
        }

        bool ProgramEqual(const ProgramLayout& layout, const URUIProgramField* fields, uint32_t count, uint32_t rows)
        {
            if (layout.rows != rows || layout.fields.size() != count)
            {
                return false;
            }
            for (uint32_t n = 0; n < count; ++n)
            {
                const auto& a = layout.fields[n];
                const auto& b = fields[n];
                if (!b.property || a.name != b.property || a.offset != b.offset ||
                    (a.kind | (a.optional ? 256u : 0u)) != b.kind || a.origin != b.origin_offset ||
                    a.keywords.size() != b.keyword_count || (!b.keywords && b.keyword_count))
                {
                    return false;
                }
                for (uint32_t k = 0; k < b.keyword_count; ++k)
                {
                    if (!b.keywords[k].text || a.keywords[k].text != b.keywords[k].text ||
                        a.keywords[k].value != b.keywords[k].value || b.keywords[k].reserved)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        bool BindProgram(ProgramLayout& layout, URUI::Element* element)
        {
            const auto& specification = element->GetPropertySpecification();
            const auto* doc = element->GetOwnerDocument();
            if (layout.document == doc && layout.specification == &specification &&
                layout.schema_revision == specification.GetRegisteredPropertyRevision())
            {
                return true;
            }
            layout.specification = nullptr;
            layout.document = nullptr;
            for (auto& field : layout.fields)
            {
                field.property = specification.GetPropertyId(field.name);
                const auto* definition = specification.GetProperty(field.property);
                field.raw = !definition;
                field.inherited = definition ? definition->IsInherited() : (field.name.rfind("--", 0) == 0);
                field.cached = false;
                field.rawCache.clear();
                if (!definition)
                {
                    if (field.name.rfind("--", 0) != 0)
                    {
                        return false;
                    }
                    for (size_t a = 0; a < field.keywords.size(); ++a)
                    {
                        for (size_t b = 0; b < a; ++b)
                        {
                            if (field.keywords[a].text == field.keywords[b].text &&
                                field.keywords[a].value != field.keywords[b].value)
                            {
                                return false;
                            }
                        }
                    }
                    continue;
                }
                for (auto& keyword : field.keywords)
                {
                    URUI::Property parsed;
                    if (!definition->ParseValue(parsed, keyword.text) || parsed.unit != URUI::Unit::KEYWORD)
                    {
                        return false;
                    }
                    keyword.native = parsed.Get<int>();
                }
                for (size_t a = 0; a < field.keywords.size(); ++a)
                {
                    for (size_t b = 0; b < a; ++b)
                    {
                        if (field.keywords[a].native == field.keywords[b].native &&
                            field.keywords[a].value != field.keywords[b].value)
                        {
                            return false;
                        }
                    }
                }
            }
            layout.specification = &specification;
            layout.document = doc;
            layout.schema_revision = specification.GetRegisteredPropertyRevision();
            return true;
        }

        uint32_t InternProgramKeyword(ProgramField& field, const URUI::String& text)
        {
            const auto found = field.resourceKeywordIndex.find(text);
            if (found != field.resourceKeywordIndex.end())
            {
                field.resource_id = found->second;
                return found->second;
            }
            if (field.resourceKeywordIndex.size() >= 65536)
            {
                throw std::bad_alloc();
            }
            const uint32_t id = static_cast<uint32_t>(field.resourceKeywordIndex.size()) + 1u;
            field.resourceKeywordIndex.emplace(text, id);
            field.resource_id = id;
            return id;
        }

        bool ReadProgramImage(URUI::Element* element, ProgramField& field, const URUI::Property& property, float* out)
        {
            if (property.unit != URUI::Unit::DECORATOR)
            {
                return false;
            }
            const auto declarations = property.Get<URUI::DecoratorsPtr>();
            if (declarations && declarations->layer_count != 1)
            {
                return false;
            }
            if (!declarations || declarations->list.empty())
            {
                field.imageCached = false;
                field.resource_id = 0;
                out[0] = 0.f;
                return true;
            }
            const auto& type = declarations->list.front().type;
            if (type != "urui-css-image" && type.find("gradient") == URUI::String::npos)
            {
                return false;
            }
            static const URUI::String empty;
            const auto* document = element->GetOwnerDocument();
            const auto& source = property.source ? property.source->path : document ? document->GetSourceURL() : empty;
            if (field.imageCached && field.lastImage == property && field.lastImageSource == source)
            {
                out[0] = static_cast<float>(field.resource_id);
                return true;
            }
            ProgramResourceKey key{property.ToString(), source};
            auto found = field.imageIndex.find(key);
            uint32_t id = 0;
            if (found != field.imageIndex.end())
            {
                id = found->second;
            }
            else
            {
                if (field.imageIndex.size() >= 65536)
                {
                    throw std::bad_alloc();
                }
                id = static_cast<uint32_t>(field.imageIndex.size()) + 1u;
                field.imageIndex.emplace(std::move(key), id);
            }
            field.resource_id = id;
            field.lastImage = property;
            field.lastImageSource = source;
            field.imageCached = true;
            out[0] = static_cast<float>(id);
            return true;
        }

        bool ReadRawProgramField(URUI::Element* element, ProgramField& field, const URUI::Property& property,
                                 float* out)
        {
            if (property.unit != URUI::Unit::STRING)
            {
                return false;
            }
            const auto& text = property.value.GetReference<URUI::String>();
            RawParseCacheEntry* entry = nullptr;
            RawParseCacheEntry ephemeral{};
            auto found = field.rawCache.find(text);
            if (found != field.rawCache.end())
            {
                entry = &found->second;
            }
            else
            {
                URUI::Property parsed;
                uint32_t kind = 8;
                auto try_number = [&]()
                {
                    auto* number = URUI::StyleSheetSpecification::GetParser("number");
                    if (number && number->ParseValue(parsed, text, {}))
                    {
                        kind = parsed.Get<float>() == 0.f ? 3u : 1u;
                        return true;
                    }
                    return false;
                };
                auto try_length = [&]()
                {
                    auto* length = URUI::StyleSheetSpecification::GetParser("length");
                    if (length && length->ParseValue(parsed, text, {}) &&
                        (parsed.unit == URUI::Unit::PX || parsed.unit == URUI::Unit::DP))
                    {
                        kind = 2;
                        return true;
                    }
                    return false;
                };
                auto try_color = [&]()
                {
                    auto* color = URUI::StyleSheetSpecification::GetParser("color");
                    if (color && color->ParseValue(parsed, text, {}))
                    {
                        kind = 4;
                        return true;
                    }
                    return false;
                };
                auto try_image = [&]()
                {
                    auto* image = URUI::StyleSheetSpecification::GetParser("css_image");
                    if ((text != "none" || field.kind == 16) && image &&
                        image->ParseValue(parsed, text, {{"effect", 1}}))
                    {
                        kind = 16;
                        return true;
                    }
                    return false;
                };

                // Dispatch to the expected parser(s) for field.kind directly instead of trying all 4 parsers.
                if (field.kind == 1 || field.kind == 32)
                {
                    if (!try_number() && field.kind == 32)
                    {
                        // Non-numeric token for number-or-keyword falls through as keyword (kind = 8)
                        // unless it parses as length/color/image (which must be rejected as invalid keyword).
                        if (!try_length() && !try_color())
                        {
                            try_image();
                        }
                    }
                }
                else if (field.kind == 2)
                {
                    if (!try_number())
                    {
                        try_length();
                    }
                }
                else if (field.kind == 4)
                {
                    try_color();
                }
                else if (field.kind == 16)
                {
                    try_image();
                }
                else
                {
                    if (!try_number() && !try_length() && !try_color())
                    {
                        try_image();
                    }
                }

                uint32_t resourceId = 0;
                if (kind == 8 && field.kind == 8 && field.keywords.empty())
                {
                    resourceId = InternProgramKeyword(field, text);
                }
                ephemeral.text = text;
                ephemeral.property = std::move(parsed);
                ephemeral.kind = kind;
                ephemeral.resourceId = resourceId;
                ephemeral.valid = true;
                if (field.rawCache.size() < 256)
                {
                    auto [it, _] = field.rawCache.emplace(text, std::move(ephemeral));
                    entry = &it->second;
                }
                else
                {
                    entry = &ephemeral;
                }
            }

            if (field.kind == 16)
            {
                entry->property.source = property.source;
                return ReadProgramImage(element, field, entry->property, out);
            }
            if (field.kind == 32)
            {
                if (entry->kind & 1u)
                {
                    out[0] = entry->property.Get<float>();
                    out[1] = 0.f;
                    return std::isfinite(out[0]);
                }
                if (entry->kind != 8u)
                {
                    return false;
                }
                for (const auto& keyword : field.keywords)
                {
                    if (keyword.text == text)
                    {
                        out[0] = keyword.value;
                        out[1] = 1.f;
                        return true;
                    }
                }
                return false;
            }
            if (!(entry->kind & field.kind))
            {
                return false;
            }
            if (field.kind == 1)
            {
                out[0] = entry->property.Get<float>();
            }
            else if (field.kind == 2)
            {
                out[0] = entry->property.unit == URUI::Unit::NUMBER
                             ? 0.f
                             : element->ResolveLength(entry->property.GetNumericValue());
            }
            else if (field.kind == 4)
            {
                const auto color = entry->property.Get<URUI::Colourb>();
                out[0] = color.red / 255.f;
                out[1] = color.green / 255.f;
                out[2] = color.blue / 255.f;
                out[3] = color.alpha / 255.f;
            }
            else if (field.keywords.empty())
            {
                out[0] = static_cast<float>(entry->resourceId);
            }
            else
            {
                bool found = false;
                for (const auto& keyword : field.keywords)
                {
                    if (keyword.text == text)
                    {
                        out[0] = keyword.value;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
            for (uint32_t n = 0; n < (field.kind == 4 ? 4u : 1u); ++n)
            {
                if (!std::isfinite(out[n]))
                {
                    return false;
                }
            }
            return true;
        }

        bool ReadProgramField(URUI::Element* element, ProgramField& field, float* out)
        {
            const auto* property = field.raw ? element->GetProperty(field.name) : element->GetProperty(field.property);
            if (!property)
            {
                return field.optional;
            }
            if (field.raw && property->unit == URUI::Unit::STRING)
            {
                return ReadRawProgramField(element, field, *property, out);
            }
            if (field.kind == 16)
            {
                return ReadProgramImage(element, field, *property, out);
            }
            if (field.kind == 32)
            {
                if (property->unit == URUI::Unit::NUMBER)
                {
                    out[0] = property->Get<float>();
                    out[1] = 0.f;
                    return std::isfinite(out[0]);
                }
                if (property->unit != URUI::Unit::KEYWORD || field.raw)
                {
                    return false;
                }
                for (const auto& keyword : field.keywords)
                {
                    if (keyword.native == property->Get<int>())
                    {
                        out[0] = keyword.value;
                        out[1] = 1.f;
                        return true;
                    }
                }
                return false;
            }
            if (field.raw && field.kind == 8)
            {
                return false;
            }
            if (field.kind == 1)
            {
                if (field.property == URUI::PropertyId::Opacity)
                {
                    out[0] = element->GetComputedValues().opacity();
                }
                else
                {
                    if (property->unit != URUI::Unit::NUMBER)
                    {
                        return false;
                    }
                    out[0] = property->Get<float>();
                }
            }
            else if (field.kind == 2)
            {
                if (field.property == URUI::PropertyId::Width || field.property == URUI::PropertyId::Height)
                {
                    const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
                    out[0] = field.property == URUI::PropertyId::Width ? size.x : size.y;
                }
                else if (field.property == URUI::PropertyId::FontSize)
                {
                    out[0] = element->GetComputedValues().font_size();
                }
                else if (field.property == URUI::PropertyId::LineHeight)
                {
                    out[0] = element->GetComputedValues().line_height().value;
                }
                else if (URUI::Any(property->unit & URUI::Unit::LENGTH))
                {
                    out[0] = element->ResolveLength(property->GetNumericValue());
                }
                else if (property->unit == URUI::Unit::NUMBER && property->Get<float>() == 0.f)
                {
                    out[0] = 0.f;
                }
                else if (property->unit == URUI::Unit::PERCENT && property->definition &&
                         property->definition->GetRelativeTarget() != URUI::RelativeTarget::None)
                {
                    out[0] = element->ResolveNumericValue(property->GetNumericValue(),
                                                          RelativeBase(element, property->definition));
                }
                else
                {
                    return false;
                }
            }
            else if (field.kind == 4)
            {
                if (property->unit != URUI::Unit::COLOUR)
                {
                    return false;
                }
                const auto color = property->Get<URUI::Colourb>();
                out[0] = color.red / 255.f;
                out[1] = color.green / 255.f;
                out[2] = color.blue / 255.f;
                out[3] = color.alpha / 255.f;
            }
            else
            {
                if (property->unit != URUI::Unit::KEYWORD)
                {
                    return false;
                }
                const auto native = property->Get<int>();
                if (field.keywords.empty())
                {
                    if (!field.cached || field.cachedProperty != *property)
                    {
                        InternProgramKeyword(field, property->ToString());
                        field.cachedProperty = *property;
                        field.cached = true;
                    }
                    out[0] = static_cast<float>(field.resource_id);
                }
                else
                {
                    bool found = false;
                    for (const auto& keyword : field.keywords)
                    {
                        if (keyword.native == native)
                        {
                            out[0] = keyword.value;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        return false;
                    }
                }
            }
            for (uint32_t n = 0; n < (field.kind == 4 ? 4u : 1u); ++n)
            {
                if (!std::isfinite(out[n]))
                {
                    return false;
                }
            }
            return true;
        }

        bool FillProgram(ProgramLayout& layout, URUI::Element* element, float* values)
        {
            std::fill_n(values, static_cast<size_t>(layout.rows) * 4, 0.f);
            if (!BindProgram(layout, element))
            {
                return false;
            }
            for (auto& field : layout.fields)
            {
                if (!ReadProgramField(element, field, values + field.offset))
                {
                    std::fill_n(values, static_cast<size_t>(layout.rows) * 4, 0.f);
                    return false;
                }
                if (field.origin >= 0)
                {
                    values[field.origin] = static_cast<float>(ProgramOrigin(element, field));
                }
            }
            return true;
        }
    }

    struct ProgramLayoutStore
    {
        std::unordered_map<uint64_t, ProgramLayout> layouts;
        uint64_t nextProgramLayout = 0;
    };

    std::unique_ptr<ProgramLayoutStore, ProgramLayoutStoreDeleter> CreateProgramLayoutStore()
    {
        return std::unique_ptr<ProgramLayoutStore, ProgramLayoutStoreDeleter>(new ProgramLayoutStore());
    }

    void DestroyProgramLayoutStore(ProgramLayoutStore* store) noexcept
    {
        delete store;
    }

    void ClearProgramLayoutsForDocument(ProgramLayoutStore* store, URUIDocumentHandle owner) noexcept
    {
        if (!store)
        {
            return;
        }
        for (auto it = store->layouts.begin(); it != store->layouts.end();)
        {
            if (it->second.owner == owner)
            {
                it = store->layouts.erase(it);
            }
            else
            {
                ++it;
            }
        }
    }

    int ValidateProgramLayoutBinding(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t elementToken,
                                     uint64_t program, uint32_t float4_count)
    {
        if (!surface.programLayouts)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto found = surface.programLayouts->layouts.find(program);
        if (found == surface.programLayouts->layouts.end() || found->second.owner != owner)
        {
            return URUI_ERR_NOT_READY;
        }
        if (float4_count < found->second.rows)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        auto* element = ResolveElement(surface, owner, elementToken);
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        auto* root = ResolveElement(surface, owner, found->second.root);
        if (!root || root->GetOwnerDocument() != element->GetOwnerDocument())
        {
            return URUI_ERR_NOT_READY;
        }
        return URUI_OK;
    }

    int FillProgramLayoutValues(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t elementToken,
                                uint64_t program, float* values, uint32_t float4_count)
    {
        if (!surface.programLayouts)
        {
            return URUI_ERR_NOT_READY;
        }
        auto found = surface.programLayouts->layouts.find(program);
        if (found == surface.programLayouts->layouts.end() || found->second.owner != owner)
        {
            return URUI_ERR_NOT_READY;
        }
        if (!values || float4_count < found->second.rows)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        auto* element = ResolveElement(surface, owner, elementToken);
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        auto* root = ResolveElement(surface, owner, found->second.root);
        if (!root || root->GetOwnerDocument() != element->GetOwnerDocument())
        {
            return URUI_ERR_NOT_READY;
        }
        return FillProgram(found->second, element, values) ? URUI_OK : URUI_ERR_INVALID_ARG;
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_program_parameter_api_version(void)
    {
        return 2;
    }

    int urui_program_layout_register(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t exemplar,
                                     const char* name, const URUIProgramField* fields, uint32_t count, uint32_t rows,
                                     uint64_t* output)
    try
    {
        if (!output)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *output = 0;
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!surface->programLayouts)
        {
            return URUI_ERR_NOT_READY;
        }
        if (!name || !*name || !rows || rows > 512 || count > 2048 || (!fields && count))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = ResolveElement(*surface, owner, exemplar);
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        auto* document = element->GetOwnerDocument();
        if (!document || (element != document && element->GetParentNode() != document))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto& store = *surface->programLayouts;
        for (auto& item : store.layouts)
        {
            if (item.second.owner == owner && item.second.name == name)
            {
                if (!ProgramEqual(item.second, fields, count, rows))
                {
                    return URUI_ERR_INVALID_ARG;
                }
                item.second.root = exemplar;
                item.second.specification = nullptr;
                item.second.document = nullptr;
                if (!BindProgram(item.second, element))
                {
                    return URUI_ERR_INVALID_ARG;
                }
                *output = item.first;
                return URUI_OK;
            }
        }
        std::array<uint8_t, 2048> used{};
        ProgramLayout layout;
        layout.owner = owner;
        layout.root = exemplar;
        layout.name = name;
        layout.rows = rows;
        layout.fields.reserve(count);
        for (uint32_t n = 0; n < count; ++n)
        {
            const auto& source = fields[n];
            const auto kind = source.kind & ~256u;
            if (!source.property || !*source.property ||
                (kind != 1 && kind != 2 && kind != 4 && kind != 8 && kind != 16 && kind != 32) ||
                source.keyword_count > 128 || (!source.keywords && source.keyword_count) ||
                (source.keyword_count && kind != 8 && kind != 32))
            {
                return URUI_ERR_INVALID_ARG;
            }
            const auto width = kind == 4 ? 4u : kind == 32 ? 2u : 1u;
            if (source.offset > rows * 4 || width > rows * 4 - source.offset || source.origin_offset < -1 ||
                source.origin_offset >= static_cast<int32_t>(rows * 4))
            {
                return URUI_ERR_INVALID_ARG;
            }
            for (uint32_t k = 0; k < width; ++k)
            {
                if (used[source.offset + k]++)
                {
                    return URUI_ERR_INVALID_ARG;
                }
            }
            if (source.origin_offset >= 0 && used[static_cast<uint32_t>(source.origin_offset)]++)
            {
                return URUI_ERR_INVALID_ARG;
            }
            ProgramField field;
            field.name = source.property;
            field.offset = source.offset;
            field.kind = kind;
            field.optional = (source.kind & 256u) != 0;
            field.origin = source.origin_offset;
            field.keywords.reserve(source.keyword_count);
            for (uint32_t k = 0; k < source.keyword_count; ++k)
            {
                const auto& keyword = source.keywords[k];
                if (!keyword.text || !std::isfinite(keyword.value) || keyword.reserved)
                {
                    return URUI_ERR_INVALID_ARG;
                }
                field.keywords.push_back({keyword.text, keyword.value, 0});
            }
            layout.fields.push_back(std::move(field));
        }
        if (!BindProgram(layout, element))
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (store.nextProgramLayout == UINT64_MAX)
        {
            return URUI_ERR_INTERNAL;
        }
        const auto identity = ++store.nextProgramLayout;
        store.layouts.emplace(identity, std::move(layout));
        *output = identity;
        return URUI_OK;
    }
    catch (const std::bad_alloc&)
    {
        return URUI_ERR_BUDGET;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_program_layout_values(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                   uint64_t program, float* values, uint32_t rows)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        return FillProgramLayoutValues(*surface, owner, token, program, values, rows);
    }
    catch (const std::bad_alloc&)
    {
        return URUI_ERR_BUDGET;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_program_layout_release(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t program)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!surface->programLayouts)
        {
            return URUI_ERR_NOT_READY;
        }
        auto found = surface->programLayouts->layouts.find(program);
        if (found == surface->programLayouts->layouts.end() || found->second.owner != owner)
        {
            return URUI_ERR_NOT_READY;
        }
        surface->programLayouts->layouts.erase(found);
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }
}
