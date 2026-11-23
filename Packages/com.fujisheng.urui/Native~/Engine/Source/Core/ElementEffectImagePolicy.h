#pragma once
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/Property.h"
#include "../../Include/URUI/Core/StyleSheetTypes.h"

namespace URUI
{
    inline bool DirectElementEffectImage(Element* element)
    {
        if (element->IsEffectCaptureRequired())
        {
            return false;
        }
        if (element->GetTagName() != "img")
        {
            return false;
        }
        const auto* image = element->GetProperty(PropertyId::ImageEffect);
        const auto image_name = image ? image->Get<String>() : String();
        if (!image_name.empty() && image_name != "none")
        {
            return false;
        }
        const auto& computed = element->GetComputedValues();
        if (computed.has_decorator() || computed.has_box_shadow() || computed.background_color().alpha != 0 ||
            computed.border_top_width() || computed.border_right_width() || computed.border_bottom_width() ||
            computed.border_left_width() || computed.border_top_left_radius() || computed.border_top_right_radius() ||
            computed.border_bottom_left_radius() || computed.border_bottom_right_radius())
        {
            return false;
        }
        for (const auto id : {PropertyId::BackgroundImage, PropertyId::BorderImageSource})
        {
            const auto* property = element->GetProperty(id);
            if (property && property->unit == Unit::DECORATOR)
            {
                const auto decorators = property->Get<DecoratorsPtr>();
                if (decorators && !decorators->list.empty())
                {
                    return false;
                }
            }
        }
        const auto* effect = element->GetProperty(PropertyId::ElementEffect);
        auto* document = element->GetOwnerDocument();
        if (effect && document)
        {
            if (const auto& resources = document->GetResourceContext())
            {
                if (const auto* condition = resources->GetEffectImageCondition(effect->Get<String>()))
                {
                    const auto* value = element->GetProperty(condition->property);
                    if (!value || value->ToString() != condition->direct_value)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }
}
