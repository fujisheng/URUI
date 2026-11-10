#include "CssTokenizer.h"
#include "PropertyParserString.h"

namespace URUI {

PropertyParserString::PropertyParserString(bool font_family) : font_family(font_family) {}

PropertyParserString::~PropertyParserString() {}

bool PropertyParserString::ParseValue(Property& property, const String& value, const ParameterMap& /*parameters*/) const
{
	if (font_family) {
		Vector<CssFontFamily> families;
		if (!ParseCssFontFamilies(value, families)) return false;
		property.value = value; property.unit = Unit::STRING;
		return true;
	}
	Vector<Diagnostic> diagnostics;
	const auto tokens = TokenizeCss(value, "urui:string", diagnostics);
	String decoded;
	for (size_t i = 0; i < tokens.size(); ++i) {
		const auto& token = tokens[i];
		if (i && token.kind == CssTokenKind::Ident && tokens[i - 1].kind == CssTokenKind::Ident) decoded += ' ';
		decoded += token.kind == CssTokenKind::Ident || token.kind == CssTokenKind::String ? token.value : token.raw;
	}
	property.value = Variant(decoded);
	property.unit = Unit::STRING;

	return true;
}

} // namespace URUI
