#include "../../Include/URUI/Core/PropertyDefinition.h"
#include "../../Include/URUI/Core/Log.h"
#include "../../Include/URUI/Core/StyleSheetSpecification.h"
#include "CssTokenizer.h"

namespace URUI
{

    PropertyDefinition::PropertyDefinition(PropertyId id, const String& _default_value, bool _inherited,
                                           bool _forces_layout, const PropertySpecification* specification)
        : id(id), specification(specification), default_value(_default_value, Unit::UNKNOWN),
          relative_target(RelativeTarget::None)
    {
        inherited = _inherited;
        forces_layout = _forces_layout;
        default_value.definition = this;
    }

    PropertyDefinition::~PropertyDefinition()
    {
    }

    UniquePtr<PropertyDefinition> PropertyDefinition::Clone(const PropertySpecification& specification) const
    {
        auto copy = MakeUnique<PropertyDefinition>(id, String(), inherited, forces_layout, &specification);
        copy->default_value = default_value;
        copy->default_value.definition = copy.get();
        copy->parsers = parsers;
        copy->relative_target = relative_target;
        copy->registration_syntax = registration_syntax;
        return copy;
    }

    PropertyDefinition& PropertyDefinition::AddParser(const String& parser_name, const String& parser_parameters)
    {
        ParserState new_parser;

        // Fetch the parser.
        new_parser.parser = StyleSheetSpecification::GetParser(parser_name);
        new_parser.name = parser_name;
        if (new_parser.parser == nullptr)
        {
            Log::Message(Log::LT_ERROR, "Property was registered with invalid parser '%s'.", parser_name.c_str());
            return *this;
        }

        // Split the parameter list, and set up the map.
        if (!parser_parameters.empty())
        {
            StringList parameter_list;
            StringUtilities::ExpandString(parameter_list, parser_parameters);

            int parameter_value = 0;
            for (const String& parameter : parameter_list)
            {
                // Look for an optional parameter value such as in "normal=400".
                const size_t i_equal = parameter.find('=');
                if (i_equal != String::npos)
                {
                    if (!TypeConverter<String, int>::Convert(parameter.substr(i_equal + 1), parameter_value))
                    {
                        Log::Message(Log::LT_ERROR, "Parser was added with invalid parameter '%s'.", parameter.c_str());
                        return *this;
                    }
                }

                new_parser.parameters[parameter.substr(0, i_equal)] = parameter_value;
                parameter_value += 1;
            }
        }

        const int parser_index = (int)parsers.size();
        parsers.push_back(new_parser);

        // If the default value has not been parsed successfully yet, run it through the new parser.
        if (default_value.unit == Unit::UNKNOWN)
        {
            String unparsed_value = default_value.value.Get<String>();
            if (new_parser.parser->ParseValue(default_value, unparsed_value, new_parser.parameters))
            {
                default_value.parser_index = parser_index;
            }
            else
            {
                default_value.value = unparsed_value;
                default_value.unit = Unit::UNKNOWN;
            }
        }

        return *this;
    }

    bool PropertyDefinition::ParseValue(Property& property, const String& value) const
    {
        if (!registration_syntax.empty() && registration_syntax.front() != '<')
        {
            Vector<Diagnostic> diagnostics;
            const auto tokens = TokenizeCss(value, "urui:registered-keyword", diagnostics);
            const CssToken* name = nullptr;
            for (const auto& token : tokens)
            {
                if (token.kind != CssTokenKind::Whitespace)
                {
                    if (name || token.kind != CssTokenKind::Ident)
                    {
                        return false;
                    }
                    name = &token;
                }
            }
            if (!name || !diagnostics.empty() || parsers.empty())
            {
                return false;
            }
            const auto found = parsers[0].parameters.find(name->value);
            if (found == parsers[0].parameters.end())
            {
                return false;
            }
            property = Property(found->second, Unit::KEYWORD);
            property.definition = this;
            property.parser_index = 0;
            return true;
        }
        for (size_t i = 0; i < parsers.size(); i++)
        {
            property.definition = this;
            if (parsers[i].parser->ParseValue(property, value, parsers[i].parameters))
            {
                property.definition = this;
                property.parser_index = (int)i;
                return true;
            }
        }

        property.unit = Unit::UNKNOWN;
        return false;
    }

    bool PropertyDefinition::IsValidKeyword(int keyword, int* out_parser_index) const
    {
        const auto* keyword_parser = StyleSheetSpecification::GetParser("keyword");
        if (!keyword_parser)
        {
            return false;
        }
        for (int i = 0; i < (int)parsers.size(); i++)
        {
            if (parsers[i].parser != keyword_parser)
            {
                continue;
            }
            for (const auto& entry : parsers[i].parameters)
            {
                if (entry.second == keyword)
                {
                    if (out_parser_index)
                    {
                        *out_parser_index = i;
                    }
                    return true;
                }
            }
        }
        return false;
    }

    bool PropertyDefinition::GetValue(String& value, const Property& property) const
    {
        value = property.value.Get<String>();

        switch (property.unit)
        {
        case Unit::KEYWORD:
        {
            int parser_index = property.parser_index;
            if (parser_index < 0 || parser_index >= (int)parsers.size())
            {
                // Look for the keyword parser in the property's list of parsers
                const auto* keyword_parser = StyleSheetSpecification::GetParser("keyword");
                for (int i = 0; i < (int)parsers.size(); i++)
                {
                    if (parsers[i].parser == keyword_parser)
                    {
                        parser_index = i;
                        break;
                    }
                }
                // If we couldn't find it, exit now
                if (parser_index < 0 || parser_index >= (int)parsers.size())
                {
                    return false;
                }
            }

            int keyword = property.value.Get<int>();
            for (const auto& name_keyword : parsers[parser_index].parameters)
            {
                if (name_keyword.second == keyword)
                {
                    value = name_keyword.first;
                    break;
                }
            }

            return false;
        }
        break;

        default:
            value += ToString(property.unit);
            break;
        }

        return true;
    }

    bool PropertyDefinition::IsInherited() const
    {
        return inherited;
    }

    bool PropertyDefinition::IsLayoutForced() const
    {
        return forces_layout;
    }

    const Property* PropertyDefinition::GetDefaultValue() const
    {
        return &default_value;
    }

    RelativeTarget PropertyDefinition::GetRelativeTarget() const
    {
        return relative_target;
    }

    PropertyId PropertyDefinition::GetId() const
    {
        return id;
    }

    PropertyDefinition& PropertyDefinition::SetRelativeTarget(RelativeTarget relative_target)
    {
        this->relative_target = relative_target;
        return *this;
    }

}
