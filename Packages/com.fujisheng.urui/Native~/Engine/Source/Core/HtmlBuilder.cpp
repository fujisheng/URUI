#include "HtmlBuilder.h"
#include "DocumentHeader.h"
#include <URUI/Core/Core.h>
#include <URUI/Core/Context.h>
#include <URUI/Core/ElementDocument.h>
#include <URUI/Core/ElementText.h>
#include <URUI/Core/Elements/ElementFormControlSelect.h>
#include <URUI/Core/Elements/ElementFormControlTextArea.h>
#include <URUI/Core/Factory.h>
#include <URUI/Core/StringUtilities.h>
#include <URUI/Core/SystemInterface.h>

namespace URUI {
URUI_RTTI_Define(ElementTemplate)
URUI_RTTI_Define(ElementComment)

namespace {
String Attribute(const HtmlNode& node, const String& name)
{
    for (const auto& attribute : node.attributes) if (attribute.name == name) return attribute.value;
    return {};
}
String TextContent(const HtmlNode& node)
{
    String text;
    for (const auto& child : node.children) {
        if (child.kind == HtmlNodeKind::Text) text += child.text;
        else if (child.kind != HtmlNodeKind::Comment) text += TextContent(child);
    }
    return text;
}
ElementAttributes Attributes(const HtmlNode& node)
{
    ElementAttributes attributes;
    for (const auto& attribute : node.attributes) attributes.emplace(attribute.name, attribute.value);
    return attributes;
}
void Failure(Vector<Diagnostic>& diagnostics, const HtmlDocument& parsed, const HtmlNode* node, const String& message)
{
    diagnostics.push_back({DiagnosticSeverity::Error, "HTML_BUILD", message, parsed.sourceUrl, node ? node->start : SourceSpan{}});
}
bool BuildOptions(const Vector<HtmlNode>& nodes, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics,
    Vector<ElementPtr>& options, int& selected, Element& owner);
void InstallOptions(ElementFormControlSelect& select, Vector<ElementPtr> options, int selected);
ElementPtr BuildNode(const HtmlNode& node, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics, Element& owner)
{
    if (node.kind == HtmlNodeKind::Text) {
        auto element = Factory::InstanceElement(&owner, "#text", "#text", {});
        auto* text = urui_dynamic_cast<ElementText*>(element.get());
        if (!text) { Failure(diagnostics, parsed, &node, "Text instancing failed."); return nullptr; }
        text->SetText(node.text);
        return element;
    }
    if (node.kind == HtmlNodeKind::Comment) {
        auto element = Factory::InstanceElement(&owner, "#comment", "#comment", {});
        auto* comment = urui_dynamic_cast<ElementComment*>(element.get());
        if (!comment) { Failure(diagnostics, parsed, &node, "Comment instancing failed."); return nullptr; }
        comment->text = node.text;
        comment->SetProperty("display", "none");
        return element;
    }
    auto element = Factory::InstanceElement(&owner, node.tag, node.tag, Attributes(node));
    if (!element) { Failure(diagnostics, parsed, &node, "Element <" + node.tag + "> instancing failed."); return nullptr; }
    if (node.kind == HtmlNodeKind::Template) {
        auto* item = urui_dynamic_cast<ElementTemplate*>(element.get());
        if (!item) { Failure(diagnostics, parsed, &node, "Template instancer must create ElementTemplate."); return nullptr; }
        HtmlDocument content;
        content.kind = HtmlDocumentKind::Fragment;
        content.sourceUrl = parsed.sourceUrl;
        content.nodes = node.children;
        item->SetContent(content);
        return element;
    }
    if (auto* textarea = urui_dynamic_cast<ElementFormControlTextArea*>(element.get())) {
        textarea->SetValue(TextContent(node));
        return element;
    }
    if (auto* select = urui_dynamic_cast<ElementFormControlSelect*>(element.get())) {
        Vector<ElementPtr> options;
        int selected = -1;
        if (!BuildOptions(node.children, parsed, diagnostics, options, selected, *element)) return nullptr;
        InstallOptions(*select, std::move(options), selected);
        return element;
    }
    for (const auto& child : node.children) {
        auto built = BuildNode(child, parsed, diagnostics, *element);
        if (!built) return nullptr;
        element->AppendChild(std::move(built));
    }
    return element;
}
bool BuildOptions(const Vector<HtmlNode>& nodes, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics,
    Vector<ElementPtr>& options, int& selected, Element& owner)
{
    options.reserve(nodes.size());
    int firstEnabled = -1;
    for (const auto& node : nodes) {
        if (node.kind == HtmlNodeKind::Text || node.kind == HtmlNodeKind::Comment) continue;
        if (node.tag != "option") { Failure(diagnostics, parsed, &node, "Only option elements are supported inside select."); return false; }
        auto option = BuildNode(node, parsed, diagnostics, owner);
        if (!option) return false;
        if (!option->HasAttribute("value")) option->SetAttribute("value", TextContent(node));
        const int index = int(options.size());
        if (!option->HasAttribute("disabled") && firstEnabled < 0) firstEnabled = index;
        if (option->HasAttribute("selected")) selected = index;
        options.push_back(std::move(option));
    }
    if (selected < 0) selected = firstEnabled;
    return true;
}
void InstallOptions(ElementFormControlSelect& select, Vector<ElementPtr> options, int selected)
{
    select.RemoveAll();
    for (auto& option : options) select.Add(std::move(option));
    select.SetSelection(selected);
}
Vector<ElementPtr> BuildNodes(const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics, Element& owner)
{
    Vector<ElementPtr> nodes;
    nodes.reserve(parsed.nodes.size());
    for (const auto& node : parsed.nodes) {
        auto element = BuildNode(node, parsed, diagnostics, owner);
        if (!element) return {};
        nodes.push_back(std::move(element));
    }
    return nodes;
}
void Serialize(const HtmlNode& node, String& output, bool rawText = false)
{
    if (node.kind == HtmlNodeKind::Text) {
        output += rawText ? node.text : StringUtilities::EncodeHtml(node.text);
    } else if (node.kind == HtmlNodeKind::Comment) {
        output += "<!--" + node.text + "-->";
    } else {
        output += "<" + node.tag;
        for (const auto& attribute : node.attributes)
            output += " " + attribute.name + "=\"" + StringUtilities::EncodeHtml(attribute.value) + "\"";
        output += ">";
        if (!IsHtmlVoidElement(node.tag)) {
            for (const auto& child : node.children) Serialize(child, output, node.tag == "style");
            output += "</" + node.tag + ">";
        }
    }
}
} // namespace

bool IsHtmlVoidElement(const String& tag)
{
    return tag == "area" || tag == "base" || tag == "br" || tag == "col" || tag == "embed" || tag == "hr" ||
        tag == "img" || tag == "input" || tag == "link" || tag == "meta" || tag == "param" || tag == "source" || tag == "track" || tag == "wbr";
}
Vector<ElementPtr> ElementTemplate::CloneContent(Vector<Diagnostic>& diagnostics) const
{
    return BuildNodes(content, diagnostics, *const_cast<ElementTemplate*>(this));
}
void ElementTemplate::GetInnerHTML(String& output) const
{
    for (const auto& node : content.nodes) Serialize(node, output);
}
bool BuildHtmlDocument(ElementDocument& target, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics)
{
    if (parsed.kind != HtmlDocumentKind::Document || target.GetTagName() != "html" || target.GetNumChildren() != 0) {
        Failure(diagnostics, parsed, nullptr, "Document building requires an empty html candidate.");
        return false;
    }
    const HtmlNode* html = nullptr;
    for (const auto& node : parsed.nodes) if (node.tag == "html") html = &node;
    if (!html) { Failure(diagnostics, parsed, nullptr, "Missing document element."); return false; }
    HtmlDocument fragment;
    fragment.kind = HtmlDocumentKind::Fragment;
    fragment.sourceUrl = parsed.sourceUrl;
    fragment.nodes = html->children;
    DocumentHeader header;
    header.source = parsed.sourceUrl;
    // The author sheet follows this subset of the browser's user-agent defaults.
    header.css.push_back({parsed.sourceUrl,
        "html,body,div,p,pre,blockquote,address,section,article,header,footer,main,nav,aside,figure,figcaption,ul,ol,li,dl,dt,dd,form,h1,h2,h3,h4,h5,h6{display:block;}"
        "head,title,link,meta,style,template{display:none;}body{margin:8px;}pre{white-space:pre;}"
        "button,input,select,textarea,progress{display:inline-block;}"
        "select>option{display:none;}"
        "urui-internal-select-value{display:block;}"
        "urui-internal-select-arrow{width:0;height:0;margin-top:7px;border-top:6px solid #323232;"
        "border-left:5px solid transparent;border-right:5px solid transparent;}"
        "urui-internal-select-options{background-color:white;color:black;border:1px solid gray;max-height:220px;overflow-y:auto;}"
        "urui-internal-select-options>option{padding:6px 8px;}"
        "urui-internal-select-options>option:checked,urui-internal-select-options>option:hover{background-color:#f5f5f5;color:#323232;}"
        "urui-internal-scrollbar-vertical{width:12px;}urui-internal-scrollbar-horizontal{height:12px;}"
        "urui-internal-scrollbar-vertical urui-internal-range-track,urui-internal-scrollbar-horizontal urui-internal-range-track{background-color:#d9d9d9;}"
        "urui-internal-scrollbar-vertical urui-internal-range-thumb{width:10px;min-height:18px;margin-left:1px;background-color:white;}"
        "urui-internal-scrollbar-horizontal urui-internal-range-thumb{height:10px;min-width:18px;margin-top:1px;background-color:white;}"
        "urui-internal-scrollbar-corner{background-color:#d9d9d9;}"
        "[hidden]{display:none;}"
        "b,strong{font-weight:bold;}i,em{font-style:italic;}table{display:table;}thead,tbody,tfoot{display:table-row-group;}"
        "tr{display:table-row;}td,th{display:table-cell;}col{display:table-column;}colgroup{display:table-column-group;}", true, 1, CssOrigin::UserAgent});
    for (const auto& node : html->children) if (node.tag == "head") {
        for (const auto& resource : node.children) {
            if (resource.tag == "title") header.title = TextContent(resource);
            if (resource.tag == "style")
                header.css.push_back({parsed.sourceUrl, TextContent(resource), true, int(resource.start.line)});
            if (resource.tag == "link" && StringUtilities::ToLower(Attribute(resource, "rel")) == "stylesheet") {
                const auto href = Attribute(resource, "href");
                if (href.empty()) { Failure(diagnostics, parsed, &resource, "Stylesheet link requires href."); return false; }
                String path;
                GetSystemInterface()->JoinPath(path, parsed.sourceUrl, href);
                header.css.push_back({path, {}, false, int(resource.start.line)});
            }
        }
    }
    // Registrations and font-face matching must exist before node attributes are parsed.
    if (!target.ProcessHeader(&header)) {
        Failure(diagnostics, parsed, html, target.GetResourceError());
        return false;
    }
    auto children = BuildNodes(fragment, diagnostics, target);
    if (children.size() != html->children.size()) return false;
    target.SetAttributes(Attributes(*html));
    for (auto& child : children) target.AppendChild(std::move(child));
    return true;
}
bool ReplaceInnerHtml(Element& target, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics)
{
    if (parsed.kind != HtmlDocumentKind::Fragment || IsHtmlVoidElement(target.GetTagName())) {
        Failure(diagnostics, parsed, nullptr, "Inner HTML requires a fragment and a non-void target.");
        return false;
    }
    if (auto* item = urui_dynamic_cast<ElementTemplate*>(&target)) { item->SetContent(parsed); return true; }
    if (auto* textarea = urui_dynamic_cast<ElementFormControlTextArea*>(&target)) {
        String text;
        for (const auto& node : parsed.nodes) if (node.kind == HtmlNodeKind::Text) text += node.text;
        textarea->SetValue(text);
        return true;
    }
    if (auto* select = urui_dynamic_cast<ElementFormControlSelect*>(&target)) {
        Vector<ElementPtr> options;
        int selected = -1;
        if (!BuildOptions(parsed.nodes, parsed, diagnostics, options, selected, target)) return false;
        InstallOptions(*select, std::move(options), selected);
        return true;
    }
    auto children = BuildNodes(parsed, diagnostics, target);
    if (children.size() != parsed.nodes.size()) return false;
    while (auto* child = target.GetFirstChild()) target.RemoveChild(child);
    for (auto& child : children) target.AppendChild(std::move(child));
    return true;
}

InnerHtmlReplacement::InnerHtmlReplacement(Element& element) : target(element.GetObserverPtr()) {}

UniquePtr<InnerHtmlReplacement> InnerHtmlReplacement::Begin(Element& target, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics)
{
    if (parsed.kind != HtmlDocumentKind::Fragment || IsHtmlVoidElement(target.GetTagName())) {
        Failure(diagnostics, parsed, nullptr, "Inner HTML requires a fragment and a non-void target.");
        return nullptr;
    }
    UniquePtr<InnerHtmlReplacement> change(new InnerHtmlReplacement(target));
    Vector<ElementPtr> children;
    int selected = -1;
    String text;
    auto* item = urui_dynamic_cast<ElementTemplate*>(&target);
    auto* textarea = urui_dynamic_cast<ElementFormControlTextArea*>(&target);
    auto* select = urui_dynamic_cast<ElementFormControlSelect*>(&target);
    if (textarea) {
        for (const auto& node : parsed.nodes) if (node.kind == HtmlNodeKind::Text) text += node.text;
    } else if (select) {
        if (!BuildOptions(parsed.nodes, parsed, diagnostics, children, selected, target)) return nullptr;
    } else if (!item) {
        children = BuildNodes(parsed, diagnostics, target);
        if (children.size() != parsed.nodes.size()) return nullptr;
    }
    if (auto* context = target.GetContext()) {
        if (auto* focus = context->GetFocusElement()) change->focused = focus->GetObserverPtr();
    }
    if (item) change->template_content = item->GetContent();
    else if (textarea) change->text_value = textarea->GetValue();
    else {
        change->previous.reserve(target.GetNumChildren());
        if (select) change->selection = select->GetSelection();
    }
    change->active = true;
    if (item) item->SetContent(parsed);
    else if (textarea) textarea->SetValue(text);
    else {
        while (auto* child = target.GetFirstChild()) change->previous.push_back(target.RemoveChild(child));
        for (auto& child : children) target.AppendChild(std::move(child));
        if (select) select->SetSelection(selected);
    }
    return change;
}

InnerHtmlReplacement::~InnerHtmlReplacement() { Rollback(); }

void InnerHtmlReplacement::Commit()
{
    active = false;
    previous.clear();
}

void InnerHtmlReplacement::Rollback()
{
    if (!active) return;
    active = false;
    auto* element = target.get();
    if (!element) { previous.clear(); return; }
    if (auto* item = urui_dynamic_cast<ElementTemplate*>(element)) item->SetContent(template_content);
    else if (auto* textarea = urui_dynamic_cast<ElementFormControlTextArea*>(element)) textarea->SetValue(text_value);
    else {
        while (auto* child = element->GetFirstChild()) element->RemoveChild(child);
        for (auto& child : previous) element->AppendChild(std::move(child));
        previous.clear();
        if (auto* select = urui_dynamic_cast<ElementFormControlSelect*>(element)) select->SetSelection(selection);
    }
    if (auto* focus = focused.get()) focus->Focus();
}
} // namespace URUI
