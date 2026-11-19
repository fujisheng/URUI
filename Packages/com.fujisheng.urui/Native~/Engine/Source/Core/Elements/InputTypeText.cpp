#include "InputTypeText.h"
#include "../../../Include/URUI/Core/ElementUtilities.h"
#include "../../../Include/URUI/Core/Elements/ElementFormControlInput.h"
#include "../../../Include/URUI/Core/PropertyIdSet.h"
#include "../../../Include/URUI/Core/StyleSheetSpecification.h"
#include "../../../Include/URUI/Core/PropertyDefinition.h"
#include "WidgetTextInputSingleLine.h"
#include "WidgetTextInputSingleLinePassword.h"

namespace URUI
{

    InputTypeText::InputTypeText(ElementFormControlInput* element, Visibility visibility, bool number)
        : InputType(element), number(number)
    {
        if (visibility == VISIBLE)
        {
            widget = MakeUnique<WidgetTextInputSingleLine>(element);
        }
        else
        {
            widget = MakeUnique<WidgetTextInputSingleLinePassword>(element);
        }
    }

    InputTypeText::~InputTypeText()
    {
    }

    String InputTypeText::GetValue() const
    {
        return number ? number_value : InputType::GetValue();
    }
    void InputTypeText::SetValue(const String& value)
    {
        if (!number)
        {
            InputType::SetValue(value);
            return;
        }
        dirty_value = true;
        number_value = value;
        widget->OnValueAttributeChanged(value);
    }

    void InputTypeText::OnUpdate()
    {
        widget->OnUpdate();
    }

    void InputTypeText::OnRender()
    {
        widget->OnRender();
    }

    void InputTypeText::OnResize()
    {
        widget->OnResize();
    }

    void InputTypeText::OnLayout()
    {
        widget->OnLayout();
    }

    bool InputTypeText::OnAttributeChange(const ElementAttributes& changed_attributes)
    {
        bool dirty_layout = false;

        auto it = changed_attributes.find("maxlength");
        if (it != changed_attributes.end())
        {
            widget->SetMaxLength(it->second.Get(-1));
        }

        it = changed_attributes.find("size");
        if (it != changed_attributes.end())
        {
            size = it->second.Get(20);
            dirty_layout = true;
        }

        it = changed_attributes.find("value");
        if (it != changed_attributes.end() && (!number || !dirty_value))
        {
            if (number)
            {
                number_value = it->second.Get<String>();
            }
            widget->OnValueAttributeChanged(it->second.Get<String>());
        }

        it = changed_attributes.find("placeholder");
        if (it != changed_attributes.end())
        {
            widget->OnPlaceholderAttributeChanged(it->second.Get<String>());
        }

        return !dirty_layout;
    }

    void InputTypeText::OnPropertyChange(const PropertyIdSet& changed_properties)
    {
        // Some inherited properties require text formatting update, mainly font and line-height properties.
        const PropertyIdSet changed_inherited_layout_properties =
            changed_properties & (element->GetPropertySpecification().GetRegisteredInheritedProperties() &
                                  element->GetPropertySpecification().GetRegisteredPropertiesForcingLayout());

        if (!changed_inherited_layout_properties.Empty())
        {
            widget->ForceFormattingOnNextLayout();
        }

        if (changed_properties.Contains(PropertyId::Color) || changed_properties.Contains(PropertyId::BackgroundColor))
        {
            widget->UpdateSelectionColours();
        }

        bool placeholder_changed = changed_properties.Contains(PropertyId::Color);
        for (const char* name : {"--urui-placeholder-opacity", "--urui-placeholder-italic"})
        {
            const auto* definition = StyleSheetSpecification::GetProperty(name);
            if (definition && changed_properties.Contains(definition->GetId()))
            {
                placeholder_changed = true;
            }
        }
        if (placeholder_changed)
        {
            widget->UpdatePlaceholderStyle();
        }
        if (changed_properties.Contains(PropertyId::CaretColor))
        {
            widget->GenerateCursor();
        }
    }

    void InputTypeText::ProcessDefaultAction(Event& /*event*/)
    {
    }

    bool InputTypeText::GetIntrinsicDimensions(Vector2f& dimensions, float& /*ratio*/)
    {
        dimensions.x = (float)(size * ElementUtilities::GetStringWidth(element, "m"));
        dimensions.y = widget->GetLineHeight();
        return true;
    }

    void InputTypeText::Select()
    {
        widget->Select();
    }

    void InputTypeText::SetSelectionRange(int selection_start, int selection_end)
    {
        widget->SetSelectionRange(selection_start, selection_end);
    }

    void InputTypeText::GetSelection(int* selection_start, int* selection_end, String* selected_text) const
    {
        widget->GetSelection(selection_start, selection_end, selected_text);
    }

    void InputTypeText::SetCompositionRange(int range_start, int range_end)
    {
        widget->SetCompositionRange(range_start, range_end);
    }

}
