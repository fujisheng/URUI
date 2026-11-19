#include "urui.h"
#include <URUI/Core.h>
#include <cassert>
#include <fstream>
#include <iterator>
#include <iostream>
#include <vector>

int main(int argc, char** argv)
{
    assert(argc == 2 && urui_create(400, 200) == 1);
    const char* families[] = {"LiberationSerif", "LiberationSans", "LiberationMono"};
    const char* faces[] = {"Regular", "Italic", "Bold", "BoldItalic"};
    std::vector<uint8_t> regular_serif;
    for (int family = 0; family < 3; ++family)
    {
        for (int face = 0; face < 4; ++face)
        {
            const std::string path = std::string(argv[1]) + "/" + families[family] + "-" + faces[face] + ".ttf.bytes";
            std::ifstream input(path, std::ios::binary);
            std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(input)), {});
            assert(!bytes.empty());
            assert(urui_surface_set_default_font_face(family, bytes.data(), int(bytes.size()), 0, face % 2,
                                                      face < 2 ? 400 : 700) == 1);
            if (family == 0 && face == 0)
            {
                regular_serif = bytes;
            }
        }
    }
    const uint8_t invalid[] = {0, 1, 2};
    assert(urui_surface_set_default_font_face(0, invalid, sizeof(invalid), 0, 0, 400) < 0);
    auto* context = URUI::GetContext("urui");
    const URUI::String mono = URUI::String(argv[1]) + "/LiberationMono-Regular.ttf.bytes";
    const URUI::String regular = URUI::String(argv[1]) + "/LiberationSerif-Regular.ttf.bytes";
    const URUI::String bold = URUI::String(argv[1]) + "/LiberationSerif-Bold.ttf.bytes";
    auto* document = context->LoadDocumentFromMemory(
        "<!doctype html><html><head><style>@font-face{font-family:'serif';src:url('" + mono +
            "');}"
            "@font-face{font-family:Author;src:url('" +
            regular +
            "');font-weight:400;}"
            "@font-face{font-family:Author;src:url('" +
            bold +
            "');font-weight:700;}"
            "#serif{font-family:serif;}#sans{font-family:sans-serif;}#mono{font-family:monospace;}"
            "#unknown{font-family:UnavailableFont,sans-serif;}#quoted{font-family:'serif';}"
            "#bold{font-weight:bold;}#italic{font-style:italic;}"
            "#w500{font-weight:500;}#w501{font-weight:501;}#w550{font-weight:550;}"
            "#i550{font-weight:550;font-style:italic;}#ibold{font-weight:700;font-style:italic;}"
            "#a400{font-family:Author;font-weight:400;}#a501{font-family:Author;font-weight:501;}#a700{font-family:Author;font-weight:700;}"
            "</style></head><body><div id='serif'>Serif</div><div id='sans'>Sans</div><div id='mono'>Mono</div>"
            "<div id='unknown'>Fallback</div><div id='quoted'>Quoted</div><div id='bold'>Bold</div><div id='italic'>Italic</div>"
            "<div id='w500'>500</div><div id='w501'>501</div><div id='w550'>550</div><div id='i550'>Italic550</div><div id='ibold'>BoldItalic</div>"
            "<div id='a400'>Author400</div><div id='a501'>Author501</div><div id='a700'>Author700</div></body></html>",
        "ui/default-font.html");
    assert(document);
    document->Show();
    context->Update();
    const auto root_face = document->GetComputedValues().font_face_handle();
    auto Face = [&](const char* id) { return document->GetElementById(id)->GetComputedValues().font_face_handle(); };
    assert(root_face && Face("serif") == root_face && Face("sans") && Face("mono"));
    assert(Face("sans") != root_face && Face("mono") != Face("sans"));
    assert(Face("unknown") == Face("sans") && Face("quoted") == Face("mono"));
    assert(Face("bold") && Face("bold") != root_face && Face("italic") && Face("italic") != root_face);
    assert(Face("w500") == root_face && Face("w501") == Face("bold") && Face("w550") == Face("bold"));
    assert(Face("i550") == Face("ibold") && Face("i550") != Face("italic"));
    assert(Face("a501") == Face("a700") && Face("a501") != Face("a400"));
    assert(urui_surface_set_default_font_face(0, regular_serif.data(), int(regular_serif.size()), 0, 0, 400) == -5);
    urui_render();
    assert(urui_command_count() > 0);
    document->Close();
    context->Update();
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(&owner) == 1);
    assert(
        urui_document_load_html(
            owner,
            "<!doctype html><html><head><style>body{margin:0;}#glyph{position:absolute;left:20px;top:20px;font-family:sans-serif;font-size:40px;color:#4080c080;}</style></head><body><span id='glyph'>Weight</span></body></html>",
            "ui/weight.html") == 1);
    assert(urui_document_set_presentation(owner, 0, 0, 1, 0) == 1);
    uint64_t last_texture = 0;
    int font_stage = 0;
    auto bounds = [&]()
    {
        urui_update();
        urui_render();
        ++font_stage;
        if (urui_command_count() != 1)
        {
            std::cerr << "font stage " << font_stage << " draw commands " << urui_command_count() << '\n';
        }
        // 膨胀替换普通单色字形，不增加绘制。
        assert(urui_command_count() == 1);
        URUICommand command{};
        assert(urui_get_commands(&command, 1) == 1);
        last_texture = command.texture;
        int nv = 0;
        int ni = 0;
        assert(urui_get_geometry_info(command.geometry, &nv, &ni) == 1);
        std::vector<URUIVertex> vertices(nv);
        std::vector<int> indices(ni);
        assert(urui_copy_geometry(command.geometry, vertices.data(), nv, indices.data(), ni) == 1);
        URUI::Vector2f lo(1e6f, 1e6f), hi(-1e6f, -1e6f);
        for (const auto& vertex : vertices)
        {
            assert(vertex.r == 32 && vertex.g == 64 && vertex.b == 96 && vertex.a == 128);
            lo.x = std::min(lo.x, vertex.x);
            lo.y = std::min(lo.y, vertex.y);
            hi.x = std::max(hi.x, vertex.x);
            hi.y = std::max(hi.y, vertex.y);
        }
        return hi - lo;
    };
    const auto plain = bounds();
    assert(urui_set_style(owner, "glyph", "--urui-font-effect", "dilate(0.5px)") == 1);
    const auto expanded = bounds();
    assert(expanded.x > plain.x && expanded.y > plain.y);
    auto alpha_sum = [&]()
    {
        int width = 0;
        int height = 0;
        int bytes = 0;
        assert(urui_get_texture_info(last_texture, &width, &height, &bytes) == 1);
        std::vector<uint8_t> pixels(bytes);
        assert(urui_copy_texture(last_texture, pixels.data(), bytes) == 1);
        uint64_t sum = 0;
        for (int i = 3; i < bytes; i += 4)
        {
            sum += pixels[i];
        }
        return sum;
    };
    const auto half_pixel_alpha = alpha_sum();
    assert(urui_set_style(owner, "glyph", "--urui-font-effect", "dilate(0.0125em)") == 1);
    assert(bounds() == expanded);
    assert(alpha_sum() == half_pixel_alpha);
    assert(urui_set_style(owner, "glyph", "--urui-font-effect", "none") == 1);
    assert(bounds() == plain);
    urui_destroy();
    std::cout << "Packaged default fonts, family matching, styles and frozen Surface bootstrap passed\n";
}
