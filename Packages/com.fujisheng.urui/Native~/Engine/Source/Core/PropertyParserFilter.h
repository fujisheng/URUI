#pragma once

#include "../../Include/URUI/Core/PropertyParser.h"

namespace URUI
{

    /**
    A property parser for the filter property.
 */

    class PropertyParserFilter : public PropertyParser
    {
    public:
        PropertyParserFilter();
        virtual ~PropertyParserFilter();

        /// Called to parse a decorator declaration.
        bool ParseValue(Property& property, const String& value, const ParameterMap& parameters) const override;
    };

}
