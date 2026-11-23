#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <URUI/Core/RenderManager.h>
#include <URUI/Core/MeshUtilities.h>
#include <URUI/Core/CallbackTexture.h>
#include <cassert>
#include <cmath>
#include <vector>
#include <array>
#include <iostream>

struct Draw
{
    URUICommand command;
    std::vector<URUIVertex> vertices;
};
static std::vector<Draw> Draws(uint64_t texture)
{
    urui_surface_update(g_test_surface);
    URUIPlanInputPacket input{};
    assert(urui_plan_begin(g_test_surface, &input) == 1);
    std::vector<Draw> result;
    for (uint32_t n = 0; n < input.command_count; n++)
    {
        const auto& command = input.commands[n];
        if (command.texture != texture)
        {
            continue;
        }
        int vertices = 0;
        int indices = 0;
        assert(urui_get_geometry_info(g_test_surface, command.geometry, &vertices, &indices) == 1);
        Draw draw{command, std::vector<URUIVertex>(vertices)};
        std::vector<int> ids(indices);
        assert(urui_copy_geometry(g_test_surface, command.geometry, draw.vertices.data(), vertices, ids.data(), indices) == 1);
        const auto& uniform = input.uniforms[n];
        for (auto& vertex : draw.vertices)
        {
            vertex.r = uint8_t(std::lround(vertex.r * uniform.tint[0]));
            vertex.g = uint8_t(std::lround(vertex.g * uniform.tint[1]));
            vertex.b = uint8_t(std::lround(vertex.b * uniform.tint[2]));
            vertex.a = uint8_t(std::lround(vertex.a * uniform.tint[3]));
            vertex.u = vertex.u * uniform.uv[0] + uniform.uv[2];
            vertex.v = vertex.v * uniform.uv[1] + uniform.uv[3];
        }
        if (!ids.empty())
        {
            result.push_back(std::move(draw));
        }
    }
    assert(urui_plan_release(g_test_surface, input.lease) == 1);
    return result;
}
static void Near(float actual, float expected)
{
    assert(std::abs(actual - expected) < .001f);
}
int main()
{
    assert(urui_surface_create(200, 200, &g_test_surface) == 1);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(g_test_surface, &owner) == 1);
    uint8_t pixels[4 * 2 * 4];
    for (auto& byte : pixels)
    {
        byte = 255;
    }
    uint64_t texture = 0;
    assert(urui_document_register_rgba_texture(g_test_surface, owner, "ui/pattern.png", 4, 2, pixels, sizeof(pixels), &texture) == 1);
    assert(
        urui_document_load_html(g_test_surface, 
            owner,
            "<!doctype html><html><head><style>body{margin:0;}#box{width:40px;height:20px;padding:5px;border:2px solid red;background-color:blue;}</style></head><body><div id='box'></div></body></html>",
            "ui/page.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, owner, 0, 0, 1, 0) == 1);
    for (const auto& draw : Draws(0))
    {
        for (const auto& vertex : draw.vertices)
        {
            assert(!(vertex.r == 0 && vertex.g == 0 && vertex.b == 0 &&
                     // 无作者黑色，不得额外生成黑色底图。
                     vertex.a == 255));
        }
    }
    assert(TestSetStyle(g_test_surface, owner, "box", "background-image", "url(pattern.png)") == 1);
    auto draws = Draws(texture);
    assert(draws.size() == 1 && (draws[0].command.flags & 6) == 6);
    // 默认定位区是 padding-box，绘制延伸至 border-box，源图尺寸不受 Surface DP 缩放影响。
    const auto& v = draws[0].vertices;
    assert(v.size() > 3);
    float minx = 1e9f;
    float maxx = -1e9f;
    float minu = 1e9f;
    float maxu = -1e9f;
    for (const auto& p : v)
    {
        minx = std::min(minx, p.x);
        maxx = std::max(maxx, p.x);
        minu = std::min(minu, p.u);
        maxu = std::max(maxu, p.u);
    }
    Near(minx, 0);
    Near(maxx, 54);
    Near(minu, -.5f);
    Near(maxu, 13);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-repeat", "no-repeat") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-size", "20px 10px") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-position", "right bottom") == 1);
    draws = Draws(texture);
    assert(draws.size() == 1 && (draws[0].command.flags & 6) == 0);
    minx = 1e9f;
    maxx = -1e9f;
    for (const auto& p : draws[0].vertices)
    {
        minx = std::min(minx, p.x);
        maxx = std::max(maxx, p.x);
    }
    Near(minx, 32);
    Near(maxx, 52);
    assert(TestSetStyle(g_test_surface, owner, "box", "background", "lime") == 1);
    // 简写重置图像及其几何属性。
    assert(Draws(texture).empty());
    assert(TestSetStyle(g_test_surface, owner, "box", "background",
                          "url(pattern.png) right bottom / 20px 10px no-repeat content-box padding-box lime") == 1);
    draws = Draws(texture);
    assert(draws.size() == 1 && (draws[0].command.flags & 6) == 0);
    minx = 1e9f;
    maxx = -1e9f;
    for (const auto& p : draws[0].vertices)
    {
        minx = std::min(minx, p.x);
        maxx = std::max(maxx, p.x);
    }
    Near(minx, 27);
    Near(maxx, 47);
    assert(TestSetStyle(g_test_surface, owner, "box", "background", "none") == 1);

    assert(TestSetStyle(g_test_surface, owner, "box", "mask-image", "none, url(pattern.png)") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "mask-repeat", "no-repeat, repeat-x") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "mask-size", "1px 1px, 8px 4px") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "mask-position", "left top, 50% 100%") == 1);
    draws = Draws(texture);
    assert(draws.size() == 1 && (draws[0].command.flags & 6) == 2);
    float miny = 1e9f;
    float maxy = -1e9f;
    for (const auto& p : draws[0].vertices)
    {
        miny = std::min(miny, p.y);
        maxy = std::max(maxy, p.y);
    }
    Near(miny, 30);
    // mask-origin 默认 border-box，none 仍占据列表位置。
    Near(maxy, 34);
    URUI::GetContext(0)->SetDensityIndependentPixelRatio(2);
    auto scaled = Draws(texture);
    assert(scaled[0].vertices.size() == draws[0].vertices.size());
    for (size_t i = 0; i < scaled[0].vertices.size(); ++i)
    {
        Near(scaled[0].vertices[i].u, draws[0].vertices[i].u);
        Near(scaled[0].vertices[i].v, draws[0].vertices[i].v);
    }
    URUI::PropertyDictionary parsed;
    auto& spec = URUI::StyleSheetSpecification::GetPropertySpecification();
    for (const char* name : {"background-size", "mask-size"})
    {
        assert(spec.ParsePropertyDeclaration(parsed, name, "cover, 50% auto"));
        assert(!spec.ParsePropertyDeclaration(parsed, name, "-1px 2px"));
        assert(!spec.ParsePropertyDeclaration(parsed, name, "auto garbage"));
        assert(!spec.ParsePropertyDeclaration(parsed, name, "\\31 2px"));
    }
    for (const char* name : {"background-position", "mask-position"})
    {
        assert(spec.ParsePropertyDeclaration(parsed, name, "top right, -2px 75%"));
        assert(!spec.ParsePropertyDeclaration(parsed, name, "left right"));
        assert(!spec.ParsePropertyDeclaration(parsed, name, "0px, "));
    }
    assert(!spec.ParsePropertyDeclaration(parsed, "background-image", "image(pattern.png)"));
    // 现有子集不含 round/space。
    assert(!spec.ParsePropertyDeclaration(parsed, "background-repeat", "round"));
    assert(TestSetStyle(g_test_surface, owner, "box", "background-image", "none, none") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-color", "blue") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "background-clip", "border-box, padding-box") == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    bool blue_fill = false;
    for (const auto& draw : Draws(0))
    {
        for (const auto& vertex : draw.vertices)
        {
            if (vertex.b == 255 && vertex.r == 0 && vertex.g == 0)
            {
                assert(vertex.x >= 2 && vertex.x <= 52 && vertex.y >= 2 && vertex.y <= 32);
                blue_fill = true;
            }
        }
    }
    // 最后一层即使是 none，仍决定背景底色绘制区。
    assert(blue_fill);
    assert(TestSetStyle(g_test_surface, owner, "box", "mask-image", "none") == 1);
    assert(TestSetStyle(g_test_surface, owner, "box", "box-shadow", "-5px -5px 0 rgba(255,0,0,.5)") == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    std::vector<URUICommand> shadow_commands(urui_command_count(g_test_surface));
    urui_get_commands(g_test_surface, shadow_commands.data(), int(shadow_commands.size()));
    bool shadow_texture = false;
    for (const auto& command : shadow_commands)
    {
        if (command.texture && command.texture != texture)
        {
            int width = 0;
            int height = 0;
            int bytes = 0;
            assert(urui_get_texture_info(g_test_surface, command.texture, &width, &height, &bytes) == 1);
            std::vector<uint8_t> data(bytes);
            assert(urui_copy_texture(g_test_surface, command.texture, data.data(), bytes) == 1);
            for (int index = 0; index + 3 < bytes; index += 4)
            {
                if (data[index + 3] >= 127 && data[index + 3] <= 128 && data[index + 1] == 0 && data[index + 2] == 0)
                {
                    assert(data[index] == data[index + 3]);
                    shadow_texture = true;
                }
            }
        }
    }
    // 阴影必须携带真实 RGBA 资源，不能从未支持的图层保存接口得到白色占位图。
    assert(shadow_texture);
    {
        auto& manager = URUI::GetContext(0)->GetRenderManager();
        URUI::Mesh mesh;
        URUI::MeshUtilities::GenerateQuad(mesh, {0, 0}, {10, 10}, URUI::ColourbPremultiplied(255));
        auto geometry = manager.MakeGeometry(std::move(mesh));
        auto failed = manager.MakeCallbackTexture([](const URUI::CallbackTextureInterface&) { return false; });
        const auto before = urui_command_count(g_test_surface);
        geometry.Render({0, 0}, failed);
        // 失败纹理不能作为无纹理白色几何提交。
        assert(urui_command_count(g_test_surface) == before);
    }
    URUIDocumentHandle progress_owner = 0;
    assert(urui_document_create(g_test_surface, &progress_owner) == 1);
    uint8_t white[16];
    for (auto& byte : white)
    {
        byte = 255;
    }
    uint64_t progress_image = 0;
    assert(urui_document_register_rgba_texture(g_test_surface, progress_owner, "ui/fill.png", 2, 2, white, 16, &progress_image) == 1);
    assert(
        urui_document_load_html(g_test_surface, 
            progress_owner,
            "<!doctype html><html><head><style>body{margin:0;}progress{position:absolute;left:0;top:0;width:40px;height:40px;--urui-fill-image:fill.png;}#obsolete{left:50px;}#quarter{top:50px;}</style></head><body><progress id='bottom' value='.5' data-urui-progress-direction='bottom'></progress><progress id='obsolete' value='.5' direction='bottom'></progress><progress id='quarter' value='.25' data-urui-progress-direction='clockwise' data-urui-progress-start-edge='left'></progress></body></html>",
            "ui/progress.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, progress_owner, 0, 0, 1, 0) == 1);
    auto progress_draws = Draws(progress_image);
    assert(progress_draws.size() == 3);
    std::vector<std::array<float, 4>> bounds;
    for (const auto& draw : progress_draws)
    {
        std::array<float, 4> box{{1e9f, 1e9f, -1e9f, -1e9f}};
        for (const auto& vertex : draw.vertices)
        {
            box[0] = std::min(box[0], vertex.x + draw.command.tx);
            box[1] = std::min(box[1], vertex.y + draw.command.ty);
            box[2] = std::max(box[2], vertex.x + draw.command.tx);
            box[3] = std::max(box[3], vertex.y + draw.command.ty);
        }
        bounds.push_back(box);
    }
    for (const auto expected :
         std::vector<std::array<float, 4>>{{{0, 0, 40, 20}}, {{50, 0, 70, 40}}, {{0, 50, 20, 70}}})
    {
        bool found = false;
        for (const auto actual : bounds)
        {
            bool match = true;
            for (int i = 0; i < 4; ++i)
            {
                match &= std::abs(actual[i] - expected[i]) < .001f;
            }
            found |= match;
        }
        assert(found);
    }
    urui_surface_destroy(g_test_surface);
    std::cout << "Standard image origin, size, position, repetition and shorthand reset passed\n";
}
