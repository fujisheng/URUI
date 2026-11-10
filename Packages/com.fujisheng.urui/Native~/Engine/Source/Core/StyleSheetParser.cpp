#include "CssParser.h"
#include "../../Include/URUI/Core/SystemInterface.h"
#include "StyleSheetParser.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/Decorator.h"
#include "../../Include/URUI/Core/Factory.h"
#include "../../Include/URUI/Core/Log.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/PropertyDefinition.h"
#include "../../Include/URUI/Core/PropertySpecification.h"
#include "../../Include/URUI/Core/StreamMemory.h"
#include "../../Include/URUI/Core/StyleSheet.h"
#include "../../Include/URUI/Core/StyleSheetContainer.h"
#include "../../Include/URUI/Core/StyleSheetSpecification.h"
#include "ComputeProperty.h"
#include "ControlledLifetimeResource.h"
#include "StyleSheetFactory.h"
#include "StyleSheetNode.h"
#include <algorithm>
#include <functional>
#include <string.h>
#include <cmath>
#include <cstdlib>

namespace URUI {

// @property 复用声明解析器；所有 schema 校验完成后才修改属性注册表。
struct PropertyRegistrationTransaction
{
    PropertySpecification& specification;
    explicit PropertyRegistrationTransaction(PropertySpecification& specification) : specification(specification) {}
    Vector<String> added;
    Vector<std::pair<PropertyDefinition*, Property>> defaults;
    bool committed = false;
    ~PropertyRegistrationTransaction()
    {
        if (committed) return;
        for (auto it = added.rbegin(); it != added.rend(); ++it) specification.RemoveLastProperty(*it);
        for (auto& entry : defaults) entry.first->SetDefaultValue(entry.second);
    }
};


// 声明能力查询子集；按真正的属性解析器判断，未知能力为 false。
static std::pair<bool, bool> SupportsDeclaration(String condition, const PropertySpecification& specification)
{
    const auto trim = [](const String& value) {
        const auto begin = value.find_first_not_of(" \t\r\n\f");
        return begin == String::npos ? String{} : value.substr(begin, value.find_last_not_of(" \t\r\n\f") - begin + 1);
    };
    condition = trim(condition);
    if (condition.compare(0, 4, "not ") == 0) {
        auto result = SupportsDeclaration(condition.substr(4), specification);
        return {result.first, result.first && !result.second};
    }
    if (condition.size() < 3 || condition.front() != '(' || condition.back() != ')') return {false, false};
    condition = trim(condition.substr(1, condition.size() - 2));
    if (!condition.empty() && condition.front() == '(') return SupportsDeclaration(condition, specification);
    Vector<Diagnostic> diagnostics;
    const auto declarations = ParseCssDeclarations(condition, "", diagnostics);
    if (!diagnostics.empty() || declarations.size() != 1 || declarations.front().important) return {false, false};
    PropertyDictionary properties;
    const auto& declaration = declarations.front();
    return {true, specification.ParsePropertyDeclaration(properties, declaration.propertyName, declaration.tokenValue)};
}

static bool IsEscapedCharacter(const String& string, size_t index)
{
	if (index == 0 || index > string.size())
		return false;

	size_t num_preceding_backslashes = 0;
	for (size_t i = index; i > 0 && string[i - 1] == '\\'; --i)
		num_preceding_backslashes += 1;

	return (num_preceding_backslashes % 2) == 1;
}

static String UnescapeSelectorToken(StringView token)
{
    return DecodeCssIdentifier(String(token.begin(), token.end()));
}

static String ExtractUnescapedSelectorToken(const String& string, size_t begin, size_t end)
{
	return UnescapeSelectorToken(StringView(string, begin, end - begin));
}

static size_t FindAttributeSelectorEnd(const String& rule, size_t start_index)
{
	char quote = 0;
	for (size_t index = start_index + 1; index < rule.size(); index++)
	{
		const char c = rule[index];
		if ((c == '\'' || c == '"') && !IsEscapedCharacter(rule, index))
		{
			if (!quote)
				quote = c;
			else if (quote == c)
				quote = 0;
		}
		else if (c == ']' && !quote && !IsEscapedCharacter(rule, index))
		{
			return index;
		}
	}

	return String::npos;
}

static size_t FindFirstUnescaped(const String& string, size_t begin, size_t end, StringView tokens)
{
	for (size_t index = begin; index < end; index++)
	{
		if (std::find(tokens.begin(), tokens.end(), string[index]) != tokens.end() && !IsEscapedCharacter(string, index))
			return index;
	}

	return String::npos;
}

static bool ParseAttributeSelector(AttributeSelector& attribute, const String& rule, size_t begin, size_t end)
{
	Vector<Diagnostic> diagnostics;
	const auto all = TokenizeCss(rule.substr(begin, end - begin), "urui:selector", diagnostics);
	if (!diagnostics.empty()) return false;
	Vector<const CssToken*> tokens;
	for (const auto& token : all) if (token.kind != CssTokenKind::Whitespace) tokens.push_back(&token);
	if (tokens.empty() || tokens.front()->kind != CssTokenKind::Ident) return false;
	attribute.name = AsciiLowerCss(tokens.front()->value);
	if (tokens.size() == 1) return true;
	size_t i = 1;
	if (tokens[i]->kind != CssTokenKind::Delimiter || tokens[i]->value.size() != 1) return false;
	const char op = tokens[i]->value[0];
	if (String("=~|^$*").find(op) == String::npos) return false;
	attribute.type = AttributeSelectorType(op);
	if (op != '=') {
		if (i + 1 >= tokens.size() || tokens[i + 1]->kind != CssTokenKind::Delimiter || tokens[i + 1]->value != "=" ||
			tokens[i]->span.byteOffset + tokens[i]->span.length != tokens[i + 1]->span.byteOffset) return false;
		++i;
	}
	++i;
	if (i + 1 != tokens.size() || (tokens[i]->kind != CssTokenKind::Ident && tokens[i]->kind != CssTokenKind::String)) return false;
	attribute.value = tokens[i]->value;
	return true;
}

class AbstractPropertyParser : NonCopyMoveable {
protected:
	~AbstractPropertyParser() = default;

public:
	virtual bool Parse(const String& name, const String& value) = 0;
	virtual bool ParseDeclaration(const CssDeclaration& declaration) {
		return !declaration.important && Parse(declaration.propertyName, declaration.tokenValue);
	}
};

/*
 *  PropertySpecificationParser just passes the parsing to a property specification. Usually
 *    the main stylesheet specification, except for e.g. @decorator blocks.
 */
class RegistrationParser final : public AbstractPropertyParser
{
public:
    String syntax, initial_value;
    bool inherited = false, has_inherits = false, invalid = false;
    bool Parse(const String& name, const String& value) override
    {
        if (name == "syntax")
        {
            syntax = value;
            if (syntax.size() >= 2 && ((syntax.front() == '\"' && syntax.back() == '\"') || (syntax.front() == '\'' && syntax.back() == '\'')))
                syntax = syntax.substr(1, syntax.size() - 2);
        }
        else if (name == "initial-value") initial_value = value;
        else if (name == "inherits")
        {
            const auto flag = StringUtilities::ToLower(value);
            has_inherits = (flag == "true" || flag == "false"); inherited = (flag == "true");
        }
        else { invalid = true; return false; }
        return true;
    }
    String parser_name, parser_parameters, fallback_keyword;
    Property value;
    bool Value()
    {
        if (invalid || !has_inherits || initial_value.empty()) return false;
        if (syntax == "<number>") parser_name = "number";
        else if (syntax == "<length>") parser_name = "length";
        else if (syntax == "<color>") parser_name = "color";
        else if (syntax == "<image>") { parser_name = "css_image"; parser_parameters = "effect"; }
        else {
            Vector<Diagnostic> diagnostics;
            auto tokens = TokenizeCss(syntax, "urui:property-syntax", diagnostics);
            if (!diagnostics.empty()) return false;
            Vector<String> parts;
            for (const auto& token : tokens) { if (token.kind != CssTokenKind::Whitespace) parts.push_back(token.value); }
            const bool number_auto = parts == Vector<String>{"<", "number", ">", "|", "auto"} ||
                parts == Vector<String>{"auto", "|", "<", "number", ">"};
            if (number_auto) { parser_name = "number"; fallback_keyword = "auto"; }
            else {
            bool expect_name = true;
            for (const auto& token : tokens) {
                if (token.kind == CssTokenKind::Whitespace) continue;
                if (expect_name) {
                    if (token.kind != CssTokenKind::Ident || token.value.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789-_") != String::npos ||
                        token.value == "inherit" || token.value == "initial" || token.value == "unset" || token.value == "default") return false;
                    if (!parser_parameters.empty()) parser_parameters += ',';
                    parser_parameters += token.value;
                } else if (token.value != "|") return false;
                expect_name = !expect_name;
            }
            if (expect_name || parser_parameters.empty()) return false;
            parser_name = "keyword";
            }
        }
        PropertyDefinition definition(PropertyId::Invalid, initial_value, inherited, false);
        definition.AddParser(parser_name, parser_parameters);
        if (!fallback_keyword.empty()) definition.AddParser("keyword", fallback_keyword);
        if (!definition.ParseValue(value, initial_value)) return false;
        // Existing Pixels parameters require a computationally independent px
        // initial value. Font/viewport-relative lengths are outside this schema.
        if (parser_name == "length" && value.unit != Unit::PX) return false;
        if (value.unit == Unit::NUMBER && !std::isfinite(value.Get<float>())) return false;
        value.definition = nullptr;
        return true;
    }
};

class PropertySpecificationParser final : public AbstractPropertyParser {
private:
	// The dictionary to store the properties in.
	PropertyDictionary& properties;

