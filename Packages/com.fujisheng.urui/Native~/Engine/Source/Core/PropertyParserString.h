#pragma once

#include "../../Include/URUI/Core/PropertyParser.h"

namespace URUI {

/**
    A passthrough property parser that parses a string.
 */

class PropertyParserString : public PropertyParser {
public:
	explicit PropertyParserString(bool font_family = false);
	virtual ~PropertyParserString();

	/// Called to parse a CSS string declaration.
	/// @param[out] property The property to set the parsed value on.
	/// @param[in] value The raw value defined for this property.
	/// @param[in] parameters The parameters defined for this property; not used for this parser.
	/// @return True if the value was validated successfully, false otherwise.
	bool ParseValue(Property& property, const String& value, const ParameterMap& parameters) const override;
private:
	bool font_family;
};

} // namespace URUI
