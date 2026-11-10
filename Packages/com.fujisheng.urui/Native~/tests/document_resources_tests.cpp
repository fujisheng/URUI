#include "urui.h"
#include <URUI/Core.h>
#include <cassert>
#include <cstring>
#include <iostream>
#include <vector>
#include <algorithm>
#include <cmath>

static int Register(UruiDocumentHandle owner, const char* source, const char* value)
{
    return urui_document_register_resource(owner, source, reinterpret_cast<const uint8_t*>(value), int(std::strlen(value)));
}
static void Load(UruiDocumentHandle owner, const char* url)
{
    assert(urui_document_load_html(owner, "<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='box'></div><img src='icons/shared.png' width='4' height='4'><img id='natural' src='icons/shared.png'><img id='sized' src='icons/shared.png' width='3' height='5'></body></html>", url) == 1);
    assert(urui_document_set_presentation(owner, 0, 0, 1, 0) == 1);
}
static void ScopedRgbaMatchesTheOwnedTextureProtocol()
{
    UruiDocumentHandle a = 0, b = 0;
    assert(urui_document_create(&a) == 1 && urui_document_create(&b) == 1);
    const uint8_t pixels[] = {255, 0, 0, 128, 99, 42, 17, 0};
    uint64_t first = 0, shared = 0;
    assert(urui_document_register_rgba_texture(a, "/alpha.png", 2, 1, pixels, sizeof(pixels), &first) == 1);
    assert(urui_document_register_rgba_texture(b, "/alpha.png", 2, 1, pixels, sizeof(pixels), &shared) == 1 && shared == first);
    uint8_t copy[8] = {};
    assert(urui_copy_texture(first, copy, sizeof(copy)) == 1);
    assert(copy[0] == 128 && copy[3] == 128 && copy[4] == 0 && copy[5] == 0 && copy[6] == 0 && copy[7] == 0);
    assert(urui_document_close(a) == 1 && urui_texture_exists(first) == 1);
    assert(urui_document_close(b) == 1 && urui_texture_exists(first) == 0);
}
static void ResolverConfigurationRequiresNoLoadedDocuments()
{
    UruiDocumentHandle owner = 0;
    assert(urui_document_create(&owner) == 1);
    assert(urui_set_image_resolver(nullptr, nullptr) == 1);
    assert(urui_document_load_html(owner, "<!doctype html><html><head></head><body></body></html>", "/resolver-lifecycle.html") == 1);
    assert(urui_set_image_resolver(nullptr, nullptr) == -2);
    assert(urui_document_close(owner) == 1);
    assert(urui_set_image_resolver(nullptr, nullptr) == 1);
}
int main()
{
    assert(urui_document_resource_api_version() == 3);
    assert(urui_command_protocol_version() == 2 && urui_texture_protocol_version() == 1);
    assert(urui_create(400, 200) == 1);
    ScopedRgbaMatchesTheOwnedTextureProtocol();
    ResolverConfigurationRequiresNoLoadedDocuments();
    UruiDocumentHandle a = 0, b = 0;
    assert(urui_document_create(&a) == 1 && urui_document_create(&b) == 1);
    assert(Register(a, "ui/theme.css", "#box{width:37px;height:10px;background-color:red;}") == 1);
    assert(Register(b, "ui/theme.css", "#box{width:91px;}") < 0);
    assert(std::strstr(urui_last_error(), "ui/theme.css"));
    assert(Register(b, "ui/theme.css", "#box{width:37px;height:10px;background-color:red;}") == 1);
    uint8_t pixels[64];
    for (int index = 0; index < 64; index += 4) { pixels[index] = pixels[index+3] = 255; pixels[index+1] = pixels[index+2] = 0; }
    uint64_t imageA = 0, imageB = 0;
    assert(urui_document_register_rgba_texture(a, "ui/icons/shared.png", 4, 4, pixels, sizeof(pixels), &imageA) == 1);
    assert(urui_document_register_rgba_texture(b, "ui/icons/shared.png", 4, 4, pixels, sizeof(pixels), &imageB) == 1);
    assert(imageA && imageA == imageB);
    assert(urui_document_root(a) == 0 && urui_document_root(b) == 0);
    Load(a, "ui/a.html"); Load(b, "ui/b.html"); urui_update(); urui_render();
    assert(urui_document_add_stylesheet(b, "#box{mask-image:url(icons/shared.png);}", "ui/theme.css", 0) == 1);
    urui_update(); urui_render();
    std::vector<UruiCommand> commands(urui_command_count());
    assert(urui_get_commands(commands.data(), int(commands.size())) == int(commands.size()));
    bool repeating_mask = false, clamped_image = false;
    for (const auto& command : commands) if (command.texture == imageB) {
        repeating_mask |= (command.flags & 6) == 6;
        clamped_image |= (command.flags & 6) == 0;
    }
    assert(repeating_mask && clamped_image);
    auto RepeatU = [&]() {
        std::vector<UruiCommand> draws(urui_command_count());
        assert(urui_get_commands(draws.data(), int(draws.size())) == int(draws.size()));
        for (const auto& draw : draws) if (draw.texture == imageB && (draw.flags & 6) == 6) {
            int vertices = 0, indices = 0;
            assert(urui_get_geometry_info(draw.geometry, &vertices, &indices) == 1);
            std::vector<UruiVertex> data(vertices); std::vector<int> ids(indices);
            assert(urui_copy_geometry(draw.geometry, data.data(), vertices, ids.data(), indices) == 1);
            float maximum = 0; for (const auto& vertex : data) maximum = std::max(maximum, vertex.u);
            return maximum;
        }
        return -1.f;
    };
    const auto natural_repeat_u = RepeatU(); assert(natural_repeat_u > 0);
    URUI::GetContext("urui")->SetDensityIndependentPixelRatio(2); urui_update(); urui_render();
    assert(std::abs(RepeatU() - natural_repeat_u) < .001f);
    float image_box[6];
    assert(urui_get_element_box(b, "natural", image_box) == 1 && image_box[2] == 4 && image_box[3] == 4);
    assert(urui_get_element_box(b, "sized", image_box) == 1 && image_box[2] == 3 && image_box[3] == 5);
    assert(urui_set_style(b, "natural", "--urui-image-density", "surface") == 1);
    assert(urui_set_style(b, "sized", "--urui-image-density", "surface") == 1); urui_update();
    assert(urui_get_element_box(b, "natural", image_box) == 1 && image_box[2] == 8 && image_box[3] == 8);
    assert(urui_get_element_box(b, "sized", image_box) == 1 && image_box[2] == 3 && image_box[3] == 5);
    assert(urui_set_style(b, "natural", "--urui-image-density", "css-pixels") == 1); urui_update();
    assert(urui_get_element_box(b, "natural", image_box) == 1 && image_box[2] == 4);
    URUI::GetContext("urui")->SetDensityIndependentPixelRatio(1); urui_update(); urui_render();
    float box[6]; assert(urui_get_element_box(a, "box", box) == 1 && box[2] == 37);
    const auto rootA = urui_document_root(a), rootB = urui_document_root(b);
    UruiFrameStamp before{}; assert(urui_render_plan_stamp(&before) == 1);
    assert(Register(a, "ui/invalid.css", "@property --bad{syntax:\"<angle>\";inherits:false;initial-value:0deg;}") == 1);
    assert(urui_document_load_html(a, "<!doctype html><html><head><link rel='stylesheet' href='invalid.css'></head><body></body></html>", "ui/failed.html") < 0);
    assert(urui_document_root(a) == rootA && urui_document_root(b) == rootB);
    urui_update(); urui_render();
    assert(urui_get_element_box(a, "box", box) == 1 && box[2] == 37);
    assert(urui_document_close(a) == 1); urui_update(); urui_render();
    assert(urui_document_root(b) == rootB && urui_texture_exists(imageB) == 1);
    int width = 0, height = 0;
    assert(urui_document_preload_image(b, "icons/shared.png", &width, &height) == 1 && width == 4 && height == 4);
    UruiFrameStamp after{}; assert(urui_render_plan_stamp(&after) == 1 && before.epoch == after.epoch);
    assert(urui_document_close(b) == 1); urui_update();
    assert(urui_texture_exists(imageB) == 0);
    urui_destroy();
    std::cout << "Pending resources, URI conflicts, rollback and shared texture retirement passed\n";
}
