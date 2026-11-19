#pragma once

#include "../../Include/URUI/Core/PropertyParser.h"

namespace URUI
{

    /**
    A property parser for the --urui-font-effect property.
 */

    class PropertyParserFontEffect : public PropertyParser
    {
    public:
        PropertyParserFontEffect();
        virtual ~PropertyParserFontEffect();

        /// Called to parse a --urui-font-effect declaration.
        bool ParseValue(Property& property, const String& value, const ParameterMap& parameters) const override;
    };

}
