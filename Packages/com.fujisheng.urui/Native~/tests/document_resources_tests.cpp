#include "urui.h"
#include "../render_plan_protocol.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;

static uint64_t CurrentPlanEpoch()
{
    URUIPlanInputPacket input{};
    assert(urui_plan_begin(g_test_surface, &input) == URUI_OK);
    const uint64_t epoch = input.epoch;
    assert(urui_plan_release(g_test_surface, input.lease) == URUI_OK);
    return epoch;
}
#include <URUI/Core.h>
#include <cassert>
#include <cstring>
#include <iostream>
#include <vector>
#include <algorithm>
#include <cmath>

static int Register(URUIDocumentHandle owner, const char* source, const char* value)
{
    return urui_document_register_resource(g_test_surface, owner, source, reinterpret_cast<const uint8_t*>(value),
                                           int(std::strlen(value)));
}
static void Load(URUIDocumentHandle owner, const char* url)
{
    assert(
        urui_document_load_html(g_test_surface, 
            owner,
            "<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='box'></div><img src='icons/shared.png' width='4' height='4'><img id='natural' src='icons/shared.png'><img id='sized' src='icons/shared.png' width='3' height='5'></body></html>",
            url) == 1);
    assert(urui_document_set_presentation(g_test_surface, owner, 0, 0, 1, 0) == 1);
}
static void ScopedRgbaMatchesTheOwnedTextureProtocol()
{
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    assert(urui_document_create(g_test_surface, &a) == 1 && urui_document_create(g_test_surface, &b) == 1);
    const uint8_t pixels[] = {255, 0, 0, 128, 99, 42, 17, 0};
    uint64_t first = 0;
    uint64_t shared = 0;
    assert(urui_document_register_rgba_texture(g_test_surface, a, "/alpha.png", 2, 1, pixels, sizeof(pixels), &first) == 1);
    assert(urui_document_register_rgba_texture(g_test_surface, b, "/alpha.png", 2, 1, pixels, sizeof(pixels), &shared) == 1 &&
           shared == first);
    uint8_t copy[8] = {};
    assert(urui_copy_texture(g_test_surface, first, copy, sizeof(copy)) == 1);
    assert(copy[0] == 128 && copy[3] == 128 && copy[4] == 0 && copy[5] == 0 && copy[6] == 0 && copy[7] == 0);
    assert(urui_document_close(g_test_surface, a) == 1 && urui_texture_exists(g_test_surface, first) == 1);
    assert(urui_document_close(g_test_surface, b) == 1 && urui_texture_exists(g_test_surface, first) == 0);
}
static void ResolverConfigurationRequiresNoLoadedDocuments()
{
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(g_test_surface, &owner) == 1);
    assert(urui_surface_set_image_resolver(g_test_surface, nullptr, nullptr) == 1);
    assert(urui_document_load_html(g_test_surface, owner, "<!doctype html><html><head></head><body></body></html>",
                                   "/resolver-lifecycle.html") == 1);
    assert(urui_surface_set_image_resolver(g_test_surface, nullptr, nullptr) == -2);
    assert(urui_document_close(g_test_surface, owner) == 1);
    assert(urui_surface_set_image_resolver(g_test_surface, nullptr, nullptr) == 1);
}
int main()
{
    assert(urui_document_resource_api_version() == 4);
    assert(urui_command_protocol_version() == 3 && urui_texture_protocol_version() == 1);
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    ScopedRgbaMatchesTheOwnedTextureProtocol();
    ResolverConfigurationRequiresNoLoadedDocuments();
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    assert(urui_document_create(g_test_surface, &a) == 1 && urui_document_create(g_test_surface, &b) == 1);
    assert(Register(a, "ui/theme.css", "#box{width:37px;height:10px;background-color:red;}") == 1);
    assert(Register(b, "ui/theme.css", "#box{width:91px;}") < 0);
    assert(std::strstr(TestLastError(g_test_surface).c_str(), "ui/theme.css"));
    assert(Register(b, "ui/theme.css", "#box{width:37px;height:10px;background-color:red;}") == 1);
    uint8_t pixels[64];
    for (int index = 0; index < 64; index += 4)
    {
        pixels[index] = pixels[index + 3] = 255;
        pixels[index + 1] = pixels[index + 2] = 0;
    }
    uint64_t imageA = 0;
    uint64_t imageB = 0;
    assert(urui_document_register_rgba_texture(g_test_surface, a, "ui/icons/shared.png", 4, 4, pixels, sizeof(pixels), &imageA) == 1);
    assert(urui_document_register_rgba_texture(g_test_surface, b, "ui/icons/shared.png", 4, 4, pixels, sizeof(pixels), &imageB) == 1);
    assert(imageA && imageA == imageB);
    assert(urui_document_root(g_test_surface, a) == 0 && urui_document_root(g_test_surface, b) == 0);
    Load(a, "ui/a.html");
    Load(b, "ui/b.html");
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(urui_document_add_stylesheet(g_test_surface, b, "#box{mask-image:url(icons/shared.png);}", "ui/theme.css", 0) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    std::vector<URUICommand> commands(urui_command_count(g_test_surface));
    assert(urui_get_commands(g_test_surface, commands.data(), int(commands.size())) == int(commands.size()));
    bool repeating_mask = false;
    bool clamped_image = false;
    for (const auto& command : commands)
    {
        if (command.texture == imageB)
        {
            repeating_mask |= (command.flags & 6) == 6;
            clamped_image |= (command.flags & 6) == 0;
        }
    }
    assert(repeating_mask && clamped_image);
    auto RepeatU = [&]()
    {
        std::vector<URUICommand> draws(urui_command_count(g_test_surface));
        assert(urui_get_commands(g_test_surface, draws.data(), int(draws.size())) == int(draws.size()));
        for (const auto& draw : draws)
        {
            if (draw.texture == imageB && (draw.flags & 6) == 6)
            {
                int vertices = 0;
                int indices = 0;
                assert(urui_get_geometry_info(g_test_surface, draw.geometry, &vertices, &indices) == 1);
                std::vector<URUIVertex> data(vertices);
                std::vector<int> ids(indices);
                assert(urui_copy_geometry(g_test_surface, draw.geometry, data.data(), vertices, ids.data(), indices) == 1);
                float maximum = 0;
                for (const auto& vertex : data)
                {
                    maximum = std::max(maximum, vertex.u);
                }
                return maximum;
            }
        }
        return -1.f;
    };
    const auto natural_repeat_u = RepeatU();
    assert(natural_repeat_u > 0);
    URUI::GetContext(0)->SetDensityIndependentPixelRatio(2);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(std::abs(RepeatU() - natural_repeat_u) < .001f);
    float image_box[6];
    assert(TestGetElementBox(g_test_surface, b, "natural", image_box) == 1 && image_box[2] == 4 && image_box[3] == 4);
    assert(TestGetElementBox(g_test_surface, b, "sized", image_box) == 1 && image_box[2] == 3 && image_box[3] == 5);
    assert(TestSetStyle(g_test_surface, b, "natural", "--urui-image-density", "surface") == 1);
    assert(TestSetStyle(g_test_surface, b, "sized", "--urui-image-density", "surface") == 1);
    urui_surface_update(g_test_surface);
    assert(TestGetElementBox(g_test_surface, b, "natural", image_box) == 1 && image_box[2] == 8 && image_box[3] == 8);
    assert(TestGetElementBox(g_test_surface, b, "sized", image_box) == 1 && image_box[2] == 3 && image_box[3] == 5);
    assert(TestSetStyle(g_test_surface, b, "natural", "--urui-image-density", "css-pixels") == 1);
    urui_surface_update(g_test_surface);
    assert(TestGetElementBox(g_test_surface, b, "natural", image_box) == 1 && image_box[2] == 4);
    URUI::GetContext(0)->SetDensityIndependentPixelRatio(1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    float box[6];
    assert(TestGetElementBox(g_test_surface, a, "box", box) == 1 && box[2] == 37);
    const auto rootA = urui_document_root(g_test_surface, a);
    const auto rootB = urui_document_root(g_test_surface, b);
    const uint64_t before_epoch = CurrentPlanEpoch();
    assert(Register(a, "ui/invalid.css", "@property --bad{syntax:\"<angle>\";inherits:false;initial-value:0deg;}") ==
           1);
    assert(urui_document_load_html(g_test_surface, 
               a, "<!doctype html><html><head><link rel='stylesheet' href='invalid.css'></head><body></body></html>",
               "ui/failed.html") < 0);
    assert(urui_document_root(g_test_surface, a) == rootA && urui_document_root(g_test_surface, b) == rootB);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(TestGetElementBox(g_test_surface, a, "box", box) == 1 && box[2] == 37);
    assert(urui_document_close(g_test_surface, a) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(urui_document_root(g_test_surface, b) == rootB && urui_texture_exists(g_test_surface, imageB) == 1);
    int width = 0;
    int height = 0;
    assert(urui_document_preload_image(g_test_surface, b, "icons/shared.png", &width, &height) == 1 && width == 4 && height == 4);
    const uint64_t after_epoch = CurrentPlanEpoch();
    assert(before_epoch != 0 && after_epoch != 0);
    assert(urui_document_close(g_test_surface, b) == 1);
    urui_surface_update(g_test_surface);
    assert(urui_texture_exists(g_test_surface, imageB) == 0);
    urui_surface_destroy(g_test_surface);
    std::cout << "Pending resources, URI conflicts, rollback and shared texture retirement passed\n";
}
