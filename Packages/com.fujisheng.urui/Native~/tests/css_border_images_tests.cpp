#include "urui.h"
#include <URUI/Core.h>
#include <cassert>
#include <cmath>
#include <vector>
#include <iostream>

static std::vector<URUIVertex> Vertices(uint64_t texture)
{
    urui_update();
    urui_render();
    std::vector<URUICommand> commands(urui_command_count());
    urui_get_commands(commands.data(), int(commands.size()));
    std::vector<URUIVertex> vertices;
    for (const auto& draw : commands)
    {
        if (draw.texture == texture)
        {
            assert((draw.flags & 6) == 0);
            int nv = 0;
            int ni = 0;
            assert(urui_get_geometry_info(draw.geometry, &nv, &ni) == 1);
            std::vector<URUIVertex> part(nv);
            std::vector<int> ids(ni);
            assert(urui_copy_geometry(draw.geometry, part.data(), nv, ids.data(), ni) == 1);
            vertices.insert(vertices.end(), part.begin(), part.end());
        }
    }
    return vertices;
}
int main()
{
    assert(urui_create(160, 160) == 1);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(&owner) == 1);
    uint8_t pixels[10 * 10 * 4];
    for (auto& byte : pixels)
    {
        byte = 255;
    }
    uint64_t texture = 0;
    assert(urui_document_register_rgba_texture(owner, "ui/border.png", 10, 10, pixels, sizeof(pixels), &texture) == 1);
    assert(
        urui_document_load_html(
            owner,
            "<!doctype html><html><head><style>body{margin:0;}#box{width:40px;height:20px;border:4px solid red;}</style></head><body><div id='box'></div></body></html>",
            "ui/border.html") == 1);
    assert(urui_document_set_presentation(owner, 0, 0, 1, 0) == 1);
    assert(urui_set_style(owner, "box", "border-image", "url(border.png) 2 fill / 3px / 1px stretch") == 1);
    auto vertices = Vertices(texture);
    assert(vertices.size() == 36);
    float minx = 1e9f;
    float maxx = -1e9f;
    bool inner = false;
    for (const auto& v : vertices)
    {
        minx = std::min(minx, v.x);
        maxx = std::max(maxx, v.x);
        inner |= std::abs(v.x - 2) < .001f;
    }
    assert(std::abs(minx + 1) < .001f && std::abs(maxx - 49) < .001f && inner);
    assert(urui_set_style(owner, "box", "border-image-slice", "20%") == 1);
    vertices = Vertices(texture);
    // 不含 fill，八块边框。
    assert(vertices.size() == 32);
    assert(urui_set_style(owner, "box", "border-image-width", "auto") == 1);
    vertices = Vertices(texture);
    inner = false;
    for (const auto& v : vertices)
    {
        inner |= std::abs(v.x - 1) < .001f;
    }
    assert(inner);
    auto* context = URUI::GetContext("urui");
    context->SetDensityIndependentPixelRatio(2);
    auto scaled = Vertices(texture);
    assert(scaled.size() == vertices.size());
    for (size_t i = 0; i < vertices.size(); ++i)
    {
        assert(vertices[i].x == scaled[i].x && vertices[i].u == scaled[i].u);
    }
    assert(urui_set_style(owner, "box", "border", "1px solid blue") == 1);
    // border 简写也重置 border-image。
    assert(Vertices(texture).empty());
    assert(urui_set_style(owner, "box", "border-image", "url(border.png) 2 / 2") == 1);
    vertices = Vertices(texture);
    inner = false;
    for (const auto& v : vertices)
    {
        inner |= std::abs(v.x - 2) < .001f;
    }
    assert(inner);
    assert(urui_set_style(owner, "box", "border-image-width", "100px") == 1);
    vertices = Vertices(texture);
    for (const auto& v : vertices)
    {
        assert(v.x >= 0 && v.x <= 42 && v.y >= 0 && v.y <= 22);
    }
    URUI::PropertyDictionary parsed;
    auto& spec = URUI::StyleSheetSpecification::GetPropertySpecification();
    assert(!spec.ParsePropertyDeclaration(parsed, "border-image-slice", "2px"));
    assert(!spec.ParsePropertyDeclaration(parsed, "border-image-width", "-1"));
    assert(!spec.ParsePropertyDeclaration(parsed, "border-image-outset", "10%"));
    assert(!spec.ParsePropertyDeclaration(parsed, "border-image-source", "url(a.png),url(b.png)"));
    assert(!spec.ParsePropertyDeclaration(parsed, "border-image", "url(a.png) 2 / / 1 2 3 4 5"));
    assert(urui_set_style(owner, "box", "border-image", "url(border.png) 2 fill / 3px stretch") == 1);
    assert(urui_set_style(owner, "box", "--urui-image-effect", "border-probe") == 1);
    auto paint = [&]()
    {
        urui_update();
        urui_render();
        std::vector<URUIPaintCommand> values(urui_get_paint_commands(nullptr, 0));
        urui_get_paint_commands(values.data(), int(values.size()));
        assert(values.size() == 1 && values[0].reserved == 1);
        std::vector<URUICommand> draws(urui_command_count());
        urui_get_commands(draws.data(), int(draws.size()));
        const auto& draw = draws[values[0].command_index];
        assert(draw.texture == texture);
        int nv = 0;
        int ni = 0;
        assert(urui_get_geometry_info(draw.geometry, &nv, &ni) == 1);
        // 直接复用九宫格网格，没有额外覆盖图。
        assert(nv == 36 && ni == 54);
        return values[0].shader;
    };
    const auto shader = paint();
    assert(paint() == shader);
    assert(urui_set_style(owner, "box", "--urui-image-effect", "border-next") == 1);
    assert(paint() != shader);
    assert(urui_set_style(owner, "box", "--urui-image-effect", "none") == 1);
    urui_update();
    urui_render();
    assert(urui_get_paint_commands(nullptr, 0) == 0);
    urui_destroy();
    std::cout << "Standard border image slices, widths, outsets, fill and border reset passed\n";
}
