#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <cassert>
#include <cstring>
#include <iostream>
int main()
{
    assert(urui_surface_create(200, 100, &g_test_surface) == 1);
    char output[8192];
    int required = 0;
    const char* valid =
        "<!doctype html><html><head><style>.x::before{content:'<fake id=\"wrong\">';}</style></head><body><button id='real' disabled>Text &amp; body</button><template><span id='inert'>Inert</span></template></body></html>";
    assert(urui_validate_html(valid, "ui/authoring.html", 0, output, sizeof(output), &required) == 0);
    assert(required > 0 && std::strstr(output, "\"id\":\"real\"") && !std::strstr(output, "\"id\":\"wrong\""));
    assert(std::strstr(output, "\"id\":\"inert\"") && std::strstr(output, "\"inert\":true"));
    assert(urui_validate_html("<div id='fragment'></div>", "ui/fragment.html", 1, output, sizeof(output), &required) ==
           0);
    assert(urui_validate_html("<div>missing", "ui/fragment.html", 1, output, sizeof(output), &required) > 0);
    assert(std::strstr(output, "ui/fragment.html") && std::strstr(output, "HTML_"));
    assert(urui_validate_html(valid, "ui/authoring.html", 0, output, 1, &required) == URUI_ERR_BUFFER_TOO_SMALL && required > 1);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(g_test_surface, &owner) == 1);
    assert(urui_document_load_html(g_test_surface, owner, "<!doctype html><html><head></head><body><div id='keep'></div></body></html>",
                                   "ui/active.html") == 1);
    const auto root = urui_document_root(g_test_surface, owner);
    assert(urui_validate_html(valid, "ui/authoring.html", 0, output, sizeof(output), &required) == 0);
    assert(urui_document_root(g_test_surface, owner) == root);
    const char* resources =
        "<!doctype html><html><head><style>body{background-image:url('page image.png');}</style></head><body><div style='color:red;' data-urui-key='panel'></div></body></html>";
    assert(urui_validate_html(resources, "ui/resources.html", 0, output, sizeof(output), &required) == 0);
    assert(std::strstr(output, "\"inlineStyle\":\"color:red;\"") &&
           std::strstr(output, "\"styleText\":\"body{background-image:"));
    assert(std::strstr(output, "\"data-urui-key\":\"panel\""));
    assert(urui_validate_stylesheet("ui/resources.css",
                                    "body{background-image:url('page image.png');mask-image:url(mask\\ image.png);}",
                                    output, sizeof(output), &required) == 0);
    assert(std::strstr(output, "\"urls\":[\"page image.png\"]") &&
           std::strstr(output, "\"urls\":[\"mask image.png\"]"));
    assert(urui_validate_stylesheet(
               "ui/border.css", ".panel{border-image:url('panel).png') 10% 274 0 27 fill / 0 274px 0 27px stretch;}",
               output, sizeof(output), &required) == 0);
    assert(std::strstr(output, "\"borderImage\":{\"fill\":true"));
    assert(std::strstr(output, "\"slices\":[10.000000,274.000000,0.000000,27.000000]"));
    assert(std::strstr(output, "\"percentages\":[true,false,false,false]"));
    urui_surface_destroy(g_test_surface);
    std::cout << "Stateless strict authoring validation and AST inventory passed\n";
}
