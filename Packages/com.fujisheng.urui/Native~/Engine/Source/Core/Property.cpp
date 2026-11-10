#include "../../Include/URUI/Core/Property.h"
#include "../../Include/URUI/Core/PropertyDefinition.h"

namespace URUI {

Property::Property() : unit(Unit::UNKNOWN)
{
	definition = nullptr;
	parser_index = -1;
}

String Property::ToString() const
{
	if (unit == Unit::CSS_WIDE) {
		switch (Get<CssWideKeyword>()) {
		case CssWideKeyword::Inherit: return "inherit";
		case CssWideKeyword::Initial: return "initial";
		case CssWideKeyword::Unset: return "unset";
		}
	}
	if (!definition)
		return value.Get<String>() + URUI::ToString(unit);

	String string;
	definition->GetValue(string, *this);
	return string;
}

NumericValue Property::GetNumericValue() const
{
	NumericValue result;
	if (Any(unit & Unit::NUMERIC))
	{
		if (value.GetInto(result.number))
			result.unit = unit;
	}
	return result;
}

} // namespace URUI
