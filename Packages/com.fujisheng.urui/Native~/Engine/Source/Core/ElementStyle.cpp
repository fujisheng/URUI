#include "DocumentFontContext.h"
#include "CssTokenizer.h"
#include "ElementStyle.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/Context.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/ElementUtilities.h"
#include "../../Include/URUI/Core/FontEngineInterface.h"
#include "../../Include/URUI/Core/Log.h"
#include "../../Include/URUI/Core/Math.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/Property.h"
#include "../../Include/URUI/Core/PropertyDefinition.h"
#include "../../Include/URUI/Core/PropertyDictionary.h"
#include "../../Include/URUI/Core/PropertyIdSet.h"
#include "../../Include/URUI/Core/StyleSheet.h"
#include "../../Include/URUI/Core/StyleSheetSpecification.h"
#include "../../Include/URUI/Core/TransformPrimitive.h"
#include "ComputeProperty.h"
#include "ControlledLifetimeResource.h"
#include "ElementDefinition.h"
#include "PropertiesIterator.h"
#include "PropertyShorthandDefinition.h"
#include <algorithm>
#include <optional>

namespace URUI {

struct ElementStyleData {
	const ExpandedShorthands empty_shorthands;
	Property returned_property_storage;
};
static ControlledLifetimeResource<ElementStyleData> element_style_data;

static PseudoClassState operator|(PseudoClassState lhs, PseudoClassState rhs)
{
	return PseudoClassState(int(lhs) | int(rhs));
}
static PseudoClassState operator&(PseudoClassState lhs, PseudoClassState rhs)
{
	return PseudoClassState(int(lhs) & int(rhs));
}

ElementStyle::ElementStyle(Element* _element)
{
	element = _element;
}

const Property* ElementStyle::GetLocalProperty(PropertyId id, const PropertyDictionary& inline_properties, const ElementDefinition* definition,
	const PropertyDictionary* animations, const PropertyDictionary* transitions, const PropertyDictionary* hints)
{
	const Property* winner = inline_properties.GetProperty(id);
	auto consider = [&](const Property* property) {
		if (property && (!winner || winner->priority < property->priority)) winner = property;
	};
	consider(definition ? definition->GetProperty(id) : nullptr);
	consider(animations ? animations->GetProperty(id) : nullptr);
	consider(transitions ? transitions->GetProperty(id) : nullptr);
	consider(hints ? hints->GetProperty(id) : nullptr);
	return winner;
}

const Property* ElementStyle::GetLocalCustomProperty(const String& name, const PropertyDictionary& inline_properties,
	const ElementDefinition* definition)
{
	const Property* local = inline_properties.GetCustomProperty(name);
	const Property* sheet = definition ? definition->GetCustomProperty(name) : nullptr;
	return !local ? sheet : !sheet || !(local->priority < sheet->priority) ? local : sheet;
}

const Property* ElementStyle::GetLocalShorthand(ShorthandId id, const PropertyDictionary& inline_properties, const ElementDefinition* definition)
{
	const Property* local = inline_properties.GetVarShorthand(id);
	const Property* sheet = definition ? definition->GetProperties().GetVarShorthand(id) : nullptr;
	return !local ? sheet : !sheet || !(local->priority < sheet->priority) ? local : sheet;
}

ElementStyle::PropertyElementPair ElementStyle::GetSpecifiedProperty(const PropertySources& sources, PropertyId id)
{
	const auto* definition = sources.element->GetPropertySpecification().GetProperty(id);
	if (!definition) return {};
	const auto* local = GetLocalProperty(id, sources.inline_properties, sources.definition, sources.animations, sources.transitions, &sources.element->GetStyle()->presentational_properties);
	bool inherit = definition->IsInherited();
	if (local) {
		if (local->unit != Unit::CSS_WIDE) return {local, sources.element};
		const auto keyword = local->Get<CssWideKeyword>();
		inherit = keyword == CssWideKeyword::Inherit || (keyword == CssWideKeyword::Unset && inherit);
	}
	if (inherit) {
		auto* parent = sources.element->GetParentNode();
		if (parent && parent->GetOwnerDocument() == sources.element->GetOwnerDocument())
			return GetSpecifiedProperty(parent->GetStyle()->GetPropertySources(), id);
	}
	return {definition->GetDefaultValue(), nullptr};
}

ElementStyle::PropertyElementPair ElementStyle::GetSpecifiedCustomProperty(const PropertySources& sources, const String& name)
{
	const auto id = sources.element->GetPropertySpecification().GetPropertyId(name);
	if (id != PropertyId::Invalid) {
		auto value = GetSpecifiedProperty(sources, id);
		if (!value.element && value.property) value.element = sources.element;
		return value;
	}
	const Property* local = nullptr;
	if (sources.element_inline_properties) {
		local = sources.inline_properties.GetCustomProperty(name);
		if (!local) local = sources.element_inline_properties->GetCustomProperty(name);
	}
	if (!local) local = GetLocalCustomProperty(name, sources.inline_properties, sources.definition);
	if (local) {
		if (local->unit != Unit::CSS_WIDE) return {local, sources.element};
		if (local->Get<CssWideKeyword>() == CssWideKeyword::Initial) return {};
	}
	auto* parent = sources.element->GetParentNode();
	if (parent && parent->GetOwnerDocument() == sources.element->GetOwnerDocument())
		return GetSpecifiedCustomProperty(parent->GetStyle()->GetPropertySources(), name);
	return {};
}

ElementStyle::PropertyElementPair ElementStyle::GetSpecifiedShorthand(const PropertySources& sources, ShorthandId id)
{
	const Property* local_shorthand = GetLocalShorthand(id, sources.inline_properties, sources.definition);
	if (local_shorthand)
		return {local_shorthand, sources.element};

	const ShorthandDefinition* shorthand_definition = sources.element->GetPropertySpecification().GetShorthand(id);
	if (!shorthand_definition)
		return {};

	if (shorthand_definition->inherited)
	{
		Element* parent = sources.element->GetParentNode();
		while (parent)
		{
			const Property* parent_shorthand = parent->GetStyle()->GetLocalShorthand(id);
			if (parent_shorthand)
				return {parent_shorthand, parent};

			parent = parent->GetParentNode();
		}
	}

	return {};
}

struct ElementStyle::VariableContext {
	UnorderedMap<String, uint8_t> state;
	Vector<String> stack;
	SmallUnorderedSet<String> invalid;
	void clear() { state.clear(); stack.clear(); invalid.clear(); }
};
void ElementStyle::CollectVariableCycles(const PropertySources& sources, const String& name, VariableContext& context)
{
	if (context.state[name] == 2) return;
	if (context.state[name] == 1) {
		auto first = std::find(context.stack.begin(), context.stack.end(), name);
		for (; first != context.stack.end(); ++first) context.invalid.insert(*first);
		return;
	}
	if (context.stack.size() >= 512) { context.invalid.insert(name); return; }
	context.state[name] = 1;
	context.stack.push_back(name);
	const auto [property, owner] = GetSpecifiedCustomProperty(sources, name);
	if (property && owner == sources.element && property->unit == Unit::VAR_EXPRESSION) {
		Vector<CssVariableReference> dependencies;
		if (!ScanCssVariables(property->Get<String>(), dependencies)) context.invalid.insert(name);
		for (const auto& dependency : dependencies) CollectVariableCycles(sources, dependency.name, context);
	}
	context.stack.pop_back();
	context.state[name] = 2;
}

ElementStyle::SubstitutionResult ElementStyle::SubstituteVariableOnce(const PropertySources& sources, const String& value,
	SmallUnorderedSet<String>& variable_dependencies, VariableContext& cycle_chain, String& result)
{
	Vector<CssVariableReference> references;
	if (!ScanCssVariables(value, references)) return SubstitutionResult::Error;
	if (references.empty()) return SubstitutionResult::NoVariablesFound;
	const auto& reference = references.front();
	const size_t begin = reference.begin, end_parenthesis = reference.end - 1;
	const bool has_fallback = reference.hasFallback;
	const size_t end_name = has_fallback ? reference.fallbackBegin - 1 : end_parenthesis;
	String var_name = reference.name;
	CollectVariableCycles(sources, var_name, cycle_chain);
	variable_dependencies.insert(var_name);
	auto SubstituteResult = [&](String substitute_value) {
		variable_dependencies.insert(std::move(var_name));
		const String before = value.substr(0, begin), after = value.substr(end_parenthesis + 1);
		auto separator = [](const String& left, const String& right) -> const char* {
			if (left.empty() || right.empty()) return "";
			auto name = [](char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') ||
				(c >= 'A' && c <= 'Z') || c == '-' || c == '_' || c == '.' || uint8_t(c) >= 128; };
			const char a = left.back(), b = right.front();
			return ((name(a) && name(b)) || (name(a) && (b == '(' || b == '%')) || ((a == '#' || a == '@') && name(b))) ? "/**/" : "";
		};
		if (before.size() + substitute_value.size() + after.size() > 16u * 1024u * 1024u) return SubstitutionResult::Error;
		if (substitute_value.empty()) result = before + separator(before, after) + after;
		else result = before + separator(before, substitute_value) + substitute_value + separator(substitute_value, after) + after;
		return SubstitutionResult::Substituted;
	};

	auto [custom_property, property_element] = GetSpecifiedCustomProperty(sources, var_name);
	const auto* registration = sources.element->GetPropertySpecification().GetProperty(var_name);
	if (registration && registration->GetRegistrationSyntax().empty()) registration = nullptr;
	Property registered_value;
	if (registration) {
		bool valid = custom_property && property_element && cycle_chain.invalid.count(var_name) == 0;
		if (valid && custom_property->unit == Unit::VAR_EXPRESSION) {
			String substituted;
			VariableContext inherited_chain;
			auto& chain = property_element == sources.element ? cycle_chain : inherited_chain;
			const auto owner_sources = property_element == sources.element ? sources : property_element->GetStyle()->GetPropertySources();
			valid = SubstituteVariables(owner_sources, custom_property->Get<String>(), variable_dependencies, chain, substituted) &&
				registration->ParseValue(registered_value, substituted);
		} else if (valid) registered_value = *custom_property;
		if (!valid) { registered_value = *registration->GetDefaultValue(); property_element = sources.element; }
		if (Any(registered_value.unit & Unit::LENGTH)) {
			registered_value.value = property_element->ResolveLength(registered_value.GetNumericValue());
			registered_value.unit = Unit::PX;
		}
		custom_property = &registered_value;
	}
	const bool valid_property = custom_property && property_element && (registration || cycle_chain.invalid.count(var_name) == 0);
	if (!valid_property && has_fallback)
	{
		return SubstituteResult(StringUtilities::StripWhitespace(StringView{value, end_name + 1, end_parenthesis - end_name - 1}));
	}

	if (!valid_property)
	{
		Log::Message(Log::LT_ERROR, "Invalid substitution, variable '%s' not defined. In element: %s", var_name.c_str(),
			sources.element->GetAddress().c_str());
		return SubstitutionResult::Error;
	}

	String substitution_value;
	if (custom_property->unit == Unit::VAR_EXPRESSION)
	{
		// Reset cycle chain if (and only if) moving up to a new element.
		VariableContext new_cycle_chain;
		VariableContext& next_cycle_chain = (property_element == sources.element ? cycle_chain : new_cycle_chain);

		// We cannot always use the definition or inline properties from the style of the property element. Particularly
		// in TransitionPropertyChanges, these are not yet set for the current element. Instead, use the passed-in
		// values when the property element is the passed-in element. Otherwise, for ancestor elements, we assume they
		// are fully determined here, so fetch them from their elements.
		const PropertySources& next_sources = (property_element == sources.element ? sources : property_element->GetStyle()->GetPropertySources());

		if (!SubstituteVariables(next_sources, custom_property->Get<String>(), variable_dependencies, next_cycle_chain, substitution_value)) {
			if (has_fallback) return SubstituteResult(StringUtilities::StripWhitespace(StringView{value, end_name + 1, end_parenthesis - end_name - 1}));
			return SubstitutionResult::Error;
		}
	}
	else
	{
        // 类型化参数在 var() 替换时保留 float32 的全部有效位及单位。
        // Property 的显示文本会截短小数，不能作为数值传输格式。
        switch (custom_property->unit)
        {
        case Unit::NUMBER: substitution_value = CreateString("%.9g", custom_property->Get<float>()); break;
        case Unit::PX: substitution_value = CreateString("%.9gpx", custom_property->Get<float>()); break;
        case Unit::DP: {
            // 密度单位只由 URUI 数值 API 写入；传给 CSS 值解析器时转换为标准 px。
            const auto* context = sources.element->GetContext();
            const float ratio = context ? context->GetDensityIndependentPixelRatio() : 1.f;
            substitution_value = CreateString("%.9gpx", custom_property->Get<float>() * ratio);
            break;
        }
        case Unit::PERCENT: substitution_value = CreateString("%.9g%%", custom_property->Get<float>()); break;
        default: substitution_value = custom_property->ToString(); break;
        }
	}

	return SubstituteResult(std::move(substitution_value));
}

bool ElementStyle::SubstituteVariables(const PropertySources& sources, String value, SmallUnorderedSet<String>& variable_dependencies,
	VariableContext& cycle_chain, String& result)
{
	bool looping = true;
	for (int i = 0; looping; i++)
	{
		SubstitutionResult substitution = SubstituteVariableOnce(sources, value, variable_dependencies, cycle_chain, result);
		switch (substitution)
		{
		case SubstitutionResult::Substituted:
		{
			value = std::move(result);
		}
		break;
		case SubstitutionResult::NoVariablesFound:
		{
			if (i == 0)
			{
				Log::Message(Log::LT_ERROR, "Invalid substitution, expected 'var()': %s. In element: %s", value.c_str(),
					sources.element->GetAddress().c_str());
				return false;
			}
			looping = false;
		}
		break;
		case SubstitutionResult::Error: return false;
		}
	}

	result = std::move(value);
	return true;
}

const Property* ElementStyle::ResolveVariables(const PropertySources& sources, const ExpandedShorthands& substituted_shorthands, PropertyId id,
	const Property* property, SmallUnorderedSet<String>& variable_dependencies, Property& property_storage)
{
	if (property->unit == Unit::SHORTHAND_PLACEHOLDER)
	{
		const auto expansion = substituted_shorthands.find(property->Get<ShorthandId>());
		const Property* property_from_shorthand = expansion == substituted_shorthands.end() ? nullptr : expansion->second.GetProperty(id);
		if (!property_from_shorthand)
		{
			Log::Message(Log::LT_ERROR, "Property '%s' could not be resolved: Pending substitution from a shorthand.",
				sources.element->GetPropertySpecification().GetPropertyName(id).c_str());
			return nullptr;
		}

		return property_from_shorthand;
	}

	if (property->unit != Unit::VAR_EXPRESSION)
		return property;

	String substitution_result;
	VariableContext cycle_chain;
	if (!SubstituteVariables(sources, property->Get<String>(), variable_dependencies, cycle_chain, substitution_result))
		return nullptr;

	const PropertyDefinition* property_definition = sources.element->GetPropertySpecification().GetProperty(id);
	if (!property_definition)
	{
		Log::Message(Log::LT_ERROR, "Invalid substitution, property '%s' not defined.", sources.element->GetPropertySpecification().GetPropertyName(id).c_str());
		return nullptr;
	}

	if (!property_definition->GetRegistrationSyntax().empty()) {
		if (!property_definition->ParseValue(property_storage, substitution_result)) return nullptr;
		property_storage.priority = property->priority;
		return &property_storage;
	}
	PropertyDictionary parsed;
	if (!sources.element->GetPropertySpecification().ParsePropertyDeclaration(parsed, id, substitution_result))
	{
		Log::Message(Log::LT_ERROR, "Invalid substitution, property '%s' has invalid value: %s", sources.element->GetPropertySpecification().GetPropertyName(id).c_str(),
			substitution_result.c_str());
		return nullptr;
	}
	property_storage = *parsed.GetProperty(id);
	property_storage.priority = property->priority;
	return &property_storage;
}

const Property* ElementStyle::ResolveVariablesWithShorthandExpansion(const PropertySources& sources, PropertyId id, const Property* property,
	std::optional<ExpandedShorthands>& substituted_shorthands, SmallUnorderedSet<String>& variable_dependencies, Property& property_storage)
{
	if (!property)
		return nullptr;
	if (property->unit != Unit::SHORTHAND_PLACEHOLDER && property->unit != Unit::VAR_EXPRESSION)
		return property;
	if (property->unit == Unit::SHORTHAND_PLACEHOLDER && !substituted_shorthands.has_value())
	{
		// Lazy-compute shorthand expansion.
		substituted_shorthands.emplace();
		ExpandVarShorthands(*substituted_shorthands, sources, std::nullopt);
	}
	variable_dependencies.clear();
	return ResolveVariables(sources, (substituted_shorthands ? *substituted_shorthands : element_style_data->empty_shorthands), id, property,
		variable_dependencies, property_storage);
}

void ElementStyle::ExpandVarShorthands(ExpandedShorthands& out_substituted_shorthands, const PropertySources& sources,
	std::optional<DirtyPropertiesRef> dirty_properties)
{
	out_substituted_shorthands.clear();
	SmallUnorderedSet<String> variable_dependencies;
	VariableContext cycle_chain;

	for (const PropertyDictionary* properties : {sources.definition ? &sources.definition->GetProperties() : nullptr, &sources.inline_properties})
	{
		if (!properties)
			continue;

		for (const auto& [shorthand_id, property] : properties->GetVarShorthands())
		{
			URUI_ASSERT(property.unit == Unit::VAR_EXPRESSION);
			if (GetLocalShorthand(shorthand_id, sources.inline_properties, sources.definition) != &property) continue;
			variable_dependencies.clear();
			cycle_chain.clear();

			String substitution_result;
			if (!SubstituteVariables(sources, property.Get<String>(), variable_dependencies, cycle_chain, substitution_result))
				continue;

			PropertyDictionary expanded;
			if (!sources.element->GetPropertySpecification().ParseShorthandDeclaration(expanded, shorthand_id, substitution_result)) continue;
			out_substituted_shorthands.emplace(shorthand_id, std::move(expanded));

			if (dirty_properties.has_value())
			{
				const bool dirty_variables_used = std::any_of(variable_dependencies.begin(), variable_dependencies.end(),
					[&](const String& variable_name) { return dirty_properties->variables.count(variable_name) != 0; });

				// Dirty all properties of the current shorthand either if: (1) it uses a dirtied variable, or (2) the shorthand itself is dirty.
				if (dirty_variables_used || dirty_properties->var_shorthands.count(shorthand_id) != 0)
				{
					const PropertyIdSet ids = sources.element->GetPropertySpecification().GetShorthandUnderlyingProperties(shorthand_id);
					for (const PropertyId id : ids)
						dirty_properties->properties.Insert(id);
				}
			}
		}
	}
}

void ElementStyle::TransitionPropertyChanges(const PropertySources& sources, PropertyIdSet& changed_properties,
	const ElementDefinition* new_definition)
{
	// Apply transition to relevant properties if a transition is defined on the element. Properties that are part of a
	// transition are removed from the properties list.

	URUI_ASSERT(sources.element);
	if (!sources.definition || !new_definition || changed_properties.Empty())
		return;

	// We get the local property directly here to intercept property changes even before the computed values are ready.
	const Property* transition_property = GetLocalProperty(PropertyId::Transition, sources.inline_properties, new_definition);
	if (!transition_property)
		return;
	if (transition_property->value.GetType() != Variant::TRANSITIONLIST)
		return;

	const TransitionList& transition_list = transition_property->value.GetReference<TransitionList>();
	if (transition_list.none)
		return;

	SmallUnorderedSet<String> variable_dependencies;

	Property property_storage_old;
	Property property_storage_new;
	std::optional<ExpandedShorthands> substituted_shorthands_old;
	std::optional<ExpandedShorthands> substituted_shorthands_new;

	PropertySources new_sources{sources.element, new_definition, sources.inline_properties, nullptr, sources.animations, nullptr};

	auto add_transition = [&](const Transition& transition) {
		const Property* start_value = GetSpecifiedProperty(sources, transition.id).property;
		start_value = ResolveVariablesWithShorthandExpansion(sources, transition.id, start_value, substituted_shorthands_old, variable_dependencies,
			property_storage_old);

		const Property* target_value = GetSpecifiedProperty(new_sources, transition.id).property;
		target_value = ResolveVariablesWithShorthandExpansion(new_sources, transition.id, target_value, substituted_shorthands_new,
			variable_dependencies, property_storage_new);

		bool transition_added = false;
		if (start_value && target_value && (*start_value != *target_value))
			transition_added = sources.element->StartTransition(transition, *start_value, *target_value);

		return transition_added;
	};

	if (transition_list.all)
	{
		Transition transition = transition_list.transitions[0];
		for (auto it = changed_properties.begin(); it != changed_properties.end();)
		{
			transition.id = *it;
			if (add_transition(transition))
				it = changed_properties.Erase(it);
			else
				++it;
		}
	}
	else
	{
		for (const Transition& transition : transition_list.transitions)
		{
			if (changed_properties.Contains(transition.id))
			{
				if (add_transition(transition))
					changed_properties.Erase(transition.id);
			}
		}
	}
}

void ElementStyle::UpdateDefinition()
{
	URUI_ZoneScoped;

	SharedPtr<const ElementDefinition> new_definition;

	if (const StyleSheet* style_sheet = element->GetStyleSheet())
	{
		new_definition = style_sheet->GetElementDefinition(element);
	}

	// Switch the property definitions if the definition has changed.
	if (new_definition != definition)
	{
		PropertyIdSet changed_properties;
		UnorderedSet<String> changed_variables;
		UnorderedSet<ShorthandId> changed_shorthands;

		if (definition)
		{
			changed_properties = definition->GetPropertyIds();
			for (const auto& [name, _] : definition->GetProperties().GetCustomProperties())
				changed_variables.insert(name);
			for (const auto& [shorthand_id, _] : definition->GetProperties().GetVarShorthands())
				changed_shorthands.insert(shorthand_id);
		}

		if (new_definition)
		{
			changed_properties |= new_definition->GetPropertyIds();
			for (const auto& [name, _] : new_definition->GetProperties().GetCustomProperties())
				changed_variables.insert(name);
			for (const auto& [shorthand_id, _] : new_definition->GetProperties().GetVarShorthands())
				changed_shorthands.insert(shorthand_id);
		}

		if (definition && new_definition)
		{
			// Remove properties that compare equal from the changed list. Except if they can take variables: In this
			// case, the two properties may compare equal in value but represent different resolved properties depending
			// on their context. In that case we need to proceed to resolve them fully.
			const PropertyIdSet properties_in_both_definitions = (definition->GetPropertyIds() & new_definition->GetPropertyIds());

			for (PropertyId id : properties_in_both_definitions)
			{
				const Property* p0 = definition->GetProperty(id);
				const Property* p1 = new_definition->GetProperty(id);
				if (p0 && p1 && *p0 == *p1 && p0->unit != Unit::VAR_EXPRESSION && p1->unit != Unit::SHORTHAND_PLACEHOLDER)
					changed_properties.Erase(id);
			}

			// Transition changed properties if transition property is set
			TransitionPropertyChanges(GetPropertySources(), changed_properties, new_definition.get());
		}

		definition = new_definition;

		DirtyProperties(changed_properties);
		dirty_variables.insert(std::make_move_iterator(changed_variables.begin()), std::make_move_iterator(changed_variables.end()));
		dirty_var_shorthands.insert(changed_shorthands.begin(), changed_shorthands.end());
	}
}

bool ElementStyle::SetPseudoClass(const String& pseudo_class, bool activate, bool override_class)
{
	bool changed = false;

	if (activate)
	{
		PseudoClassState& state = pseudo_classes[pseudo_class];
		changed = (state == PseudoClassState::Clear);
		state = (state | (override_class ? PseudoClassState::Override : PseudoClassState::Set));
	}
	else
	{
		auto it = pseudo_classes.find(pseudo_class);
		if (it != pseudo_classes.end())
		{
			PseudoClassState& state = it->second;
			state = (state & (override_class ? PseudoClassState::Set : PseudoClassState::Override));
			if (state == PseudoClassState::Clear)
			{
				pseudo_classes.erase(it);
				changed = true;
			}
		}
	}

	return changed;
}

bool ElementStyle::IsPseudoClassSet(const String& pseudo_class) const
{
	return (pseudo_classes.count(pseudo_class) == 1);
}

const PseudoClassMap& ElementStyle::GetActivePseudoClasses() const
{
	return pseudo_classes;
}

bool ElementStyle::SetClass(const String& class_name, bool activate)
{
	const auto class_location = std::find(classes.begin(), classes.end(), class_name);

	bool changed = false;
	if (activate)
	{
		if (class_location == classes.end())
		{
			classes.push_back(class_name);
			changed = true;
		}
	}
	else
	{
		if (class_location != classes.end())
		{
			classes.erase(class_location);
			changed = true;
		}
	}

	if (changed) element->InvalidateCompiledQueries(4);
	return changed;
}

bool ElementStyle::IsClassSet(const String& class_name) const
{
	return std::find(classes.begin(), classes.end(), class_name) != classes.end();
}

void ElementStyle::SetClassNames(const String& class_names)
{
	element->InvalidateCompiledQueries(4);
	classes.clear();
	StringUtilities::ExpandString(classes, class_names, ' ');
}

String ElementStyle::GetClassNames() const
{
	String class_names;
	for (size_t i = 0; i < classes.size(); i++)
	{
		if (i != 0)
		{
			class_names += " ";
		}
		class_names += classes[i];
	}

	return class_names;
}

const StringList& ElementStyle::GetClassNameList() const
{
	return classes;
}

std::optional<Property> ElementStyle::CaptureInlineTransition(PropertyId id) const
{
	if (!element->GetContext() || id == PropertyId::Transition || id == PropertyId::Animation) return {};
	const auto* control = GetProperty(PropertyId::Transition);
	if (!control || control->unit != Unit::TRANSITION || control->value.GetReference<TransitionList>().none) return {};
	if (const auto* property = GetProperty(id)) return *property;
	return {};
}
void ElementStyle::ApplyInlineTransition(PropertyId id, const Property& before)
{
	const auto* control = GetProperty(PropertyId::Transition);
	if (!control || control->unit != Unit::TRANSITION) return;
	const auto& list = control->value.GetReference<TransitionList>();
	std::optional<Transition> selected;
	for (const auto& transition : list.transitions)
		if (list.all || transition.id == id) { selected = transition; selected->id = id; }
	if (!selected || (selected->duration <= 0 && selected->delay <= 0)) return;
	auto sources = GetPropertySources();
	sources.transitions = nullptr;
	const auto specified = GetSpecifiedProperty(sources, id);
	if (!specified.property) return;
	std::optional<ExpandedShorthands> shorthands;
	SmallUnorderedSet<String> dependencies;
	Property storage;
	const auto* target = ResolveVariablesWithShorthandExpansion(sources, id, specified.property, shorthands, dependencies, storage);
	if (target && before != *target) element->StartTransition(*selected, before, *target);
}

bool ElementStyle::SetProperty(PropertyId id, const Property& property)
{
	Property new_property = property;

	new_property.priority.inlineStyle = true;
	new_property.definition = element->GetPropertySpecification().GetProperty(id);
	if (!new_property.definition)
		return false;

	const auto before = CaptureInlineTransition(id);
	inline_properties.SetProperty(id, new_property);
	DirtyProperty(id);
	if (before) ApplyInlineTransition(id, *before);

	return true;
}

void ElementStyle::SetPresentationalProperty(PropertyId id, const Property& property)
{
	Property hint = property;
	hint.definition = element->GetPropertySpecification().GetProperty(id);
	hint.priority = {};
	hint.priority.presentationalHint = true;
	presentational_properties.SetProperty(id, hint);
	DirtyProperty(id);
}
void ElementStyle::RemovePresentationalProperty(PropertyId id)
{
	presentational_properties.RemoveProperty(id);
	DirtyProperty(id);
}

void ElementStyle::AppendCustomPropertyNames(UnorderedSet<String>& names) const
{
    for (const auto& entry : inline_properties.GetCustomProperties()) names.insert(entry.first);
    if (definition) for (const auto& entry : definition->GetProperties().GetCustomProperties()) names.insert(entry.first);
}

bool ElementStyle::SetAnimatedProperty(PropertyId id, const Property& property, bool transition)
{
	Property effect = property;
	effect.definition = element->GetPropertySpecification().GetProperty(id);
	if (!effect.definition) return false;
	effect.priority = {};
	effect.priority.originImportance = transition ? CssOriginImportance::Transition : CssOriginImportance::Animation;
    auto& destination = transition ? transition_properties : animation_properties;
    // Numbers and byte colors are immutable values. Re-publishing an identical
    // sample must not dirty style/geometry (including a transition's seeded frame).
    // Reference-valued transforms, filters and decorators keep their original path.
    const auto* previous = destination.GetProperty(id);
    if ((effect.unit == Unit::NUMBER || effect.unit == Unit::COLOUR) && previous &&
        previous->definition == effect.definition && previous->priority.originImportance == effect.priority.originImportance &&
        *previous == effect) return true;
    destination.SetProperty(id, effect);
    if (!transition && !sequence_layers.empty()) RecomposeSequenceAnimations();
	DirtyProperty(id);
	return true;
}
void ElementStyle::RemoveAnimatedProperty(PropertyId id, bool transition)
{
	auto& properties = transition ? transition_properties : animation_properties;
	if (!properties.GetProperty(id)) return;
	properties.RemoveProperty(id);
	if (!transition && !sequence_layers.empty()) RecomposeSequenceAnimations();
	DirtyProperty(id);
}

void ElementStyle::SetCustomProperty(const String& name, const Property& property)
{
    element->DirtyNativeRendering();
	auto local = property;
	local.priority.inlineStyle = true;
	inline_properties.SetCustomProperty(name, local);
	dirty_variables.insert(name);
}

void ElementStyle::SetVarShorthand(ShorthandId id, const Property& property)
{
    element->DirtyNativeRendering();
	auto local = property;
	local.priority.inlineStyle = true;
	inline_properties.SetVarShorthand(id, local);
	dirty_var_shorthands.insert(id);
}

void ElementStyle::RemoveProperty(PropertyId id)
{
	const auto before = CaptureInlineTransition(id);
	int size_before = inline_properties.GetNumProperties();
	inline_properties.RemoveProperty(id);

	if (inline_properties.GetNumProperties() != size_before) {
		DirtyProperty(id);
		if (before) ApplyInlineTransition(id, *before);
	}
}

void ElementStyle::RemoveCustomProperty(const String& name)
{
    element->DirtyNativeRendering();
	if (inline_properties.RemoveCustomProperty(name))
		dirty_variables.insert(name);
}

void ElementStyle::RemoveVarShorthand(ShorthandId id)
{
    element->DirtyNativeRendering();
	if (inline_properties.RemoveVarShorthand(id))
		dirty_var_shorthands.insert(id);
}

bool ElementStyle::ValidateVariableReadCache() const
{
    const auto* document = element->GetOwnerDocument();
    const auto* context = element->GetContext();
    if (!document || !context) return false;
    const auto viewport = context->GetDimensions();
    const auto dp = context->GetDensityIndependentPixelRatio();
    bool same = viewport.x == variable_viewport_width && viewport.y == variable_viewport_height && dp == variable_dp_ratio;
    bool reaches_document = false;
    size_t count = 0;
    for (auto* current = element; current && current->GetOwnerDocument() == document; current = current->GetParentNode(), ++count)
    {
        const auto* style = current->GetStyle();
        reaches_document |= current == document;
        // 布局尚未提交时仍走即时解析，不能用旧代际遮盖父变量的直接写入。
        if (style->AnyPropertiesDirty()) return false;
        if (count >= variable_scopes.size() || variable_scopes[count].style != style ||
            variable_scopes[count].revision != style->GetComputedRevision()) same = false;
    }
    // 脱离树的元素仍持有 ownerDocument；rem 等值可引用父链之外的文档字号。
    if (!reaches_document) return false;
    if (count != variable_scopes.size()) same = false;
    if (!same)
    {
        variable_scopes.clear();
        for (auto* current = element; current && current->GetOwnerDocument() == document; current = current->GetParentNode())
            variable_scopes.push_back({current->GetStyle(), current->GetStyle()->GetComputedRevision()});
        if (++variable_generation == 0)
        {
            for (auto& value : variable_values) value.second.generation = 0;
            variable_generation = 1;
        }
        variable_viewport_width = viewport.x; variable_viewport_height = viewport.y; variable_dp_ratio = dp;
    }
    return true;
}

const Property* ElementStyle::ResolveVariableRead(PropertyId id, const Property* property) const
{
    const bool stable = ValidateVariableReadCache();
    if (stable)
    {
        const auto found = variable_values.find(id);
        if (found != variable_values.end() && found->second.generation == variable_generation)
        {
            const auto& value = found->second;
            if (value.expression == *property && value.expression.priority == property->priority &&
                value.expression.definition == property->definition && value.expression.source == property->source &&
                value.expression.parser_index == property->parser_index)
                return value.property ? &*value.property : nullptr;
        }
    }
    variable_resolution_count++;
    SmallUnorderedSet<String> dependencies;
    const auto* resolved = ResolveVariables(id, property, dependencies, element_style_data->returned_property_storage);
    if (!stable) return resolved;
    auto& value = variable_values[id];
    value.property = resolved ? std::optional<Property>(*resolved) : std::nullopt;
    value.expression = *property;
    value.generation = variable_generation;
    return value.property ? &*value.property : nullptr;
}

const Property* ElementStyle::GetProperty(PropertyId id) const
{
	const PropertyElementPair result = GetSpecifiedProperty(GetPropertySources(), id);
	if (!result.element)
		return result.property;

    const Property* resolved;
    if (result.property->unit == Unit::VAR_EXPRESSION)
        resolved = result.element->GetStyle()->ResolveVariableRead(id, result.property);
    else
    {
        SmallUnorderedSet<String> variable_dependencies;
        resolved = result.element->GetStyle()->ResolveVariables(id, result.property, variable_dependencies, element_style_data->returned_property_storage);
    }
	if (resolved && resolved->unit == Unit::CSS_WIDE) {
		const auto* definition = element->GetPropertySpecification().GetProperty(id);
		const auto keyword = resolved->Get<CssWideKeyword>();
		const bool inherit = keyword == CssWideKeyword::Inherit || (keyword == CssWideKeyword::Unset && definition->IsInherited());
		auto* parent = result.element->GetParentNode();
		if (inherit && parent && parent->GetOwnerDocument() == result.element->GetOwnerDocument()) return parent->GetStyle()->GetProperty(id);
		return definition->GetDefaultValue();
	}
	const auto* registration = element->GetPropertySpecification().GetProperty(id);
	if (registration && !registration->GetRegistrationSyntax().empty()) {
		if (!resolved) return registration->GetDefaultValue();
		if (Any(resolved->unit & Unit::LENGTH)) {
			Property absolute = *resolved;
			absolute.value = result.element->ResolveLength(resolved->GetNumericValue());
			absolute.unit = Unit::PX;
			element_style_data->returned_property_storage = std::move(absolute);
			return &element_style_data->returned_property_storage;
		}
	}
	return resolved;
}

bool ElementStyle::HasCustomPropertyExpression(const String& name) const
{
    const auto source=GetSpecifiedCustomProperty(GetPropertySources(),name);
    return source.property&&source.property->unit==Unit::VAR_EXPRESSION;
}

const Property* ElementStyle::GetCustomProperty(const String& name) const
{
	const PropertyElementPair result = GetSpecifiedCustomProperty(GetPropertySources(), name);
	if (!result.element || !result.property || result.property->unit != Unit::VAR_EXPRESSION)
		return result.property;

	SmallUnorderedSet<String> variable_dependencies;
	VariableContext cycle_chain;
	String substitution_result;
	if (!SubstituteVariables(result.element->GetStyle()->GetPropertySources(), result.property->Get<String>(), variable_dependencies, cycle_chain,
			substitution_result))
		return nullptr;

	element_style_data->returned_property_storage = Property{substitution_result, Unit::STRING};
	return &element_style_data->returned_property_storage;
}

const Property* ElementStyle::GetLocalProperty(PropertyId id) const
{
	return GetLocalProperty(id, inline_properties, definition.get(), sequence_layers.empty() ? &animation_properties : &composed_animations, &transition_properties, &presentational_properties);
}

const Property* ElementStyle::GetLocalPropertyWithResolvedVariables(PropertyId id) const
{
	const Property* property = GetLocalProperty(id);
	if (!property)
		return nullptr;

	SmallUnorderedSet<String> variable_dependencies;
	const auto* resolved = ResolveVariables(id, property, variable_dependencies, element_style_data->returned_property_storage);
	return resolved && resolved->unit == Unit::CSS_WIDE ? GetProperty(id) : resolved;
}

const Property* ElementStyle::GetLocalCustomProperty(const String& name) const
{
	return GetLocalCustomProperty(name, inline_properties, definition.get());
}

const Property* ElementStyle::GetLocalShorthand(ShorthandId id) const
{
	return GetLocalShorthand(id, inline_properties, definition.get());
}

const PropertyDictionary& ElementStyle::GetLocalStyleProperties() const
{
	return inline_properties;
}

static float ComputeLength(NumericValue value, Element* element)
{
	float font_size = 0.f;
	float doc_font_size = 0.f;
	float dp_ratio = 1.0f;
	Vector2f vp_dimensions(1.0f);

	if (Any(value.unit & Unit::DP_SCALABLE_LENGTH))
	{
		if (Context* context = element->GetContext())
			dp_ratio = context->GetDensityIndependentPixelRatio();
	}

	switch (value.unit)
	{
	case Unit::EM: font_size = element->GetComputedValues().font_size(); break;
	case Unit::REM:
		if (ElementDocument* document = element->GetOwnerDocument())
			doc_font_size = document->GetComputedValues().font_size();
		else
			doc_font_size = DefaultComputedValues().font_size();
		break;
	case Unit::VW:
	case Unit::VH:
		if (Context* context = element->GetContext())
			vp_dimensions = Vector2f(context->GetDimensions());
		break;
	default: break;
	}

	const float result = ComputeLength(value, font_size, doc_font_size, dp_ratio, vp_dimensions);
	return result;
}

float ElementStyle::ResolveNumericValue(NumericValue value, float base_value) const
{
	if (value.unit == Unit::PX)
		return value.number;
	else if (Any(value.unit & Unit::LENGTH))
		return ComputeLength(value, element);

	switch (value.unit)
	{
	case Unit::NUMBER: return value.number * base_value;
	case Unit::PERCENT: return value.number * base_value * 0.01f;
	case Unit::X: return value.number;
	case Unit::DEG:
	case Unit::RAD: return ComputeAngle(value);
	default: break;
	}

	URUI_ERROR;
	return 0.f;
}

float ElementStyle::ResolveRelativeLength(NumericValue value, RelativeTarget relative_target) const
{
	// There is an exception on font-size properties, as 'em' units here refer to parent font size instead
	if (Any(value.unit & Unit::LENGTH) && !(value.unit == Unit::EM && relative_target == RelativeTarget::ParentFontSize))
	{
		const float result = ComputeLength(value, element);
		return result;
	}

	float base_value = 0.0f;

	switch (relative_target)
	{
	case RelativeTarget::None: base_value = 1.0f; break;
	case RelativeTarget::ContainingBlockWidth: base_value = element->GetContainingBlock().x; break;
	case RelativeTarget::ContainingBlockHeight: base_value = element->GetContainingBlock().y; break;
	case RelativeTarget::FontSize: base_value = element->GetComputedValues().font_size(); break;
	case RelativeTarget::ParentFontSize:
	{
		auto p = element->GetParentNode();
		base_value = (p ? p->GetComputedValues().font_size() : DefaultComputedValues().font_size());
	}
	break;
	case RelativeTarget::LineHeight: base_value = element->GetLineHeight(); break;
	}

	float scale_value = 0.0f;

	switch (value.unit)
	{
	case Unit::EM:
	case Unit::NUMBER: scale_value = value.number; break;
	case Unit::PERCENT: scale_value = value.number * 0.01f; break;
	default: break;
	}

	return base_value * scale_value;
}

void ElementStyle::DirtyInheritedProperties()
{
	dirty_properties |= element->GetPropertySpecification().GetRegisteredInheritedProperties();
}

void ElementStyle::DirtyPropertiesWithUnits(Units units)
{
	// Dirty all the properties of this element that use the unit(s).
	for (auto it = Iterate(); !it.AtEnd(); ++it)
	{
		auto name_property_pair = *it;
		PropertyId id = name_property_pair.first;
		const Property& property = name_property_pair.second;
        if (Any(property.unit & units) || property.unit == Unit::VAR_EXPRESSION)
			DirtyProperty(id);
	}
    // var() 简写缓存可能包含通过 URUI API 写入的密度长度；单位环境改变时重新展开。
    for (const auto& shorthand : expanded_shorthands) dirty_var_shorthands.insert(shorthand.first);
}

void ElementStyle::DirtyPropertiesWithUnitsRecursive(Units units)
{
	DirtyPropertiesWithUnits(units);

	// Now dirty all of our descendant's properties that use the unit(s).
	int num_children = element->GetNumChildren(true);
	for (int i = 0; i < num_children; ++i)
		element->GetChild(i)->GetStyle()->DirtyPropertiesWithUnitsRecursive(units);
}

bool ElementStyle::AnyPropertiesDirty() const
{
	auto* document = element->GetOwnerDocument();
	const auto font_revision = document ? document->GetFontContext().GetRevision() : 0;
	return font_context_revision != font_revision || registered_property_revision != element->GetPropertySpecification().GetRegisteredPropertyRevision() ||
        !dirty_properties.Empty() || !dirty_variables.empty() || !dirty_var_shorthands.empty();
}

const Property* ElementStyle::ResolveKeyFrameProperty(PropertyId id, const Property* property, const PropertyDictionary& block_properties,
	std::optional<ExpandedShorthands>& block_substituted_shorthands, SmallUnorderedSet<String>& variable_dependencies, Property& property_storage)
{
	variable_dependencies.clear();
	PropertySources sources{element, definition.get(), block_properties, &inline_properties};
	return ResolveVariablesWithShorthandExpansion(sources, id, property, block_substituted_shorthands, variable_dependencies, property_storage);
}

PlainPropertiesIterator ElementStyle::Iterate() const
{
	return PlainPropertiesIterator{
		inline_properties.GetProperties(),
		definition ? &definition->GetProperties().GetProperties() : nullptr,
		sequence_layers.empty() ? &animation_properties.GetProperties() : &composed_animations.GetProperties(),
        &transition_properties.GetProperties(), &presentational_properties.GetProperties()
	};
}

UniquePtr<AllPropertiesIterator> ElementStyle::IterateAll(Element* filter_inherited_by) const
{
	return MakeUnique<AllPropertiesIterator>(inline_properties, definition ? &definition->GetProperties() : nullptr,
		filter_inherited_by ? filter_inherited_by->GetStyle() : nullptr, element->GetPropertySpecification());
}

void ElementStyle::Initialize()
{
	element_style_data.Initialize();
}
void ElementStyle::Shutdown()
{
	element_style_data.Shutdown();
}

void ElementStyle::DirtyProperty(PropertyId id)
{
    element->DirtyNativeRendering();
	dirty_properties.Insert(id);
    if (id >= PropertyId::FirstCustomId)
    {
        const auto& name = element->GetPropertySpecification().GetPropertyName(id);
        if (StringUtilities::StartsWith(name, "--")) dirty_variables.insert(name);
    }
}


namespace {
const Style::ComputedValues& WideSource(const Property& property, PropertyId id, const Style::ComputedValues* parent, const PropertySpecification& specification)
{
	const auto keyword = property.Get<CssWideKeyword>();
	const auto* definition = specification.GetProperty(id);
	const bool inherit = keyword == CssWideKeyword::Inherit || (keyword == CssWideKeyword::Unset && definition && definition->IsInherited());
	return inherit && parent ? *parent : DefaultComputedValues();
}
void CopyComputedProperty(Style::ComputedValues& target, const Style::ComputedValues& source, PropertyId id)
{
	switch (id) {
	case PropertyId::MarginTop: target.margin_top(source.margin_top()); break;
	case PropertyId::MarginRight: target.margin_right(source.margin_right()); break;
	case PropertyId::MarginBottom: target.margin_bottom(source.margin_bottom()); break;
	case PropertyId::MarginLeft: target.margin_left(source.margin_left()); break;
	case PropertyId::PaddingTop: target.padding_top(source.padding_top()); break;
	case PropertyId::PaddingRight: target.padding_right(source.padding_right()); break;
	case PropertyId::PaddingBottom: target.padding_bottom(source.padding_bottom()); break;
	case PropertyId::PaddingLeft: target.padding_left(source.padding_left()); break;
	case PropertyId::BorderTopWidth: target.border_top_width(source.border_top_width()); break;
	case PropertyId::BorderRightWidth: target.border_right_width(source.border_right_width()); break;
	case PropertyId::BorderBottomWidth: target.border_bottom_width(source.border_bottom_width()); break;
	case PropertyId::BorderLeftWidth: target.border_left_width(source.border_left_width()); break;
	case PropertyId::BorderTopColor: target.border_top_color(source.border_top_color()); break;
	case PropertyId::BorderRightColor: target.border_right_color(source.border_right_color()); break;
	case PropertyId::BorderBottomColor: target.border_bottom_color(source.border_bottom_color()); break;
	case PropertyId::BorderLeftColor: target.border_left_color(source.border_left_color()); break;
	case PropertyId::BorderTopLeftRadius: target.border_top_left_radius(source.border_top_left_radius()); break;
	case PropertyId::BorderTopRightRadius: target.border_top_right_radius(source.border_top_right_radius()); break;
	case PropertyId::BorderBottomRightRadius: target.border_bottom_right_radius(source.border_bottom_right_radius()); break;
	case PropertyId::BorderBottomLeftRadius: target.border_bottom_left_radius(source.border_bottom_left_radius()); break;
	case PropertyId::Display: target.display(source.display()); break;
	case PropertyId::Position: target.position(source.position()); break;
	case PropertyId::Top: target.top(source.top()); break;
	case PropertyId::Right: target.right(source.right()); break;
	case PropertyId::Bottom: target.bottom(source.bottom()); break;
	case PropertyId::Left: target.left(source.left()); break;
	case PropertyId::Float: target.float_(source.float_()); break;
	case PropertyId::Clear: target.clear(source.clear()); break;
	case PropertyId::BoxSizing: target.box_sizing(source.box_sizing()); break;
	case PropertyId::ZIndex: target.z_index(source.z_index()); break;
	case PropertyId::Width: target.width(source.width()); break;
	case PropertyId::MinWidth: target.min_width(source.min_width()); break;
	case PropertyId::MaxWidth: target.max_width(source.max_width()); break;
	case PropertyId::Height: target.height(source.height()); break;
	case PropertyId::MinHeight: target.min_height(source.min_height()); break;
	case PropertyId::MaxHeight: target.max_height(source.max_height()); break;
	case PropertyId::VerticalAlign: target.vertical_align(source.vertical_align()); break;
	case PropertyId::OverflowX: target.overflow_x(source.overflow_x()); break;
	case PropertyId::OverflowY: target.overflow_y(source.overflow_y()); break;
	case PropertyId::Clip: target.clip(source.clip()); break;
	case PropertyId::Visibility: target.visibility(source.visibility()); break;
	case PropertyId::TextOverflow: target.text_overflow(source.text_overflow()); break;
	case PropertyId::BackgroundColor: target.background_color(source.background_color()); break;
	case PropertyId::Color: target.color(source.color()); break;
	case PropertyId::ImageColor: target.image_color(source.image_color()); break;
	case PropertyId::Opacity: target.opacity(source.opacity()); break;
	case PropertyId::FontStyle: target.font_style(source.font_style()); break;
	case PropertyId::FontWeight: target.font_weight(source.font_weight()); break;
	case PropertyId::FontKerning: target.font_kerning(source.font_kerning()); break;
	case PropertyId::LetterSpacing: target.letter_spacing(source.letter_spacing()); break;
	case PropertyId::TextAlign: target.text_align(source.text_align()); break;
	case PropertyId::TextDecoration: target.text_decoration(source.text_decoration()); break;
	case PropertyId::TextTransform: target.text_transform(source.text_transform()); break;
	case PropertyId::WhiteSpace: target.white_space(source.white_space()); break;
	case PropertyId::WordBreak: target.word_break(source.word_break()); break;
	case PropertyId::RowGap: target.row_gap(source.row_gap()); break;
	case PropertyId::ColumnGap: target.column_gap(source.column_gap()); break;
	case PropertyId::Drag: target.drag(source.drag()); break;
	case PropertyId::TabIndex: target.tab_index(source.tab_index()); break;
	case PropertyId::Focus: target.focus(source.focus()); break;
	case PropertyId::ScrollbarMargin: target.scrollbar_margin(source.scrollbar_margin()); break;
	case PropertyId::OverscrollBehavior: target.overscroll_behavior(source.overscroll_behavior()); break;
	case PropertyId::PointerEvents: target.pointer_events(source.pointer_events()); break;
	case PropertyId::Perspective: target.perspective(source.perspective()); target.has_local_perspective(source.has_local_perspective()); break;
	case PropertyId::PerspectiveOriginX: target.perspective_origin_x(source.perspective_origin_x()); break;
	case PropertyId::PerspectiveOriginY: target.perspective_origin_y(source.perspective_origin_y()); break;
	case PropertyId::Transform: target.has_local_transform(source.has_local_transform()); break;
	case PropertyId::TransformOriginX: target.transform_origin_x(source.transform_origin_x()); break;
	case PropertyId::TransformOriginY: target.transform_origin_y(source.transform_origin_y()); break;
	case PropertyId::TransformOriginZ: target.transform_origin_z(source.transform_origin_z()); break;
	case PropertyId::Decorator: target.has_decorator(source.has_decorator()); break;
	case PropertyId::MaskImage: target.has_mask_image(source.has_standard_mask_image()); break;
	case PropertyId::UruiMaskImage: target.has_urui_mask_image(source.has_urui_mask_image()); break;
	case PropertyId::FontEffect: target.has_font_effect(source.has_font_effect()); break;
	case PropertyId::Filter: target.has_filter(source.has_filter()); break;
	case PropertyId::BackdropFilter: target.has_backdrop_filter(source.has_backdrop_filter()); break;
	case PropertyId::BoxShadow: target.has_box_shadow(source.has_box_shadow()); break;
	case PropertyId::FlexBasis: target.flex_basis(source.flex_basis()); break;
	case PropertyId::Language: target.language(source.language()); break;
	case PropertyId::Direction: target.direction(source.direction()); break;
	default: break; // Properties without a stored field are read through GetProperty.
	}
}
} // namespace

PropertyIdSet ElementStyle::ComputeValues(Style::ComputedValues& values, const Style::ComputedValues* parent_values,
	const Style::ComputedValues* document_values, bool values_are_default_initialized, float dp_ratio, Vector2f vp_dimensions)
{
	if (!AnyPropertiesDirty())
		return PropertyIdSet();
    const auto schema_revision = element->GetPropertySpecification().GetRegisteredPropertyRevision();
    const bool schema_changed = registered_property_revision != schema_revision;
    if (schema_changed)
    {
        // 默认值属于注册表；即使当前节点没有匹配规则变化，也必须刷新其变量依赖。
        for (auto id : element->GetPropertySpecification().GetRegisteredProperties())
        {
            const auto& name = element->GetPropertySpecification().GetPropertyName(id);
            if (StringUtilities::StartsWith(name, "--")) DirtyProperty(id);
        }
        registered_property_revision = schema_revision;
    }
	auto* document = element->GetOwnerDocument();
	const auto font_revision = document ? document->GetFontContext().GetRevision() : 0;
	const bool font_context_changed = font_context_revision != font_revision;
	font_context_revision = font_revision;
	if (font_context_changed) dirty_properties.Insert(PropertyId::FontFamily);
    computed_revision++;

	URUI_ZoneScopedC(0xFF7F50);

	// We're a bit conservative here and recompute shorthand substitutions also if any variables have been dirtied at
	// all. @performance We could be more strict and only recompute shorthands if any variables they *use* have been dirtied.
	if (!dirty_variables.empty() || !dirty_var_shorthands.empty())
	{
		ExpandVarShorthands(expanded_shorthands, GetPropertySources(), GetDirtyPropertiesRef());
	}
	// A failed substitution still changes the computed value to unset. Mark
	// dependencies before resolving, including font-size's early computation.
	if (!dirty_variables.empty()) {
		for (auto it = Iterate(); !it.AtEnd(); ++it) {
			const auto property = *it;
			if (property.second.unit == Unit::VAR_EXPRESSION) dirty_properties.Insert(property.first);
		}
	}

	// Generally, this is how it works:
	//   1. Assign default values (clears any removed properties)
	//   2. Inherit inheritable values from parent
	//   3. Assign any local properties (from inline style or stylesheet)
	//   4. Dirty properties in children that are inherited

    bool paint_only = !values_are_default_initialized && values.font_face_handle() != 0 && !font_context_changed && !schema_changed &&
        dirty_variables.empty() && dirty_var_shorthands.empty();
    if (paint_only) for (auto id : dirty_properties) {
        if (id != PropertyId::Opacity && id != PropertyId::Color && id != PropertyId::BackgroundColor) {
            paint_only = false; break;
        }
    }
    if (paint_only) {
        // 这三类属性不影响字体和长度计算；继承通知与样式代际仍走下方共同提交。
        bool unused_font_change = false;
        for (auto id : dirty_properties) {
            if (const auto* property = GetProperty(id))
                ComputeValue(values, dp_ratio, vp_dimensions, values.font_size(), document_values ? document_values->font_size() : values.font_size(),
                    unused_font_change, id, property);
            else {
                const auto* definition = element->GetPropertySpecification().GetProperty(id);
                CopyComputedProperty(values, definition->IsInherited() && parent_values ? *parent_values : DefaultComputedValues(), id);
            }
        }
    } else {
		Property property_storage;
		const float font_size_before = values.font_size();
		const auto display_before = values.display();
		const Style::LineHeight line_height_before = values.line_height();

		const float border_before[] = {values.border_top_width(), values.border_right_width(), values.border_bottom_width(), values.border_left_width()};

		// The next flag is just a small optimization, if the element was just created we don't need to copy all the default values.
		if (!values_are_default_initialized)
		{
			// This needs to be done in case some properties were removed and thus not in our local style anymore.
			// If we skipped this, the old dirty value would be unmodified, instead, now it is set to its default value.
			// Strictly speaking, we only really need to do this for the dirty, non-inherited values. However, in most
			// cases it seems simply assigning all non-inherited values is faster than iterating the dirty properties.
			values.CopyNonInherited(DefaultComputedValues());
		}

		if (parent_values)
			values.CopyInherited(*parent_values);
		else if (!values_are_default_initialized)
			values.CopyInherited(DefaultComputedValues());

		SmallUnorderedSet<String> variable_dependencies;
		bool dirty_em_properties = false;

		// Always do font-size first if dirty, because of em-relative values
		if (dirty_properties.Contains(PropertyId::FontSize))
		{
			if (const Property* property = GetLocalProperty(PropertyId::FontSize))
			{
				variable_dependencies.clear();
				property = ResolveVariables(PropertyId::FontSize, property, variable_dependencies, property_storage);
				if (property) {
					if (property->unit == Unit::CSS_WIDE) values.font_size(WideSource(*property, PropertyId::FontSize, parent_values, element->GetPropertySpecification()).font_size());
					else values.font_size(ComputeFontsize(property->GetNumericValue(), values, parent_values, document_values, dp_ratio, vp_dimensions));
				}
			}
			else if (parent_values)
				values.font_size(parent_values->font_size());

			if (font_size_before != values.font_size())
			{
				dirty_em_properties = true;
				dirty_properties.Insert(PropertyId::LineHeight);
			}
		}
		else
		{
			values.font_size(font_size_before);
		}

		const float font_size = values.font_size();
		const float document_font_size = (document_values ? document_values->font_size() : DefaultComputedValues().font_size());

		// Since vertical-align depends on line-height we compute this before iteration
		if (dirty_properties.Contains(PropertyId::LineHeight))
		{
			if (const Property* property = GetLocalProperty(PropertyId::LineHeight))
			{
				variable_dependencies.clear();
				property = ResolveVariables(PropertyId::LineHeight, property, variable_dependencies, property_storage);
				if (property) {
					if (property->unit == Unit::CSS_WIDE) {
						const auto height = WideSource(*property, PropertyId::LineHeight, parent_values, element->GetPropertySpecification()).line_height();
						values.line_height(height.inherit_type == Style::LineHeight::Number ?
							Style::LineHeight(font_size * height.inherit_value, Style::LineHeight::Number, height.inherit_value) : height);
					} else values.line_height(ComputeLineHeight(property, font_size, document_font_size, dp_ratio, vp_dimensions));
				}
			}
			else if (parent_values)
			{
				// Line height has a special inheritance case for numbers/percent: they inherit them directly instead of computed length, but for lengths,
				// they inherit the length. See CSS specs for details. Percent is already converted to number.
				if (parent_values->line_height().inherit_type == Style::LineHeight::Number)
					values.line_height(Style::LineHeight(font_size * parent_values->line_height().inherit_value, Style::LineHeight::Number,
						parent_values->line_height().inherit_value));
				else
					values.line_height(parent_values->line_height());
			}

			if (line_height_before.value != values.line_height().value || line_height_before.inherit_value != values.line_height().inherit_value)
				dirty_properties.Insert(PropertyId::VerticalAlign);
		}
		else
		{
			values.line_height(line_height_before);
		}

		bool dirty_font_face_handle = font_context_changed || values.font_face_handle() == 0;

		for (auto it = Iterate(); !it.AtEnd(); ++it)
		{
			variable_dependencies.clear();
			auto id_property_pair = *it;
			const PropertyId id = id_property_pair.first;
			const Property* property = ResolveVariables(id, &id_property_pair.second, variable_dependencies, property_storage);
			if (!property)
				continue;

			for (const String& variable_name : variable_dependencies)
			{
				if (dirty_variables.count(variable_name) != 0)
					dirty_properties.Insert(id);
			}

			if (dirty_em_properties && property->unit == Unit::EM)
				dirty_properties.Insert(id);

			if (property->unit == Unit::CSS_WIDE) {
				CopyComputedProperty(values, WideSource(*property, id, parent_values, element->GetPropertySpecification()), id);
				if (id == PropertyId::FontFamily || id == PropertyId::FontStyle || id == PropertyId::FontWeight || id == PropertyId::FontKerning ||
					id == PropertyId::FontSize || id == PropertyId::LetterSpacing) dirty_font_face_handle = true;
			} else ComputeValue(values, dp_ratio, vp_dimensions, font_size, document_font_size, dirty_font_face_handle, id, property);
		}

		const bool out_of_flow = values.position() == Style::Position::Absolute || values.position() == Style::Position::Fixed;
		const bool flex_item = parent_values && (parent_values->display() == Style::Display::Flex || parent_values->display() == Style::Display::InlineFlex);
		if (element->GetTagName() != "#text" && element->GetTagName() != "#comment" &&
			(out_of_flow || values.float_() != Style::Float::None || flex_item || element->GetOwnerDocument() == element)) {
			switch (values.display()) {
			case Style::Display::InlineFlex: values.display(Style::Display::Flex); break;
			case Style::Display::InlineTable: values.display(Style::Display::Table); break;
			case Style::Display::Inline: case Style::Display::InlineBlock:
			case Style::Display::TableRow: case Style::Display::TableRowGroup:
			case Style::Display::TableColumn: case Style::Display::TableColumnGroup: case Style::Display::TableCell:
				values.display(Style::Display::Block); break;
			default: break;
			}
		}
		if (values.display() != display_before) dirty_properties.Insert(PropertyId::Display);

		// Border width computes to zero for none/hidden. Resolve all four sides even
		// when only style was authored, since the omitted width defaults to medium.
		auto border_width = [&](PropertyId width, PropertyId style) {
			const auto* line_style = GetProperty(style);
			if (!line_style || line_style->unit != Unit::KEYWORD || line_style->Get<int>() != 2) return 0.f;
			const auto* property = GetLocalProperty(width);
			if (property) {
				variable_dependencies.clear();
				property = ResolveVariables(width, property, variable_dependencies, property_storage);
			}
			if (!property) property = element->GetPropertySpecification().GetProperty(width)->GetDefaultValue();
			if (property->unit == Unit::CSS_WIDE) {
				const auto& source = WideSource(*property, width, parent_values, element->GetPropertySpecification());
				switch (width) {
				case PropertyId::BorderTopWidth: return source.border_top_width();
				case PropertyId::BorderRightWidth: return source.border_right_width();
				case PropertyId::BorderBottomWidth: return source.border_bottom_width();
				case PropertyId::BorderLeftWidth: return source.border_left_width();
				default: return 0.f;
				}
			}
			if (property->unit == Unit::KEYWORD) {
				constexpr float keyword_widths[] = {1.f, 3.f, 5.f}; // thin, medium, thick
				const int keyword = property->Get<int>();
				return keyword >= 0 && keyword < 3 ? keyword_widths[keyword] : 0.f;
			}
			return float(ComputeBorderWidth(ComputeLength(property->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions)));
		};
		values.border_top_width(int16_t(border_width(PropertyId::BorderTopWidth, PropertyId::BorderTopStyle)));
		if (values.border_top_width() != border_before[0]) dirty_properties.Insert(PropertyId::BorderTopWidth);
		values.border_right_width(int16_t(border_width(PropertyId::BorderRightWidth, PropertyId::BorderRightStyle)));
		if (values.border_right_width() != border_before[1]) dirty_properties.Insert(PropertyId::BorderRightWidth);
		values.border_bottom_width(int16_t(border_width(PropertyId::BorderBottomWidth, PropertyId::BorderBottomStyle)));
		if (values.border_bottom_width() != border_before[2]) dirty_properties.Insert(PropertyId::BorderBottomWidth);
		values.border_left_width(int16_t(border_width(PropertyId::BorderLeftWidth, PropertyId::BorderLeftStyle)));
		if (values.border_left_width() != border_before[3]) dirty_properties.Insert(PropertyId::BorderLeftWidth);

		// The font-face handle is nulled when local font properties are set. In that case we need to retrieve a new handle.
		if (dirty_font_face_handle)
		{
			URUI_ZoneScopedN("FontFaceHandle");
			auto* document = element->GetOwnerDocument();
			values.font_face_handle(document ? document->GetFontContext().ResolveFace(values.font_family(), values.font_style(), values.font_weight(), values.font_size()) :
				GetFontEngineInterface()->GetFontFaceHandle(values.font_family(), values.font_style(), values.font_weight(), values.font_size()));
		}

    }

	// Next, pass inheritable dirty properties onto our children
	PropertyIdSet dirty_inherited_properties = (dirty_properties & element->GetPropertySpecification().GetRegisteredInheritedProperties());
	// A parent's flex-container state changes its children's computed display.
	if (dirty_properties.Contains(PropertyId::Display)) dirty_inherited_properties.Insert(PropertyId::Display);

	// Special case for text-overflow: It's not really inherited, but the value is used by the element's children. Insert
	// it here so they are notified of a change.
	if (dirty_properties.Contains(PropertyId::TextOverflow))
		dirty_inherited_properties.Insert(PropertyId::TextOverflow);

	// Propagate dirty inherited properties to children. Var shorthands are local to the element and never inherited.
	if (!dirty_properties.Empty() || !dirty_variables.empty())
	{
		for (int i = 0; i < element->GetNumChildren(true); i++)
		{
			auto child = element->GetChild(i);
			child->GetStyle()->dirty_properties |= dirty_inherited_properties;
			child->GetStyle()->dirty_variables.insert(dirty_variables.begin(), dirty_variables.end());
			for (auto id : dirty_properties) {
				const auto* local = child->GetStyle()->GetLocalProperty(id);
				if (local && (local->unit == Unit::VAR_EXPRESSION ||
					(local->unit == Unit::CSS_WIDE && local->Get<CssWideKeyword>() == CssWideKeyword::Inherit)))
					child->GetStyle()->dirty_properties.Insert(id);
			}
		}
	}

    // Independent scalar/color custom values can update a snapshot in place.
    // Font/schema changes or unresolved variable dependencies still require a full read.
    bool value_only = !dirty_properties.Empty() && !font_context_changed && !schema_changed;
    value_snapshot_values.clear();
    const auto& snapshot_specification = element->GetPropertySpecification();
    if(value_only)for (auto id : dirty_properties) {
        if (id == PropertyId::Opacity) {
            // 快照与最终 computed 值一致，不能导出未规范化的百分比或越界作者值。
            value_snapshot_values.push_back({id, 1u, {values.opacity(), 0, 0, 0}});
            continue;
        }
        const auto* definition = snapshot_specification.GetProperty(id);
        const auto* value=element->GetProperty(id);
        if (id < PropertyId::FirstCustomId || !definition || !value ||
            (value->unit!=Unit::NUMBER&&value->unit!=Unit::COLOUR)) {
            value_only = false; break;
        }
        SnapshotValue output{id,value->unit==Unit::NUMBER?1u:4u,{0,0,0,0}};
        if(output.kind==1)output.values[0]=value->Get<float>();
        else {const auto color=value->Get<Colourb>();output.values[0]=color.red/255.f;output.values[1]=color.green/255.f;output.values[2]=color.blue/255.f;output.values[3]=color.alpha/255.f;}
        value_snapshot_values.push_back(output);
    }
    if (value_only) for (const auto& name : dirty_variables) {
        const auto id = snapshot_specification.GetPropertyId(name);
        if (id == PropertyId::Invalid || !dirty_properties.Contains(id)) { value_only = false; break; }
    }
    value_snapshot_revision = value_only ? computed_revision - 1 : UINT64_MAX;
    if(!value_only)value_snapshot_values.clear();
	PropertyIdSet result(std::move(dirty_properties));
	dirty_properties.Clear();
	dirty_variables.clear();
	dirty_var_shorthands.clear();
	return result;
}


bool ElementStyle::PrepareSequenceBaseline(uint64_t owner, const PropertyIdSet& properties, PropertyDictionary& baseline) const
{
    if (!owner || sequence_layers.find(owner) != sequence_layers.end()) return false;
    for (PropertyId id : properties)
    {
        for (const auto& entry : sequence_layers) if (entry.second.baseline.GetProperty(id)) return false;
        const auto* property = GetProperty(id);
        if (!property) return false;
        baseline.SetProperty(id, *property);
    }
    return true;
}
const PropertyDictionary* ElementStyle::GetSequenceBaseline(uint64_t owner) const
{
    const auto found = sequence_layers.find(owner);
    return found == sequence_layers.end() ? nullptr : &found->second.baseline;
}
bool ElementStyle::CaptureSequence(uint64_t owner, const PropertyDictionary& baseline)
{
    if (!owner || sequence_layers.find(owner) != sequence_layers.end()) return false;
    sequence_layers.emplace(owner, SequenceLayer{baseline, {}});
    RecomposeSequenceAnimations();
    return true;
}
bool ElementStyle::SetSequenceValues(uint64_t owner, const PropertyDictionary& values)
{
    auto found = sequence_layers.find(owner);
    if (found == sequence_layers.end()) return false;
    PropertyDictionary candidate;
    for (const auto& entry : values.GetProperties())
    {
        if (!found->second.baseline.GetProperty(entry.first)) return false;
        Property property = entry.second;
        property.definition = element->GetPropertySpecification().GetProperty(entry.first);
        if (!property.definition) return false;
        property.priority = {};
        property.priority.originImportance = CssOriginImportance::Animation;
        candidate.SetProperty(entry.first, property);
    }
    PropertyIdSet changed;
    for (const auto& entry : found->second.values.GetProperties()) changed.Insert(entry.first);
    for (const auto& entry : candidate.GetProperties()) changed.Insert(entry.first);
    found->second.values = std::move(candidate);
    RecomposeSequenceAnimations();
    DirtyProperties(changed);
    return true;
}
void ElementStyle::RemoveSequence(uint64_t owner)
{
    auto found = sequence_layers.find(owner);
    if (found == sequence_layers.end()) return;
    PropertyIdSet changed;
    for (const auto& entry : found->second.values.GetProperties()) changed.Insert(entry.first);
    sequence_layers.erase(found);
    RecomposeSequenceAnimations();
    DirtyProperties(changed);
}
void ElementStyle::RecomposeSequenceAnimations()
{
    composed_animations = animation_properties;
    for (const auto& layer : sequence_layers)
        for (const auto& property : layer.second.values.GetProperties()) composed_animations.SetProperty(property.first, property.second);
}

ElementStyle::PropertySources ElementStyle::GetPropertySources() const
{
	return {element, definition.get(), inline_properties, nullptr, sequence_layers.empty() ? &animation_properties : &composed_animations, &transition_properties};
}

ElementStyle::DirtyPropertiesRef ElementStyle::GetDirtyPropertiesRef()
{
	return {dirty_properties, dirty_variables, dirty_var_shorthands};
}

void ElementStyle::DirtyProperties(const PropertyIdSet& properties)
{
    element->DirtyNativeRendering();
	dirty_properties |= properties;
}

const Property* ElementStyle::ResolveVariables(PropertyId id, const Property* property, SmallUnorderedSet<String>& variable_dependencies,
	Property& property_storage) const
{
	return ResolveVariables(GetPropertySources(), expanded_shorthands, id, property, variable_dependencies, property_storage);
}

void ElementStyle::ComputeValue(Style::ComputedValues& values, float dp_ratio, Vector2f vp_dimensions, const float font_size,
	const float document_font_size, bool& dirty_font_face_handle, const PropertyId id, const Property* p)
{
	using namespace Style;

	// clang-format off
	switch (id)
	{
	case PropertyId::MarginTop:
		values.margin_top(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::MarginRight:
		values.margin_right(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::MarginBottom:
		values.margin_bottom(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::MarginLeft:
		values.margin_left(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::PaddingTop:
		values.padding_top(ComputeLengthPercentage(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::PaddingRight:
		values.padding_right(ComputeLengthPercentage(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::PaddingBottom:
		values.padding_bottom(ComputeLengthPercentage(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::PaddingLeft:
		values.padding_left(ComputeLengthPercentage(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::BorderTopWidth:
		values.border_top_width(ComputeBorderWidth(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions)));
		break;
	case PropertyId::BorderRightWidth:
		values.border_right_width(
			ComputeBorderWidth(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions)));
		break;
	case PropertyId::BorderBottomWidth:
		values.border_bottom_width(
			ComputeBorderWidth(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions)));
		break;
	case PropertyId::BorderLeftWidth:
		values.border_left_width(ComputeBorderWidth(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions)));
		break;

	case PropertyId::BorderTopColor:
		values.border_top_color(p->Get<Colourb>());
		break;
	case PropertyId::BorderRightColor:
		values.border_right_color(p->Get<Colourb>());
		break;
	case PropertyId::BorderBottomColor:
		values.border_bottom_color(p->Get<Colourb>());
		break;
	case PropertyId::BorderLeftColor:
		values.border_left_color(p->Get<Colourb>());
		break;

	case PropertyId::BorderTopLeftRadius:
		values.border_top_left_radius(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::BorderTopRightRadius:
		values.border_top_right_radius(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::BorderBottomRightRadius:
		values.border_bottom_right_radius(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::BorderBottomLeftRadius:
		values.border_bottom_left_radius(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::Display:
		values.display((Display)p->Get<int>());
		break;
	case PropertyId::Position:
		values.position((Position)p->Get<int>());
		break;

	case PropertyId::Top:
		values.top(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::Right:
		values.right(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::Bottom:
		values.bottom(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::Left:
		values.left(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::Float:
		values.float_((Float)p->Get<int>());
		break;
	case PropertyId::Clear:
		values.clear((Clear)p->Get<int>());
		break;
	case PropertyId::BoxSizing:
		values.box_sizing((BoxSizing)p->Get<int>());
		break;

	case PropertyId::ZIndex:
		values.z_index((p->unit == Unit::KEYWORD ? ZIndex(ZIndex::Auto) : ZIndex(ZIndex::Number, p->Get<float>())));
		break;

	case PropertyId::Width:
		values.width(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::MinWidth:
		values.min_width(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::MaxWidth:
		values.max_width(ComputeMaxSize(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::Height:
		values.height(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::MinHeight:
		values.min_height(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::MaxHeight:
		values.max_height(ComputeMaxSize(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::LineHeight:
		// (Line-height computed above)
		break;
	case PropertyId::VerticalAlign:
		values.vertical_align(ComputeVerticalAlign(p, values.line_height().value, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::OverflowX:
		values.overflow_x((Overflow)p->Get< int >());
		break;
	case PropertyId::OverflowY:
		values.overflow_y((Overflow)p->Get< int >());
		break;
	case PropertyId::Clip:
		values.clip(ComputeClip(p));
		break;
	case PropertyId::Visibility:
		values.visibility((Visibility)p->Get< int >());
		break;
	case PropertyId::TextOverflow:
		values.text_overflow(p->unit == Unit::KEYWORD ? p->Get<TextOverflow>() : TextOverflow::String);
		break;

	case PropertyId::BackgroundColor:
		values.background_color(p->Get<Colourb>());
		break;
	case PropertyId::Color:
		values.color(p->Get<Colourb>());
		break;
	case PropertyId::ImageColor:
		values.image_color(p->Get<Colourb>());
		break;
	case PropertyId::Opacity:
		values.opacity(Math::Clamp(p->Get<float>() * (p->unit == Unit::PERCENT ? .01f : 1.f), 0.f, 1.f));
		break;

	case PropertyId::FontFamily:
		// Fetched from element's properties.
		dirty_font_face_handle = true;
		break;
	case PropertyId::FontStyle:
		values.font_style((FontStyle)p->Get< int >());
		dirty_font_face_handle = true;
		break;
	case PropertyId::FontWeight:
		values.font_weight((FontWeight)p->Get< int >());
		dirty_font_face_handle = true;
		break;
	case PropertyId::FontSize:
		// (font-size computed above)
		dirty_font_face_handle = true;
		break;
	case PropertyId::FontKerning:
		values.font_kerning((FontKerning)p->Get<int>());
		dirty_font_face_handle = true;
		break;
	case PropertyId::LetterSpacing:
		values.letter_spacing(p->unit == Unit::KEYWORD ? 0.f : ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		dirty_font_face_handle = true;
		break;

	case PropertyId::TextAlign:
		values.text_align((TextAlign)p->Get<int>());
		break;
	case PropertyId::TextDecoration:
		values.text_decoration((TextDecoration)p->Get<int>());
		break;
	case PropertyId::TextTransform:
		values.text_transform((TextTransform)p->Get<int>());
		break;
	case PropertyId::WhiteSpace:
		values.white_space((WhiteSpace)p->Get<int>());
		break;
	case PropertyId::WordBreak:
		values.word_break((WordBreak)p->Get<int>());
		break;

	case PropertyId::RowGap:
		values.row_gap(ComputeLengthPercentage(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::ColumnGap:
		values.column_gap(ComputeLengthPercentage(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::Drag:
		values.drag((Drag)p->Get< int >());
		break;
	case PropertyId::TabIndex:
		values.tab_index((TabIndex)p->Get< int >());
		break;
	case PropertyId::Focus:
		values.focus((Focus)p->Get<int>());
		break;
	case PropertyId::ScrollbarMargin:
		values.scrollbar_margin(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::OverscrollBehavior:
		values.overscroll_behavior((OverscrollBehavior)p->Get<int>());
		break;
	case PropertyId::PointerEvents:
		values.pointer_events((PointerEvents)p->Get<int>());
		break;

	case PropertyId::Perspective:
		values.perspective(p->unit == Unit::KEYWORD ? 0.f : ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		values.has_local_perspective(values.perspective() > 0.f);
		break;
	case PropertyId::PerspectiveOriginX:
		values.perspective_origin_x(ComputeOrigin(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::PerspectiveOriginY:
		values.perspective_origin_y(ComputeOrigin(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::Transform:
		values.has_local_transform(p->Get<TransformPtr>() != nullptr);
		break;
	case PropertyId::TransformOriginX:
		values.transform_origin_x(ComputeOrigin(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::TransformOriginY:
		values.transform_origin_y(ComputeOrigin(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;
	case PropertyId::TransformOriginZ:
		values.transform_origin_z(ComputeLength(p->GetNumericValue(), font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::Decorator:
		values.has_decorator(p->unit == Unit::DECORATOR && p->value.GetType() == Variant::DECORATORSPTR && p->value.GetReference<DecoratorsPtr>());
		break;
	case PropertyId::MaskImage: {
        const auto declarations = p->unit == Unit::DECORATOR && p->value.GetType() == Variant::DECORATORSPTR ? p->value.GetReference<DecoratorsPtr>() : DecoratorsPtr{};
        values.has_mask_image(declarations && !declarations->list.empty());
        break;
    }
	case PropertyId::UruiMaskImage:
		values.has_urui_mask_image(p->unit == Unit::DECORATOR && p->value.GetType() == Variant::DECORATORSPTR && p->value.GetReference<DecoratorsPtr>());
		break;
	case PropertyId::FontEffect:
		values.has_font_effect(p->unit == Unit::FONTEFFECT && p->value.GetType() == Variant::FONTEFFECTSPTR && p->value.GetReference<FontEffectsPtr>());
		break;
	case PropertyId::Filter:
		values.has_filter(p->unit == Unit::FILTER && p->value.GetType() == Variant::FILTERSPTR && p->value.GetReference<FiltersPtr>());
		break;
	case PropertyId::BackdropFilter:
		values.has_backdrop_filter(p->unit == Unit::FILTER && p->value.GetType() == Variant::FILTERSPTR && p->value.GetReference<FiltersPtr>());
		break;
	case PropertyId::BoxShadow:
		values.has_box_shadow(p->unit == Unit::BOXSHADOWLIST && p->value.GetType() == Variant::BOXSHADOWLIST && !p->value.GetReference<BoxShadowList>().empty());
		break;

	case PropertyId::FlexBasis:
		values.flex_basis(ComputeLengthPercentageAuto(p, font_size, document_font_size, dp_ratio, vp_dimensions));
		break;

	case PropertyId::Language:
		values.language(p->Get<String>());
		break;
	case PropertyId::Direction:
		values.direction(p->Get<Direction>());
		break;

	// Fetched from element's properties.
	case PropertyId::Cursor:
	case PropertyId::Transition:
	case PropertyId::Animation:
	case PropertyId::AlignContent:
	case PropertyId::AlignItems:
	case PropertyId::AlignSelf:
	case PropertyId::FlexDirection:
	case PropertyId::FlexGrow:
	case PropertyId::FlexShrink:
	case PropertyId::FlexWrap:
	case PropertyId::JustifyContent:
		break;
	// Navigation properties. Must be manually retrieved with 'GetProperty()'.
	case PropertyId::NavUp:
	case PropertyId::NavDown:
	case PropertyId::NavLeft:
	case PropertyId::NavRight:
		break;
	// Unhandled properties. Must be manually retrieved with 'GetProperty()'.
	case PropertyId::BorderTopStyle:
	case PropertyId::BorderRightStyle:
	case PropertyId::BorderBottomStyle:
	case PropertyId::BorderLeftStyle:
	case PropertyId::FillImage:
	case PropertyId::CaretColor:
	case PropertyId::BorderImageSource:
	case PropertyId::BorderImageSlice:
	case PropertyId::BorderImageWidth:
	case PropertyId::BorderImageOutset:
	case PropertyId::BorderImageRepeat:
	case PropertyId::BackgroundImage:
	case PropertyId::BackgroundSize:
	case PropertyId::BackgroundPosition:
	case PropertyId::BackgroundRepeat:
	case PropertyId::BackgroundOrigin:
	case PropertyId::BackgroundClip:
	case PropertyId::MaskSize:
	case PropertyId::MaskPosition:
	case PropertyId::MaskRepeat:
	case PropertyId::MaskOrigin:
	case PropertyId::MaskClip:
		break;
	// Invalid properties
	case PropertyId::Invalid:
	case PropertyId::NumDefinedIds:
	case PropertyId::MaxNumIds:
		break;
	}
	// clang-format on
}
} // namespace URUI
