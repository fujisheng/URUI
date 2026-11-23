#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "urui.h"
#include <cassert>
#include <iostream>
#include <string>
#include <vector>

bool HasDirectImage()
{
    std::vector<URUIPaintCommand> commands(urui_get_paint_commands(g_test_surface, nullptr, 0));
    assert(urui_get_paint_commands(g_test_surface, commands.data(), commands.size()) == int(commands.size()));
    for (const auto& command : commands)
    {
        if (command.reserved == 2)
        {
            return true;
        }
    }
    return false;
}
int ResolveDirectImage(URUISurfaceHandle, URUIDocumentHandle, const char*, uint64_t* id, uint64_t* source, int* w,
                       int* h)
{
    *source = 1;
    *id = 0x8000000000000001ULL;
    *w = 64;
    *h = 64;
    return 1;
}
void ReleaseDirectImage(URUISurfaceHandle, uint64_t)
{
}
int main()
{
    assert(urui_surface_create(128, 128, &g_test_surface) == 1);
    assert(urui_surface_set_direct_color_filters(g_test_surface, 2) == -1);
    assert(urui_surface_set_direct_color_filters(g_test_surface, 1) == 1);
    assert(urui_surface_set_image_resolver(g_test_surface, ResolveDirectImage, ReleaseDirectImage) == 1);
    auto load = [](const std::string& extra)
    {
        auto html =
            "<!doctype html><html><head><style>body{margin:0;}img{width:64px;height:64px;filter:brightness(2) contrast(.5);" +
            extra + "}</style></head><body><img id='direct' src='input.png'></body></html>";
        const auto owner = LoadTestDocument(g_test_surface, html.c_str(), "/direct.html");
        urui_surface_update(g_test_surface);
        urui_surface_render(g_test_surface);
        return owner;
    };
    auto owner = load("");
    assert(HasDirectImage());
    assert(urui_get_layer_commands(g_test_surface, nullptr, 0) == 0);
    std::vector<URUIPaintCommand> paints(urui_get_paint_commands(g_test_surface, nullptr, 0));
    assert(urui_get_paint_commands(g_test_surface, paints.data(), paints.size()) == int(paints.size()));
    uint64_t shader = 0;
    for (const auto& paint : paints)
    {
        if (paint.reserved == 2)
        {
            shader = paint.shader;
        }
    }
    assert(shader && urui_paint_color_filters(g_test_surface, shader, nullptr, 0) == 2);
    URUIColorFilter chain[2]{{99, 99}, {99, 99}};
    assert(urui_paint_color_filters(g_test_surface, shader, chain, 1) == URUI_ERR_BUFFER_TOO_SMALL &&
           chain[0].kind == 99);
    assert(urui_paint_color_filters(g_test_surface, shader, chain, 2) == 2);
    assert(chain[0].kind == 2 && chain[0].value == 2);
    assert(chain[1].kind == 3 && chain[1].value == .5f);
    assert(urui_surface_set_direct_color_filters(g_test_surface, 0) == URUI_ERR_REENTRANCY);
    assert(urui_document_close(g_test_surface, owner) == 1);
    assert(urui_paint_color_filters(g_test_surface, shader, chain, 2) == -2);
    for (const auto* unsafe : {"background-color:red;", "opacity:.5;", "filter:blur(1px);", "--urui-effect:probe;",
                               "border:1px solid red;", "border-radius:5px;", "box-shadow:1px 1px 1px red;"})
    {
        owner = load(unsafe);
        assert(!HasDirectImage());
        assert(urui_get_layer_commands(g_test_surface, nullptr, 0) > 0);
        assert(urui_document_close(g_test_surface, owner) == 1);
    }
    owner = load("filter:brightness(var(--amount));--amount:.5;");
    assert(HasDirectImage());
    auto read_amount = []
    {
        std::vector<URUIPaintCommand> commands(urui_get_paint_commands(g_test_surface, nullptr, 0));
        assert(urui_get_paint_commands(g_test_surface, commands.data(), commands.size()) == int(commands.size()));
        for (const auto& command : commands)
        {
            if (command.reserved == 2)
            {
                URUIColorFilter value{};
                assert(urui_paint_color_filters(g_test_surface, command.shader, &value, 1) == 1);
                return value.value;
            }
        }
        assert(false);
        return -1.f;
    };
    assert(read_amount() == .5f);
    const auto image = urui_element_find(g_test_surface, owner, urui_document_root(g_test_surface, owner), "direct", nullptr);
    assert(image && urui_element_number(g_test_surface, owner, image, "--amount", 2, 0) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(read_amount() == 2.f);
    assert(urui_document_close(g_test_surface, owner) == 1);
    std::string long_chain = "filter:";
    for (int i = 0; i < 17; ++i)
    {
        long_chain += "brightness(1) ";
    }
    owner = load(long_chain + ";");
    assert(!HasDirectImage());
    assert(urui_get_layer_commands(g_test_surface, nullptr, 0) > 0);
    assert(urui_document_close(g_test_surface, owner) == 1);
    assert(urui_surface_set_direct_color_filters(g_test_surface, 0) == 1);
    owner = load("");
    assert(!HasDirectImage());
    assert(urui_get_layer_commands(g_test_surface, nullptr, 0) > 0);
    assert(urui_document_close(g_test_surface, owner) == 1);
    urui_surface_destroy(g_test_surface);
    std::cout << "direct CSS image filter eligibility and safe layer fallback passed\n";
}