	// The specification used to parse the values. Normally the default stylesheet specification, but not for e.g. all at-rules such as decorators.
	const PropertySpecification& specification;

public:
	PropertySpecificationParser(PropertyDictionary& properties, const PropertySpecification& specification) :
		properties(properties), specification(specification)
	{}

	bool Parse(const String& name, const String& value) override { return specification.ParsePropertyDeclaration(properties, name, value); }
	bool ParseDeclaration(const CssDeclaration& declaration) override {
		PropertyDictionary parsed;
		if (!specification.ParsePropertyDeclaration(parsed, declaration.propertyName, declaration.tokenValue)) return false;
		auto save = [&](Property property) {
			if (declaration.important) property.priority.originImportance = CssOriginImportance::AuthorImportant;
			property.priority.sourceOrder = declaration.sourceOrder;
			return property;
		};
		PropertyDictionary prioritized;
		for (auto& [id, property] : parsed.GetProperties()) prioritized.SetProperty(id, save(property));
		for (auto& [name, property] : parsed.GetCustomProperties()) prioritized.SetCustomProperty(name, save(property));
		for (auto& [id, property] : parsed.GetVarShorthands()) prioritized.SetVarShorthand(id, save(property));
		properties.Merge(prioritized);
		return true;
	}
};

/*
 *  Spritesheets need a special parser because its property names are arbitrary keys,
 *    while its values are always rectangles. Thus, it must be parsed with a special "rectangle" parser
 *    for every name-value pair. We can probably optimize this for @performance.
 */
class SpritesheetPropertyParser final : public AbstractPropertyParser {
private:
	String image_source;
	float image_resolution_factor = 1.f;
	SpriteDefinitionList sprite_definitions;

	PropertyDictionary properties;
	PropertySpecification specification;
	PropertyId id_src, id_rx, id_ry, id_rw, id_rh, id_resolution;
	ShorthandId id_rectangle;

public:
	SpritesheetPropertyParser() : specification(6, 1)
	{
		id_src = specification.RegisterProperty("src", "", false, false).AddParser("string").GetId();
		id_rx = specification.RegisterProperty("rectangle-x", "", false, false).AddParser("length").GetId();
		id_ry = specification.RegisterProperty("rectangle-y", "", false, false).AddParser("length").GetId();
		id_rw = specification.RegisterProperty("rectangle-w", "", false, false).AddParser("length").GetId();
		id_rh = specification.RegisterProperty("rectangle-h", "", false, false).AddParser("length").GetId();
		id_rectangle = specification.RegisterShorthand("rectangle", "rectangle-x, rectangle-y, rectangle-w, rectangle-h", ShorthandType::FallThrough);
		id_resolution = specification.RegisterProperty("resolution", "", false, false).AddParser("resolution").GetId();
	}

	const String& GetImageSource() const { return image_source; }
	const SpriteDefinitionList& GetSpriteDefinitions() const { return sprite_definitions; }
	float GetImageResolutionFactor() const { return image_resolution_factor; }

	void Clear()
	{
		image_resolution_factor = 1.f;
		image_source.clear();
		sprite_definitions.clear();
	}

