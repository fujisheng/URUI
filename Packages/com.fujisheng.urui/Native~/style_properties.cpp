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
#include <stdexcept>
#include <unordered_map>
#include <vector>

namespace URUI::Bridge
{
    struct StyleStore
    {
        struct Value
        {
            URUI::String name;
            URUI::String text;
            URUI::String source;
            int kind = 0;
            float x = 0;
            float y = 0;
            float z = 0;
            float w = 0;
            bool operator==(const Value& other) const
            {
                return name == other.name && text == other.text && source == other.source && kind == other.kind &&
                       x == other.x && y == other.y && z == other.z && w == other.w;
            }
        };

        struct Snapshot
        {
            uint64_t owner = 0;
            uint64_t stamp = UINT64_MAX;
            uint64_t revision = 0;
            float width = -1;
            float height = -1;
            bool hasUnregisteredCustom = false;
            bool hasUnregisteredExpressions = false;
            float parentWidth = -1;
            float parentHeight = -1;
            std::vector<Value> values;
            std::vector<URUIStyleValue> serialized;
            std::vector<char> text;
        };

        struct SnapshotNode
        {
            uint64_t owner = 0;
            std::array<Snapshot, 2> banks;
            std::array<uint32_t, 2> pins{};
            uint32_t current = 0;
        };

        struct FrozenSnapshot
        {
            uint64_t owner = 0;
            uint64_t token = 0;
            uint64_t version = 0;
            uint32_t bank = 0;
        };

        std::unordered_map<uint64_t, SnapshotNode> snapshots;
        std::vector<FrozenSnapshot> frozenSnapshots;
        std::vector<uint32_t> idleFrozenSnapshots;
        uint64_t nextFrozenVersion = 0;
        uint64_t nextRevision = 0;
        uint64_t lastPrunedHitTestRevision = 0;
        uint64_t builds = 0;
        uint64_t strings = 0;
        uint64_t copies = 0;
        std::unordered_map<const URUI::PropertyDefinition*, std::unordered_map<uint64_t, URUI::String>> keywordText;

        // Pre-sorted registered PropertyIds cached per (specification, schemaRevision).
        const URUI::PropertySpecification* cachedSpec = nullptr;
        uint64_t cachedSpecRevision = 0;
        std::vector<URUI::PropertyId> sortedPropertyIds;

        FrozenSnapshot* FindFrozen(uint64_t owner, const URUIFrozenStyleHandle* handle) noexcept
        {
            if (!handle || !handle->slot || handle->slot > frozenSnapshots.size() || !handle->version)
            {
                return nullptr;
            }
            auto& value = frozenSnapshots[static_cast<size_t>(handle->slot - 1)];
            return value.version == handle->version && value.owner == owner ? &value : nullptr;
        }

        void ReleaseFrozen(FrozenSnapshot& frozen) noexcept
        {
            auto found = snapshots.find(frozen.token);
            if (found != snapshots.end() && found->second.owner == frozen.owner)
            {
                auto& pins = found->second.pins[frozen.bank];
                if (pins > 0)
                {
                    --pins;
                }
            }
            const auto index = static_cast<uint32_t>(&frozen - frozenSnapshots.data());
            frozen = {};
            idleFrozenSnapshots.push_back(index);
        }

        URUI::String KeywordText(const URUI::String& name, const URUI::Property& property)
        {
            // Standard property definitions are fixed for the session; never retain document custom definitions.
            if (name.rfind("--", 0) == 0)
            {
                return property.ToString();
            }
            auto& values = keywordText[property.definition];
            const uint64_t key =
                (uint64_t(uint32_t(property.parser_index)) << 32) | uint32_t(property.Get<int>());
            auto found = values.find(key);
            if (found == values.end())
            {
                found = values.emplace(key, property.ToString()).first;
            }
            return found->second;
        }

