#include "urui.h"
#include "test_document.h"
#include <URUI/Core.h>
#include <cassert>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <vector>

int main()
{
    const auto fixtures = std::filesystem::path(__FILE__).parent_path() / "fixtures";
    std::ifstream input(fixtures / "layout.html");
    const URUI::String html((std::istreambuf_iterator<char>(input)), {});
    std::ifstream reference(fixtures / "layout-browser.tsv");
    assert(!html.empty() && reference);
    assert(urui_create(400, 400) == 1);
    auto* context = URUI::GetContext("urui");
    auto* document = context->LoadDocumentFromMemory(html, (fixtures / "layout.html").string());
    assert(document); document->Show(); context->Update();
    int failures = 0, count = 0, currentWidth = 400, currentFont = 16;
    URUI::String line;
    while (std::getline(reference, line)) {
        std::istringstream row(line);
        int width, font;
        URUI::String id;
        float expected[6];
        row >> width >> font >> id;
        for (auto& value : expected) row >> value;
        assert(row);
        if (width != currentWidth) { context->SetDimensions({width, 400}); currentWidth = width; }
        if (font != currentFont) { document->SetProperty("font-size", std::to_string(font) + "px"); currentFont = font; }
        context->Update();
        auto* element = document->GetElementById(id);
        assert(element);
        const auto offset = element->GetAbsoluteOffset(URUI::BoxArea::Border);
        const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
        const auto& style = element->GetComputedValues();
        const float actual[] = {offset.x, offset.y, size.x, size.y, style.font_size(), float(style.border_top_width())};
        const char* fields[] = {"x", "y", "width", "height", "font-size", "border-top"};
        for (int i = 0; i < 6; ++i) if (std::abs(actual[i] - expected[i]) > .5f) {
            std::cerr << width << '/' << font << ' ' << id << ' ' << fields[i] << ": browser=" << expected[i] << " urui=" << actual[i] << '\n';
            ++failures;
        }
        ++count;
    }
    assert(count == 39);
    if (document->GetComputedValues().color() != URUI::Colourb(0, 0, 0)) { std::cerr << "Default text color must be black\n"; ++failures; }
    std::ifstream whitespace_input(fixtures / "flex-whitespace.html");
    const URUI::String whitespace_html((std::istreambuf_iterator<char>(whitespace_input)), {});
    assert(!whitespace_html.empty());
    auto* whitespace = context->LoadDocumentFromMemory(whitespace_html, "ui/flex-space.html");
    assert(whitespace); whitespace->Show(); context->Update();
    auto* row = whitespace->GetElementById("row");
    assert(row->GetNumChildren() > 2); // 空白仍属于 DOM，只是不生成 flex item。
    assert(std::abs(whitespace->GetElementById("first")->GetAbsoluteOffset().x) < .01f);
    assert(std::abs(whitespace->GetElementById("last")->GetAbsoluteOffset().x - 20.f) < .01f);
    auto* text = urui_dynamic_cast<URUI::ElementText*>(row->GetChild(0));
    assert(text);
    text->SetText("\xc2\xa0"); context->Update(); // 不间断空格是内容，不可作为文档空白抛弃。
    assert(whitespace->GetElementById("last")->GetAbsoluteOffset().x > 20.f);
    text->SetText(" \t\r\n\f"); context->Update();
    assert(text->GetText() == " \t\r\n\f");
    assert(std::abs(whitespace->GetElementById("last")->GetAbsoluteOffset().x - 20.f) < .01f);
    document->Close(); whitespace->Close(); context->Update();
    std::ifstream visibility_input(fixtures / "visibility.html");
    const URUI::String visibility_html((std::istreambuf_iterator<char>(visibility_input)), {});
    const auto visibility_owner = LoadTestDocument(visibility_html.c_str(), "ui/visibility.html");
    auto* visibility = context->GetDocument(context->GetNumDocuments() - 1);
    auto* parent = visibility->GetElementById("parent");
    auto* inherited = visibility->GetElementById("inherited");
    auto* shown = visibility->GetElementById("shown");
    assert(inherited->GetComputedValues().visibility() == URUI::Style::Visibility::Hidden);
    assert(!parent->IsVisible(true) && !inherited->IsVisible(true));
    assert(shown->IsVisible(true) && shown->Focus());
    assert(!inherited->Focus());
    assert(visibility->FindNextTabElement(visibility, true) == shown);
    assert(visibility->FindNextTabElement(visibility, false) == shown);
    assert(!visibility->GetElementById("gone-child")->IsVisible(true));
    assert(context->GetElementAtPoint({30, 30}) == shown);
    assert(context->GetElementAtPoint({230, 20}) == visibility->GetElementById("layer-child"));
    assert(context->GetElementAtPoint({5, 5}) != parent);
    assert(context->GetElementAtPoint({5, 5}) != inherited);
    urui_update(); UruiPlanInputPacket input_frame{};assert(urui_plan_begin(&input_frame)==1);
    bool green = false, blue = false;
    for (uint32_t command=0;command<input_frame.command_count;command++) {
        const auto& draw=input_frame.commands[command];
        int vertices = 0, indices = 0;
        assert(urui_get_geometry_info(draw.geometry, &vertices, &indices) == 1);
        std::vector<UruiVertex> painted(vertices); std::vector<int> triangles(indices);
        assert(urui_copy_geometry(draw.geometry, painted.data(), vertices, triangles.data(), indices) == 1);
        const auto& tint=input_frame.uniforms[command].tint;
        for(auto& vertex:painted) {vertex.r=uint8_t(std::lround(vertex.r*tint[0]));vertex.g=uint8_t(std::lround(vertex.g*tint[1]));vertex.b=uint8_t(std::lround(vertex.b*tint[2]));vertex.a=uint8_t(std::lround(vertex.a*tint[3]));}
        for (const auto& vertex : painted) {
            assert(!(vertex.r == 255 && vertex.g == 0 && vertex.b == 0));
            green |= vertex.r == 0 && vertex.g == 255 && vertex.b == 0;
            blue |= vertex.r == 0 && vertex.g == 0 && vertex.b == 255;
        }
    }
    assert(green && blue);assert(urui_plan_release(input_frame.lease)==1);
    parent->SetProperty("display", "none"); context->Update();
    assert(!shown->IsVisible(true) && !shown->Focus());
    assert(context->GetElementAtPoint({30, 30}) != shown);
    parent->SetProperty("display", "block"); context->Update();
    assert(shown->IsVisible(true) && shown->Focus());
    assert(context->GetElementAtPoint({30, 30}) == shown);
    assert(urui_document_close(visibility_owner) == 1);
    urui_destroy();
    assert(failures == 0);
    std::cout << "Browser-referenced layout: " << count << " boxes passed (0.5 CSS px tolerance)\n";
}
