#pragma once

#include "../../Include/URUI/Core/Filter.h"
#include "../../Include/URUI/Core/ID.h"

namespace URUI {

class FilterBasic : public Filter {
public:
	bool Initialise(const String& name, float value);

	CompiledFilter CompileFilter(Element* element) const override;

private:
	String name;
	float value = 0.f;
};

class FilterBasicInstancer : public FilterInstancer {
public:
	enum class ValueType { NumberPercent, Angle };

	FilterBasicInstancer(ValueType value_type, const char* default_value);

	SharedPtr<Filter> InstanceFilter(const String& name, const PropertyDictionary& properties) override;

private:
	struct PropertyIds {
		PropertyId value;
	};
	PropertyIds ids = {};
};

} // namespace URUI
