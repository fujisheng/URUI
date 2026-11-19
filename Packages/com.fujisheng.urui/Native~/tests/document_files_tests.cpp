#include "urui.h"
#include <cassert>
#include <cstring>
#include <iostream>

static int Register(URUIDocumentHandle owner, const char* source, const char* text)
{
    return urui_document_register_resource(owner, source, reinterpret_cast<const uint8_t*>(text),
                                           int(std::strlen(text)));
}
static const char* Html =
    "<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='box'></div></body></html>";
int main()
{
    char standalone_url[128];
    assert(urui_resolve_resource_url("/ui/page.html", "icons/my icon.png", standalone_url, sizeof(standalone_url)) > 0);
    assert(!std::strcmp(standalone_url, "/ui/icons/my%20icon.png"));
    assert(urui_document_resource_api_version() == 3 && urui_create(400, 200) == 1);
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
        const auto required = urui_resolve_resource_url(test[0], test[1], nullptr, 0);
        assert(required > 0 && urui_resolve_resource_url(test[0], test[1], output, sizeof(output)) == required);
        assert(!std::strcmp(output, test[2]));
    }
    URUIDocumentHandle bom = 0;
    assert(urui_document_create(&bom) == 1);
    assert(Register(bom, "ui/bom.css",
                    "\xef\xbb\xbf"
                    "#box{width:63px;height:10px;}") == 1);
    assert(
        urui_document_load_html(
            bom,
            "<!doctype html><html><head><link rel='stylesheet' href='bom.css'></head><body><div id='box'></div></body></html>",
            "ui/bom.html") == 1);
    float bomBox[6];
    assert(urui_get_element_box(bom, "box", bomBox) == 1 && bomBox[2] == 63);
    assert(urui_document_close(bom) == 1);
    urui_update();
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    URUIDocumentHandle candidate = 0;
    assert(urui_document_create(&a) == 1 && urui_document_create(&b) == 1);
    assert(Register(a, "ui/theme.css", "#box{width:37px;height:10px;background-color:red;}") == 1);
    assert(urui_document_load_html(a, Html, "ui/a.html") == 1);
    assert(urui_document_set_presentation(a, 0, 0, 1, 0) == 1);
    urui_update();
    urui_render();
    const auto root = urui_document_root(a);
    URUIFrameStamp before{};
    assert(urui_render_plan_stamp(&before) == 1);
    assert(Register(b, "ui/theme.css", "#box{width:91px;}") < 0);
    assert(std::strstr(urui_last_error(), "ui/theme.css"));
    assert(Register(a, "ui/invalid.css", "@property --bad{syntax:\"<angle>\";inherits:false;initial-value:0deg;}") ==
           1);
    assert(urui_document_load_html(
               a, "<!doctype html><html><head><link rel='stylesheet' href='invalid.css'></head><body></body></html>",
               "ui/failed.html") == -8);
    assert(urui_document_root(a) == root);
    assert(Register(a, "ui/theme.css",
                    "@font-face{font-family:broken;src:url(bad.ttf);}#box{width:91px;font-family:broken;}") == 1);
    assert(Register(a, "ui/bad.ttf", "not a font") == 1);
    assert(urui_document_load_html(a, Html, "ui/failed-font.html") == -8);
    assert(std::strstr(urui_last_error(), "ui/bad.ttf"));
    assert(urui_document_root(a) == root);
    urui_update();
    float box[6];
    assert(urui_get_element_box(a, "box", box) == 1 && box[2] == 37);
    assert(urui_document_add_stylesheet(a, "@font-face{font-family:missing;src:url(missing.ttf);}#box{width:99px;}",
                                        "ui/live.css", 0) < 0);
    urui_update();
    assert(urui_get_element_box(a, "box", box) == 1 && box[2] == 37);
    assert(urui_document_create(&candidate) == 1 && urui_document_set_replacement(candidate, a) == 1);
    assert(Register(candidate, "ui/theme.css", "#box{width:51px;}") == 1);
    assert(urui_document_load_html(candidate, Html, "ui/replacement.html") == 1);
    assert(urui_get_element_box(a, "box", box) == 1 && box[2] == 37);
    assert(urui_get_element_box(candidate, "box", box) == 1 && box[2] == 51);
    assert(urui_document_close(a) == 1);
    urui_update();
    urui_render();
    assert(urui_document_root(candidate) != 0);
    URUIFrameStamp after{};
    assert(urui_render_plan_stamp(&after) == 1 && before.epoch == after.epoch);
    urui_destroy();
    std::cout << "Document-owned head files, CSS/font rollback, replacement groups and stable Surface epoch passed\n";
}
