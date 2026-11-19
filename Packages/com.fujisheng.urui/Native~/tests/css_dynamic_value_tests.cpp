#include "urui.h"
#include <URUI/Core.h>
#include "Engine/Source/Core/ElementStyle.h"
#include <cassert>
#include <iostream>

int main()
{
    assert(urui_create(400, 200) == 1);
    auto* context = URUI::GetContext("urui");
    auto* document = context->LoadDocumentFromMemory(
        "<!doctype html><html><head><style>body{width:100px;font-size:14px;}"
        "#width{width:var(--w);--w:50px;} #font{font-size:var(--s);--s:20px;}"
        "</style></head><body><div id='width'></div><div id='font'></div></body></html>",
        "ui/dynamic-values.html");
    assert(document);
    document->Show();
    context->Update();
    auto* width = document->GetElementById("width");
    auto* font = document->GetElementById("font");
    assert(width->GetBox().GetSize().x == 50 && font->GetComputedValues().font_size() == 20);
    width->SetProperty("--w", "garbage");
    context->Update();
    assert(width->GetBox().GetSize().x == 100);
    font->SetProperty("--s", "30px");
    context->Update();
    assert(font->GetComputedValues().font_size() == 30);
    width->SetProperty("--w", "75px");
    context->Update();
    assert(width->GetBox().GetSize().x == 75);
    font->SetProperty("--s", "garbage");
    context->Update();
    assert(font->GetComputedValues().font_size() == 14);
    auto* memoDocument = context->LoadDocumentFromMemory(
        "<!doctype html><html><head><style>"
        "@property --read-color{syntax:'<color>';inherits:false;initial-value:transparent;}"
        "#left{--accent:red;--unused:red;}#right{--accent:blue;--unused:blue;}"
        "#memo{color:var(--accent);--read-color:var(--unused);height:var(--density,10px);}"
        "#memo.important{color:var(--accent)!important;}"
        "</style></head><body><div id='left'><div id='memo'></div></div><div id='right'></div></body></html>",
        "ui/property-read-memo.html");
    assert(memoDocument);
    memoDocument->Show();
    context->Update();
    auto* memo = memoDocument->GetElementById("memo");
    auto* left = memoDocument->GetElementById("left");
    auto* right = memoDocument->GetElementById("right");
    auto* style = memo->GetStyle();
    const auto custom = memo->GetPropertySpecification().GetPropertyId("--read-color");
    auto color = [&](URUI::PropertyId id)
    {
        const auto* value = memo->GetProperty(id);
        assert(value);
        return value->Get<URUI::Colourb>();
    };
    assert(color(URUI::PropertyId::Color) == URUI::Colourb(255, 0, 0));
    const auto before = style->GetVariableResolutionCount();
    for (int n = 0; n < 256; n++)
    {
        assert(color(URUI::PropertyId::Color) == URUI::Colourb(255, 0, 0));
    }
    const auto resolutions = style->GetVariableResolutionCount() - before;
    std::cout << "stable CSS variable resolutions: " << resolutions << " / 256 reads\n";
    assert(color(custom) == URUI::Colourb(255, 0, 0));
    left->SetProperty("--accent", "blue");
    left->SetProperty("--unused", "blue");
    assert(color(URUI::PropertyId::Color) == URUI::Colourb(0, 0, 255));
    assert(color(custom) == URUI::Colourb(0, 0, 255));
    context->Update();
    assert(color(custom) == URUI::Colourb(0, 0, 255));
    left->SetProperty("--accent", "red");
    left->SetProperty("--unused", "red");
    context->Update();
    assert(color(URUI::PropertyId::Color) == URUI::Colourb(255, 0, 0));
    assert(color(custom) == URUI::Colourb(255, 0, 0));
    auto moved = left->RemoveChild(memo);
    right->AppendChild(std::move(moved));
    context->Update();
    assert(color(URUI::PropertyId::Color) == URUI::Colourb(0, 0, 255));
    assert(color(custom) == URUI::Colourb(0, 0, 255));
    memo->SetClass("important", true);
    context->Update();
    assert(color(URUI::PropertyId::Color) == URUI::Colourb(0, 0, 255));
    assert(memo->GetProperty(URUI::PropertyId::Color)->priority.IsImportant());
    memo->SetClass("important", false);
    context->Update();
    assert(!memo->GetProperty(URUI::PropertyId::Color)->priority.IsImportant());
    style->SetAnimatedProperty(URUI::PropertyId::Color, URUI::Property(URUI::Colourb(0, 255, 0), URUI::Unit::COLOUR),
                               false);
    assert(color(URUI::PropertyId::Color) == URUI::Colourb(0, 255, 0));
    context->Update();
    style->RemoveAnimatedProperty(URUI::PropertyId::Color, false);
    context->Update();
    assert(color(URUI::PropertyId::Color) == URUI::Colourb(0, 0, 255));
    style->SetCustomProperty("--density", URUI::Property(4.125f, URUI::Unit::DP));
    context->Update();
    assert(memo->GetProperty(URUI::PropertyId::Height)->Get<float>() == 4.125f);
    context->SetDensityIndependentPixelRatio(2);
    assert(memo->GetProperty(URUI::PropertyId::Height)->Get<float>() == 8.25f);
    context->Update();
    assert(memo->GetProperty(URUI::PropertyId::Height)->Get<float>() == 8.25f);
    auto* detachedDocument =
        context->LoadDocumentFromMemory("<!doctype html><html><head><style>"
                                        "@property --gap{syntax:'<length>';inherits:false;initial-value:0px;}"
                                        "body{font-size:16px;}#detached{--gap:1rem;height:var(--gap);}"
                                        "</style></head><body><div id='detached'></div></body></html>",
                                        "ui/detached-variable.html");
    assert(detachedDocument);
    detachedDocument->Show();
    context->Update();
    auto* detached = detachedDocument->GetElementById("detached");
    auto detachedOwner = detached->GetParentNode()->RemoveChild(detached);
    assert(detached->GetProperty(URUI::PropertyId::Height)->Get<float>() == 16.f);
    detachedDocument->SetProperty("font-size", "32px");
    context->Update();
    assert(detached->GetProperty(URUI::PropertyId::Height)->Get<float>() == 32.f);
    detachedOwner.reset();
    detachedDocument->Close();
    assert(resolutions <= 1);
    memoDocument->Close();
    urui_destroy();
    std::cout << "Dynamic CSS value invalidation contracts passed\n";
}