	bool Parse(const String& name, const String& value) override
	{
		if (name == "src")
		{
			if (!specification.ParsePropertyDeclaration(properties, id_src, value))
				return false;

			if (const Property* property = properties.GetProperty(id_src))
			{
				if (property->unit == Unit::STRING)
					image_source = property->Get<String>();
			}
		}
		else if (name == "resolution")
		{
			if (!specification.ParsePropertyDeclaration(properties, id_resolution, value))
				return false;

			if (const Property* property = properties.GetProperty(id_resolution))
			{
				if (property->unit == Unit::X)
					image_resolution_factor = property->Get<float>();
			}
		}
		else
		{
			if (!specification.ParseShorthandDeclaration(properties, id_rectangle, value))
				return false;

			Vector2f position, size;
			if (auto p = properties.GetProperty(id_rx))
				position.x = p->Get<float>();
			if (auto p = properties.GetProperty(id_ry))
				position.y = p->Get<float>();
			if (auto p = properties.GetProperty(id_rw))
				size.x = p->Get<float>();
			if (auto p = properties.GetProperty(id_rh))
				size.y = p->Get<float>();

			sprite_definitions.emplace_back(name, Rectanglef::FromPositionSize(position, size));
		}

		return true;
	}
};

/*
 * Media queries need a special parser because they have unique properties that
 * aren't admissible in other property declaration contexts.
 */
class MediaQueryPropertyParser final : public AbstractPropertyParser {
private:
	// The dictionary to store the properties in.
	PropertyDictionary* properties = nullptr;
	PropertySpecification specification;

	static PropertyId CastId(MediaQueryId id) { return static_cast<PropertyId>(id); }

public:
	MediaQueryPropertyParser() : specification(14, 0)
	{
		specification.RegisterProperty("width", "", false, false, CastId(MediaQueryId::Width)).AddParser("length");
		specification.RegisterProperty("min-width", "", false, false, CastId(MediaQueryId::MinWidth)).AddParser("length");
		specification.RegisterProperty("max-width", "", false, false, CastId(MediaQueryId::MaxWidth)).AddParser("length");

		specification.RegisterProperty("height", "", false, false, CastId(MediaQueryId::Height)).AddParser("length");
		specification.RegisterProperty("min-height", "", false, false, CastId(MediaQueryId::MinHeight)).AddParser("length");
		specification.RegisterProperty("max-height", "", false, false, CastId(MediaQueryId::MaxHeight)).AddParser("length");

		specification.RegisterProperty("aspect-ratio", "", false, false, CastId(MediaQueryId::AspectRatio)).AddParser("ratio");
		specification.RegisterProperty("min-aspect-ratio", "", false, false, CastId(MediaQueryId::MinAspectRatio)).AddParser("ratio");
		specification.RegisterProperty("max-aspect-ratio", "", false, false, CastId(MediaQueryId::MaxAspectRatio)).AddParser("ratio");

		specification.RegisterProperty("resolution", "", false, false, CastId(MediaQueryId::Resolution)).AddParser("resolution");
		specification.RegisterProperty("min-resolution", "", false, false, CastId(MediaQueryId::MinResolution)).AddParser("resolution");
		specification.RegisterProperty("max-resolution", "", false, false, CastId(MediaQueryId::MaxResolution)).AddParser("resolution");

		specification.RegisterProperty("orientation", "", false, false, CastId(MediaQueryId::Orientation))
			.AddParser("keyword", "landscape, portrait");

		specification.RegisterProperty("theme", "", false, false, CastId(MediaQueryId::Theme)).AddParser("string");
	}

	void SetTargetProperties(PropertyDictionary* _properties) { properties = _properties; }

	void Clear() { properties = nullptr; }

	bool Parse(const String& name, const String& value) override
	{
		URUI_ASSERT(properties);
		return specification.ParsePropertyDeclaration(*properties, name, value);
	}
};

/*
 * Font faces need a special parser because they have unique properties that
 * aren't admissible in other property declaration contexts.
 */
class FontFacePropertyParser final : public AbstractPropertyParser {
private:
	// The dictionary to store the properties in.
	PropertyDictionary* properties = nullptr;
	PropertySpecification specification;

	static PropertyId CastId(FontFaceId id) { return static_cast<PropertyId>(id); }

public:
	StringList sources;

	const PropertySpecification& GetPropertySpecification() { return specification; }

	FontFacePropertyParser() : specification(5, 0)
	{
		// standard
		specification.RegisterProperty("font-family", "", false, false, CastId(FontFaceId::FontFamily)).AddParser("string");
		specification.RegisterProperty("font-weight", "normal", false, false, CastId(FontFaceId::FontWeight))
			.AddParser("keyword", "normal=400, bold=700")
			.AddParser("number", "min=1,max=1000");
		specification.RegisterProperty("font-style", "normal", false, false, CastId(FontFaceId::FontStyle)).AddParser("keyword", "normal, italic");
		// src handled by Parse

		// extended
		specification.RegisterProperty("--urui-fallback-face", "false", false, false, CastId(FontFaceId::FallbackFace))
			.AddParser("keyword", "false, true");
		specification.RegisterProperty("--urui-face-index", "0", false, false, CastId(FontFaceId::FaceIndex)).AddParser("number");
	}

	void SetTargetProperties(PropertyDictionary* _properties) { properties = _properties; }

	void Clear()
	{
		properties = nullptr;
		sources.clear();
	}

