#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include "Engine/Source/Core/HtmlBuilder.h"
#include <cassert>
#include <cmath>
#include <iostream>

static URUI::ElementDocument* Load(const URUI::String& css, const URUI::String& body,
                                   const URUI::String& attributes = "")
{
    auto* context = URUI::GetContext(0);
    auto* doc = context->LoadDocumentFromMemory("<!doctype html><html " + attributes + "><head><style>" + css +
                                                    "</style></head><body>" + body + "</body></html>",
                                                "ui/standard.html");
    assert(doc);
    doc->Show();
    context->Update();
    return doc;
}
static void Opacity(URUI::Element* element, float expected)
{
    const float value = element->GetComputedValues().opacity();
    if (std::abs(value - expected) > .001f)
    {
        std::cerr << "Expected opacity=" << expected << " actual=" << value << '\n';
    }
    assert(std::abs(value - expected) < .001f);
}
int main(int argc, char** argv)
{
    assert(argc == 2 && urui_surface_create(400, 200, &g_test_surface) == 1);
    assert(urui_surface_set_ui_time(g_test_surface, 0) == 1);
    auto* context = URUI::GetContext(0);
    const URUI::String mode = argv[1];
    if (mode == "registrations")
    {
        auto* a = Load(
            "@property --size{syntax:'<length>';inherits:false;initial-value:4px;}"
            "@property --tone{syntax:'<color>';inherits:true;initial-value:#123456;}"
            "@property --mode{syntax:'low | high';inherits:false;initial-value:low;}"
            "#same{width:var(--size);color:var(--tone);--mode:high;}",
            "<div id='same' style='--size:7px'></div><template id='entry'><div style='--size:10px'></div></template>");
        auto* element = a->GetElementById("same");
        assert(element->GetProperty("--size") && element->GetProperty("--size")->unit == URUI::Unit::PX);
        assert(element->GetBox().GetSize().x == 7);
        assert(element->GetComputedValues().color() == URUI::Colourb(0x12, 0x34, 0x56));
        assert(element->GetProperty("--mode")->unit == URUI::Unit::KEYWORD &&
               element->GetProperty("--mode")->Get<int>() == 1);
        auto detached = a->CreateElement("div");
        assert(detached->SetProperty("--size", "9px"));
        auto htmlNode = a->CreateElement("html");
        assert(htmlNode->GetOwnerDocument() == a && !urui_dynamic_cast<URUI::ElementDocument*>(htmlNode.get()));
        auto htmlCopy = a->Clone();
        assert(htmlCopy->GetOwnerDocument() == a && !urui_dynamic_cast<URUI::ElementDocument*>(htmlCopy.get()));
        auto entry = a->GetElementById("entry")->Clone();
        auto* b = Load("@property --size{syntax:'<length>';inherits:true;initial-value:12px;}"
                       "#same{width:var(--size);}",
                       "<div id='same'></div>");
        assert(b->GetElementById("same")->GetBox().GetSize().x == 12);
        assert(detached->GetOwnerDocument() == a && detached->GetProperty("--size")->Get<float>() == 9);
        assert(element->GetBox().GetSize().x == 7);
        element->RemoveProperty("--size");
        context->Update();
        assert(element->GetBox().GetSize().x == 4);
        b->Close();
        context->Update();
        assert(element->GetBox().GetSize().x == 4);
        a->Close();
        context->Update();
        assert(detached->GetOwnerDocument() == nullptr);
        assert(detached->GetPropertySpecification().GetProperty("--size"));
        assert(detached->GetProperty("--size")->ToString() == "9px");
        auto copy = detached->Clone();
        assert(copy && copy->GetPropertySpecification().GetProperty("--size"));
        assert(copy->GetProperty("--size")->ToString() == "9px");
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto instances = urui_dynamic_cast<URUI::ElementTemplate*>(entry.get())->CloneContent(diagnostics);
        assert(diagnostics.empty() && instances.size() == 1);
        assert(instances.front()->GetProperty("--size")->unit == URUI::Unit::PX);
        assert(instances.front()->GetProperty("--size")->Get<float>() == 10);
        instances.clear();
        entry.reset();
        htmlNode.reset();
        htmlCopy.reset();
        copy.reset();
        detached.reset();
    }
    else if (mode == "registered-invalid")
    {
        auto* doc = Load("@property --size{syntax:'<length>';inherits:false;initial-value:4px;}"
                         "@property --mode{syntax:'low | high';inherits:false;initial-value:low;}"
                         "#bad{--size:8px;--size:garbage;width:var(--size,9px);--mode:HIGH;}"
                         "#cycle{--size:var(--size);width:var(--size,9px);}"
                         "#parent{font-size:20px;--size:2em;} #child{font-size:10px;--size:inherit;width:var(--size);}",
                         "<div id='bad'></div><div id='cycle'></div><div id='parent'><div id='child'></div></div>");
        assert(doc->GetElementById("bad")->GetBox().GetSize().x == 4);
        assert(doc->GetElementById("bad")->GetProperty("--mode")->Get<int>() == 0);
        assert(doc->GetElementById("cycle")->GetBox().GetSize().x == 4);
        assert(doc->GetElementById("child")->GetBox().GetSize().x == 40);
    }
    else if (mode == "time")
    {
        auto* doc = Load("@keyframes pulse{from{opacity:0;}to{opacity:1;}} #same{opacity:.3;}"
                         ".go #same{animation:1000ms linear pulse 200ms;}",
                         "<div id='same'></div>");
        auto* element = doc->GetElementById("same");
        doc->SetClass("go", true);
        context->Update();
        assert(urui_surface_set_ui_time(g_test_surface, .7) == 1);
        context->Update();
        Opacity(element, .5f);
        element->SetProperty("opacity", ".9!important");
        context->Update();
        Opacity(element, .9f);
        element->RemoveProperty("opacity");
        context->Update();
        Opacity(element, .5f);
        doc->SetClass("go", false);
        context->Update();
        Opacity(element, .3f);
        assert(!element->SetProperty("animation", "10oops linear pulse"));
        assert(!element->SetProperty("transition", "opacity 1sfoo"));
    }
    else if (mode == "attributes")
    {
        auto* doc = Load("#override{direction:ltr;}",
                         "<div id='override' dir='rtl'></div><div id='child' lang='en' dir='LTR'></div>",
                         "lang='zh-cn' dir='rtl'");
        assert(doc->GetComputedValues().language() == "zh-cn");
        assert(doc->GetComputedValues().direction() == URUI::Style::Direction::Rtl);
        assert(doc->GetElementById("override")->GetComputedValues().direction() == URUI::Style::Direction::Ltr);
        auto* child = doc->GetElementById("child");
        assert(child->GetComputedValues().direction() == URUI::Style::Direction::Ltr);
        child->RemoveAttribute("lang");
        child->RemoveAttribute("dir");
        context->Update();
        assert(child->GetComputedValues().language() == "zh-cn");
        assert(child->GetComputedValues().direction() == URUI::Style::Direction::Rtl);
        assert(URUI::StyleSheetSpecification::GetProperty("-urui-direction") == nullptr);
    }
    else if (mode == "transition")
    {
        auto* doc = Load("@property --progress{syntax:'<number>';inherits:false;initial-value:0;}"
                         "#same{--progress:0;opacity:var(--progress);transition:--progress 1000ms linear;}"
                         ".go #same{--progress:1;}",
                         "<div id='same'></div>");
        auto* element = doc->GetElementById("same");
        doc->SetClass("go", true);
        context->Update();
        assert(urui_surface_set_ui_time(g_test_surface, .5) == 1);
        context->Update();
        Opacity(element, .5f);
        assert(urui_surface_set_ui_time(g_test_surface, 1.1) == 1);
        context->Update();
        Opacity(element, 1);
    }
    else
    {
        assert(false);
    }
    urui_surface_destroy(g_test_surface);
    std::cout << "Standard CSS " << mode << " contracts passed\n";
}
