#include "test_document.h"
#include "urui.h"
#include <URUI/Core.h>
#include <URUI/Core/Elements/ElementFormControlSelect.h>
#include <URUI/Core/Elements/ElementFormControlTextArea.h>
#include <cassert>
#include <cmath>
#include <iostream>
#if defined(URUI_HTML_BUILDER_TESTING)
#include "HtmlBuilder.h"
#define URUI_HAS_HTML_BUILDER 1
#endif

class FailingInstancer final : public URUI::ElementInstancer
{
public:
    URUI::ElementPtr InstanceElement(URUI::Element*, const URUI::String&, const URUI::ElementAttributes&) override
    {
        return nullptr;
    }
    void ReleaseElement(URUI::Element*) override
    {
        std::abort();
    }
};

class DataAttributeLog final : public URUI::SystemInterface
{
public:
    int binding_errors = 0;
    bool LogMessage(URUI::Log::Type, const URUI::String& message) override
    {
        if (message.find("data model") != URUI::String::npos)
        {
            ++binding_errors;
        }
        return true;
    }
};

int main()
{
    assert(urui_create(400, 200) == 1);
    auto* context = URUI::GetContext("urui");
    // Standard data-* attributes are inert application data, including old binding names.
    DataAttributeLog data_log;
    auto* previous_system = URUI::GetSystemInterface();
    URUI::SetSystemInterface(&data_log);
    auto* data_document = context->LoadDocumentFromMemory(
        "<!doctype html><html><head></head><body data-model='application-data'>"
        "<span id='data' data-value='plain' data-for='literal'>{{literal}}</span></body></html>",
        "ui/data.html");
    URUI::SetSystemInterface(previous_system);
    assert(data_document && data_log.binding_errors == 0);
    assert(data_document->GetElementById("data")->GetInnerHTML() == "{{literal}}");
    assert(data_document->GetElementById("data")->GetAttribute<URUI::String>("data-value", "") == "plain");
    data_document->Close();
    context->Update();
    auto* document = context->LoadDocumentFromMemory(
        "<!doctype html><html id='root' lang='zh'><head><style>:root { font-size:20px; } body {font-size:10px;}"
        "#rem {width:1rem;height:1rem;}</style></head><body id='body' class='page'><div id='content'>"
        "<span>A</span> <span>B</span><p>&amp;lt;</p><pre>  A\n B</pre></div><div id='rem'></div>"
        "<select id='select'><option value='a'>A</option><option value='b' selected hidden>B</option></select>"
        "<textarea id='textarea'>\n&lt;b&gt; &amp;lt;</textarea>"
        "<input id='upper-checkbox' type='CHECKBOX' checked='false'><input id='upper-range' type='RaNgE'>"
        "<div id='hidden' hidden='false'></div><div id='override-hidden' hidden style='display:block;'></div>"
        "<template id='item'><img id='inert' src='deferred.png'><span>A &amp; B</span></template>"
        "<!--kept--></body></html>",
        "ui/dom.html");
    assert(document && document->GetTagName() == "html" && document->GetId() == "root");
    auto* body = document->GetElementById("body");
    assert(body && body != document && body->GetTagName() == "body" && body->IsClassSet("page"));
    assert(document->Matches(":root") && !body->Matches(":root"));
    assert(document->GetElementById("upper-checkbox")->IsClassSet("checkbox"));
    assert(document->GetElementById("upper-range")->IsClassSet("range"));
    assert(!body->Matches("#urui body"));
    assert(document->Matches(":first-child:last-child:only-child"));
    document->Show();
    context->Update();
    assert(document->GetElementById("hidden")->GetComputedValues().display() == URUI::Style::Display::None);
    assert(document->GetElementById("override-hidden")->GetComputedValues().display() == URUI::Style::Display::Block);
    document->GetElementById("hidden")->RemoveAttribute("hidden");
    context->Update();
    assert(document->GetElementById("hidden")->GetComputedValues().display() == URUI::Style::Display::Block);
    assert(std::abs(document->GetComputedValues().font_size() - 20.f) < .001f);
    assert(std::abs(body->GetComputedValues().font_size() - 10.f) < .001f);
    assert(std::abs(document->GetElementById("rem")->GetBox().GetSize().x - 20.f) < .001f);
    auto* content = document->GetElementById("content");
    assert(content && content->GetNumChildren() == 5);
    assert(urui_dynamic_cast<URUI::ElementText*>(content->GetChild(1))->GetText() == " ");
    assert(content->GetChild(3)->GetInnerHTML() == "&amp;lt;");
    auto clone = content->Clone();
    assert(clone && clone->GetInnerHTML() == content->GetInnerHTML());
    assert(!clone->GetParentNode());
    auto* select = urui_dynamic_cast<URUI::ElementFormControlSelect*>(document->GetElementById("select"));
    assert(select && select->GetNumOptions() == 2 && select->GetSelection() == 1 && select->GetValue() == "b");
    assert(select->GetNumChildren() == 2);
    assert(select->GetOption(0)->GetParentNode() == select);
    assert(document->QuerySelector("#select > option:nth-child(2)") == select->GetOption(1));
    select->ShowSelectBox();
    context->Update();
    URUI::Element* popup = nullptr;
    for (int i = select->GetNumChildren(); i < select->GetNumChildren(true); ++i)
    {
        if (select->GetChild(i)->GetNumChildren() == 2 &&
            select->GetChild(i)->GetFirstChild()->GetTagName() == "option")
        {
            popup = select->GetChild(i);
        }
    }
    assert(popup && popup->GetNumChildren() == 2 && popup->GetFirstChild() != select->GetOption(0));
    assert(popup->GetChild(1)->GetComputedValues().display() == URUI::Style::Display::None);
    assert(select->GetNumChildren() == 2 &&
           document->QuerySelector("#select > option:first-child") == select->GetOption(0));
    popup->GetFirstChild()->Click();
    assert(select->GetValue() == "a" && select->GetSelection() == 0 && !select->IsSelectBoxVisible());
    select->ShowSelectBox();
    select->SetSelection(1);
    select->CancelSelectBox();
    assert(select->GetValue() == "a" && !select->IsSelectBoxVisible());
    assert(select->GetInnerHTML().find("<option") != URUI::String::npos);
    auto selectClone = select->Clone();
    assert(selectClone && selectClone->GetInnerHTML() == select->GetInnerHTML());
    select->SetInnerHTML("<option value='new' selected>New</option>");
    context->Update();
    assert(select->GetNumOptions() == 1 && select->GetSelection() == 0 && select->GetValue() == "new");
    assert(select->GetNumChildren() == 1 && select->GetFirstChild() == select->GetOption(0));
    auto* textarea = urui_dynamic_cast<URUI::ElementFormControlTextArea*>(document->GetElementById("textarea"));
    assert(textarea && textarea->GetValue() == "<b> &lt;");
    assert(!document->GetElementById("inert"));
    assert(!document->QuerySelector("#inert"));
#if defined(URUI_HAS_HTML_BUILDER)
    auto* item = urui_dynamic_cast<URUI::ElementTemplate*>(document->GetElementById("item"));
    assert(item && item->GetNumChildren() == 0);
    URUI::Vector<URUI::Diagnostic> diagnostics;
    auto instances = item->CloneContent(diagnostics);
    assert(diagnostics.empty() && instances.size() == 2);
    assert(instances.front()->GetId() == "inert" && !instances.front()->GetParentNode());
    assert(instances.back()->GetInnerHTML() == "A &amp; B");
    auto templateClone = item->Clone();
    assert(templateClone && templateClone->GetNumChildren() == 0 &&
           templateClone->GetInnerHTML() == static_cast<URUI::Element*>(item)->GetInnerHTML());
    const auto before = content->GetInnerHTML();
    auto* originalChild = content->GetFirstChild();
    content->SetInnerHTML("<div/>");
    assert(content->GetInnerHTML() == before && content->GetFirstChild() == originalChild);
    FailingInstancer failure;
    URUI::Factory::RegisterElementInstancer("urui-fail", &failure);
    content->SetInnerHTML("<div>candidate</div><urui-fail></urui-fail>");
    assert(content->GetInnerHTML() == before && content->GetFirstChild() == originalChild);
    auto plain = document->CreateTextNode("<img> &amp;lt;");
    assert(plain && plain->GetNumChildren() == 0);
    assert(urui_dynamic_cast<URUI::ElementText*>(plain.get())->GetText() == "<img> &amp;lt;");
    content->AppendChild(std::move(plain));
    assert(!content->QuerySelector("img"));
    content->SetInnerHTML("<div title='&quot;&lt;&amp;'>ok</div>");
    assert(content->GetNumChildren() == 1);
    assert(content->GetInnerHTML().find("</div>") != URUI::String::npos);
    assert(content->GetInnerHTML().find("&quot;&lt;&amp;") != URUI::String::npos);
#else
    std::cerr << "Strict HTML builder is not implemented\n";
    return 1;
#endif
    clone.reset();
    selectClone.reset();
#if defined(URUI_HAS_HTML_BUILDER)
    instances.clear();
    templateClone.reset();
#endif
    const auto owner = LoadTestDocument(
        "<!doctype html><html><head></head><body><div id='kept'>before</div></body></html>", "ui/transaction.html");
    const auto root = urui_document_root(owner);
    const auto kept = urui_element_find(owner, root, "kept", nullptr);
    assert(kept != 0 && kept != UINT64_MAX);
    assert(urui_document_load_html(owner, "<!doctype html><html><head></head><body><div/></body></html>",
                                   "ui/invalid.html") == -9);
    assert(urui_document_root(owner) == root && urui_element_find(owner, root, "kept", nullptr) == kept);
    assert(urui_element_write(owner, kept, 3, nullptr, "<div/>") == -2);
    assert(urui_element_set_inner_html(owner, kept, "<p>broken", "ui/fragment.html") == -9);
    assert(urui_element_child_count(owner, kept) == 1);
    urui_destroy();
    std::cout << "HTML DOM contracts passed\n";
}
