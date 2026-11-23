#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "ComputeProperty.h"
#include "ElementStyle.h"
#include <URUI/Core.h>
#include <URUI/Core/StyleSheetContainer.h>
#include <cassert>
#include <cmath>
#include <iostream>
#if __has_include("CssParser.h")
#include "CssParser.h"
#define URUI_HAS_CSS_PARSER 1
#endif

static URUI::ElementDocument* Load(const URUI::String& css, const URUI::String& body)
{
    auto* document = URUI::GetContext(0)->LoadDocumentFromMemory(
        "<!doctype html><html><head><style>" + css + "</style></head><body>" + body + "</body></html>",
        "ui/cascade.html");
    assert(document);
    document->Show();
    URUI::GetContext(0)->Update();
    return document;
}
static void Width(URUI::Element* element, float expected)
{
    if (!element || std::abs(element->GetBox().GetSize().x - expected) >= .001f)
    {
        std::cerr << "Expected width=" << expected << " actual=" << (element ? element->GetBox().GetSize().x : -1)
                  << '\n';
    }
    assert(element && std::abs(element->GetBox().GetSize().x - expected) < .001f);
}
int main()
{
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    assert(urui_surface_set_ui_time(g_test_surface, 0) == 1);
    auto* context = URUI::GetContext(0);
    URUI::String classes;
    for (unsigned i = 0; i < 20; ++i)
    {
        classes += ".x";
    }
    auto* specificity = Load("#target{width:10px;}" + classes + "{width:20px;}", "<div id='target' class='x'></div>");
    Width(specificity->GetElementById("target"), 10);
    specificity->Close();
    auto* priority = Load("#target{width:30px !important;} #target{width:40px;} .x{width:90px !important;}",
                          "<div id='target' class='x' style='width:50px'></div>");
    auto* target = priority->GetElementById("target");
    Width(target, 30);
    target->SetProperty("width", "60px !important");
    context->Update();
    Width(target, 60);
    target->RemoveProperty("width");
    context->Update();
    Width(target, 30);
    priority->Close();
    auto* roundtrip = Load(
        "#target{width:30px!important;--gap:20px!important;margin-top:30px!important;}",
        "<div id='parent'><div id='target' style='width:50px!important;--gap:5px!important;margin:var(--gap)!important'></div></div>");
    auto* parent = roundtrip->GetElementById("parent");
    parent->SetInnerHTML(parent->GetInnerHTML());
    context->Update();
    Width(roundtrip->GetElementById("target"), 50);
    assert(roundtrip->GetElementById("target")->GetComputedValues().margin_top().value == 5);
    roundtrip->Close();
    {
        auto* origin = Load(".x{width:25px;}", "<div id='target' class='x'></div>");
        auto uaSheet = URUI::Factory::InstanceStyleSheetString("#target{width:80px;}");
        assert(uaSheet);
        uaSheet->SetOrigin(URUI::CssOrigin::UserAgent);
        origin->SetStyleSheetContainer(origin->GetStyleSheetContainer()->CombineStyleSheetContainer(*uaSheet));
        context->Update();
        Width(origin->GetElementById("target"), 25);
        auto importantUa = URUI::Factory::InstanceStyleSheetString("#target{width:81px!important;}");
        assert(importantUa);
        importantUa->SetOrigin(URUI::CssOrigin::UserAgent);
        origin->GetElementById("target")->SetProperty("width", "90px!important");
        origin->SetStyleSheetContainer(origin->GetStyleSheetContainer()->CombineStyleSheetContainer(*importantUa));
        context->Update();
        Width(origin->GetElementById("target"), 81);
        origin->Close();
    }
    auto* invalid =
        Load("#target{width:20px;} #target,:not-a-supported-pseudo{width:99px;}", "<div id='target'></div>");
    Width(invalid->GetElementById("target"), 20);
    invalid->Close();
    for (const auto& selector : {":not(:unsupported)", "[attr!value]", ":nth-child(foo)", "div >", "[data=123]"})
    {
        auto* rejected =
            Load(URUI::String("#target{width:20px;} #target,") + selector + "{width:99px;}", "<div id='target'></div>");
        Width(rejected->GetElementById("target"), 20);
        rejected->Close();
    }
    auto* selectors = Load("[data-note='a,b']{width:24px;} #\\31 23{width:26px;}",
                           "<div id='attribute' data-note='a,b'></div><div id='123'></div>");
    Width(selectors->GetElementById("attribute"), 24);
    Width(selectors->GetElementById("123"), 26);
    selectors->Close();
    auto* ids = Load("#target{width:20px;} #absent#target{width:99px;}", "<div id='target'></div>");
    Width(ids->GetElementById("target"), 20);
    ids->Close();
    auto* repeatedId = Load("#target#target{width:30px;} #target.x{width:40px;}", "<div id='target' class='x'></div>");
    Width(repeatedId->GetElementById("target"), 30);
    repeatedId->Close();
    auto* inherit =
        Load("body{font-size:22px;width:100px;} #target{font-size:initial;width:inherit;} #child{font-size:unset;}",
             "<div id='target'><span id='child'></span></div>");
    assert(inherit->GetElementById("target")->GetComputedValues().font_size() ==
           URUI::DefaultComputedValues().font_size());
    Width(inherit->GetElementById("target"), 100);
    assert(inherit->GetElementById("child")->GetComputedValues().font_size() ==
           URUI::DefaultComputedValues().font_size());
    inherit->QuerySelector("body")->SetProperty("width", "120px");
    context->Update();
    Width(inherit->GetElementById("target"), 120);
    inherit->Close();
    auto* variables =
        Load("body{width:100px;--Text:20px;--text:30px;--x:10px;} #case{width:var(--Text);}"
             "#cycle{--a:var(--b,13px);--b:var(--a);width:var(--a,44px);} #invalid{width:18px;width:var(--missing);}"
             "#initial{--x:initial;width:var(--x,40px);} #unused{--v:5px;--a:var(--v,var(--a));width:var(--a,46px);}"
             "#outside{--a:var(--a);--c:var(--a,47px);width:var(--c);}",
             "<div id='case'></div><div id='cycle'></div><div id='invalid'></div><div id='initial'></div>"
             "<div id='unused'></div><div id='outside'></div>");
    Width(variables->GetElementById("case"), 20);
    Width(variables->GetElementById("cycle"), 44);
    Width(variables->GetElementById("invalid"), 100);
    Width(variables->GetElementById("initial"), 40);
    Width(variables->GetElementById("unused"), 46);
    Width(variables->GetElementById("outside"), 47);
    variables->Close();
    auto* density = Load("#target{height:var(--density);margin:var(--density);}", "<div id='target'></div>");
    auto* density_target = density->GetElementById("target");
    density_target->GetStyle()->SetCustomProperty("--density", URUI::Property(4.125f, URUI::Unit::DP));
    context->Update();
    assert(std::abs(density_target->GetBox().GetSize().y - 4.125f) < .001f);
    context->SetDensityIndependentPixelRatio(2);
    context->Update();
    assert(std::abs(density_target->GetBox().GetSize().y - 8.25f) < .001f);
    assert(std::abs(density_target->GetComputedValues().margin_left().value - 8.25f) < .001f);
    context->SetDensityIndependentPixelRatio(1);
    context->Update();
    assert(std::abs(density_target->GetBox().GetSize().y - 4.125f) < .001f);
    density->Close();
    auto* numbers =
        Load("#invalid-number{width:20px;width:12oops3px;} #spaced{width:21px;width:12 px;}"
             "#hex{width:22px;width:0x1p2px;} #joined{--n:12;width:var(--n)px;}"
             "#private-dp{width:23px;width:4dp;} #private-x{width:24px;width:2x;} body{width:100px;}",
             "<div id='invalid-number'></div><div id='spaced'></div><div id='hex'></div><div id='joined'></div>"
             "<div id='private-dp'></div><div id='private-x'></div>");
    Width(numbers->GetElementById("invalid-number"), 20);
    Width(numbers->GetElementById("spaced"), 21);
    Width(numbers->GetElementById("hex"), 22);
    Width(numbers->GetElementById("joined"), 100);
    Width(numbers->GetElementById("private-dp"), 23);
    Width(numbers->GetElementById("private-x"), 24);
    numbers->Close();
    auto* shorthand = Load("#target{border-top-width:8px;border-color:red;border:solid;}", "<div id='target'></div>");
    assert(shorthand->GetElementById("target")->GetComputedValues().border_top_width() != 8);
    shorthand->Close();
    auto* font = Load("#target{font-style:italic;font-weight:bold;line-height:2;font:16px 'Test Family';}",
                      "<div id='target'></div>");
    auto* textStyle = font->GetElementById("target");
    assert(textStyle->GetComputedValues().font_style() == URUI::Style::FontStyle::Normal);
    assert(textStyle->GetComputedValues().font_weight() == URUI::Style::FontWeight::Normal);
    assert(std::abs(textStyle->GetComputedValues().line_height().value - 19.2f) < .001f);
    assert(textStyle->SetProperty("font", "italic 700 20px/1.5 'Test Family'"));
    context->Update();
    assert(textStyle->GetComputedValues().font_size() == 20);
    assert(std::abs(textStyle->GetComputedValues().line_height().value - 30.f) < .001f);
    assert(!textStyle->SetProperty("font", "bold"));
    assert(textStyle->SetProperty("border", "red solid 2px"));
    context->Update();
    assert(textStyle->GetComputedValues().border_top_width() == 2);
    font->Close();
    auto* varShorthands = Load(
        "body{width:100px;--a:1px;--b:2px;--edge:solid 2px red;--top:solid 4px blue;}"
        "#important{margin:var(--a)!important;} #same{border:var(--edge);border-top:var(--top);}"
        "#failed{border:var(--edge);border-top:var(--missing);} #wide{margin:10px;width:var(--missing,inherit);font-size:var(--missing,initial);}",
        "<div id='important' style='margin:var(--b)'></div><div id='same'></div><div id='failed'></div><div id='wide'></div>");
    assert(varShorthands->GetElementById("important")->GetComputedValues().margin_top().value == 1);
    assert(varShorthands->GetElementById("same")->GetComputedValues().border_top_width() == 4);
    assert(varShorthands->GetElementById("same")->GetComputedValues().border_bottom_width() == 2);
    assert(varShorthands->GetElementById("failed")->GetComputedValues().border_top_width() == 0);
    assert(varShorthands->GetElementById("failed")->GetComputedValues().border_bottom_width() == 2);
    Width(varShorthands->GetElementById("wide"), 100);
    assert(varShorthands->GetElementById("wide")->GetComputedValues().font_size() ==
           URUI::DefaultComputedValues().font_size());
    auto* cloneSource = varShorthands->GetElementById("same");
    assert(cloneSource->SetProperty("border", "var(--edge)"));
    assert(cloneSource->SetProperty("border-top-width", "6px"));
    auto copy = cloneSource->Clone();
    assert(copy);
    copy->SetId("copy");
    auto* copied = varShorthands->QuerySelector("body")->AppendChild(std::move(copy));
    context->Update();
    assert(copied->GetComputedValues().border_top_width() == 6);
    assert(copied->GetComputedValues().border_bottom_width() == 2);
    varShorthands->Close();
#if defined(URUI_HAS_CSS_PARSER)
    using namespace URUI;
    const CascadePriority ua{CssOriginImportance::UserAgentNormal}, author{CssOriginImportance::AuthorNormal},
        animation{CssOriginImportance::Animation}, important{CssOriginImportance::AuthorImportant},
        uaImportant{CssOriginImportance::UserAgentImportant}, transition{CssOriginImportance::Transition};
    assert(ua < author && author < animation && animation < important && important < uaImportant &&
           uaImportant < transition);
    auto parsed = ParseCss(
        ".x { --Text:'a;b}c'; WIDTH:12px; color:rgb(1, 2, 3); --Nested:var(--a, fn(a;b)); opacity:.3 !/**/IMPORTANT; }",
        "ui/tokens.css");
    assert(parsed && parsed.rules.size() == 1);
    const auto& declarations = parsed.rules.front().declarations;
    assert(declarations.size() == 5 && declarations[0].propertyName == "--Text" &&
           declarations[0].tokenValue == "'a;b}c'");
    assert(declarations[1].propertyName == "width" && declarations[4].important);
    assert(declarations[4].tokenValue == ".3");
    assert(declarations[0].span.line == 1 && declarations[0].span.column > 1);
    {
        auto* animated = Load(
            "@keyframes fade{from{opacity:0;}to{opacity:1;}}"
            "div{animation:1s linear fade;opacity:.7;} #important{opacity:.7!important;}",
            "<div id='normal'></div><div id='important'></div><div id='inline' style='opacity:.8!important'></div>");
        auto* normal = animated->GetElementById("normal");
        assert(urui_surface_set_ui_time(g_test_surface, .1) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(animated->GetElementById("important")->GetComputedValues().opacity() - .7f) < .001f);
        assert(std::abs(animated->GetElementById("inline")->GetComputedValues().opacity() - .8f) < .001f);
        assert(normal->GetComputedValues().opacity() > 0 && normal->GetComputedValues().opacity() < .7f);
        normal->SetProperty("opacity", ".9");
        context->Update();
        assert(normal->GetComputedValues().opacity() < .7f);
        assert(urui_surface_set_ui_time(g_test_surface, 1.1) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(normal->GetComputedValues().opacity() - .9f) < .001f);
        animated->Close();
        auto* transitioned =
            Load("#target{transition:opacity 1s linear;} .a{opacity:.2!important;} .b{opacity:.8!important;}",
                 "<div id='target' class='a'></div>");
        auto* box = transitioned->GetElementById("target");
        box->SetClassNames("b");
        context->Update();
        assert(urui_surface_set_ui_time(g_test_surface, 1.6) == 1);
        urui_surface_update(g_test_surface);
        assert(box->GetComputedValues().opacity() > .2f && box->GetComputedValues().opacity() < .8f);
        assert(urui_surface_set_ui_time(g_test_surface, 2.2) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(box->GetComputedValues().opacity() - .8f) < .001f);
        transitioned->Close();
        auto* repeated = Load(
            "@keyframes pulse{from{opacity:0;}to{opacity:1;}} #target{animation:.4s linear infinite alternate pulse;}",
            "<div id='target'></div>");
        assert(urui_surface_set_ui_time(g_test_surface, 3.45) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(repeated->GetElementById("target")->GetComputedValues().opacity() - .875f) < .002f);
        assert(urui_surface_set_ui_time(g_test_surface, 3.85) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(repeated->GetElementById("target")->GetComputedValues().opacity() - .125f) < .002f);
        repeated->Close();
        auto* inlineTransition = Load("#target{opacity:.2;transition:opacity 1s linear;}", "<div id='target'></div>");
        auto* inlineBox = inlineTransition->GetElementById("target");
        inlineBox->SetProperty("opacity", ".8!important");
        assert(urui_surface_set_ui_time(g_test_surface, 4.35) == 1);
        urui_surface_update(g_test_surface);
        assert(inlineBox->GetComputedValues().opacity() > .2f && inlineBox->GetComputedValues().opacity() < .8f);
        assert(urui_surface_set_ui_time(g_test_surface, 4.95) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(inlineBox->GetComputedValues().opacity() - .8f) < .001f);
        inlineTransition->Close();
        auto* instant = Load("#target{opacity:.2;transition:opacity 0s linear 1s;}", "<div id='target'></div>");
        auto* instantBox = instant->GetElementById("target");
        instantBox->SetProperty("opacity", ".8");
        context->Update();
        assert(urui_surface_set_ui_time(g_test_surface, 5.45) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(instantBox->GetComputedValues().opacity() - .2f) < .001f);
        assert(urui_surface_set_ui_time(g_test_surface, 5.95) == 1);
        urui_surface_update(g_test_surface);
        assert(std::abs(instantBox->GetComputedValues().opacity() - .8f) < .001f);
        instant->Close();
    }
#else
    std::cerr << "CSS tokenizer/declarations are not implemented\n";
    return 1;
#endif
    urui_surface_destroy(g_test_surface);
    std::cout << "CSS cascade contracts passed\n";
}
