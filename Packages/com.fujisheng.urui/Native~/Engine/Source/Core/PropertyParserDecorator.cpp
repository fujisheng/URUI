#include "PropertyParserDecorator.h"
#include "../../Include/URUI/Core/Decorator.h"
#include "../../Include/URUI/Core/Factory.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/PropertySpecification.h"
#include "../../Include/URUI/Core/StyleSheetTypes.h"
#include "CssTokenizer.h"

namespace URUI {

struct PropertyParserDecoratorData {
	const SmallUnorderedMap<String, BoxArea> area_keywords = {
		{"border-box", BoxArea::Border},
		{"padding-box", BoxArea::Padding},
		{"content-box", BoxArea::Content},
	};
};

ControlledLifetimeResource<PropertyParserDecoratorData> PropertyParserDecorator::parser_data;

void PropertyParserDecorator::Initialize()
{
	parser_data.Initialize();
}
void PropertyParserDecorator::Shutdown()
{
	if (parser_data) parser_data.Shutdown();
}

PropertyParserDecorator::PropertyParserDecorator(bool standard_image) : standard_image(standard_image) {}

PropertyParserDecorator::~PropertyParserDecorator() {}

namespace {
bool ParseStandardImage(Property& property, const String& value, PropertyId image_property)
{
    Vector<Diagnostic> diagnostics;
    const auto tokens = TokenizeCss(value, "urui:css-image", diagnostics);
    if (!diagnostics.empty()) return false;
    Vector<Vector<const CssToken*>> layers(1);
    int depth = 0;
    for (const auto& token : tokens) {
        if (token.kind == CssTokenKind::Whitespace) continue;
        if (token.kind == CssTokenKind::Delimiter) {
            if (token.value == "(") ++depth;
            else if (token.value == ")" && --depth < 0) return false;
            else if (token.value == "," && depth == 0) { layers.emplace_back(); continue; }
        }
        layers.back().push_back(&token);
    }
    if (depth != 0) return false;
    if (image_property == PropertyId::BorderImageSource && layers.size() != 1) return false;
    DecoratorDeclarationList declarations;
    declarations.value = value;
    declarations.image_property = image_property;
    declarations.layer_count = layers.size();
    for (size_t index = 0; index < layers.size(); ++index) {
        const auto& layer = layers[index];
        if (layer.empty()) return false;
        if (layer.size() == 1 && layer[0]->kind == CssTokenKind::Ident && AsciiLowerCss(layer[0]->value) == "none") continue;
        String url;
        bool is_url = false;
        if (layer.size() == 1 && layer[0]->kind == CssTokenKind::Url) {
            if (!DecodeCssUrl(*layer[0], url)) return false;
            is_url = true;
        }
        else if (layer.size() == 4 && layer[0]->kind == CssTokenKind::Ident && AsciiLowerCss(layer[0]->value) == "url" &&
            layer[1]->value == "(" && layer[2]->kind == CssTokenKind::String && layer[3]->value == ")") {
            url = layer[2]->value; is_url = true;
        }
        if (is_url) {
            auto* instancer = Factory::GetDecoratorInstancer("urui-css-image");
            if (!instancer) return false;
            const auto& specification = instancer->GetPropertySpecification();
            const auto source_id = specification.GetPropertyId("image-src");
            if (source_id == PropertyId::Invalid) return false;
            PropertyDictionary properties;
            specification.SetPropertyDefaults(properties);
            properties.SetProperty(source_id, Property(url, Unit::STRING));
            // CSS 默认重复原始尺寸，定位起点为左上；不沿用游戏装饰器的拉伸默认值。
            if (!specification.ParsePropertyDeclaration(properties, "image-fit", "repeat") ||
                !specification.ParsePropertyDeclaration(properties, "image-align-x", "0%") ||
                !specification.ParsePropertyDeclaration(properties, "image-align-y", "0%")) return false;
            declarations.list.push_back({"urui-css-image", instancer, std::move(properties), BoxArea::Auto, index});
            continue;
        }
        if (image_property == PropertyId::BorderImageSource) return false;
        if (layer[0]->kind != CssTokenKind::Ident || layer.size() < 3 || layer[1]->value != "(" || layer.back()->value != ")") return false;
        const auto type = AsciiLowerCss(layer[0]->value);
        if (type != "linear-gradient" && type != "radial-gradient" && type != "conic-gradient" &&
            type != "repeating-linear-gradient" && type != "repeating-radial-gradient" && type != "repeating-conic-gradient") return false;
        const auto begin = layer[1]->span.byteOffset;
        const auto end = layer.back()->span.byteOffset + layer.back()->span.length;
        Property parsed;
        if (!PropertyParserDecorator().ParseValue(parsed, type + value.substr(begin, end - begin), {})) return false;
        const auto gradient = parsed.Get<DecoratorsPtr>();
        if (!gradient) return false;
        declarations.list.insert(declarations.list.end(), gradient->list.begin(), gradient->list.end());
        declarations.list.back().image_layer = index;
    }
    property.unit = Unit::DECORATOR;
    property.value = declarations.list.empty() && layers.size() == 1 ? DecoratorsPtr() : MakeShared<DecoratorDeclarationList>(std::move(declarations));
    return true;
}
}

bool PropertyParserDecorator::ParseValue(Property& property, const String& decorator_string_value, const ParameterMap& parameters) const
{
    if (standard_image) {
        if (!ParseStandardImage(property, decorator_string_value, parameters.count("background") ? PropertyId::BackgroundImage : parameters.count("border") ? PropertyId::BorderImageSource : PropertyId::MaskImage)) return false;
        const auto images = property.Get<DecoratorsPtr>();
        return !parameters.count("effect") || !images || images->layer_count == 1;
    }
	// Decorators are declared as
	//   decorator: <decorator-value>[, <decorator-value> ...];
	// Where <decorator-value> is either a @decorator name:
	//   decorator: invader-theme-background <paint-area>?, ...;
	// or is an anonymous decorator with inline properties
	//   decorator: tiled-box( <shorthand properties> ) <paint-area>?, ...;
	// where <paint-area> is one of
	//   border-box, padding-box, content-box

	if (decorator_string_value.empty() || decorator_string_value == "none")
	{
		property.value = Variant(DecoratorsPtr());
		property.unit = Unit::DECORATOR;
		return true;
	}

	URUI_ZoneScoped;

	// Make sure we don't split inside the parenthesis since they may appear in decorator shorthands.
	StringList decorator_string_list;
	StringUtilities::ExpandString(decorator_string_list, decorator_string_value, ',', '(', ')');

	DecoratorDeclarationList decorators;
	decorators.value = decorator_string_value;
	decorators.list.reserve(decorator_string_list.size());

	// Get or instance each decorator in the comma-separated string list
	for (const String& decorator_string : decorator_string_list)
	{
		const size_t shorthand_open = decorator_string.find('(');
		const size_t shorthand_close = decorator_string.rfind(')');
		const bool invalid_parenthesis = (shorthand_open == String::npos || shorthand_close == String::npos || shorthand_open >= shorthand_close);
		const size_t keywords_begin = (invalid_parenthesis ? decorator_string.find(' ') : shorthand_close + 1);

		// Look-up keywords for customized paint area.
		BoxArea paint_area = BoxArea::Auto;

		{
			StringList keywords;
			if (keywords_begin < decorator_string.size())
				StringUtilities::ExpandString(keywords, decorator_string.substr(keywords_begin), ' ');

			for (const String& keyword : keywords)
			{
				if (keyword.empty())
					continue;

				auto it = parser_data->area_keywords.find(StringUtilities::ToLower(keyword));
				if (it == parser_data->area_keywords.end())
					return false; // Bail out if we have an invalid keyword.

				paint_area = it->second;
			}
		}

		if (invalid_parenthesis)
		{
			// We found no parenthesis, that means the value must be a name of a @decorator rule.
			decorators.list.emplace_back(DecoratorDeclaration{decorator_string.substr(0, keywords_begin), nullptr, {}, paint_area});
		}
		else
		{
			// Since we have parentheses it must be an anonymous decorator with inline properties
			const String type = StringUtilities::StripWhitespace(decorator_string.substr(0, shorthand_open));

			// Check for valid decorator type
			DecoratorInstancer* instancer = Factory::GetDecoratorInstancer(type);
			if (!instancer)
			{
				Log::Message(Log::LT_WARNING, "Decorator type '%s' not found.", type.c_str());
				return false;
			}

			const String shorthand = decorator_string.substr(shorthand_open + 1, shorthand_close - shorthand_open - 1);
			const PropertySpecification& specification = instancer->GetPropertySpecification();

			// Parse the shorthand properties given by the 'decorator' shorthand property
			PropertyDictionary properties;
			if (!specification.ParsePropertyDeclaration(properties, "decorator", shorthand))
			{
				// Empty values are allowed in decorators, if the value is not empty we must have encountered a parser error.
				if (!StringUtilities::StripWhitespace(shorthand).empty())
					return false;
			}

			// Set unspecified values to their defaults
			specification.SetPropertyDefaults(properties);

			decorators.list.emplace_back(DecoratorDeclaration{type, instancer, std::move(properties), paint_area});
		}
	}

	if (decorators.list.empty())
		return false;

	property.value = Variant(MakeShared<DecoratorDeclarationList>(std::move(decorators)));
	property.unit = Unit::DECORATOR;

	return true;
}

String PropertyParserDecorator::ConvertAreaToString(BoxArea area)
{
	for (const auto& it : parser_data->area_keywords)
	{
		if (it.second == area)
			return it.first;
	}
	return String();
}

} // namespace URUI
