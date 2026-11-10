#pragma once

#include "../../Include/URUI/Core/PropertyParser.h"
#include "ControlledLifetimeResource.h"

namespace URUI {

/**
    A property parser for the decorator property.
 */

class PropertyParserDecorator : public PropertyParser {
public:
	explicit PropertyParserDecorator(bool standard_image = false);
	virtual ~PropertyParserDecorator();

	/// Called to parse a decorator declaration.
	bool ParseValue(Property& property, const String& value, const ParameterMap& parameters) const override;

	static String ConvertAreaToString(BoxArea area);

	static void Initialize();
	static void Shutdown();

private:
	bool standard_image;
	static ControlledLifetimeResource<struct PropertyParserDecoratorData> parser_data;
};

} // namespace URUI
