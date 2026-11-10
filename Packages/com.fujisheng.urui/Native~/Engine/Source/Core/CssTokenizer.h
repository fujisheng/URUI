#pragma once
#include "CssDeclaration.h"

namespace URUI {
enum class CssTokenKind { Ident, String, Url, Number, Dimension, Percentage, Whitespace, Delimiter, BadString };
struct CssToken {
    CssTokenKind kind;
    String value;
    String raw;
    SourceSpan span;
};
Vector<CssToken> TokenizeCss(const String& css, const String& sourceUrl, Vector<Diagnostic>& diagnostics, size_t initial_offset = 0);
String DecodeCssIdentifier(const String& raw);
bool DecodeCssUrl(const CssToken& token, String& url);
struct CssFontFamily { String name; int generic = -1; };
bool ParseCssFontFamilies(const String& value, Vector<CssFontFamily>& families);
String AsciiLowerCss(String value);
String SerializeCssIdentifier(const String& value);
struct CssVariableReference {
    String name;
    size_t begin = 0, end = 0, fallbackBegin = 0;
    bool hasFallback = false;
};
bool ScanCssVariables(const String& value, Vector<CssVariableReference>& references);
bool SplitCssNumericValue(const String& value, String& number, String& unit);
} // namespace URUI