	bool Parse(const String& name, const String& value) override
	{
		URUI_ASSERT(properties);
		if (name == "font-family") {
			Vector<CssFontFamily> families;
			if (!ParseCssFontFamilies(value, families) || families.size() != 1 || families[0].generic >= 0 || families[0].name.empty()) return false;
			properties->SetProperty(CastId(FontFaceId::FontFamily), Property(families[0].name, Unit::STRING));
			return true;
		}

		if (name == "src")
		{
			Vector<Diagnostic> diagnostics;
			const auto all = TokenizeCss(value, "urui:font-src", diagnostics);
			if (!diagnostics.empty()) return false;
			Vector<const CssToken*> tokens;
			for (const auto& token : all) if (token.kind != CssTokenKind::Whitespace) tokens.push_back(&token);
			StringList parsed;
			size_t i = 0;
			while (i < tokens.size()) {
				String path;
				if (tokens[i]->kind == CssTokenKind::Url) {
					if (!DecodeCssUrl(*tokens[i++], path)) return false;
				} else if (i + 3 < tokens.size() && tokens[i]->kind == CssTokenKind::Ident && AsciiLowerCss(tokens[i]->value) == "url" &&
					tokens[i + 1]->value == "(" && tokens[i + 2]->kind == CssTokenKind::String && tokens[i + 3]->value == ")") {
					path = tokens[i + 2]->value; i += 4;
				} else return false;
				if (path.empty()) return false;
				parsed.push_back(std::move(path));
				if (i + 3 < tokens.size() && tokens[i]->kind == CssTokenKind::Ident && AsciiLowerCss(tokens[i]->value) == "format" &&
					tokens[i + 1]->value == "(" && tokens[i + 2]->kind == CssTokenKind::String && tokens[i + 3]->value == ")") i += 4;
				if (i == tokens.size()) break;
				if (tokens[i++]->value != "," || i == tokens.size()) return false;
			}
			sources = std::move(parsed);
			return !sources.empty();
		}

		return specification.ParsePropertyDeclaration(*properties, name, value);
	}
};

struct StyleSheetParserData {
	// The following parsers are reasonably heavy to initialize, so we construct them during library initialization.
	SpritesheetPropertyParser spritesheet;
	MediaQueryPropertyParser media_query;
	FontFacePropertyParser font_face;
};

static ControlledLifetimeResource<StyleSheetParserData> style_sheet_property_parsers;

StyleSheetParser::StyleSheetParser(PropertySpecification* specification, Vector<Diagnostic>* diagnostics) :
    diagnostic_sink(diagnostics), property_specification(specification ? *specification : StyleSheetSpecification::GetMutablePropertySpecification())
{
	line_number = 0;
	stream = nullptr;
}

StyleSheetParser::~StyleSheetParser() {}

void StyleSheetParser::Report(DiagnosticSeverity severity, const String& code, const String& message, SourceSpan span)
{
    span.line += line_offset;
    if (diagnostic_sink) diagnostic_sink->push_back({severity, code, message, stream_file_name, span});
    else Log::Message(severity == DiagnosticSeverity::Error ? Log::LT_ERROR : Log::LT_WARNING,
        "%s:%u:%u [%s] %s", stream_file_name.c_str(), span.line, span.column, code.c_str(), message.c_str());
}

void StyleSheetParser::Initialise()
{
	style_sheet_property_parsers.Initialize();
}

void StyleSheetParser::Shutdown()
{
	style_sheet_property_parsers.Shutdown();
}

static bool IsValidIdentifier(const String& str)
{
	if (str.empty())
		return false;

	for (size_t i = 0; i < str.size(); i++)
	{
		char c = str[i];
		bool valid = ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || (c == '-') || (c == '_'));
		if (!valid)
			return false;
	}

	return true;
}

static void PostprocessKeyframes(KeyframesMap& keyframes_map)
{
	for (auto& keyframes_pair : keyframes_map)
	{
		Keyframes& keyframes = keyframes_pair.second;
		auto& blocks = keyframes.blocks;
		auto& property_ids = keyframes.property_ids;

		// Sort keyframes on selector value.
		std::sort(blocks.begin(), blocks.end(), [](const KeyframeBlock& a, const KeyframeBlock& b) { return a.normalized_time < b.normalized_time; });

		// Add all property names specified by any block
		if (blocks.size() > 0)
			property_ids.reserve(blocks.size() * blocks[0].properties.GetNumProperties());
		for (auto& block : blocks)
		{
			for (auto& property : block.properties.GetProperties())
				property_ids.push_back(property.first);
		}
		// Remove duplicate property names
		std::sort(property_ids.begin(), property_ids.end());
		property_ids.erase(std::unique(property_ids.begin(), property_ids.end()), property_ids.end());
		property_ids.shrink_to_fit();
	}
}

bool StyleSheetParser::ParseKeyframeBlock(KeyframesMap& keyframes_map, const String& identifier, const String& rules,
	const PropertyDictionary& properties)
{
	if (!IsValidIdentifier(identifier))
	{
		Log::Message(Log::LT_WARNING, "Invalid keyframes identifier '%s' at %s:%d", identifier.c_str(), stream_file_name.c_str(), line_number);
		return false;
	}
	if (properties.Empty())
		return true;

	StringList rule_list;
	StringUtilities::ExpandString(rule_list, rules);

	Vector<float> rule_values;
	rule_values.reserve(rule_list.size());

	for (auto rule : rule_list)
	{
		float value = 0.0f;
		int count = 0;
		rule = StringUtilities::ToLower(std::move(rule));
		if (rule == "from")
			rule_values.push_back(0.0f);
		else if (rule == "to")
			rule_values.push_back(1.0f);
		else if (sscanf(rule.c_str(), "%f%%%n", &value, &count) == 1)
			if (count > 0 && value >= 0.0f && value <= 100.0f)
				rule_values.push_back(0.01f * value);
	}

	if (rule_values.empty())
	{
		Log::Message(Log::LT_WARNING, "Invalid keyframes rule(s) '%s' at %s:%d", rules.c_str(), stream_file_name.c_str(), line_number);
		return false;
	}

	Keyframes& keyframes = keyframes_map[identifier];

	for (float selector : rule_values)
	{
		auto it = std::find_if(keyframes.blocks.begin(), keyframes.blocks.end(),
			[selector](const KeyframeBlock& keyframe_block) { return Math::Absolute(keyframe_block.normalized_time - selector) < 0.0001f; });
		if (it == keyframes.blocks.end())
		{
			keyframes.blocks.emplace_back(selector);
			it = (keyframes.blocks.end() - 1);
		}
		else
		{
			// In case of duplicate keyframes, we only use the latest definition as per CSS rules
			it->properties = PropertyDictionary();
		}

		it->properties.Import(properties);
	}

	return true;
}