        const std::vector<URUI::PropertyId>& SortedProperties(const URUI::PropertySpecification& spec)
        {
            const uint64_t rev = spec.GetRegisteredPropertyRevision();
            if (cachedSpec != &spec || cachedSpecRevision != rev)
            {
                sortedPropertyIds.clear();
                const auto& registered = spec.GetRegisteredProperties();
                sortedPropertyIds.reserve(registered.Size());
                for (auto id : registered)
                {
                    sortedPropertyIds.push_back(id);
                }
                std::sort(sortedPropertyIds.begin(), sortedPropertyIds.end(),
                          [&spec](URUI::PropertyId a, URUI::PropertyId b)
                          { return spec.GetPropertyName(a) < spec.GetPropertyName(b); });
                cachedSpec = &spec;
                cachedSpecRevision = rev;
            }
            return sortedPropertyIds;
        }
    };

    static_assert(sizeof(URUIFrozenStyleHandle) == 16, "frozen style handle ABI");

    namespace
    {
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

        int OriginById(URUI::Element* element, const URUI::PropertySpecification& spec, URUI::PropertyId id)
        {
            const auto* definition = spec.GetProperty(id);
            const bool inherited = definition && definition->IsInherited();
            for (auto* current = element; current; current = inherited ? current->GetParentNode() : nullptr)
            {
                if (const auto* property = current->GetLocalProperty(id))
                {
                    return OriginFromPriority(property->priority.originImportance);
                }
            }
            return 0;
        }

        int OriginByName(URUI::Element* element, const URUI::String& name)
        {
            const auto& specification = element->GetPropertySpecification();
            const auto id = specification.GetPropertyId(name);
            if (id != URUI::PropertyId::Invalid)
            {
                return OriginById(element, specification, id);
            }
            const bool inherited = name.rfind("--", 0) == 0;
            for (auto* current = element; current; current = inherited ? current->GetParentNode() : nullptr)
            {
                if (const auto* property = current->GetLocalProperty(name))
                {
                    return OriginFromPriority(property->priority.originImportance);
                }
            }
            return 0;
        }

        StyleStore::Value ReadStyleValue(StyleStore& store, URUI::Element* element, const URUI::String& name,
                                         const URUI::Property& property, bool customUnregistered)
        {
            StyleStore::Value result;
            result.name = name;
            if (property.unit == URUI::Unit::NUMBER)
            {
                result.kind = 1;
                result.x = property.Get<float>();
            }
            else if (URUI::Any(property.unit & URUI::Unit::LENGTH))
            {
                result.kind = 2;
                result.x = element->ResolveLength(property.GetNumericValue());
            }
            else if (property.unit == URUI::Unit::PERCENT && property.definition &&
                     property.definition->GetRelativeTarget() != URUI::RelativeTarget::None)
            {
                result.kind = 2;
                result.x =
                    element->ResolveNumericValue(property.GetNumericValue(), RelativeBase(element, property.definition));
            }
            else if (property.unit == URUI::Unit::COLOUR)
            {
                const auto color = property.Get<URUI::Colourb>();
                result.kind = 4;
                result.x = color.red / 255.f;
                result.y = color.green / 255.f;
                result.z = color.blue / 255.f;
                result.w = color.alpha / 255.f;
            }
            else if (property.unit == URUI::Unit::DECORATOR)
            {
                const auto declarations = property.Get<URUI::DecoratorsPtr>();
                if (declarations && declarations->layer_count != 1)
                {
                    return result;
                }
                result.kind = 16;
                result.source = property.source               ? property.source->path
                                : element->GetOwnerDocument() ? element->GetOwnerDocument()->GetSourceURL()
                                                              : "";
                if (!declarations || declarations->list.empty())
                {
                    return result;
                }
                const auto& declaration = declarations->list.front();
                if (declaration.type == "urui-css-image")
                {
                    const auto id = declaration.instancer->GetPropertySpecification().GetPropertyId("image-src");
                    const auto* source = declaration.properties.GetProperty(id);
                    if (!source)
                    {
                        return StyleStore::Value{};
                    }
                    result.x = 1.f;
                    result.text = source->Get<URUI::String>();
                }
                else
                {
                    if (declaration.type.find("gradient") == URUI::String::npos)
                    {
                        return StyleStore::Value{};
                    }
                    result.x = 2.f;
                    result.text = declarations->value;
                }
            }
            else if (customUnregistered && property.unit == URUI::Unit::STRING)
            {
                const auto text = property.Get<URUI::String>();
                URUI::Property parsed;
                auto* number = URUI::StyleSheetSpecification::GetParser("number");
                auto* length = URUI::StyleSheetSpecification::GetParser("length");
                auto* color = URUI::StyleSheetSpecification::GetParser("color");
                if (number && number->ParseValue(parsed, text, {}))
                {
                    result.kind = 1;
                    result.x = parsed.Get<float>();
                    if (result.x == 0.f)
                    {
                        result.kind |= 2;
                    }
                }
                else if (length && length->ParseValue(parsed, text, {}) &&
                         (parsed.unit == URUI::Unit::PX || parsed.unit == URUI::Unit::DP))
                {
                    result.kind = 2;
                    result.x = element->ResolveLength(parsed.GetNumericValue());
                }
                else if (color && color->ParseValue(parsed, text, {}))
                {
                    result = ReadStyleValue(store, element, name, parsed, false);
                }
                else
                {
                    auto* image = URUI::StyleSheetSpecification::GetParser("css_image");
                    if (text != "none" && image && image->ParseValue(parsed, text, {{"effect", 1}}))
                    {
                        parsed.source = property.source;
                        result = ReadStyleValue(store, element, name, parsed, false);
                    }
                    else
                    {
                        result.kind = 8;
                        result.text = text;
                    }
                }
            }
            else if (property.unit == URUI::Unit::STRING)
            {
                result.kind = 8;
                result.text = property.Get<URUI::String>();
            }
            else if (property.unit == URUI::Unit::KEYWORD && property.definition)
            {
                result.kind = 8;
                result.text = store.KeywordText(name, property);
            }
            if (!std::isfinite(result.x) || !std::isfinite(result.y) || !std::isfinite(result.z) ||
                !std::isfinite(result.w))
            {
                result.kind = 0;
            }
            return result;
        }

