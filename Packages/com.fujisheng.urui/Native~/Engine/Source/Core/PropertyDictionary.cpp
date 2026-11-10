#include "../../Include/URUI/Core/PropertyDictionary.h"
#include "../../Include/URUI/Core/ID.h"

namespace URUI {

PropertyDictionary::PropertyDictionary() {}

void PropertyDictionary::SetProperty(PropertyId id, const Property& property)
{
	URUI_ASSERT(id != PropertyId::Invalid);
	properties[id] = property;
}

void PropertyDictionary::RemoveProperty(PropertyId id)
{
	URUI_ASSERT(id != PropertyId::Invalid);
	properties.erase(id);
}

const Property* PropertyDictionary::GetProperty(PropertyId id) const
{
	PropertyMap::const_iterator iterator = properties.find(id);
	if (iterator == properties.end())
		return nullptr;

	return &(*iterator).second;
}

void PropertyDictionary::SetCustomProperty(String name, Property property)
{
	custom_properties.insert_or_assign(std::move(name), std::move(property));
}

bool PropertyDictionary::RemoveCustomProperty(const String& name)
{
	return custom_properties.erase(name) > 0;
}

const Property* PropertyDictionary::GetCustomProperty(const String& name) const
{
	auto it = custom_properties.find(name);
	if (it == custom_properties.end())
		return nullptr;

	return &it->second;
}

void PropertyDictionary::SetVarShorthand(ShorthandId id, Property property)
{
	URUI_ASSERT(property.unit == Unit::VAR_EXPRESSION);
	var_shorthands.insert_or_assign(id, std::move(property));
}

bool PropertyDictionary::RemoveVarShorthand(ShorthandId id)
{
	return var_shorthands.erase(id) > 0;
}

const Property* PropertyDictionary::GetVarShorthand(ShorthandId id) const
{
	auto it = var_shorthands.find(id);
	if (it == var_shorthands.end())
		return nullptr;

	return &it->second;
}

const UnorderedMap<ShorthandId, Property>& PropertyDictionary::GetVarShorthands() const
{
	return var_shorthands;
}

bool PropertyDictionary::Empty() const
{
	return properties.empty() && custom_properties.empty() && var_shorthands.empty();
}

int PropertyDictionary::GetNumProperties() const
{
	return (int)properties.size();
}

const PropertyMap& PropertyDictionary::GetProperties() const
{
	return properties;
}

const UnorderedMap<String, Property>& PropertyDictionary::GetCustomProperties() const
{
	return custom_properties;
}

void PropertyDictionary::Import(const PropertyDictionary& other, const Specificity* specificity, uint64_t source_order)
{
	auto priority = [&](const Property& property) {
		auto value = property.priority;
		if (specificity) { value.specificity = *specificity; value.sourceOrder += source_order; }
		return value;
	};
	for (const auto& [id, property] : other.properties) SetProperty(id, property, priority(property));
	for (const auto& [name, property] : other.custom_properties) SetCustomProperty(name, property, priority(property));
	for (const auto& [id, property] : other.var_shorthands) SetVarShorthand(id, property, priority(property));
}

void PropertyDictionary::Merge(const PropertyDictionary& other, uint64_t source_order_offset)
{
	auto priority = [&](const Property& property) { auto p = property.priority; p.sourceOrder += source_order_offset; return p; };
	for (const auto& [id, property] : other.properties) SetProperty(id, property, priority(property));
	for (const auto& [name, property] : other.custom_properties) SetCustomProperty(name, property, priority(property));
	for (const auto& [id, property] : other.var_shorthands) SetVarShorthand(id, property, priority(property));
}

void PropertyDictionary::SetOrigin(CssOrigin origin)
{
	for (auto& entry : properties) entry.second.priority.SetOrigin(origin);
	for (auto& entry : custom_properties) entry.second.priority.SetOrigin(origin);
	for (auto& entry : var_shorthands) entry.second.priority.SetOrigin(origin);
}

void PropertyDictionary::Clear()
{
	properties.clear();
	custom_properties.clear();
	var_shorthands.clear();
}

void PropertyDictionary::SetSourceOfAllProperties(const SharedPtr<const PropertySource>& property_source)
{
	for (auto& p : properties)
		p.second.source = property_source;
	for (auto& p : custom_properties)
		p.second.source = property_source;
	for (auto& p : var_shorthands)
		p.second.source = property_source;
}

void PropertyDictionary::SetProperty(PropertyId id, const Property& property, const CascadePriority& priority)
{
	PropertyMap::iterator iterator = properties.find(id);
	if (iterator != properties.end() && priority < iterator->second.priority)
		return;

	Property& new_property = (properties[id] = property);
	new_property.priority = priority;
}

void PropertyDictionary::SetCustomProperty(const String& name, const Property& property, const CascadePriority& priority)
{
	const auto it = custom_properties.find(name);
	if (it != custom_properties.end() && priority < it->second.priority)
		return;

	Property& new_property = custom_properties.insert_or_assign(name, property).first->second;
	new_property.priority = priority;
}

void PropertyDictionary::SetVarShorthand(ShorthandId id, const Property& property, const CascadePriority& priority)
{
	URUI_ASSERT(id != ShorthandId::Invalid);
	const auto it = var_shorthands.find(id);
	if (it != var_shorthands.end() && priority < it->second.priority)
		return;

	Property& new_property = var_shorthands.insert_or_assign(id, property).first->second;
	new_property.priority = priority;
}

} // namespace URUI
