#pragma once

#include "../../Include/URUI/Core/PropertyParser.h"

namespace URUI
{

    /**
    A property parser that validates a value is part of a specified list of keywords.
 */

    class PropertyParserKeyword : public PropertyParser
    {
    public:
        PropertyParserKeyword();
        virtual ~PropertyParserKeyword();

        /// Called to parse a CSS keyword declaration.
        /// @param[out] property The property to set the parsed value on.
        /// @param[in] value The raw value defined for this property.
        /// @param[in] parameters The parameters defined for this property.
        /// @return True if the value was validated successfully, false otherwise.
        bool ParseValue(Property& property, const String& value, const ParameterMap& parameters) const override;
    };

}
