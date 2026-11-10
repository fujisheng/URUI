#include "InputTypeRange.h"
#include "../../../Include/URUI/Core/ElementUtilities.h"
#include "../../../Include/URUI/Core/Elements/ElementFormControlInput.h"
#include "WidgetSlider.h"
#include <cmath>
#include <cstdlib>
#include <limits>

namespace URUI {

namespace {
// HTML numeric controls use the HTML floating-point syntax, not CSS dimensions.
bool ParseNumber(const String& text, float& value)
{
    size_t i = 0;
    if (i < text.size() && text[i] == '-') ++i;
    const auto integer = i;
    while (i < text.size() && text[i] >= '0' && text[i] <= '9') ++i;
    bool digits = i != integer;
    if (i < text.size() && text[i] == '.') {
        const auto fraction = ++i;
        while (i < text.size() && text[i] >= '0' && text[i] <= '9') ++i;
        if (i == fraction) return false;
        digits = true;
    }
    if (!digits) return false;
    if (i < text.size() && (text[i] == 'e' || text[i] == 'E')) {
        ++i;
        if (i < text.size() && (text[i] == '-' || text[i] == '+')) ++i;
        const auto exponent = i;
        while (i < text.size() && text[i] >= '0' && text[i] <= '9') ++i;
        if (i == exponent) return false;
    }
    if (i != text.size()) return false;
    char* end = nullptr; value = std::strtof(text.c_str(), &end);
    return end == text.c_str() + text.size() && std::isfinite(value);
}
}

InputTypeRange::InputTypeRange(ElementFormControlInput* element) : InputType(element)
{
	widget = new WidgetSlider(element);
	widget->Initialise();
}

InputTypeRange::~InputTypeRange()
{
	delete widget;
}

String InputTypeRange::GetValue() const
{
	return CreateString("%f", widget->GetValue());
}

void InputTypeRange::SetValue(const String& text)
{
    float value;
    widget->SetValue(ParseNumber(text, value) ? value : std::numeric_limits<float>::quiet_NaN());
}
RangeControlInfo InputTypeRange::GetRangeInfo() const { return widget->GetRangeInfo(); }

void InputTypeRange::OnUpdate()
{
	widget->Update();
}

void InputTypeRange::OnResize()
{
	widget->FormatElements();
}

bool InputTypeRange::OnAttributeChange(const ElementAttributes& changed_attributes)
{
	bool dirty_layout = false;

	auto it_orientation = changed_attributes.find("data-urui-orientation");
	if (it_orientation != changed_attributes.end())
	{
		bool is_vertical = (it_orientation->second.Get<String>() == "vertical");
		widget->SetOrientation(is_vertical ? WidgetSlider::VERTICAL : WidgetSlider::HORIZONTAL);
		dirty_layout = true;
	}

    if (!initialized || changed_attributes.count("min") || changed_attributes.count("max") ||
        changed_attributes.count("step") || changed_attributes.count("value") || changed_attributes.count("data-urui-exact-range")) {
        float minimum = 0, maximum = 100, step = 1, authored_value = 0;
        const bool has_minimum = ParseNumber(element->GetAttribute<String>("min", ""), minimum);
        if (!has_minimum) minimum = 0;
        if (!ParseNumber(element->GetAttribute<String>("max", ""), maximum)) maximum = 100;
        maximum = std::max(minimum, maximum);
        const auto step_text = element->GetAttribute<String>("step", "");
        if (!ParseNumber(step_text, step) || step <= 0) step = 1;
        const bool valid_value = ParseNumber(element->GetAttribute<String>("value", ""), authored_value);
        const float target = !initialized || changed_attributes.count("value") ?
            (valid_value ? authored_value : std::numeric_limits<float>::quiet_NaN()) : widget->GetValue();
        const float base = has_minimum ? minimum : valid_value ? authored_value : 0;
        const int flags = (StringUtilities::ToLower(step_text) == "any" ? 1 : 0) | (element->HasAttribute("data-urui-exact-range") ? 2 : 0);
        initialized = true;
        widget->Configure({minimum, maximum, step, base, flags}, target);
    }

	return !dirty_layout;
}

void InputTypeRange::ProcessDefaultAction(Event& /*event*/) {}

bool InputTypeRange::GetIntrinsicDimensions(Vector2f& dimensions, float& /*ratio*/)
{
	widget->GetDimensions(dimensions);
	dimensions *= ElementUtilities::GetDensityIndependentPixelRatio(element);
	return true;
}

} // namespace URUI
