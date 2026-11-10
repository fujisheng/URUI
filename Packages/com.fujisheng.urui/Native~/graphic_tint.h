#pragma once
#include <URUI/Core/Element.h>
#include <URUI/Core/PropertyDefinition.h>
#include <URUI/Core/PropertyIdSet.h>
#include <URUI/Core/StyleSheetSpecification.h>

// 图形自身的状态倍率；不继承到子图形，也不参与遮罩输入。
namespace urui_graphic {
inline bool Changed(const URUI::PropertyIdSet& changed) {
    const auto* definition = URUI::StyleSheetSpecification::GetProperty("--urui-graphic-tint");
    return definition && changed.Contains(definition->GetId());
}
inline URUI::Colourb Tint(URUI::Element& element, URUI::Colourb color) {
    const auto* definition = URUI::StyleSheetSpecification::GetProperty("--urui-graphic-tint");
    if (!definition) return color;
    const auto* property = element.GetProperty(definition->GetId());
    if (!property || property->unit != URUI::Unit::COLOUR) return color;
    const auto tint = property->Get<URUI::Colourb>();
    if (tint == URUI::Colourb(255)) return color;
    color.red = uint8_t((int(color.red) * tint.red + 127) / 255);
    color.green = uint8_t((int(color.green) * tint.green + 127) / 255);
    color.blue = uint8_t((int(color.blue) * tint.blue + 127) / 255);
    color.alpha = uint8_t((int(color.alpha) * tint.alpha + 127) / 255);
    return color;
}
}
