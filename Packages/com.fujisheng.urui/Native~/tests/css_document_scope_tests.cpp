#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <cassert>
#include <cmath>
#include <iostream>
#include <filesystem>

static URUI::ElementDocument* Load(URUI::Context& context, const char* source, const char* initial, int width,
                                   const char* endpoint)
{
    const URUI::String css = URUI::String("@property --scoped{syntax:'<number>';inherits:false;initial-value:") +
                             initial + ";} @keyframes pulse{from{opacity:0;}to{opacity:" + endpoint +
                             ";}} #same{width:" + std::to_string(width) +
                             "px;height:10px;opacity:var(--scoped);} .go #same{animation:1s linear pulse;}";
    auto* document = context.LoadDocumentFromMemory("<!doctype html><html><head><style>" + css +
                                                        "</style></head><body><div id='same'></div></body></html>",
                                                    source);
    assert(document);
    document->Show();
    context.Update();
    return document;
}
static void Opacity(URUI::Element* element, float expected)
{
    const float actual = element->GetComputedValues().opacity();
    if (std::abs(actual - expected) >= .001f)
    {
        std::cerr << "Expected scoped opacity=" << expected << " actual=" << actual << '\n';
    }
    assert(std::abs(actual - expected) < .001f);
}
int main()
{
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    assert(urui_surface_set_ui_time(g_test_surface, 0) == 1);
    auto* context = URUI::GetContext(0);
    auto* a = Load(*context, "ui/a.html", ".2", 50, ".4");
    auto* elementA = a->GetElementById("same");
    Opacity(elementA, .2f);
    auto* b = Load(*context, "ui/b.html", ".8", 80, ".9");
    auto* elementB = b->GetElementById("same");
    assert(elementA != elementB);
    assert(elementA->GetBox().GetSize().x == 50 && elementB->GetBox().GetSize().x == 80);
    Opacity(elementA, .2f);
    Opacity(elementB, .8f);
    a->SetClass("go", true);
    b->SetClass("go", true);
    context->Update();
    assert(urui_surface_set_ui_time(g_test_surface, .5) == 1);
    urui_surface_update(g_test_surface);
    Opacity(elementA, .2f);
    Opacity(elementB, .45f);
    b->Close();
    context->Update();
    assert(urui_surface_set_ui_time(g_test_surface, 1.1) == 1);
    urui_surface_update(g_test_surface);
    Opacity(elementA, .2f);
    context->SetDimensions({640, 360});
    context->Update();
    Opacity(elementA, .2f);
    a->Close();
    context->Update();
    assert(URUI::StyleSheetSpecification::GetProperty("--scoped") == nullptr);
    const auto assets = (std::filesystem::path(__FILE__).parent_path() / "fixtures/fonts").string();
    auto* fonts = URUI::GetFontEngineInterface();
    const auto style = URUI::Style::FontStyle::Normal;
    const auto weight = URUI::Style::FontWeight::Normal;
    assert(URUI::LoadFontFace(assets + "/LatoLatin-Regular.ttf", "reference-a", style, weight));
    assert(URUI::LoadFontFace(assets + "/RobotoMono-Regular.ttf", "reference-b", style, weight));
    const auto referenceA = fonts->GetFontFaceHandle("reference-a", style, weight, 20);
    const auto referenceB = fonts->GetFontFaceHandle("reference-b", style, weight, 20);
    const URUI::String language = "en";
    const URUI::TextShapingContext shaping{language};
    const auto widthA = fonts->GetStringWidth(referenceA, "iiiiWWWW", shaping);
    const auto widthB = fonts->GetStringWidth(referenceB, "iiiiWWWW", shaping);
    assert(referenceA && referenceB && widthA != widthB);
    auto loadFont = [&](const char* file, const char* name)
    {
        auto* result = context->LoadDocumentFromMemory(
            URUI::String("<!doctype html><html><head><style>") + "@font-face{font-family:'Shared Face';src:url('" +
                file +
                "');}"
                "body{font:20px 'Shared Face';}</style></head><body><div id='text'>iiiiWWWW</div></body></html>",
            assets + "/" + name);
        assert(result);
        result->Show();
        context->Update();
        return result;
    };
    auto* fontA = loadFont("LatoLatin-Regular.ttf", "font-a.html");
    auto* fontB = loadFont("RobotoMono-Regular.ttf", "font-b.html");
    auto measure = [&](URUI::ElementDocument* doc)
    {
        const auto handle = doc->GetElementById("text")->GetComputedValues().font_face_handle();
        assert(handle);
        return fonts->GetStringWidth(handle, "iiiiWWWW", shaping);
    };
    assert(measure(fontA) == widthA);
    assert(measure(fontB) == widthB);
    fontB->Close();
    context->Update();
    assert(measure(fontA) == widthA);
    urui_surface_destroy(g_test_surface);
    std::cout << "Per-document CSS scope contracts passed\n";
}
