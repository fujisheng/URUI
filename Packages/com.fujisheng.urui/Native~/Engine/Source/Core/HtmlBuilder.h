#pragma once
#include "HtmlDocument.h"
#include <URUI/Core/Element.h>

namespace URUI {

bool BuildHtmlDocument(ElementDocument& target, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics);
bool ReplaceInnerHtml(Element& target, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics);
bool IsHtmlVoidElement(const String& tag);

// Keeps the original nodes alive until managed controls have initialized successfully.
class InnerHtmlReplacement {
public:
    static UniquePtr<InnerHtmlReplacement> Begin(Element& target, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics);
    ~InnerHtmlReplacement();
    Element* Target() const { return target.get(); }
    void Commit();
    void Rollback();
private:
    explicit InnerHtmlReplacement(Element& element);
    ObserverPtr<Element> target, focused;
    Vector<ElementPtr> previous;
    HtmlDocument template_content;
    String text_value;
    int selection = -1;
    bool active = false;
};

class ElementTemplate final : public Element {
public:
    URUI_RTTI_DeclareWithParent(ElementTemplate, Element)
    explicit ElementTemplate(const String& tag) : Element(tag) {}
    void SetContent(const HtmlDocument& fragment) { content = fragment; }
    const HtmlDocument& GetContent() const { return content; }
    Vector<ElementPtr> CloneContent(Vector<Diagnostic>& diagnostics) const;
    using Element::GetInnerHTML;
    void GetInnerHTML(String& output) const override;
private:
    HtmlDocument content;
};

class ElementComment final : public Element {
public:
    URUI_RTTI_DeclareWithParent(ElementComment, Element)
    explicit ElementComment(const String& tag) : Element(tag) {}
    String text;
    void GetHTML(String& output) override { output += "<!--" + text + "-->"; }
};

} // namespace URUI
