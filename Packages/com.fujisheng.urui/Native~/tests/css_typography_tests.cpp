#include "urui.h"
#include <URUI/Core.h>
#include <cassert>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <vector>

static std::string Read(const std::filesystem::path& path)
{
    std::ifstream stream(path, std::ios::binary);
    assert(stream);
    return std::string(std::istreambuf_iterator<char>(stream), {});
}

int main()
{
    const auto native = std::filesystem::path(__FILE__).parent_path().parent_path();
    const auto package = native.parent_path();
    const auto fixtures = package / "Tests~/HtmlCss/fixtures";
    const auto resources = package / "Runtime/Resources/URUI";
    assert(urui_create(1280, 720) == 1);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(&owner) == 1);
    auto register_file = [&](const std::string& uri, const std::filesystem::path& path)
    {
        const auto bytes = Read(path);
        assert(urui_document_register_resource(owner, uri.c_str(), reinterpret_cast<const uint8_t*>(bytes.data()),
                                               int(bytes.size())) == 1);
    };
    register_file("/urui/base.css", resources / "base.css");
    register_file("/fixtures/common.css", fixtures / "common.css");
    for (const auto& font : std::filesystem::directory_iterator(resources / "Fonts"))
    {
        if (font.path().extension() == ".bytes")
        {
            register_file("/urui/Fonts/" + font.path().filename().string(), font.path());
        }
    }
    assert(urui_document_set_presentation(owner, 0, 0, 1, 0) == 1);
    assert(urui_document_load_html(owner, Read(fixtures / "typography.html").c_str(), "/fixtures/typography.html") ==
           1);
    urui_update();
    urui_render();
    auto* document = URUI::GetContext("urui")->GetDocument(0);
    // Edge157，完全相同 typography.html/base.css/原始字体；完整输入 SHA 在 T13 采集清单。
    std::istringstream golden(Read(native / "tests/fixtures/typography-browser.tsv"));
    std::string id;
    float width;
    float height;
    int count;
    int failures = 0;
    int rows = 0;
    while (golden >> id >> width >> height >> count)
    {
        auto* element = document->GetElementById(id);
        assert(element);
        const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
        if (std::abs(size.x - width) > .5f || std::abs(size.y - height) > .5f)
        {
            std::cerr << id << " box browser=" << width << ',' << height << " native=" << size.x << ',' << size.y
                      << '\n';
            ++failures;
        }
        std::vector<float> lines, baselines;
        for (int i = 0; i < element->GetNumChildren(); ++i)
        {
            if (auto* text = urui_dynamic_cast<URUI::ElementText*>(element->GetChild(i)))
            {
                for (const auto& line : text->GetLines())
                {
                    if (line.width > 0)
                    {
                        lines.push_back(float(line.width));
                        baselines.push_back(text->GetAbsoluteOffset().y + line.position.y);
                    }
                }
            }
        }
        assert(int(lines.size()) == count);
        for (int i = 0; i < count; ++i)
        {
            float expected;
            float baseline;
            golden >> expected >> baseline;
            assert(golden);
            if (std::abs(baselines[i] - baseline) > .5f)
            {
                std::cerr << id << " baseline browser=" << baseline << " native=" << baselines[i] << '\n';
                ++failures;
            }
            if (std::abs(lines[i] - expected) > .5f)
            {
                std::cerr << id << " line" << i << " browser=" << expected << " native=" << lines[i] << '\n';
                ++failures;
            }
        }
        rows++;
    }
    assert(rows == 8);
    for (const auto* name : {"fractional", "fractional-small"})
    {
        const auto& values = document->GetElementById(name)->GetComputedValues();
        const auto& metrics = URUI::GetFontEngineInterface()->GetFontMetrics(values.font_face_handle());
        if (std::abs(float(metrics.size) - values.font_size()) > 1.f / 64.f)
        {
            std::cerr << name << " requested=" << values.font_size() << " font face=" << metrics.size << '\n';
            ++failures;
        }
    }
    auto* fractional = document->GetElementById("fractional");
    const auto fractional_handle = fractional->GetComputedValues().font_face_handle();
    assert(fractional->SetProperty("font-size", "16.25px"));
    urui_update();
    urui_render();
    const auto quarter_handle = fractional->GetComputedValues().font_face_handle();
    assert(quarter_handle != fractional_handle);
    assert(std::abs(URUI::GetFontEngineInterface()->GetFontMetrics(quarter_handle).size - 16.25f) < .001f);
    assert(fractional->SetProperty("font-size", "16.5001px"));
    urui_update();
    urui_render();
    assert(fractional->GetComputedValues().font_face_handle() == fractional_handle);
    auto* regular = document->GetElementById("regular");
    const auto original_width = regular->GetBox().GetSize(URUI::BoxArea::Border).x;
    assert(regular->SetProperty("letter-spacing", ".25px"));
    urui_update();
    urui_render();
    assert(std::abs(regular->GetBox().GetSize(URUI::BoxArea::Border).x - original_width - 3.5f) < .05f);
    urui_destroy();
    return failures ? 1 : 0;
}