        void SerializeSnapshot(StyleStore::Snapshot& snapshot)
        {
            auto& text = snapshot.text;
            auto& output = snapshot.serialized;
            text.clear();
            output.clear();
            output.reserve(snapshot.values.size());
            const auto append = [&text](const URUI::String& value)
            {
                const int offset = static_cast<int>(text.size());
                text.insert(text.end(), value.c_str(), value.c_str() + value.size() + 1);
                return offset;
            };
            for (const auto& value : snapshot.values)
            {
                const int kind = value.kind & 255;
                const int name = append(value.name);
                const int content = (kind == 8 || kind == 16) ? append(value.text) : -1;
                const int source = kind == 16 ? append(value.source) : -1;
                output.push_back({name, value.kind, value.x, value.y, value.z, value.w, content, source});
            }
        }

        // Validate whether an incremental primitive delta can be applied to `source` without mutating it.
        decltype(std::declval<URUI::ElementStyle*>()->GetSnapshotValueChanges(0))
        ValidatePrimitiveDelta(URUI::Element* element, const StyleStore::Snapshot& source)
        {
            if (!source.revision || source.hasUnregisteredExpressions)
            {
                return nullptr;
            }
            const auto* changed = element->GetStyle()->GetSnapshotValueChanges(source.stamp);
            if (!changed)
            {
                return nullptr;
            }
            const auto& specification = element->GetPropertySpecification();
            for (const auto& delta : *changed)
            {
                const auto& name = specification.GetPropertyName(delta.id);
                const auto existing = std::lower_bound(
                    source.values.begin(), source.values.end(), name,
                    [](const StyleStore::Value& value, const URUI::String& key) { return value.name < key; });
                if (existing == source.values.end() || existing->name != name ||
                    uint32_t(existing->kind & 255) != delta.kind || (delta.kind != 1 && delta.kind != 4))
                {
                    return nullptr;
                }
                for (float value : delta.values)
                {
                    if (!std::isfinite(value))
                    {
                        return nullptr;
                    }
                }
            }
            return changed;
        }

