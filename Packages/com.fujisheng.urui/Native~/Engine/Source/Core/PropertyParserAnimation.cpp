#include <climits>
#include "CssTokenizer.h"
#include "PropertyParserAnimation.h"
#include <cmath>
#include "../../Include/URUI/Core/PropertyDefinition.h"
#include "../../Include/URUI/Core/PropertyIdSet.h"
#include "../../Include/URUI/Core/StringUtilities.h"
#include "../../Include/URUI/Core/StyleSheetSpecification.h"
#include "PropertyShorthandDefinition.h"

namespace URUI {

enum class KeywordType { None, Tween, All, Alternate, Infinite, Paused };

struct Keyword {
	KeywordType type;
	Tween tween;
	Keyword(Tween tween) : type(KeywordType::Tween), tween(tween) {}
	Keyword(KeywordType type) : type(type) {}

	bool ValidTransition() const { return type == KeywordType::None || type == KeywordType::Tween || type == KeywordType::All; }
	bool ValidAnimation() const
	{
		return type == KeywordType::None || type == KeywordType::Tween || type == KeywordType::Alternate || type == KeywordType::Infinite ||
			type == KeywordType::Paused;
	}
};

struct PropertyParserAnimationData {
	const UnorderedMap<String, Keyword> keywords = {
		{"none", {KeywordType::None}},
		{"all", {KeywordType::All}},
		{"alternate", {KeywordType::Alternate}},
		{"infinite", {KeywordType::Infinite}},
		{"paused", {KeywordType::Paused}},
        {"linear", {Tween{Tween::Linear, Tween::In}}},
        {"ease", {Tween{.25f, .1f, .25f, 1.f}}},
        {"ease-in", {Tween{.42f, 0.f, 1.f, 1.f}}},
        {"ease-out", {Tween{0.f, 0.f, .58f, 1.f}}},
        {"ease-in-out", {Tween{.42f, 0.f, .58f, 1.f}}},

	};
};

ControlledLifetimeResource<PropertyParserAnimationData> PropertyParserAnimation::parser_data;

void PropertyParserAnimation::Initialize()
{
	parser_data.Initialize();
}

void PropertyParserAnimation::Shutdown()
{
	if (parser_data) parser_data.Shutdown();
}

PropertyParserAnimation::PropertyParserAnimation(Type type) : type(type) {}

bool PropertyParserAnimation::ParseValue(Property& property, const String& value, const ParameterMap& /*parameters*/) const
{
	StringList list_of_values;
	StringUtilities::ExpandString(list_of_values, value, ',', '(', ')');

	bool result = false;
	if (type == ANIMATION_PARSER)
	{
		result = ParseAnimation(property, list_of_values);
	}
	else if (type == TRANSITION_PARSER)
	{
		const auto* specification = property.definition ? property.definition->GetSpecification() : nullptr;
		result = ParseTransition(property, list_of_values, specification ? *specification : StyleSheetSpecification::GetPropertySpecification());
	}

	return result;
}

static bool ParseBezier(const String& argument, Tween& tween)
{
    float x1, y1, x2, y2; int consumed = 0;
    if (sscanf(argument.c_str(), "cubic-bezier( %f , %f , %f , %f ) %n", &x1, &y1, &x2, &y2, &consumed) != 4 ||
        consumed != int(argument.size()) || !std::isfinite(x1) || !std::isfinite(y1) || !std::isfinite(x2) || !std::isfinite(y2) ||
        x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1) return false;
    tween = Tween(x1, y1, x2, y2); return true;
}

bool PropertyParserAnimation::ParseAnimation(Property& property, const StringList& animation_values)
{
	AnimationList animation_list;

	for (const String& single_animation_value : animation_values)
	{
		Animation animation;
        animation.tween = parser_data->keywords.at("ease").tween;
        bool easing_found = false;

		StringList arguments;
		StringUtilities::ExpandString(arguments, single_animation_value, ' ', '(', ')');

		bool duration_found = false;
		bool delay_found = false;
		bool num_iterations_found = false;
		int none_count = 0;

		for (auto& argument : arguments)
		{
			if (argument.empty())
				continue;

            if (StringUtilities::StartsWith(argument, "linear("))
            { if (easing_found || !Tween::ParseLinear(argument, animation.tween)) return false; easing_found = true; continue; }
            if (StringUtilities::StartsWith(argument, "cubic-bezier"))
            { if (easing_found || !ParseBezier(argument, animation.tween)) return false; easing_found = true; continue; }
			// See if we have a <keyword> or <tween> specifier as defined in keywords
			auto it = parser_data->keywords.find(StringUtilities::ToLower(argument));
			if (it != parser_data->keywords.end() && it->second.ValidAnimation())
			{
				switch (it->second.type)
				{
				case KeywordType::None:
				{
					// The first 'none' fills animation-fill-mode; a second may name no animation.
					if (++none_count > 2 || (none_count == 2 && !animation.name.empty())) return false;
				}
				break;
				case KeywordType::Tween: if (easing_found) return false; easing_found = true; animation.tween = it->second.tween; break;
				case KeywordType::Alternate: animation.alternate = true; break;
				case KeywordType::Infinite:
					if (num_iterations_found)
						return false;
					animation.num_iterations = -1;
					num_iterations_found = true;
					break;
				case KeywordType::Paused: animation.paused = true; break;
				default: break;
				}
			}
			else
			{
				// Either <duration>, <delay>, <num_iterations> or a <keyframes-name>
				String number_text, unit;
				if (SplitCssNumericValue(argument, number_text, unit))
				{
					const float number = std::strtof(number_text.c_str(), nullptr);
					if (!std::isfinite(number)) return false;
					if (unit == "s" || unit == "ms") {
						const float seconds = unit == "ms" ? number * .001f : number;
						if (!duration_found) { duration_found = true; animation.duration = seconds; }
						else if (!delay_found) { delay_found = true; animation.delay = seconds; }
						else return false;
					} else if (unit.empty() && !num_iterations_found && number > 0 && number <= INT_MAX && std::floor(number) == number) {
						animation.num_iterations = int(number); num_iterations_found = true;
					} else return false;
				}
				else
				{
                    if (none_count == 2 || !animation.name.empty()) return false;
                    Vector<Diagnostic> diagnostics;
                    const auto name = TokenizeCss(argument, "urui:animation-name", diagnostics);
                    if (!diagnostics.empty() || name.size() != 1 || name[0].kind != CssTokenKind::Ident) return false;
                    animation.name = name[0].value;
				}
			}
		}

		// An omitted animation-name defaults to none.
		if (animation.duration < 0.0f || (animation.num_iterations < -1 || animation.num_iterations == 0))
		{
			return false;
		}

		if (!animation.name.empty()) animation_list.push_back(std::move(animation));
	}

	property.value = std::move(animation_list);
	property.unit = Unit::ANIMATION;

	return true;
}

bool PropertyParserAnimation::ParseTransition(Property& property, const StringList& transition_values, const PropertySpecification& specification)
{
	TransitionList transition_list{false, false, {}};

	for (const String& single_transition_value : transition_values)
	{
		Transition transition;
        transition.tween = parser_data->keywords.at("ease").tween;
        bool easing_found = false, property_found = false, none_property = false;
		PropertyIdSet target_property_ids;

		StringList arguments;
		StringUtilities::ExpandString(arguments, single_transition_value, ' ', '(', ')');

		bool duration_found = false;
		bool delay_found = false;

		for (auto& argument : arguments)
		{
			if (argument.empty())
				continue;

            if (StringUtilities::StartsWith(argument, "linear("))
            { if (easing_found || !Tween::ParseLinear(argument, transition.tween)) return false; easing_found = true; continue; }
            if (StringUtilities::StartsWith(argument, "cubic-bezier"))
            { if (easing_found || !ParseBezier(argument, transition.tween)) return false; easing_found = true; continue; }
			// See if we have a <keyword> or <tween> specifier as defined in keywords
			auto it = parser_data->keywords.find(AsciiLowerCss(argument));
			if (it != parser_data->keywords.end() && it->second.ValidTransition())
			{
				if (it->second.type == KeywordType::None)
				{
					if (property_found || transition_values.size() != 1) return false;
					property_found = true;
					none_property = true;
				}
				else if (it->second.type == KeywordType::All)
				{
					if (transition_list.transitions.size() > 0) // The all keyword can not be part of multiple definitions
						return false;
                    if (property_found) return false;
                    property_found = true;
					transition_list.all = true;
				}
				else if (it->second.type == KeywordType::Tween)
				{
                    if (easing_found) return false;
                    easing_found = true;
					transition.tween = it->second.tween;
				}
			}
			else
			{
				// Either <duration>, <delay> or a <property name>
				String number_text, unit;
				if (SplitCssNumericValue(argument, number_text, unit))
				{
					if (unit != "s" && unit != "ms") return false;
					float seconds = std::strtof(number_text.c_str(), nullptr);
					if (unit == "ms") seconds *= .001f;
					if (!std::isfinite(seconds)) return false;
					if (!duration_found) { duration_found = true; transition.duration = seconds; }
					else if (!delay_found) { delay_found = true; transition.delay = seconds; }
					else return false;
				}
				else
				{
                    if (property_found) return false;
                    property_found = true;
                    const auto canonical = argument.compare(0, 2, "--") == 0 ? argument : AsciiLowerCss(argument);
					if (auto shorthand = specification.GetShorthand(canonical))
					{
						PropertyIdSet underlying_properties = specification.GetShorthandUnderlyingProperties(shorthand->id);
						target_property_ids |= underlying_properties;
					}
					else if (auto definition = specification.GetProperty(canonical))
					{
						// Single property
						target_property_ids.Insert(definition->GetId());
					}
					else
					{
						// Unknown property name
						return false;
					}
				}
			}
		}

		// An omitted transition-property defaults to all.
		if (!property_found) transition_list.all = true;
		// Validate every token, including the remainder after 'none'.
		if ((transition_list.all && !target_property_ids.Empty())    //
			|| (!transition_list.all && !none_property && target_property_ids.Empty()) //
			|| transition.duration < 0.0f                            //
			|| transition.reverse_adjustment_factor < 0.0f           //
			|| transition.reverse_adjustment_factor > 1.0f           //
		)
		{
			return false;
		}
		if (none_property)
		{
			transition_list.none = true;
			continue;
		}

		if (transition_list.all)
		{
			transition.id = PropertyId::Invalid;
			transition_list.transitions.push_back(transition);
		}
		else
		{
			for (const PropertyId id : target_property_ids)
			{
				transition.id = id;
				transition.property_name = specification.GetPropertyName(id);
				transition_list.transitions.push_back(transition);
			}
		}
	}

	property.value = std::move(transition_list);
	property.unit = Unit::TRANSITION;

	return true;
}

} // namespace URUI
