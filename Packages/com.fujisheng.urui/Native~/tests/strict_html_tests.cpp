#include <cassert>
#include <iostream>
#include <string>
#include <chrono>
#if __has_include("HtmlParser.h")
#include "HtmlParser.h"
#define URUI_HAS_HTML_PARSER 1
#endif

#if defined(URUI_HAS_HTML_PARSER)
using namespace URUI;
static String Document(const String& body, const String& head = "")
{
    return "<!doctype html><html><head>" + head + "</head><body>" + body + "</body></html>";
}
static const HtmlNode& Child(const HtmlNode& node, const String& tag)
{
    for (const auto& child : node.children) if (child.tag == tag) return child;
    std::cerr << "Missing node: " << tag << '\n';
    std::abort();
}
static void Rejected(const String& source)
{
    auto result = ParseHtmlDocument(source, "ui/test.html");
    if (result) std::cerr << "Unexpectedly accepted: " << source << '\n';
    assert(!result && !result.document && !result.diagnostics.empty());
    for (const auto& diagnostic : result.diagnostics) {
        assert(diagnostic.severity == DiagnosticSeverity::Error);
        assert(!diagnostic.code.empty() && !diagnostic.message.empty());
        assert(diagnostic.sourceUrl == "ui/test.html");
        assert(diagnostic.span.line > 0 && diagnostic.span.column > 0);
        assert(diagnostic.span.byteOffset <= source.size());
    }
}
static void Contracts()
{
    for (const String& body : {"", "<br><img src='x'/><input disabled>",
        "<ul><li>A</li><li>B</li></ul>", "<select><option>A</option></select>",
        "<table><tbody><tr><td>A</td></tr></tbody></table>", "<urui-icon></urui-icon>"}) {
        auto result = ParseHtmlDocument(Document(body), "ui/test.html");
        if (!result) for (auto& d : result.diagnostics) std::cerr << d.code << ": " << d.message << '\n';
        assert(result && result.document->kind == HtmlDocumentKind::Document);
    }
    for (const String& body : {"<p>A<p>B</p>", "<ul><li>A<li>B</li></ul>",
        "<select><option>A<option>B</option></select>", "<div/>", "<urui-icon/>",
        "<b><i>x</b></i>", "<p><div></div></p>", "<table><tr><td>A</td></tr></table>",
        "<div>", "<svg></svg>", "<script></script>", "<div a='1' a='2'></div>"}) Rejected(Document(body));
    Rejected("<html><head></head><body></body></html>");
    Rejected("<!doctype html><html><body></body></html>");
    Rejected("<!doctype html><html><head></head></html>");
    Rejected(Document("", "<style type='text/invalid'></style>"));
    auto source = Document("<span>A</span> <span>B</span><p>&amp;lt; 中文</p>"
        "<!--note--><template><img src='deferred.png'></template><textarea>\n&lt;b&gt;</textarea>",
        "<style>.x::after { content: '<x>&amp;'; }</style>");
    auto result = ParseHtmlDocument(source, "ui/lifetime.html");
    assert(result);
    source.assign(source.size(), 'x');
    const auto& html = result.document->nodes.at(0);
    const auto& body = Child(html, "body");
    assert(body.children.at(1).kind == HtmlNodeKind::Text && body.children.at(1).text == " ");
    assert(Child(body, "p").children.at(0).text == "&lt; 中文");
    assert(body.children.at(4).kind == HtmlNodeKind::Comment && body.children.at(4).text == "note");
    assert(Child(body, "template").kind == HtmlNodeKind::Template);
    assert(Child(Child(body, "template"), "img").attributes.at(0).value == "deferred.png");
    assert(Child(body, "textarea").children.at(0).text == "<b>");
    assert(Child(Child(html, "head"), "style").children.at(0).text == ".x::after { content: '<x>&amp;'; }");
    auto fragment = ParseHtmlFragment("<span> A </span> ", "div", "ui/fragment.html");
    assert(fragment && fragment.document->kind == HtmlDocumentKind::Fragment);
    assert(fragment.document->nodes.size() == 2 && fragment.document->nodes.at(1).text == " ");
    assert(ParseHtmlFragment("<option>A</option>", "select", "ui/select.html"));
    assert(ParseHtmlFragment("<tbody><tr><td>A</td></tr></tbody>", "table", "ui/table.html"));
    assert(!ParseHtmlFragment("<tr><td>A</td></tr>", "table", "ui/table.html"));
    assert(!ParseHtmlFragment("<div/>", "div", "ui/fragment.html"));
    assert(!ParseHtmlDocument(Document(""), ""));
    assert(!ParseHtmlDocument(Document(String(1, '\0')), "ui/nul.html"));
    assert(!ParseHtmlDocument(Document(String(1, char(0xff))), "ui/utf8.html"));
#if defined(URUI_HTML_PARSER_TESTING)
    const auto measuredSource = Document("<div class=card><span>A &amp; B</span><input disabled></div>");
    const auto measured = ParseHtmlDocument(measuredSource, "ui/memory.html");
    assert(measured && measured.stats.allocationCount > 0 && measured.stats.peakParserBytes > 0);
    for (size_t limit = 0; limit < measured.stats.allocationCount; ++limit) {
        const auto failed = HtmlParserTesting::ParseWithAllocationLimit(measuredSource, "ui/memory.html", limit);
        assert(!failed && !failed.diagnostics.empty() && failed.diagnostics.front().code == "HTML_MEMORY");
        assert(HtmlParserTesting::OutstandingAllocations() == 0);
    }
    const auto start = std::chrono::steady_clock::now();
    for (unsigned i = 0; i < 1000; ++i) assert(ParseHtmlDocument(measuredSource, "ui/memory.html"));
    const auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - start).count();
    assert(HtmlParserTesting::OutstandingAllocations() == 0);
    std::cout << "Parser baseline: input=" << measuredSource.size() << " peakBytes=" << measured.stats.peakParserBytes
        << " allocations=" << measured.stats.allocationCount << " parses=1000 totalMicroseconds=" << elapsed << '\n';
#endif
}
#endif
int main()
{
#if defined(URUI_HAS_HTML_PARSER)
    Contracts();
    std::cout << "Strict HTML contracts passed\n";
#else
    std::cerr << "Strict HTML parser is not implemented\n";
    return 1;
#endif
}