        void ApplyPrimitiveDelta(StyleStore& store, URUI::Element* element, StyleStore::Snapshot& target,
                                 uint64_t stamp,
                                 const std::remove_pointer_t<decltype(ValidatePrimitiveDelta(element, target))>& changed)
        {
            const auto& specification = element->GetPropertySpecification();
            bool modified = false;
            for (const auto& delta : changed)
            {
                const auto& name = specification.GetPropertyName(delta.id);
                auto existing = std::lower_bound(
                    target.values.begin(), target.values.end(), name,
                    [](const StyleStore::Value& value, const URUI::String& key) { return value.name < key; });
                const auto* values = delta.values;
                const int kind = static_cast<int>(delta.kind) | (OriginById(element, specification, delta.id) << 8);
                if (existing->x == values[0] && existing->y == values[1] && existing->z == values[2] &&
                    existing->w == values[3] && existing->kind == kind)
                {
                    continue;
                }
                modified = true;
                existing->kind = kind;
                existing->x = values[0];
                existing->y = values[1];
                existing->z = values[2];
                existing->w = values[3];
                auto& output = target.serialized[static_cast<size_t>(existing - target.values.begin())];
                output.kind = kind;
                output.x = values[0];
                output.y = values[1];
                output.z = values[2];
                output.w = values[3];
            }
            if (modified)
            {
                target.revision = ++store.nextRevision;
            }
            target.stamp = stamp;
            store.builds++;
        }

