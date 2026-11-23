#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <cassert>
#include <cmath>
#include <iostream>

static void Near(float actual, float expected)
{
    assert(std::abs(actual - expected) < .001f);
}
int main()
{
    assert(urui_surface_create(300, 200, &g_test_surface) == 1);
    auto* context = URUI::GetContext(0);
    auto* document = context->LoadDocumentFromMemory(
        "<!doctype html><html><head><style>body{margin:0;}"
        "#viewport{width:100px;height:80px;overflow:auto;scrollbar-width:none;}"
        "#content{width:300px;height:400px;}"
        "</style></head><body><div id='viewport'><div id='content'></div></div></body></html>",
        "ui/scroll.html");
    assert(document);
    document->Show();
    context->Update();
    auto* viewport = document->GetElementById("viewport");
    assert(viewport->SetProperty("scrollbar-width", "none"));
    Near(viewport->GetClientWidth(), 100);
    Near(viewport->GetClientHeight(), 80);
    viewport->SetScrollLeft(125);
    viewport->SetScrollTop(220);
    Near(viewport->GetScrollLeft(), 125);
    Near(viewport->GetScrollTop(), 220);
    for (int i = viewport->GetNumChildren(); i < viewport->GetNumChildren(true); ++i)
    {
        assert(!viewport->GetChild(i)->IsVisible());
    }
    assert(viewport->SetProperty("scrollbar-width", "auto"));
    context->Update();
    Near(viewport->GetClientWidth(), 88);
    Near(viewport->GetClientHeight(), 68);
    int paintedParts = 0;
    for (int i = viewport->GetNumChildren(); i < viewport->GetNumChildren(true); ++i)
    {
        auto* bar = viewport->GetChild(i);
        for (int j = 0; j < bar->GetNumChildren(true); ++j)
        {
            if (bar->GetChild(j)->GetComputedValues().background_color().alpha > 0)
            {
                ++paintedParts;
            }
        }
    }
    // Both axes need a visible track and thumb in the native fallback.
    assert(paintedParts >= 4);
    assert(!viewport->SetProperty("scrollbar-width", "12px"));
    urui_surface_destroy(g_test_surface);
    std::cout << "Scrollbar visibility preserves scrollability and layout\n";
}
