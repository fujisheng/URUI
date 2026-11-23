#pragma once
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/ID.h"
#include "../../Include/URUI/Core/PropertyIdSet.h"

// 图形自身的状态倍率；不继承到子图形，也不参与遮罩输入。
namespace URUI::Graphics
{
    inline bool Changed(const URUI::PropertyIdSet& changed)
    {
        return changed.Contains(URUI::PropertyId::GraphicTint);
    }

    inline URUI::Colourb Tint(URUI::Element& element, URUI::Colourb color)
    {
        const auto* property = element.GetProperty(URUI::PropertyId::GraphicTint);
        if (!property || property->unit != URUI::Unit::COLOUR)
        {
            return color;
        }
        const auto tint = property->Get<URUI::Colourb>();
        if (tint == URUI::Colourb(255))
        {
            return color;
        }
        color.red = uint8_t((int(color.red) * tint.red + 127) / 255);
        color.green = uint8_t((int(color.green) * tint.green + 127) / 255);
        color.blue = uint8_t((int(color.blue) * tint.blue + 127) / 255);
        color.alpha = uint8_t((int(color.alpha) * tint.alpha + 127) / 255);
        return color;
    }
}