        StyleStore::Snapshot* GetOrBuildSnapshot(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t token)
        {
            auto* element = ResolveElement(surface, owner, token);
            if (!element || !surface.styles)
            {
                return nullptr;
            }
            auto& store = *surface.styles;
            auto& node = store.snapshots[token];
            node.owner = owner;
            auto* selected = &node.banks[node.current];
            selected->owner = owner;
            const auto stamp = element->GetStyle()->GetComputedRevision();
            const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
            auto* parent = element->GetParentNode();
            const auto parent_size = parent ? parent->GetBox().GetSize(URUI::BoxArea::Content) : URUI::Vector2f{};
            const bool sameSize = selected->width == size.x && selected->height == size.y &&
                                  selected->parentWidth == parent_size.x && selected->parentHeight == parent_size.y;
            if (selected->stamp == stamp && sameSize)
            {
                return selected;
            }

            // Fast incremental primitive delta path: only copy bank to alternate slot if pinned AND delta applies.
            if (sameSize)
            {
                if (const auto* changed = ValidatePrimitiveDelta(element, *selected))
                {
                    if (node.pins[node.current])
                    {
                        const uint32_t next = 1u - node.current;
                        if (node.pins[next])
                        {
                            throw std::runtime_error("both computed style versions are retained");
                        }
                        node.banks[next] = *selected;
                        node.current = next;
                        selected = &node.banks[next];
                    }
                    ApplyPrimitiveDelta(store, element, *selected, stamp, *changed);
                    return selected;
                }
            }

            // Full rebuild path: do NOT copy old bank before building new values.
            const auto& specification = element->GetPropertySpecification();
            const auto& sortedIds = store.SortedProperties(specification);
            std::vector<StyleStore::Value> values;
            values.reserve(sortedIds.size());
            bool hasUnregisteredCustom = false;
            bool hasUnregisteredExpressions = false;

            for (auto id : sortedIds)
            {
                const auto& name = specification.GetPropertyName(id);
                if (id == URUI::PropertyId::Display || id == URUI::PropertyId::Visibility)
                {
                    StyleStore::Value value;
                    value.name = name;
                    value.kind = 8 | (OriginById(element, specification, id) << 8);
                    ComputedStyleKeyword(&store, element, name.c_str(), value.text);
                    values.push_back(std::move(value));
                    continue;
                }
                if (id == URUI::PropertyId::FontSize || id == URUI::PropertyId::LineHeight ||
                    id == URUI::PropertyId::Opacity)
                {
                    StyleStore::Value value;
                    value.name = name;
                    value.kind = (id == URUI::PropertyId::Opacity ? 1 : 2) |
                                 (OriginById(element, specification, id) << 8);
                    value.x = id == URUI::PropertyId::FontSize     ? element->GetComputedValues().font_size()
                              : id == URUI::PropertyId::LineHeight ? element->GetComputedValues().line_height().value
                                                                   : element->GetComputedValues().opacity();
                    values.push_back(std::move(value));
                    continue;
                }
                if (id == URUI::PropertyId::Width || id == URUI::PropertyId::Height)
                {
                    StyleStore::Value value;
                    value.name = name;
                    value.kind = 2 | (OriginById(element, specification, id) << 8);
                    value.x = id == URUI::PropertyId::Width ? size.x : size.y;
                    values.push_back(std::move(value));
                    continue;
                }
                if (const auto* property = element->GetProperty(id))
                {
                    auto value = ReadStyleValue(store, element, name, *property, false);
                    if (value.kind)
                    {
                        value.kind |= OriginById(element, specification, id) << 8;
                        values.push_back(std::move(value));
                    }
                }
            }

            URUI::UnorderedSet<URUI::String> customNames;
            for (auto* ancestor = element; ancestor; ancestor = ancestor->GetParentNode())
            {
                ancestor->GetStyle()->AppendCustomPropertyNames(customNames);
            }
            bool appendedCustom = false;
            for (const auto& name : customNames)
            {
                if (specification.GetPropertyId(name) != URUI::PropertyId::Invalid)
                {
                    continue;
                }
                hasUnregisteredCustom = true;
                hasUnregisteredExpressions |= element->GetStyle()->HasCustomPropertyExpression(name);
                if (const auto* property = element->GetProperty(name))
                {
                    auto value = ReadStyleValue(store, element, name, *property, true);
                    if (value.kind)
                    {
                        value.kind |= OriginByName(element, name) << 8;
                        values.push_back(std::move(value));
                        appendedCustom = true;
                    }
                }
            }
            if (appendedCustom)
            {
                std::sort(values.begin(), values.end(),
                          [](const StyleStore::Value& a, const StyleStore::Value& b) { return a.name < b.name; });
            }
            store.builds++;

            // If values are completely identical to the currently selected snapshot, keep revision and avoid switching banks.
            if (selected->revision && values == selected->values)
            {
                selected->stamp = stamp;
                selected->width = size.x;
                selected->height = size.y;
                selected->parentWidth = parent_size.x;
                selected->parentHeight = parent_size.y;
                selected->hasUnregisteredCustom = hasUnregisteredCustom;
                selected->hasUnregisteredExpressions = hasUnregisteredExpressions;
                return selected;
            }

            // Only switch banks if the current bank is frozen; no copy of the old bank is needed.
            if (node.pins[node.current])
            {
                const uint32_t next = 1u - node.current;
                if (node.pins[next])
                {
                    throw std::runtime_error("both computed style versions are retained");
                }
                node.current = next;
                selected = &node.banks[next];
                selected->owner = owner;
            }
            selected->values = std::move(values);
            selected->revision = ++store.nextRevision;
            selected->hasUnregisteredCustom = hasUnregisteredCustom;
            selected->hasUnregisteredExpressions = hasUnregisteredExpressions;
            SerializeSnapshot(*selected);
            selected->stamp = stamp;
            selected->width = size.x;
            selected->height = size.y;
            selected->parentWidth = parent_size.x;
            selected->parentHeight = parent_size.y;
            return selected;
        }
    }

    std::unique_ptr<StyleStore, StyleStoreDeleter> CreateStyleStore()
    {
        return std::unique_ptr<StyleStore, StyleStoreDeleter>(new StyleStore());
    }

    void DestroyStyleStore(StyleStore* store) noexcept
    {
        delete store;
    }

    void ClearStyleStoreDocument(StyleStore* store, URUIDocumentHandle owner, URUI::ElementDocument* /*doc*/) noexcept
    {
        if (!store)
        {
            return;
        }
        // Release all frozen handles belonging to the closed document so slots never leak.
        for (auto& frozen : store->frozenSnapshots)
        {
            if (frozen.version != 0 && frozen.owner == owner)
            {
                store->ReleaseFrozen(frozen);
            }
        }
        for (auto it = store->snapshots.begin(); it != store->snapshots.end();)
        {
            if (it->second.owner == owner)
            {
                it = store->snapshots.erase(it);
            }
            else
            {
                ++it;
            }
        }
        // Invalidate cached PropertySpecification pointer to avoid ABA reuse after document teardown.
        store->cachedSpec = nullptr;
        store->cachedSpecRevision = 0;
        store->sortedPropertyIds.clear();
    }

