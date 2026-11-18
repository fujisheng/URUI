#pragma once
#include <URUI/Core/Types.h>
#include <vector>

namespace URUI {
class PropertyDictionary;

enum class DiagnosticSeverity { Warning, Error };
struct SourceSpan {
    uint32_t byteOffset = 0;
    uint32_t length = 0;
    uint32_t line = 1;
    uint32_t column = 1;
};
struct Diagnostic {
    DiagnosticSeverity severity = DiagnosticSeverity::Error;
    String code, message, sourceUrl;
    SourceSpan span;
};
enum class HtmlNodeKind { Element, Text, Comment, Template };
struct HtmlAttribute {
    String name, value;
    SourceSpan span;
};
struct HtmlNode {
    HtmlNodeKind kind = HtmlNodeKind::Element;
    String tag, text;
    std::vector<HtmlAttribute> attributes;
    std::vector<HtmlNode> children;
    SourceSpan start, end;
    SharedPtr<const PropertyDictionary> compiledStyle;
    uint32_t localId = UINT32_MAX, subtreeEnd = 0;
    StringList compiledClasses;
};
enum class HtmlDocumentKind { Document, Fragment };
struct HtmlDocument {
    HtmlDocumentKind kind = HtmlDocumentKind::Document;
    String sourceUrl;
    std::vector<HtmlNode> nodes;
    uint64_t blueprintIdentity = 0;
};
struct HtmlParseStats {
    size_t allocationCount = 0;
    size_t peakParserBytes = 0;
};
struct HtmlParseResult {
    std::unique_ptr<HtmlDocument> document;
    std::vector<Diagnostic> diagnostics;
    HtmlParseStats stats;
    explicit operator bool() const { return document != nullptr; }
};

} // namespace URUI