bool StyleSheetParser::ParseDecoratorBlock(const String& at_name, NamedDecoratorMap& named_decorator_map,
	const SharedPtr<const PropertySource>& source)
{
	StringList name_type;
	StringUtilities::ExpandString(name_type, at_name, ':');

	if (name_type.size() != 2 || name_type[0].empty() || name_type[1].empty())
	{
		Log::Message(Log::LT_WARNING, "Decorator syntax error at %s:%d. Use syntax: '@decorator name : type { ... }'.", stream_file_name.c_str(),
			line_number);
		return false;
	}

	const String& name = name_type[0];
	String decorator_type = name_type[1];

	auto it_find = named_decorator_map.find(name);
	if (it_find != named_decorator_map.end())
	{
		Log::Message(Log::LT_WARNING, "Decorator with name '%s' already declared, ignoring decorator at %s:%d.", name.c_str(),
			stream_file_name.c_str(), line_number);
		return false;
	}

	// Get the instancer associated with the decorator type
	DecoratorInstancer* decorator_instancer = Factory::GetDecoratorInstancer(decorator_type);
	PropertyDictionary properties;

	if (!decorator_instancer)
	{
		// Type is not a declared decorator type, instead, see if it is another decorator name, then we inherit its properties.
		auto it = named_decorator_map.find(decorator_type);
		if (it != named_decorator_map.end())
		{
			// Yes, try to retrieve the instancer from the parent type, and add its property values.
			decorator_instancer = Factory::GetDecoratorInstancer(it->second.type);
			properties = it->second.properties;
			decorator_type = it->second.type;
		}

		// If we still don't have an instancer, we cannot continue.
		if (!decorator_instancer)
		{
			Log::Message(Log::LT_WARNING, "Invalid decorator type '%s' declared at %s:%d.", decorator_type.c_str(), stream_file_name.c_str(),
				line_number);
			return false;
		}
	}

	const PropertySpecification& property_specification = decorator_instancer->GetPropertySpecification();

	PropertySpecificationParser parser(properties, property_specification);
	ReadProperties(parser);

	// Set non-defined properties to their defaults
	property_specification.SetPropertyDefaults(properties);
	properties.SetSourceOfAllProperties(source);

	named_decorator_map.emplace(name, NamedDecorator{std::move(decorator_type), decorator_instancer, std::move(properties)});

	return true;
}

bool StyleSheetParser::ParseFontFaceBlock(Vector<FontFaceRule>& rules, const SharedPtr<const PropertySource>& source)
{
	FontFacePropertyParser font_face_property_parser;

	PropertyDictionary properties;
	font_face_property_parser.SetTargetProperties(&properties);

	ReadProperties(font_face_property_parser);

	// Set non-defined properties to their defaults
	font_face_property_parser.GetPropertySpecification().SetPropertyDefaults(properties);
	properties.SetSourceOfAllProperties(source);

	// check required properties
	if (!properties.GetProperty(static_cast<URUI::PropertyId>(URUI::FontFaceId::FontFamily)))
	{
		Log::Message(Log::LT_WARNING, "@font-face block missing font-family at %s:%d.", stream_file_name.c_str(), line_number);
		return false;
	}

	if (font_face_property_parser.sources.empty())
	{
		Log::Message(Log::LT_WARNING, "@font-face block missing src at %s:%d.", stream_file_name.c_str(), line_number);
		return false;
	}

	auto get_property = [&properties](FontFaceId id) { return properties.GetProperty(static_cast<PropertyId>(id)); };
	const String family = get_property(FontFaceId::FontFamily)->Get<String>();
	const bool is_fallback = get_property(FontFaceId::FallbackFace)->Get<bool>();
	const int face_index = get_property(FontFaceId::FaceIndex)->Get<int>();
	const Style::FontWeight weight = get_property(FontFaceId::FontWeight)->Get<Style::FontWeight>();
	const Style::FontStyle style = get_property(FontFaceId::FontStyle)->Get<Style::FontStyle>();
	if (family.empty()) return false;
	FontFaceRule rule;
	rule.family = family; rule.style = style; rule.weight = weight;
	rule.face_index = face_index; rule.fallback = is_fallback;
	for (const String& src : font_face_property_parser.sources) {
		String path;
		GetSystemInterface()->JoinPath(path, stream_file_name, src);
		rule.sources.push_back(std::move(path));
	}
	rules.push_back(std::move(rule));
	return true;
}

bool StyleSheetParser::ParseMediaFeatureMap(const String& rules, PropertyDictionary& properties, MediaQueryModifier& modifier)
{
	style_sheet_property_parsers->media_query.SetTargetProperties(&properties);

	enum ParseState { Global, Name, Value };
	ParseState state = Global;

	char character = 0;

	String name;

	String current_string;

	modifier = MediaQueryModifier::None;

	for (size_t cursor = 0; cursor < rules.length(); cursor++)
	{
		character = rules[cursor];

		switch (character)
		{
		case ' ':
		{
			if (state == Global)
			{
				current_string = StringUtilities::StripWhitespace(StringUtilities::ToLower(std::move(current_string)));

				if (current_string == "not")
				{
					// we can only ever see one "not" on the entire global query.
					if (modifier != MediaQueryModifier::None)
					{
						Log::Message(Log::LT_WARNING, "Unexpected '%s' in @media query list at %s:%d.", current_string.c_str(),
							stream_file_name.c_str(), line_number);
						return false;
					}

					modifier = MediaQueryModifier::Not;
					current_string.clear();
				}
			}

			break;
		}
		case '(':
		{
			if (state != Global)
			{
				Log::Message(Log::LT_WARNING, "Unexpected '(' in @media query list at %s:%d.", stream_file_name.c_str(), line_number);
				return false;
			}

			current_string = StringUtilities::StripWhitespace(StringUtilities::ToLower(std::move(current_string)));

			// allow an empty string to pass through only if we had just parsed a modifier.
			if (current_string != "and" && (!properties.Empty() || !current_string.empty()))
			{
				Log::Message(Log::LT_WARNING, "Unexpected '%s' in @media query list at %s:%d. Expected 'and'.", current_string.c_str(),
					stream_file_name.c_str(), line_number);
				return false;
			}

			current_string.clear();
			state = Name;
		}
		break;
		case ')':
		{
			if (state != Value)
			{
				Log::Message(Log::LT_WARNING, "Unexpected ')' in @media query list at %s:%d.", stream_file_name.c_str(), line_number);
				return false;
			}

			current_string = StringUtilities::StripWhitespace(current_string);

			if (!style_sheet_property_parsers->media_query.Parse(name, current_string))
				Log::Message(Log::LT_WARNING, "Syntax error parsing media-query property declaration '%s: %s;' in %s: %d.", name.c_str(),
					current_string.c_str(), stream_file_name.c_str(), line_number);

			current_string.clear();
			state = Global;
		}
		break;
		case ':':
		{
			if (state != Name)
			{
				Log::Message(Log::LT_WARNING, "Unexpected ':' in @media query list at %s:%d.", stream_file_name.c_str(), line_number);
				return false;
			}

			current_string = StringUtilities::StripWhitespace(StringUtilities::ToLower(std::move(current_string)));

			if (!IsValidIdentifier(current_string))
			{
				Log::Message(Log::LT_WARNING, "Malformed property name '%s' in @media query list at %s:%d.", current_string.c_str(),
					stream_file_name.c_str(), line_number);
				return false;
			}

			name = current_string;
			current_string.clear();

			state = Value;
		}
		break;
		default: current_string += character;
		}
	}

	if (properties.Empty())
	{
		Log::Message(Log::LT_WARNING, "Media query list parsing yielded no properties at %s:%d.", stream_file_name.c_str(), line_number);
	}

	return true;
}