    void EvictStyleStoreToken(StyleStore* store, uint64_t token) noexcept
    {
        if (!store)
        {
            return;
        }
        const auto found = store->snapshots.find(token);
        if (found != store->snapshots.end() && found->second.pins[0] == 0 && found->second.pins[1] == 0)
        {
            store->snapshots.erase(found);
        }
    }

    void PruneStyleStore(StyleStore* store, SurfaceContext& surface) noexcept
    {
        if (!store || !surface.documentRegistry || store->snapshots.empty())
        {
            return;
        }
        const uint64_t hitRevision = surface.context ? surface.context->GetHitTestRevision() : 0;
        if (hitRevision != 0 && store->lastPrunedHitTestRevision == hitRevision)
        {
            return;
        }
        store->lastPrunedHitTestRevision = hitRevision;
        for (auto it = store->snapshots.begin(); it != store->snapshots.end();)
        {
            auto* doc = ResolveDocument(surface, it->second.owner);
            const bool alive = doc && surface.elementReferences.Resolve(it->first, doc) != nullptr;
            if (!alive && it->second.pins[0] == 0 && it->second.pins[1] == 0)
            {
                it = store->snapshots.erase(it);
            }
            else
            {
                ++it;
            }
        }
    }

    void RecordStyleStringRead(StyleStore* store) noexcept
    {
        if (store)
        {
            store->strings++;
        }
    }

