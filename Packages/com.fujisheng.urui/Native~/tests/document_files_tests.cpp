#include "urui.h"
#include "../render_plan_protocol.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <cassert>
#include <cstring>
#include <iostream>

static uint64_t CurrentPlanEpoch()
{
    URUIPlanInputPacket input{};
    assert(urui_plan_begin(g_test_surface, &input) == URUI_OK);
    const uint64_t epoch = input.epoch;
    assert(urui_plan_release(g_test_surface, input.lease) == URUI_OK);
    return epoch;
}

static int Register(URUIDocumentHandle owner, const char* source, const char* text)
{
    return urui_document_register_resource(g_test_surface, owner, source, reinterpret_cast<const uint8_t*>(text),
                                           int(std::strlen(text)));
}
static const char* Html =
    "<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='box'></div></body></html>";
int main()
{
    char standalone_url[128];
    int standalone_req = 0;
    assert(urui_resolve_resource_url("/ui/page.html", "icons/my icon.png", standalone_url, sizeof(standalone_url),
                                     &standalone_req) == URUI_OK &&
           standalone_req > 0);
    assert(!std::strcmp(standalone_url, "/ui/icons/my%20icon.png"));
    assert(urui_document_resource_api_version() == 4 && urui_surface_create(400, 200, &g_test_surface) == 1);
    const char* urls[][3] = {
        {"ui/a.html", "/icons/shared.png", "/icons/shared.png"},
        {"ui/a.html", "icons/my icon.png", "ui/icons/my%20icon.png"},
        {"ui/a.html", "icons/my%20icon.png", "ui/icons/my%20icon.png"},
        {"ui/a.html", "icons/图标.png", "ui/icons/%E5%9B%BE%E6%A0%87.png"},
        {"ui/a.html", "../icons/../shared.png", "shared.png"},
        {"https://example.test/ui/a.html", "/icons/shared.png", "https://example.test/icons/shared.png"},
        {"https://example.test/ui/a.html", "//other.test/icon.png", "https://other.test/icon.png"},
        {"https://example.test/ui/a.html?old#fragment", "?new", "https://example.test/ui/a.html?new"},
        {"https://example.test/ui/a.html?old#fragment", "#new", "https://example.test/ui/a.html?old#new"},
        {"ui/a.html", "%2E%2e/icon.png", "icon.png"},
        {"file:///tmp/ui/a.html", "../my font.ttf", "file:///tmp/my%20font.ttf"}};
    for (const auto& test : urls)
    {
        char output[512];
        int required = 0;
        assert(urui_resolve_resource_url(test[0], test[1], nullptr, 0, &required) == URUI_ERR_BUFFER_TOO_SMALL &&
               required > 0);
        int written = 0;
        assert(urui_resolve_resource_url(test[0], test[1], output, sizeof(output), &written) == URUI_OK &&
               written == required);
        assert(!std::strcmp(output, test[2]));
    }
    URUIDocumentHandle bom = 0;
    assert(urui_document_create(g_test_surface, &bom) == 1);
    assert(Register(bom, "ui/bom.css",
                    "\xef\xbb\xbf"
                    "#box{width:63px;height:10px;}") == 1);
    assert(
        urui_document_load_html(g_test_surface, 
            bom,
            "<!doctype html><html><head><link rel='stylesheet' href='bom.css'></head><body><div id='box'></div></body></html>",
            "ui/bom.html") == 1);
    float bomBox[6];
    assert(TestGetElementBox(g_test_surface, bom, "box", bomBox) == 1 && bomBox[2] == 63);
    assert(urui_document_close(g_test_surface, bom) == 1);
    urui_surface_update(g_test_surface);
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    URUIDocumentHandle candidate = 0;
    assert(urui_document_create(g_test_surface, &a) == 1 && urui_document_create(g_test_surface, &b) == 1);
    assert(Register(a, "ui/theme.css", "#box{width:37px;height:10px;background-color:red;}") == 1);
    assert(urui_document_load_html(g_test_surface, a, Html, "ui/a.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, a, 0, 0, 1, 0) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    const auto root = urui_document_root(g_test_surface, a);
    const uint64_t before_epoch = CurrentPlanEpoch();
    assert(Register(b, "ui/theme.css", "#box{width:91px;}") < 0);
    assert(std::strstr(TestLastError(g_test_surface).c_str(), "ui/theme.css"));
    assert(Register(a, "ui/invalid.css", "@property --bad{syntax:\"<angle>\";inherits:false;initial-value:0deg;}") ==
           1);
    assert(urui_document_load_html(g_test_surface, 
               a, "<!doctype html><html><head><link rel='stylesheet' href='invalid.css'></head><body></body></html>",
               "ui/failed.html") == -8);
    assert(urui_document_root(g_test_surface, a) == root);
    assert(Register(a, "ui/theme.css",
                    "@font-face{font-family:broken;src:url(bad.ttf);}#box{width:91px;font-family:broken;}") == 1);
    assert(Register(a, "ui/bad.ttf", "not a font") == 1);
    assert(urui_document_load_html(g_test_surface, a, Html, "ui/failed-font.html") == -8);
    assert(std::strstr(TestLastError(g_test_surface).c_str(), "ui/bad.ttf"));
    assert(urui_document_root(g_test_surface, a) == root);
    urui_surface_update(g_test_surface);
    float box[6];
    assert(TestGetElementBox(g_test_surface, a, "box", box) == 1 && box[2] == 37);
    assert(urui_document_add_stylesheet(g_test_surface, a, "@font-face{font-family:missing;src:url(missing.ttf);}#box{width:99px;}",
                                        "ui/live.css", 0) < 0);
    urui_surface_update(g_test_surface);
    assert(TestGetElementBox(g_test_surface, a, "box", box) == 1 && box[2] == 37);
    assert(urui_document_create(g_test_surface, &candidate) == 1 && urui_document_set_replacement(g_test_surface, candidate, a) == 1);
    assert(Register(candidate, "ui/theme.css", "#box{width:51px;}") == 1);
    assert(urui_document_load_html(g_test_surface, candidate, Html, "ui/replacement.html") == 1);
    assert(TestGetElementBox(g_test_surface, a, "box", box) == 1 && box[2] == 37);
    assert(TestGetElementBox(g_test_surface, candidate, "box", box) == 1 && box[2] == 51);
    assert(urui_document_close(g_test_surface, a) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(urui_document_root(g_test_surface, candidate) != 0);
    const uint64_t after_epoch = CurrentPlanEpoch();
    assert(before_epoch != 0 && after_epoch != 0);
    urui_surface_destroy(g_test_surface);
    std::cout << "Document-owned head files, CSS/font rollback, replacement groups and stable Surface epoch passed\n";
}
