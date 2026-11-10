#include "WidgetTextInputSingleLine.h"
#include "../../../Include/URUI/Core/Dictionary.h"
#include "../../../Include/URUI/Core/ElementText.h"
#include "../../../Include/URUI/Core/Elements/ElementFormControl.h"
#include <algorithm>

namespace URUI {

WidgetTextInputSingleLine::WidgetTextInputSingleLine(ElementFormControl* parent) : WidgetTextInput(parent)
{
	// Single line text controls should clip to the content area, see visual test: text_input_overflow.html
	parent->SetClipArea(BoxArea::Content);
}

void WidgetTextInputSingleLine::SanitizeValue(String& value)
{
	value.erase(std::remove_if(value.begin(), value.end(), [](char c) { return c == '\r' || c == '\n' || c == '\t'; }), value.end());
}

void WidgetTextInputSingleLine::LineBreak()
{
	DispatchChangeEvent(true);
}

} // namespace URUI
