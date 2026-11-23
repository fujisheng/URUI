#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <cassert>
#include <cmath>
#include <cstring>
#include <vector>
#include <iostream>
static int gradient_draws()
{
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    std::vector<URUICommand> commands(urui_command_count(g_test_surface));
    urui_get_commands(g_test_surface, commands.data(), int(commands.size()));
    int count = 0;
    for (const auto& draw : commands)
    {
        if (draw.texture)
        {
            int w = 0;
            int h = 0;
            int n = 0;
            assert(urui_get_texture_info(g_test_surface, draw.texture, &w, &h, &n) == 1 && n == w * h * 4);
            std::vector<uint8_t> rgba(n);
            assert(urui_copy_texture(g_test_surface, draw.texture, rgba.data(), n) == 1);
            bool colored = false;
            for (int i = 0; i < n; i += 4)
            {
                colored |= rgba[i] > 0 || rgba[i + 1] > 0 || rgba[i + 2] > 0;
            }
            assert(colored);
            ++count;
        }
    }
    return count;
}
int main()
{
    assert(urui_surface_create(200, 200, &g_test_surface) == 1);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(g_test_surface, &owner) == 1);
    assert(
        urui_document_load_html(g_test_surface, 
            owner,
            "<!doctype html><html><head><style>body{margin:0;}#box{width:80px;height:40px;}</style></head><body><div id='box'></div></body></html>",
            "ui/gradient.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, owner, 0, 0, 1, 0) == 1);
    for (const char* gradient :
         {"linear-gradient(to right, red, blue)", "radial-gradient(circle at center, red, blue)",
          "conic-gradient(from 90deg at center, red, blue)", "repeating-linear-gradient(to right, red 0px, blue 8px)"})
    {
        assert(TestSetStyle(g_test_surface, owner, "box", "background-image", gradient) == 1);
        assert(gradient_draws() == 1);
    }
    assert(TestSetStyle(g_test_surface, owner, "box", "background-size", "20px 10px") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-position", "right bottom") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-repeat", "no-repeat") == 1);
    assert(gradient_draws() == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "mask-image", "linear-gradient(to right, white, transparent)") == 1);
    assert(gradient_draws() == 2);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-image", "none") == 1);
    assert(gradient_draws() == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "mask-image", "none") == 1);
    assert(gradient_draws() == 0);
    urui_surface_destroy(g_test_surface);
    std::cout << "Standard linear/radial/conic/repeating gradient images and mask geometry passed\n";
}