    bool ComputedStyleKeyword(StyleStore* store, URUI::Element* element, const char* name, std::string& value)
    {
        if (!store || !element || !name)
        {
            return false;
        }
        const auto id = element->GetPropertySpecification().GetPropertyId(name);
        if (id != URUI::PropertyId::Display && id != URUI::PropertyId::Visibility)
        {
            return false;
        }
        const auto* definition = element->GetPropertySpecification().GetProperty(id);
        auto property = *definition->GetDefaultValue();
        const auto& computed = element->GetComputedValues();
        property.value =
            id == URUI::PropertyId::Display ? static_cast<int>(computed.display()) : static_cast<int>(computed.visibility());
        value = store->KeywordText(name, property);
        return true;
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_style_api_version(void)
    {
        return 5;
    }

    int urui_style_frozen_api_version(void)
    {
        return 2;
    }

    int urui_style_snapshot_freeze(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                   const URUIFrozenStyleHandle* previous, URUIFrozenStyleHandle* output)
    try
    {
        if (!output)
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto prior = previous ? *previous : URUIFrozenStyleHandle{};
        *output = {};
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!surface->styles || !ResolveElement(*surface, owner, token))
        {
            return URUI_ERR_NOT_READY;
        }
        auto& store = *surface->styles;
        auto* old = previous ? store.FindFrozen(owner, &prior) : nullptr;
        if (previous && (!old || old->token != token))
        {
            return URUI_ERR_NOT_READY;
        }
        if (store.nextFrozenVersion == UINT64_MAX)
        {
            return URUI_ERR_INTERNAL;
        }
        if (old)
        {
            store.ReleaseFrozen(*old);
        }
        ActiveSurfaceScope scope(surface);
        auto* snapshot = GetOrBuildSnapshot(*surface, owner, token);
        if (!snapshot)
        {
            return URUI_ERR_NOT_READY;
        }
        auto& node = store.snapshots.at(token);
        if (node.pins[node.current] == UINT32_MAX)
        {
            return URUI_ERR_INTERNAL;
        }
        auto& scopes = store.frozenSnapshots;
        auto& idle = store.idleFrozenSnapshots;
        uint32_t index = 0;
        if (!idle.empty())
        {
            index = idle.back();
            idle.pop_back();
        }
        else
        {
            if (scopes.size() >= 65536)
            {
                return URUI_ERR_BUDGET;
            }
            if (scopes.size() == scopes.capacity())
            {
                const auto cap = std::max(size_t(16), scopes.size() * 2);
                scopes.reserve(cap);
                idle.reserve(cap);
            }
            index = static_cast<uint32_t>(scopes.size());
            scopes.emplace_back();
        }
        auto& frozen = scopes[index];
        frozen = {owner, token, ++store.nextFrozenVersion, node.current};
        node.pins[node.current]++;
        *output = {uint64_t(index) + 1, frozen.version};
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_style_snapshot_release(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                    const URUIFrozenStyleHandle* handle)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!surface->styles)
        {
            return URUI_ERR_NOT_READY;
        }
        auto* frozen = surface->styles->FindFrozen(owner, handle);
        if (!frozen)
        {
            return URUI_ERR_NOT_READY;
        }
        surface->styles->ReleaseFrozen(*frozen);
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_style_snapshot_copy(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                 const URUIFrozenStyleHandle* handle, uint64_t expected, URUIStyleValue* output,
                                 int capacity, char* names, int names_capacity, uint64_t* revision, int* value_count,
                                 int* name_bytes)
    try
    {
        if (capacity < 0 || names_capacity < 0 || !revision || !value_count || !name_bytes)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *revision = 0;
        *value_count = 0;
        *name_bytes = 0;
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!surface->styles)
        {
            return URUI_ERR_NOT_READY;
        }
        auto& store = *surface->styles;
        const auto* frozen = store.FindFrozen(owner, handle);
        if (!frozen)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto found = store.snapshots.find(frozen->token);
        if (found == store.snapshots.end() || found->second.owner != owner ||
            !ResolveElement(*surface, owner, frozen->token))
        {
            return URUI_ERR_NOT_READY;
        }
        const auto& snapshot = found->second.banks[frozen->bank];
        if (snapshot.serialized.size() > static_cast<size_t>(INT32_MAX) ||
            snapshot.text.size() > static_cast<size_t>(INT32_MAX))
        {
            return URUI_ERR_INTERNAL;
        }
        *revision = snapshot.revision;
        *value_count = static_cast<int>(snapshot.serialized.size());
        *name_bytes = static_cast<int>(snapshot.text.size());
        if (expected && expected != *revision)
        {
            return URUI_ERR_STALE_REVISION;
        }
        if (capacity < *value_count || names_capacity < *name_bytes || (!output && *value_count) ||
            (!names && *name_bytes))
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        if (*value_count)
        {
            std::memcpy(output, snapshot.serialized.data(), snapshot.serialized.size() * sizeof(URUIStyleValue));
        }
        if (*name_bytes)
        {
            std::memcpy(names, snapshot.text.data(), snapshot.text.size());
        }
        store.copies++;
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_style_snapshot(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                    uint64_t expected_revision, URUIStyleValue* output, int capacity, char* names,
                                    int names_capacity, uint64_t* revision, int* value_count, int* name_bytes)
    try
    {
        if (capacity < 0 || names_capacity < 0 || !revision || !value_count || !name_bytes)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *revision = 0;
        *value_count = 0;
        *name_bytes = 0;
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        auto* snapshot = GetOrBuildSnapshot(*surface, owner, token);
        if (!snapshot)
        {
            return URUI_ERR_NOT_READY;
        }
        *revision = snapshot->revision;
        *value_count = static_cast<int>(snapshot->serialized.size());
        *name_bytes = static_cast<int>(snapshot->text.size());
        if (expected_revision && expected_revision != *revision)
        {
            return URUI_ERR_STALE_REVISION;
        }
        if (capacity < *value_count || names_capacity < *name_bytes || (!output && *value_count) ||
            (!names && *name_bytes))
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        if (*value_count)
        {
            std::memcpy(output, snapshot->serialized.data(), snapshot->serialized.size() * sizeof(URUIStyleValue));
        }
        if (*name_bytes)
        {
            std::memcpy(names, snapshot->text.data(), snapshot->text.size());
        }
        surface->styles->copies++;
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_style_diagnostics(URUISurfaceHandle surfaceHandle, uint64_t* values, int capacity)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!values || capacity < 4)
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (!surface->styles)
        {
            return URUI_ERR_NOT_READY;
        }
        values[0] = surface->styles->builds;
        values[1] = surface->styles->strings;
        values[2] = surface->styles->copies;
        values[3] = surface->styles->snapshots.size();
        return URUI_OK;
    }
}
