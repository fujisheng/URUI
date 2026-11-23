#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "../../Tests~/HtmlCss/Native/TextLayoutProbe.h"
#include <URUI/Core.h>
#include <cassert>
#include <cmath>

int main(int argc, char** argv)
{
    assert(argc == 2 && urui_surface_create(400, 200, &g_test_surface) == 1);
    assert(URUI::LoadFontFace(argv[1], "Probe", URUI::Style::FontStyle::Normal, URUI::Style::FontWeight::Normal));
    const auto owner = LoadTestDocument(g_test_surface, 
        "<!doctype html><html><head><style>body{margin:12px;font:20px Probe;}"
        "p{margin:0;line-height:24px;}button{position:absolute;left:40px;top:80px;width:200px;height:44px;"
        "box-sizing:border-box;border:2px solid black;padding:0;display:flex;align-items:center;justify-content:center;font:20px Probe;line-height:24px;}"
        "</style></head><body><p id='text'>first<br>second</p><button id='button'>Action</button></body></html>",
        "ui/probe.html");
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(urui_validation_text_lines("ui/probe.html", "text", nullptr, 0) == 2);
    URUIValidationTextLine lines[2]{};
    lines[0].x = -99;
    assert(urui_validation_text_lines("ui/probe.html", "text", lines, 1) == 2 && lines[0].x == -99);
    assert(urui_validation_text_lines("ui/probe.html", "text", lines, 2) == 2);
    assert(lines[0].x == 12 && lines[1].x == 12 && lines[0].width > 0 && lines[1].width > 0);
    assert(std::abs(lines[1].baseline - lines[0].baseline - 24.f) < .01f);
    assert(lines[0].y < lines[0].baseline && lines[0].y + lines[0].height > lines[0].baseline);
    assert(urui_validation_text_lines("ui/probe.html", "button", lines, 2) == 1);
    assert(std::abs(lines[0].x + lines[0].width * .5f - 140.f) < .5f);
    assert(std::abs(lines[0].y + lines[0].height * .5f - 102.f) < .5f);
    auto* context = URUI::GetContext(0);
    assert(context->GetElementAtPoint({140, 102}) == context->GetDocument(0)->GetElementById("button"));
    assert(urui_validation_text_lines("ui/missing.html", "text", nullptr, 0) == -2);
    assert(urui_validation_text_lines("ui/probe.html", "missing", nullptr, 0) == -2);
    assert(urui_validation_text_lines("ui/probe.html", "text", nullptr, 1) == -1);
    const auto other = LoadTestDocument(g_test_surface, "<!doctype html><html><head></head><body><p id='text'>other</p></body></html>",
                                        "ui/probe.html");
    assert(urui_validation_text_lines("ui/probe.html", "text", nullptr, 0) == -2);
    assert(urui_document_close(g_test_surface, other) == 1 && urui_document_close(g_test_surface, owner) == 1);
    assert(urui_validation_text_lines("ui/probe.html", "text", nullptr, 0) == -2);
    urui_surface_destroy(g_test_surface);
}