bool StyleSheetParser::RegisterPropertyRules(Stream* input, int begin_line_number, PropertyRegistrationTransaction& transaction)
{
    struct Declaration { String name, initial, syntax, parser, parameters, fallback_keyword; bool inherited; Property value; SourceSpan span; };
    Vector<Declaration> declarations;
    stream_file_name = StringUtilities::Replace(input->GetSourceURL().GetURL(), '|', ':');
    String text;
    input->Read(text, input->Length() - input->Tell());
    const auto css = ParseCss(text, stream_file_name);
    for (const auto& rule : css.rules) {
        if (rule.atRule != "property") continue;
        RegistrationParser parser;
        for (const auto& descriptor : rule.declarations) if (!parser.ParseDeclaration(descriptor)) parser.invalid = true;
        Declaration declaration;
        declaration.name = StringUtilities::StripWhitespace(rule.prelude);
        declaration.span = rule.span;
        declaration.initial = parser.initial_value;
        declaration.inherited = parser.inherited;
        if (declaration.name.size() <= 2 || declaration.name.compare(0, 2, "--") != 0 ||
            declaration.name.find_first_of(" \t\r\n{}:;()\\") != String::npos || !parser.Value()) {
            Report(DiagnosticSeverity::Error, "CSS_PROPERTY_RULE", "Invalid @property '" + declaration.name + "'.", declaration.span);
            return false;
        }
        declaration.value = parser.value;
        declaration.syntax = parser.syntax;
        declaration.parser = parser.parser_name;
        declaration.parameters = parser.parser_parameters;
        declaration.fallback_keyword = parser.fallback_keyword;
        declarations.push_back(std::move(declaration));
    }
    UnorderedMap<String, size_t> unique;
    size_t registered = 0; for (auto id : property_specification.GetRegisteredProperties()) { (void)id; registered++; }
    size_t added = 0;
    for (size_t i = 0; i < declarations.size(); i++)
    {
        auto& declaration = declarations[i];
        if (const auto* current = property_specification.GetProperty(declaration.name))
        {
            if ((current->GetRegistrationSyntax().empty() && current->GetDefaultValue()->unit != declaration.value.unit) || current->IsInherited() != declaration.inherited ||
                (!current->GetRegistrationSyntax().empty() && current->GetRegistrationSyntax() != declaration.syntax))
            {
                Report(DiagnosticSeverity::Error, "CSS_PROPERTY_CONFLICT", "Conflicting @property '" + declaration.name + "'.", declaration.span);
                return false;
            }
        }
        else if (unique.find(declaration.name) == unique.end()) added++;
        auto prior = unique.find(declaration.name);
        if (prior != unique.end() && (declarations[prior->second].inherited != declaration.inherited || declarations[prior->second].syntax != declaration.syntax))
        {
            Report(DiagnosticSeverity::Error, "CSS_PROPERTY_CONFLICT", "Conflicting @property '" + declaration.name + "'.", declaration.span);
            return false;
        }
        unique[declaration.name] = i;
    }
    if (registered + added >= size_t(PropertyId::MaxNumIds))
    { Report(DiagnosticSeverity::Error, "CSS_PROPERTY_CAPACITY", "Registered property capacity exceeded.", {}); return false; }
    for (const auto& item : unique)
    {
        const auto& declaration = declarations[item.second];
        if (const auto* current = property_specification.GetProperty(declaration.name))
        {
            auto* target = const_cast<PropertyDefinition*>(current);
            transaction.defaults.push_back({target, *current->GetDefaultValue()});
            target->SetDefaultValue(declaration.value);
        }
        else
        {
            auto& definition = property_specification.RegisterProperty(declaration.name, declaration.initial, declaration.inherited, false);
            definition.AddParser(declaration.parser, declaration.parameters);
            if (!declaration.fallback_keyword.empty()) definition.AddParser("keyword", declaration.fallback_keyword);
            definition.SetRegistrationSyntax(declaration.syntax);
            transaction.added.push_back(declaration.name);
        }
    }
    return true;
}

