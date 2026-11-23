#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <cassert>
#include <cstring>
#include <fstream>
#include <iterator>
#include <iostream>
#include <vector>

static void Register(URUIDocumentHandle owner, const std::string& uri, const std::vector<uint8_t>& bytes)
{
    assert(urui_document_register_resource(g_test_surface, owner, uri.c_str(), bytes.data(), int(bytes.size())) == 1);
}
static uint64_t Setup(URUIDocumentHandle owner, const char* folder, const std::string& font, const char* opacity)
{
    const std::string base = std::string("ui/") + folder + "/";
    const std::string css =
        "@property --drag-opacity{syntax:\"<number>\";inherits:false;initial-value:" + std::string(opacity) +
        ";}"
        "@font-face{font-family:LocalFace;src:url(font.ttf);}body{margin:0;font-family:LocalFace;font-size:20px;}"
        "#drag{--urui-drag:clone;width:100px;height:40px;opacity:var(--drag-opacity);background-color:red;}img{width:10px;height:10px;}";
    Register(owner, base + "theme.css", {css.begin(), css.end()});
    std::ifstream input(font, std::ios::binary);
    std::vector<uint8_t> font_bytes((std::istreambuf_iterator<char>(input)), {});
    assert(!font_bytes.empty());
    Register(owner, base + "font.ttf", font_bytes);
    uint8_t pixels[] = {255, 255, 255, 255};
    uint64_t texture = 0;
    assert(urui_document_register_rgba_texture(g_test_surface, owner, (base + "image.png").c_str(), 1, 1, pixels, sizeof(pixels),
                                               &texture) == 1);
    assert(
        urui_document_load_html(g_test_surface, 
            owner,
            "<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='drag'><img src='image.png'>WWWW</div></body></html>",
            (base + "page.html").c_str()) == 1);
    return texture;
}
static std::vector<URUICommand> Draws()
{
    std::vector<URUICommand> draws(urui_command_count(g_test_surface));
    assert(urui_get_commands(g_test_surface, draws.data(), int(draws.size())) == int(draws.size()));
    return draws;
}
static int Count(uint64_t texture)
{
    int count = 0;
    for (const auto& draw : Draws())
    {
        if (draw.texture == texture)
        {
            ++count;
        }
    }
    return count;
}
static uint64_t GlyphTexture(uint64_t image)
{
    for (const auto& draw : Draws())
    {
        if (draw.texture && draw.texture != image)
        {
            return draw.texture;
        }
    }
    return 0;
}
static void Opacity(URUIDocumentHandle owner, float value, int expected)
{
    const int count = urui_get_layer_commands(g_test_surface, nullptr, 0);
    std::vector<URUILayerCommand> layers(count);
    assert(urui_get_layer_commands(g_test_surface, layers.data(), count) == count);
    int found = 0;
    for (const auto& layer : layers)
    {
        if (layer.kind == URUI_LAYER_APPLY_MASK)
        {
            URUIFilter filter{};
            char name[64];
            int required = 0;
            assert(urui_filter_info(g_test_surface, layer.mask, &filter, name, sizeof(name), &required) == URUI_OK);
            if (!std::strcmp(name, "opacity"))
            {
                assert(filter.value == value && urui_filter_document(g_test_surface, layer.mask) == owner);
                ++found;
            }
        }
    }
    assert(found == expected);
}
static void Pointer(int phase, float x, float y, int buttons)
{
    assert(urui_pointer_event(g_test_surface, 0, 0, phase, x, y, 0, buttons, 0, 0, 0) == 1);
}
int main(int argc, char** argv)
{
    assert(argc == 2 && urui_surface_create(400, 200, &g_test_surface) == 1);
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    assert(urui_document_create(g_test_surface, &a) == 1 && urui_document_create(g_test_surface, &b) == 1);
    const auto image_a = Setup(a, "a", std::string(argv[1]) + "/LiberationMono-Regular.ttf.bytes", ".5");
    const auto image_b = Setup(b, "b", std::string(argv[1]) + "/LiberationSerif-Regular.ttf.bytes", ".8");
    assert(urui_document_set_presentation(g_test_surface, a, 0, 0, 1, 0) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    const auto glyph_a = GlyphTexture(image_a);
    assert(glyph_a && Count(image_a) == 1 && Count(glyph_a) == 1);
    Pointer(0, 10, 10, 1);
    Pointer(1, 20, 20, 1);
    Pointer(1, 70, 70, 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(Count(image_a) == 2 && Count(glyph_a) == 2);
    Opacity(a, .5f, 2);
    std::vector<URUICommand> image_draws;
    for (const auto& draw : Draws())
    {
        if (draw.texture == image_a)
        {
            image_draws.push_back(draw);
        }
    }
    std::cerr << "Drag image offset: " << image_draws[1].tx - image_draws[0].tx << ", "
              << image_draws[1].ty - image_draws[0].ty << '\n';
    assert(image_draws[1].tx - image_draws[0].tx == 50 && image_draws[1].ty - image_draws[0].ty == 50);
    assert(urui_document_close(g_test_surface, a) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(Count(image_a) == 0);
    Pointer(2, 70, 70, 0);
    assert(urui_document_set_presentation(g_test_surface, b, 0, 0, 1, 0) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    const auto glyph_b = GlyphTexture(image_b);
    assert(glyph_b && glyph_b != glyph_a && Count(image_b) == 1);
    Pointer(0, 10, 10, 1);
    Pointer(1, 20, 20, 1);
    Pointer(1, 70, 70, 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(Count(image_b) == 2 && Count(glyph_b) == 2);
    Opacity(b, .8f, 2);
    Pointer(2, 70, 70, 0);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(Count(image_b) == 1);
    Opacity(b, .8f, 1);
    assert(urui_document_close(g_test_surface, b) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_destroy(g_test_surface);
    std::cout
        << "Drag proxy preserves resource URLs, document font/typed defaults, filter owner and release boundaries\n";
}
