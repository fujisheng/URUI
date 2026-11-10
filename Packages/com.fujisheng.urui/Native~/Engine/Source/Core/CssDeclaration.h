#pragma once
#include "HtmlDocument.h"
#include <URUI/Core/CascadePriority.h>

namespace URUI {
struct CssDeclaration {
    String propertyName;
    String tokenValue;
    bool important = false;
    uint64_t sourceOrder = 0;
    SourceSpan span;
};
struct CssRule {
    String prelude;
    String atRule;
    String block;
    Vector<CssDeclaration> declarations;
    Vector<CssRule> children;
    SourceSpan span;
};
struct CssParseResult {
    Vector<CssRule> rules;
    Vector<Diagnostic> diagnostics;
    explicit operator bool() const {
        for (const auto& diagnostic : diagnostics) if (diagnostic.severity == DiagnosticSeverity::Error) return false;
        return true;
    }
};
} // namespace URUI