bool StyleSheetParser::Parse(MediaBlockList& style_sheets, Stream* input, int begin_line_number, const std::function<bool()>& validate_resources)
{
    line_offset = begin_line_number - 1;
    PropertyRegistrationTransaction registration(property_specification);
    const auto begin = input->Tell();
    if (!RegisterPropertyRules(input, begin_line_number, registration) || !input->Seek(static_cast<long>(begin), SEEK_SET)) return false;
    String text;
    input->Read(text, input->Length() - input->Tell());
    stream_file_name = StringUtilities::Replace(input->GetSourceURL().GetURL(), '|', ':');
    const auto parsed = ParseCss(text, stream_file_name);
    for (const auto& diagnostic : parsed.diagnostics)
        Report(diagnostic.severity, diagnostic.code, diagnostic.message, diagnostic.span);
    uint64_t rule_count = 0;
    auto make_sheet = [] { return SharedPtr<StyleSheet>(new StyleSheet()); };
    MediaBlock current{PropertyDictionary{}, make_sheet(), MediaQueryModifier::None};
    auto finish = [&] {
        PostprocessKeyframes(current.stylesheet->keyframes);
        current.stylesheet->source_order_offset = rule_count;
        style_sheets.push_back(std::move(current));
        rule_count = 0;
    };
    auto descriptors = [&](AbstractPropertyParser& parser, const CssRule& rule) {
        for (const auto& declaration : rule.declarations)
            if (!parser.ParseDeclaration(declaration))
                Report(DiagnosticSeverity::Warning, "CSS_PROPERTY_VALUE", "Invalid CSS declaration '" + declaration.propertyName + "'.", declaration.span);
    };
    std::function<void(const Vector<CssRule>&, bool)> append;
    append = [&](const Vector<CssRule>& rules, bool inside_media) {
        for (const auto& rule : rules) {
            line_number = begin_line_number + int(rule.span.line) - 1;
            if (rule.atRule.empty()) {
                PropertyDictionary properties;
                PropertySpecificationParser parser(properties, property_specification);
                descriptors(parser, rule);
                auto leafs = ConstructNodes(*current.stylesheet->root, rule.prelude);
                if (leafs.empty()) {
                    Report(DiagnosticSeverity::Warning, "CSS_SELECTOR", "Invalid selector list '" + rule.prelude + "'; entire rule ignored.", rule.span);
                    continue;
                }
                properties.SetSourceOfAllProperties(MakeShared<PropertySource>(stream_file_name, line_number, rule.prelude));
                for (auto* leaf : leafs) leaf->ImportProperties(properties, rule_count);
                rule_count += rule.declarations.size() + 1;
            } else if (rule.atRule == "property") {
                // Top-level registrations are validated before declarations use them.
                if (inside_media) Report(DiagnosticSeverity::Warning, "CSS_CONDITIONAL_PROPERTY", "Conditional @property is outside the current registration subset.", rule.span);
            } else if (rule.atRule == "keyframes") {
                for (const auto& frame : rule.children) {
                    PropertyDictionary properties;
                    PropertySpecificationParser parser(properties, property_specification);
                    for (const auto& declaration : frame.declarations)
                        if (!declaration.important && !parser.ParseDeclaration(declaration))
                            Report(DiagnosticSeverity::Warning, "CSS_PROPERTY_VALUE", "Invalid keyframe declaration '" + declaration.propertyName + "'.", declaration.span);
                    if (!ParseKeyframeBlock(current.stylesheet->keyframes, rule.prelude, frame.prelude, properties))
                        Report(DiagnosticSeverity::Warning, "CSS_KEYFRAME", "Invalid keyframe rule.", frame.span);
                }
            } else if (rule.atRule == "supports") {
                const auto supported = SupportsDeclaration(rule.prelude, property_specification);
                if (!supported.first) Report(DiagnosticSeverity::Warning, "CSS_SUPPORTS", "Only declaration capability queries and not are supported.", rule.span);
                else if (supported.second) append(rule.children, inside_media);
            } else if (rule.atRule == "media") {
                if (inside_media) {
                    Report(DiagnosticSeverity::Warning, "CSS_NESTED_MEDIA", "Nested media queries are outside the current CSS subset.", rule.span);
                    continue;
                }
                finish();
                current = MediaBlock{PropertyDictionary{}, make_sheet(), MediaQueryModifier::None};
                if (ParseMediaFeatureMap(rule.prelude, current.properties, current.modifier)) append(rule.children, true);
                else Report(DiagnosticSeverity::Warning, "CSS_MEDIA", "Invalid media query.", rule.span);
                finish();
                current = MediaBlock{PropertyDictionary{}, make_sheet(), MediaQueryModifier::None};
            } else if (rule.atRule == "font-face" || rule.atRule == "decorator") {
                current_rule = &rule;
                auto source = MakeShared<PropertySource>(stream_file_name, line_number, rule.prelude);
                if (rule.atRule == "font-face") {
                    if (!ParseFontFaceBlock(current.stylesheet->font_faces, source))
                        Report(DiagnosticSeverity::Warning, "CSS_FONT_FACE", "Invalid font-face rule.", rule.span);
                }
                else if (rule.atRule == "decorator") {
                    if (!ParseDecoratorBlock(rule.prelude, current.stylesheet->named_decorator_map, source))
                        Report(DiagnosticSeverity::Warning, "CSS_DECORATOR", "Invalid URUI decorator rule.", rule.span);
                }
                else {
                    auto& parser = style_sheet_property_parsers->spritesheet;
                    descriptors(parser, rule);
                    const float resolution = parser.GetImageResolutionFactor();
                    if (!parser.GetImageSource().empty() && !parser.GetSpriteDefinitions().empty() && resolution > 0 && resolution < 100)
                        current.stylesheet->spritesheet_list.AddSpriteSheet(rule.prelude, parser.GetImageSource(), stream_file_name,
                            line_number, 1.f / resolution, parser.GetSpriteDefinitions());
                    parser.Clear();
                }
                current_rule = nullptr;
            } else Report(DiagnosticSeverity::Warning, "CSS_AT_RULE", "Unsupported @" + rule.atRule + " ignored.", rule.span);
        }
    };
    append(parsed.rules, false);
    finish();
    registration.committed = bool(parsed) && (!validate_resources || validate_resources());
    if (registration.committed) {
        bool changed = !registration.added.empty();
        for (const auto& entry : registration.defaults) changed = changed || *entry.first->GetDefaultValue() != entry.second;
        if (changed) property_specification.NotifyRegisteredPropertiesChanged();
    }
    return registration.committed;
}

void StyleSheetParser::ParseProperties(PropertyDictionary& parsed_properties, const String& properties)
{
	URUI_ASSERT(!stream);
	StreamMemory stream_owner((const byte*)properties.c_str(), properties.size());
	stream = &stream_owner;
	PropertySpecificationParser parser(parsed_properties, property_specification);
	ReadProperties(parser);
	stream = nullptr;
}

StyleSheetNodeListRaw StyleSheetParser::ConstructNodes(StyleSheetNode& root_node, const String& selectors)
{
    Vector<Diagnostic> diagnostics;
    const auto tokens = TokenizeCss(selectors, "urui:selector", diagnostics);
    if (!diagnostics.empty()) return {};
    StringList selector_list;
    String current;
    Vector<char> brackets;
    bool has_simple = false;
    for (size_t i = 0; i < tokens.size(); ++i) {
        const auto& token = tokens[i];
        if (token.kind == CssTokenKind::BadString) return {};
        if (token.kind == CssTokenKind::Delimiter) {
            const char c = token.value[0];
            if (c == ',' && brackets.empty()) {
                if (!has_simple || StringUtilities::StripWhitespace(current).empty()) return {};
                selector_list.push_back(StringUtilities::StripWhitespace(current));
                current.clear(); has_simple = false; continue;
            }
            if (brackets.empty()) {
                if (c == '>' || c == '+' || c == '~') {
                    if (!has_simple) return {};
                    has_simple = false;
                } else if (c == '#' || c == '.' || c == ':') {
                    if (i + 1 >= tokens.size() || tokens[i + 1].kind != CssTokenKind::Ident) return {};
                    has_simple = true;
                } else if (c != '*' && c != '[' && c != '(') return {};
                else if (c == '*' || c == '[') has_simple = true;
            }
            if (c == '(' || c == '[') brackets.push_back(c);
            else if (c == ')' || c == ']') {
                if (brackets.empty() || brackets.back() != (c == ')' ? '(' : '[')) return {};
                brackets.pop_back();
            }
        } else if (brackets.empty() && token.kind == CssTokenKind::Ident) has_simple = true;
        else if (brackets.empty() && token.kind == CssTokenKind::String) return {};
        if (token.kind == CssTokenKind::Whitespace) current += ' ';
        else if (token.kind == CssTokenKind::Ident && brackets.empty()) {
            // Canonicalize escapes without exposing their optional whitespace
            // terminator to the selector combinator parser.
            for (char c : token.value) {
                if (c == ' ' || c == '#' || c == '.' || c == ':' || c == '[' || c == ']' || c == '>' || c == '+' || c == '~' || c == '\\') current += '\\';
                current += c;
            }
        } else current += token.raw;
    }
    if (!brackets.empty() || !has_simple || StringUtilities::StripWhitespace(current).empty()) return {};
    selector_list.push_back(StringUtilities::StripWhitespace(current));
    StyleSheetNodeListRaw leafs;
    const PropertyDictionary empty;
    for (const auto& selector : selector_list) {
        auto* leaf = ImportProperties(&root_node, selector, empty, 0);
        if (!leaf || leaf == &root_node) return {};
        leafs.push_back(leaf);
    }
    return leafs;
}

