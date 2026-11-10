#pragma once
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/Property.h"
#include "../../Include/URUI/Core/PropertyDictionary.h"
#include "../../Include/URUI/Core/PropertySpecification.h"
#include "../../Include/URUI/Core/Filter.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/StyleSheetTypes.h"

namespace URUI {
inline bool DirectColorFilterRenderingEnabled = false;
// 只放行已能证明等价的独立图片；复杂绘制继续使用原来的完整子树滤镜路径。
inline FiltersPtr DirectColorFilters(Element* element) {
    if (!DirectColorFilterRenderingEnabled || !element || element->GetTagName() != "img" || element->GetNumChildren(true) != 0) return {};
    const auto& c = element->GetComputedValues();
    if (!c.has_filter() || c.has_backdrop_filter() || c.has_mask_image() || c.has_decorator() || c.has_box_shadow() ||
        c.opacity() != 1 || c.background_color().alpha != 0 || c.border_top_width() || c.border_right_width() ||
        c.border_bottom_width() || c.border_left_width() || c.border_top_left_radius() || c.border_top_right_radius() ||
        c.border_bottom_left_radius() || c.border_bottom_right_radius()) return {};
    for (const auto* name : {"--urui-image-effect", "--urui-effect"}) {
        const auto* property = element->GetProperty(name);
        if (property && property->unit == Unit::STRING) {
            const auto value = property->Get<String>(); if (!value.empty() && value != "none") return {};
        }
    }
    for (auto id : {PropertyId::BackgroundImage, PropertyId::BorderImageSource}) {
        const auto* property = element->GetProperty(id);
        if (property && property->unit == Unit::DECORATOR) {
            const auto value = property->Get<DecoratorsPtr>(); if (value && !value->list.empty()) return {};
        }
    }
    const auto* property = element->GetProperty(PropertyId::Filter);
    if (!property || property->unit != Unit::FILTER) return {};
    auto filters = property->Get<FiltersPtr>();
    if (!filters || filters->list.empty() || filters->list.size() > 16) return {};
    for (const auto& declaration : filters->list) {
        const auto& name = declaration.type;
        if (name != "grayscale" && name != "saturate" && name != "brightness" && name != "contrast" && name != "opacity") return {};
        if (!declaration.instancer || !declaration.properties.GetProperty(
            declaration.instancer->GetPropertySpecification().GetPropertyId("value"))) return {};
    }
    return filters;
}
}
