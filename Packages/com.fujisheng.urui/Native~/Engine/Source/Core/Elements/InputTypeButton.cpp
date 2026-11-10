#include "InputTypeButton.h"
#include "../../../Include/URUI/Core/Elements/ElementForm.h"
#include "../../../Include/URUI/Core/Elements/ElementFormControlInput.h"
#include "../../../Include/URUI/Core/Factory.h"

namespace URUI {

InputTypeButton::InputTypeButton(ElementFormControlInput* element) : InputType(element) {}

InputTypeButton::~InputTypeButton() {}

bool InputTypeButton::IsSubmitted()
{
	// Buttons are never submitted.
	return false;
}

bool InputTypeButton::OnAttributeChange(const ElementAttributes& changed_attributes)
{
	if (changed_attributes.find("value") != changed_attributes.end())
	{
		auto value = element->GetAttribute<String>("value", "");
		if (!value.empty() && !value_element)
			value_element =
				urui_static_cast<ElementText*>(element->AppendChild(Factory::InstanceElement(element, "#text", "", ElementAttributes()), true));

		if (value_element)
			value_element->SetText(value);

		return false;
	}
	return true;
}

void InputTypeButton::ProcessDefaultAction(Event& /*event*/) {}

bool InputTypeButton::GetIntrinsicDimensions(Vector2f& /*dimensions*/, float& /*ratio*/)
{
	return false;
}

} // namespace URUI