void StyleSheetParser::ReadProperties(AbstractPropertyParser& property_parser)
{
    if (current_rule) {
        for (const auto& declaration : current_rule->declarations)
            if (!property_parser.ParseDeclaration(declaration))
                Report(DiagnosticSeverity::Warning, "CSS_PROPERTY_VALUE", "Invalid CSS descriptor '" + declaration.propertyName + "'.", declaration.span);
        return;
    }
    if (!stream) return;
    String text;
    stream->Read(text, stream->Length() - stream->Tell());
    Vector<Diagnostic> diagnostics;
    for (const auto& declaration : ParseCssDeclarations(text, stream_file_name, diagnostics))
        if (!property_parser.ParseDeclaration(declaration))
            Log::Message(Log::LT_WARNING, "Invalid CSS declaration '%s: %s' at %s:%u", declaration.propertyName.c_str(),
                declaration.tokenValue.c_str(), stream_file_name.c_str(), declaration.span.line);
}

StyleSheetNode* StyleSheetParser::ImportProperties(StyleSheetNode* node, const String& rule, const PropertyDictionary& properties,
	uint64_t rule_source_order)
{
	StyleSheetNode* leaf_node = node;

	// Create each node going down the tree.
	for (size_t index = 0; index < rule.size();)
	{
		CompoundSelector selector;

		// Determine the combinator connecting the previous node if any.
		for (; index > 0 && index < rule.size(); index++)
		{
			bool reached_end_of_combinators = false;
			switch (rule[index])
			{
			case ' ': break;
			case '>': selector.combinator = SelectorCombinator::Child; break;
			case '+': selector.combinator = SelectorCombinator::NextSibling; break;
			case '~': selector.combinator = SelectorCombinator::SubsequentSibling; break;
			default: reached_end_of_combinators = true; break;
			}
			if (reached_end_of_combinators)
				break;
		}

		// Determine the node's requirements.
		while (index < rule.size())
		{
			size_t start_index = index;
			size_t end_index = index + 1;

			if (rule[start_index] == '*')
				start_index += 1;

			if (rule[start_index] == '[')
			{
				end_index = FindAttributeSelectorEnd(rule, start_index);
				if (end_index == String::npos)
					return nullptr;
				end_index += 1;
			}
			else
			{
				int parenthesis_count = 0;

				if (rule[start_index] == ':')
				{
					// Parse pseudo-element ::A as pseudo-class with name :A
					if (end_index < rule.size() && rule[end_index] == ':')
						end_index += 1;
				}

				// Read until we hit the next identifier. Don't match inside parenthesis in case of structural selectors.
				for (; end_index < rule.size(); end_index++)
				{
					static const String identifiers = "#.:[ >+~";
					if (parenthesis_count == 0 && identifiers.find(rule[end_index]) != String::npos && !IsEscapedCharacter(rule, end_index))
						break;

					if (rule[end_index] == '(' && !IsEscapedCharacter(rule, end_index))
						parenthesis_count += 1;
					else if (rule[end_index] == ')' && !IsEscapedCharacter(rule, end_index))
						parenthesis_count -= 1;
				}
			}

			if (end_index > start_index)
			{
				switch (rule[start_index])
				{
				case '#': {
					String id = ExtractUnescapedSelectorToken(rule, start_index + 1, end_index);
					if (selector.id.empty()) selector.id = std::move(id);
					else selector.additional_ids.push_back(std::move(id));
				} break;
				case '.': selector.class_names.push_back(ExtractUnescapedSelectorToken(rule, start_index + 1, end_index)); break;
				case ':':
				{
					String pseudo_class_name = ExtractUnescapedSelectorToken(rule, start_index + 1, end_index);
					StructuralSelector node_selector = StyleSheetFactory::GetSelector(pseudo_class_name);
					if (node_selector.type != StructuralSelectorType::Invalid)
						selector.structural_selectors.push_back(node_selector);
					else {
						pseudo_class_name = AsciiLowerCss(std::move(pseudo_class_name));
						if (pseudo_class_name != "root" && pseudo_class_name != "hover" && pseudo_class_name != "active" &&
							pseudo_class_name != "focus" && pseudo_class_name != "focus-visible" && pseudo_class_name != "checked" &&
							pseudo_class_name != "disabled" && pseudo_class_name != "enabled" && pseudo_class_name != "placeholder-shown") return nullptr;
						selector.pseudo_class_names.push_back(std::move(pseudo_class_name));
					}
				}
				break;
				case '[':
				{
					const size_t i_attr_begin = start_index + 1;
					const size_t i_attr_end = end_index - 1;
					if (i_attr_end <= i_attr_begin)
						return nullptr;

					AttributeSelector attribute;
					if (!ParseAttributeSelector(attribute, rule, i_attr_begin, i_attr_end)) return nullptr;

					selector.attributes.push_back(std::move(attribute));
				}
				break;
				default: selector.tag = AsciiLowerCss(ExtractUnescapedSelectorToken(rule, start_index, end_index)); break;
				}
			}

			index = end_index;

			// If we reached a combinator then we submit the current node and start fresh with a new node.
			static const String combinators(" >+~");
			if (combinators.find(rule[index]) != String::npos)
				break;
		}

		// Sort the classes and pseudo-classes so they are consistent across equivalent declarations that shuffle the order around.
		std::sort(selector.class_names.begin(), selector.class_names.end());
		std::sort(selector.attributes.begin(), selector.attributes.end());
		std::sort(selector.pseudo_class_names.begin(), selector.pseudo_class_names.end());
		std::sort(selector.structural_selectors.begin(), selector.structural_selectors.end());

		// Add the new child node, or retrieve the existing child if we have an exact match.
		leaf_node = leaf_node->GetOrCreateChildNode(std::move(selector));
	}

	// Merge the new properties with those already on the leaf node.
	leaf_node->ImportProperties(properties, rule_source_order);

	return leaf_node;
}

} // namespace URUI
