#include "CssTokenizer.h"
#include "PropertyParserNumber.h"
#include <cmath>
#include <stdlib.h>

namespace URUI {

struct PropertyParserNumberData {
	const UnorderedMap<String, Unit> unit_string_map = {
		{"", Unit::NUMBER},
		{"%", Unit::PERCENT},
		{"px", Unit::PX},
		{"vw", Unit::VW},
		{"vh", Unit::VH},
		{"em", Unit::EM},
		{"rem", Unit::REM},
		{"in", Unit::INCH},
		{"cm", Unit::CM},
		{"mm", Unit::MM},
		{"pt", Unit::PT},
		{"pc", Unit::PC},
		{"deg", Unit::DEG},
		{"rad", Unit::RAD},
	};
};

ControlledLifetimeResource<PropertyParserNumberData> PropertyParserNumber::parser_data;

void PropertyParserNumber::Initialize()
{
	parser_data.Initialize();
}

void PropertyParserNumber::Shutdown()
{
	if (parser_data) parser_data.Shutdown();
}

PropertyParserNumber::PropertyParserNumber(Units units, Unit zero_unit) : units(units), zero_unit(zero_unit) {}

PropertyParserNumber::~PropertyParserNumber() {}

bool PropertyParserNumber::ParseValue(Property& property, const String& value, const ParameterMap& parameters) const
{
	String str_number, str_unit;
	if (!SplitCssNumericValue(value, str_number, str_unit)) return false;

	char* str_end = nullptr;
	float float_value = strtof(str_number.c_str(), &str_end);
	if (str_number.c_str() == str_end || *str_end != 0 || !std::isfinite(float_value))
	{
		// Number conversion failed
		return false;
	}

	const auto it = parser_data->unit_string_map.find(str_unit);
	if (it == parser_data->unit_string_map.end())
	{
		// Invalid unit name
		return false;
	}

	const Unit unit = it->second;
	const auto minimum = parameters.find("min");
	const auto maximum = parameters.find("max");
	if ((minimum != parameters.end() && float_value < minimum->second) ||
		(maximum != parameters.end() && float_value > maximum->second)) return false;

	if (Any(unit & units))
	{
		property.value = float_value;
		property.unit = unit;
		return true;
	}

	// Detected unit not allowed.
	// However, we allow a value of "0" if zero_unit is set and no unit specified (that is, unit is a pure NUMBER).
	if (unit == Unit::NUMBER)
	{
		if (zero_unit != Unit::UNKNOWN && float_value == 0.0f)
		{
			property.unit = zero_unit;
			property.value = Variant(0.0f);
			return true;
		}
	}

	return false;
}

} // namespace URUI
