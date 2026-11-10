#include "urui.h"
#include "style_properties.h"
#include <URUI/Core.h>
#include <URUI/Core/PropertyParser.h>
#include <URUI/Core/PropertyDefinition.h>
#include <URUI/Core/Decorator.h>
#include <URUI/Core/StyleSheetTypes.h>
#include "Engine/Source/Core/ElementStyle.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <unordered_map>
#include <vector>

namespace urui_styles {
struct Value {
    URUI::String name, text, source;
    int kind = 0;
    float x = 0, y = 0, z = 0, w = 0;
    bool operator==(const Value& other) const {
        return name == other.name && text == other.text && source == other.source && kind == other.kind && x == other.x && y == other.y && z == other.z && w == other.w;
    }
};
struct Snapshot {
    uint64_t owner = 0;
    uint64_t stamp = UINT64_MAX, revision = 0;
    float width = -1, height = -1;
    bool has_unregistered_custom = false;
    float parent_width = -1, parent_height = -1;
    std::vector<Value> values;
    std::vector<UruiStyleValue> serialized;
    std::vector<char> text;
};
static ElementResolver resolve = nullptr;
static std::unordered_map<uint64_t, Snapshot> snapshots;
static uint64_t next_revision = 0, builds = 0, strings = 0, copies = 0;
static std::unordered_map<const URUI::PropertyDefinition*, std::unordered_map<uint64_t, URUI::String>> keyword_text;
void Begin(ElementResolver resolver) { snapshots.clear(); keyword_text.clear(); resolve = resolver; builds = strings = copies = 0; }
void End() { snapshots.clear(); keyword_text.clear(); resolve = nullptr; }
void RecordStringRead() { strings++; }
static URUI::String KeywordText(const URUI::String& name, const URUI::Property& property) {
    // 标准属性定义在引擎会话内固定；仅缓存有限的内置枚举，不持有文档的自定义注册。
    if (name.rfind("--", 0) == 0) return property.ToString();
    auto& values = keyword_text[property.definition];
    const uint64_t key = uint64_t(uint32_t(property.parser_index)) << 32 | uint32_t(property.Get<int>());
    auto found = values.find(key);
    if (found == values.end()) found = values.emplace(key, property.ToString()).first;
    return found->second;
}
bool ComputedKeyword(URUI::Element* element, const char* name, std::string& value) {
    const auto id = element->GetPropertySpecification().GetPropertyId(name);
    if (id != URUI::PropertyId::Display && id != URUI::PropertyId::Visibility) return false;
    const auto* definition = element->GetPropertySpecification().GetProperty(id);
    auto property = *definition->GetDefaultValue();
    const auto& computed = element->GetComputedValues();
    property.value = id == URUI::PropertyId::Display ? int(computed.display()) : int(computed.visibility());
    value = KeywordText(name, property);
    return true;
}
void Prune() {
    for (auto it = snapshots.begin(); it != snapshots.end();) { if (!resolve || !resolve(it->second.owner, it->first)) it = snapshots.erase(it); else ++it; }
}
static float Base(URUI::Element* element, const URUI::PropertyDefinition* definition) {
    if (!definition) return 0;
    auto* parent = element->GetParentNode();
    switch (definition->GetRelativeTarget()) {
    case URUI::RelativeTarget::ContainingBlockWidth: return parent ? parent->GetBox().GetSize(URUI::BoxArea::Content).x : 0;
    case URUI::RelativeTarget::ContainingBlockHeight: return parent ? parent->GetBox().GetSize(URUI::BoxArea::Content).y : 0;
    case URUI::RelativeTarget::FontSize: return element->GetComputedValues().font_size();
    case URUI::RelativeTarget::ParentFontSize: return parent ? parent->GetComputedValues().font_size() : element->GetComputedValues().font_size();
    case URUI::RelativeTarget::LineHeight: return element->GetComputedValues().line_height().value;
    default: return 0;
    }
}
static Value Read(URUI::Element* element, const URUI::String& name, const URUI::Property& property, bool custom_unregistered) {
    Value result; result.name = name;
    if (property.unit == URUI::Unit::NUMBER) { result.kind = 1; result.x = property.Get<float>(); }
    else if (URUI::Any(property.unit & URUI::Unit::LENGTH)) { result.kind = 2; result.x = element->ResolveLength(property.GetNumericValue()); }
    else if (property.unit == URUI::Unit::PERCENT && property.definition && property.definition->GetRelativeTarget() != URUI::RelativeTarget::None) {
        result.kind = 2; result.x = element->ResolveNumericValue(property.GetNumericValue(), Base(element, property.definition));
    }
    else if (property.unit == URUI::Unit::COLOUR) {
        const auto color = property.Get<URUI::Colourb>(); result.kind = 4;
        result.x = color.red / 255.f; result.y = color.green / 255.f; result.z = color.blue / 255.f; result.w = color.alpha / 255.f;
    }
    else if (property.unit == URUI::Unit::DECORATOR) {
        const auto declarations = property.Get<URUI::DecoratorsPtr>();
        if (declarations && declarations->layer_count != 1) return result;
        result.kind = 16;
        result.source = property.source ? property.source->path : element->GetOwnerDocument() ? element->GetOwnerDocument()->GetSourceURL() : "";
        if (!declarations || declarations->list.empty()) return result;
        const auto& declaration = declarations->list.front();
        if (declaration.type == "urui-css-image") {
            const auto id = declaration.instancer->GetPropertySpecification().GetPropertyId("image-src");
            const auto* source = declaration.properties.GetProperty(id);
            if (!source) return Value{};
            result.x = 1; result.text = source->Get<URUI::String>();
        } else {
            // 仅原生 CSS image 解析器已验证过的标准渐变进入图像快照；旧装饰器不能冒充 image。
            if (declaration.type.find("gradient") == URUI::String::npos) return Value{};
            result.x = 2; result.text = declarations->value;
        }
    }
    else if (custom_unregistered && property.unit == URUI::Unit::STRING) {
        const auto text = property.Get<URUI::String>(); URUI::Property parsed;
        auto* number = URUI::StyleSheetSpecification::GetParser("number");
        auto* length = URUI::StyleSheetSpecification::GetParser("length");
        auto* color = URUI::StyleSheetSpecification::GetParser("color");
        if (number && number->ParseValue(parsed, text, {})) { result.kind = 1; result.x = parsed.Get<float>(); if (result.x == 0) result.kind |= 2; }
        else if (length && length->ParseValue(parsed, text, {}) && (parsed.unit == URUI::Unit::PX || parsed.unit == URUI::Unit::DP)) {
            result.kind = 2; result.x = element->ResolveLength(parsed.GetNumericValue());
        }
        else if (color && color->ParseValue(parsed, text, {})) result = Read(element, name, parsed, false);
        else {
            auto* image = URUI::StyleSheetSpecification::GetParser("css_image");
            if (text != "none" && image && image->ParseValue(parsed, text, {{"effect", 1}})) {
                parsed.source = property.source; result = Read(element, name, parsed, false);
            } else { result.kind=8;result.text=text; }
        }
    }
    else if (property.unit == URUI::Unit::STRING) { result.kind=8; result.text=property.Get<URUI::String>(); }
    else if (property.unit == URUI::Unit::KEYWORD && property.definition) { result.kind=8; result.text=KeywordText(name, property); }
    if (!std::isfinite(result.x) || !std::isfinite(result.y) || !std::isfinite(result.z) || !std::isfinite(result.w)) result.kind = 0;
    return result;
}
static int Origin(URUI::Element* element,const URUI::String& name) {
    const auto& specification=element->GetPropertySpecification();
    const auto id=specification.GetPropertyId(name);
    const auto* definition=specification.GetProperty(id);
    const bool inherited=definition ? definition->IsInherited() : name.rfind("--",0)==0;
    for(auto* current=element;current;current=inherited?current->GetParentNode():nullptr) {
        if(const auto* property=id == URUI::PropertyId::Invalid ? current->GetLocalProperty(name) : current->GetLocalProperty(id)) {
            switch(property->priority.originImportance) {
            case URUI::CssOriginImportance::UserAgentNormal:return 1;
            case URUI::CssOriginImportance::AuthorNormal:return 2;
            case URUI::CssOriginImportance::Animation:return 3;
            case URUI::CssOriginImportance::AuthorImportant:
            case URUI::CssOriginImportance::UserAgentImportant:return 4;
            case URUI::CssOriginImportance::Transition:return 5;
            }
        }
    }
    return 0;
}
static void Serialize(Snapshot& snapshot) {
    auto& text = snapshot.text;
    auto& output = snapshot.serialized;
    text.clear(); output.clear();
    output.reserve(snapshot.values.size());
    const auto append = [&text](const URUI::String& value) {
        const int offset = int(text.size());
        text.insert(text.end(), value.c_str(), value.c_str() + value.size() + 1);
        return offset;
    };
    for (const auto& value : snapshot.values) {
        const int kind = value.kind & 255;
        const int name = append(value.name);
        const int content = kind == 8 || kind == 16 ? append(value.text) : -1;
        const int source = kind == 16 ? append(value.source) : -1;
        output.push_back({name, value.kind, value.x, value.y, value.z, value.w, content, source});
    }
}
static bool UpdateNumbers(URUI::Element* element, Snapshot& snapshot, uint64_t stamp) {
    if (!snapshot.revision || snapshot.has_unregistered_custom) return false;
    const auto* changed = element->GetStyle()->GetSnapshotNumericChanges(snapshot.stamp);
    if (!changed) return false;
    const auto& specification = element->GetPropertySpecification();
    std::vector<std::pair<size_t, Value>> updates;
    for (auto id : *changed) {
        const auto& name = specification.GetPropertyName(id);
        auto existing = std::lower_bound(snapshot.values.begin(), snapshot.values.end(), name,
            [](const Value& value, const URUI::String& key) { return value.name < key; });
        if (existing == snapshot.values.end() || existing->name != name || (existing->kind & 255) != 1) return false;
        const auto* property = element->GetProperty(id);
        if (!property) return false;
        auto value = Read(element, name, *property, false);
        if (value.kind != 1) return false;
        value.kind |= Origin(element, name) << 8;
        updates.emplace_back(size_t(existing - snapshot.values.begin()), std::move(value));
    }
    bool modified = false;
    for (auto& update : updates) {
        auto& previous = snapshot.values[update.first];
        if (previous == update.second) continue;
        modified = true; previous = std::move(update.second);
        auto& output = snapshot.serialized[update.first];
        output.kind = previous.kind; output.x = previous.x; output.y = previous.y; output.z = previous.z; output.w = previous.w;
    }
    if (modified) snapshot.revision = ++next_revision;
    snapshot.stamp = stamp; builds++;
    return true;
}
static Snapshot* Get(uint64_t owner, uint64_t token) {
    auto* element = resolve ? resolve(owner, token) : nullptr;
    if (!element) return nullptr;
    auto& snapshot = snapshots[token];
    snapshot.owner = owner;
    const auto stamp = element->GetStyle()->GetComputedRevision();
    const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
    auto* parent = element->GetParentNode();
    const auto parent_size = parent ? parent->GetBox().GetSize(URUI::BoxArea::Content) : URUI::Vector2f{};
    const bool same_parent = snapshot.parent_width == parent_size.x && snapshot.parent_height == parent_size.y;
    // 相对长度随包含块变化；即使本节点只有数值参数变化，也需要重新求值。
    if (snapshot.stamp == stamp && snapshot.width == size.x && snapshot.height == size.y && same_parent) return &snapshot;
    if (snapshot.width == size.x && snapshot.height == size.y && same_parent && UpdateNumbers(element, snapshot, stamp)) return &snapshot;
    std::vector<Value> values;
    snapshot.has_unregistered_custom = false;
    const auto& specification = element->GetPropertySpecification();
    for (auto id : specification.GetRegisteredProperties()) {
        const auto& name = specification.GetPropertyName(id);
        if (id == URUI::PropertyId::Display || id == URUI::PropertyId::Visibility) {
            Value value; value.name = name; value.kind = 8; ComputedKeyword(element, name.c_str(), value.text);
            values.push_back(std::move(value)); continue;
        }
        if (id == URUI::PropertyId::FontSize || id == URUI::PropertyId::LineHeight || id == URUI::PropertyId::Opacity) {
            Value value; value.name = name; value.kind = id == URUI::PropertyId::Opacity ? 1 : 2;
            value.x = id == URUI::PropertyId::FontSize ? element->GetComputedValues().font_size()
                : id == URUI::PropertyId::LineHeight ? element->GetComputedValues().line_height().value : element->GetComputedValues().opacity();
            values.push_back(value); continue;
        }
        if (id == URUI::PropertyId::Width || id == URUI::PropertyId::Height) {
            Value value; value.name = name; value.kind = 2; value.x = id == URUI::PropertyId::Width ? size.x : size.y; values.push_back(value); continue;
        }
        if (const auto* property = element->GetProperty(id)) {
            auto value = Read(element, name, *property, false);
            if (value.kind) {
                values.push_back(std::move(value));
            }
        }
    }
    URUI::UnorderedSet<URUI::String> names;
    for (auto* ancestor = element; ancestor; ancestor = ancestor->GetParentNode()) ancestor->GetStyle()->AppendCustomPropertyNames(names);
    for (const auto& name : names) {
        if (specification.GetPropertyId(name) != URUI::PropertyId::Invalid) continue;
        snapshot.has_unregistered_custom = true;
        if (const auto* property = element->GetProperty(name)) {
            auto value = Read(element, name, *property, true); if (value.kind) values.push_back(std::move(value));
        }
    }
    for(auto& value:values)value.kind|=Origin(element,value.name)<<8;
    std::sort(values.begin(), values.end(), [](const Value& a, const Value& b) { return a.name < b.name; });
    builds++;
    if (!snapshot.revision || values != snapshot.values) { snapshot.values = std::move(values); snapshot.revision = ++next_revision; Serialize(snapshot); }
    snapshot.stamp = stamp; snapshot.width = size.x; snapshot.height = size.y;
    snapshot.parent_width = parent_size.x; snapshot.parent_height = parent_size.y;
    return &snapshot;
}
}
extern "C" {
int urui_style_api_version() { return 4; }
int urui_element_style_snapshot(UruiDocumentHandle document_handle, uint64_t token, uint64_t expected_revision, UruiStyleValue* output, int capacity, char* names, int names_capacity,
    uint64_t* revision, int* value_count, int* name_bytes) try {
    if (capacity < 0 || names_capacity < 0 || !revision || !value_count || !name_bytes) return -1;
    auto* snapshot = urui_styles::Get(document_handle, token); if (!snapshot) return -1;
    *revision = snapshot->revision;
    *value_count = int(snapshot->serialized.size());
    *name_bytes = int(snapshot->text.size());
    if (expected_revision && expected_revision != *revision) return -4;
    if (capacity < *value_count || names_capacity < *name_bytes || (!output && *value_count) || (!names && *name_bytes)) return -3;
    if (*value_count) std::memcpy(output, snapshot->serialized.data(), snapshot->serialized.size() * sizeof(UruiStyleValue));
    if (*name_bytes) std::memcpy(names, snapshot->text.data(), snapshot->text.size());
    urui_styles::copies++; return 1;
} catch (...) { return -8; }
int urui_style_diagnostics(uint64_t* values, int capacity) {
    if (!values || capacity < 4) return -1;
    values[0] = urui_styles::builds; values[1] = urui_styles::strings; values[2] = urui_styles::copies; values[3] = urui_styles::snapshots.size(); return 1;
}
}
