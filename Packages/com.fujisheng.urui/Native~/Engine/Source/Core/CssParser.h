#pragma once
#include "CssTokenizer.h"

namespace URUI {
CssParseResult ParseCss(const String& css, const String& sourceUrl);
Vector<CssDeclaration> ParseCssDeclarations(const String& css, const String& sourceUrl, Vector<Diagnostic>& diagnostics);
} // namespace URUI
