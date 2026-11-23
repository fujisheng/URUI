#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "ElementStyle.h"
#include <URUI/Core.h>
#include <algorithm>
#include <cassert>
#include <chrono>
#include <iostream>
#include <filesystem>
#include <tuple>
using namespace URUI;
static constexpr auto PaintFontFamily = "paint-benchmark-family-with-a-long-name";
static auto FontPath()
{
    return (std::filesystem::path(__FILE__).parent_path() / "fixtures/fonts/LatoLatin-Regular.ttf").string();
}

static auto StableValues(const Style::ComputedValues& values)
{
    return std::make_tuple(values.font_size(), values.font_family(), values.font_face_handle(),
                           values.line_height().value, values.width().value, values.height().value,
                           values.border_top_width(), values.display(), values.position());
}

static void Contracts(Context& context)
{
    auto* document = context.LoadDocumentFromMemory(
        "<!doctype html><html><head><style>.target{font-family:paint-benchmark-family-with-a-long-name;font-size:20px;line-height:1.5;width:8em;height:25px;border:3px solid red;position:absolute;color:#112233;background-color:#445566;}.child{color:inherit;}</style></head><body><div id='parent'><div class='target' id='target'><span class='child' id='child'></span></div></div></body></html>",
        "/paint-update.html");
    assert(document);
    document->Show();
    context.Update();
    auto* element = document->GetElementById("target");
    auto* child = document->GetElementById("child");
    const auto before = StableValues(element->GetComputedValues());
    assert(element->SetProperty("opacity", ".25"));
    context.Update();
    assert(element->GetComputedValues().opacity() == .25f && StableValues(element->GetComputedValues()) == before);
    assert(element->SetProperty("color", "#abcdef"));
    context.Update();
    assert(element->GetComputedValues().color() == Colourb(171, 205, 239));
    assert(child->GetComputedValues().color() == element->GetComputedValues().color());
    assert(StableValues(element->GetComputedValues()) == before);
    assert(element->SetProperty("background-color", "#123456"));
    context.Update();
    assert(element->GetComputedValues().background_color() == Colourb(18, 52, 86));
    assert(StableValues(element->GetComputedValues()) == before);
    element->RemoveProperty("opacity");
    context.Update();
    assert(element->GetComputedValues().opacity() == 1.f);
    assert(element->SetProperty("color", "inherit"));
    context.Update();
    assert(element->GetComputedValues().color() == document->GetElementById("parent")->GetComputedValues().color());
    auto* parent = document->GetElementById("parent");
    assert(parent->SetProperty("color", "#789abc"));
    context.Update();
    assert(element->SetProperty("color", "var(--missing-color)"));
    context.Update();
    assert(element->GetComputedValues().color() == Colourb(120, 154, 188));
    assert(child->GetComputedValues().color() == element->GetComputedValues().color());
    assert(parent->SetProperty("background-color", "#123456"));
    assert(element->SetProperty("background-color", "inherit"));
    context.Update();
    assert(element->GetComputedValues().background_color() == Colourb(18, 52, 86));
    assert(element->SetProperty("background-color", "var(--missing-color)"));
    context.Update();
    assert(element->GetComputedValues().background_color().alpha == 0);
    assert(element->SetProperty("font-size", "30px"));
    assert(element->SetProperty("opacity", ".75"));
    context.Update();
    assert(element->GetComputedValues().font_size() == 30 && element->GetComputedValues().width().value == 240);
    assert(element->GetBox().GetSize(BoxArea::Content).x == 240);
    assert(element->SetProperty("--paint-color", "#aabbcc"));
    assert(element->SetProperty("color", "var(--paint-color)"));
    context.Update();
    assert(element->SetProperty("--paint-color", "#001122"));
    context.Update();
    assert(element->GetComputedValues().color() == Colourb(0, 17, 34) &&
           child->GetComputedValues().color() == Colourb(0, 17, 34));
    document->Close();
    context.Update();
}

static double UpdateCost(Context& context, bool dense)
{
    auto* document = context.LoadDocumentFromMemory(
        "<!doctype html><html><head></head><body><div id='target'></div></body></html>", "/paint-cost.html");
    assert(document);
    auto* element = document->GetElementById("target");
    if (dense)
    {
        for (auto id : element->GetPropertySpecification().GetRegisteredProperties())
        {
            assert(element->GetStyle()->SetProperty(
                id, *element->GetPropertySpecification().GetProperty(id)->GetDefaultValue()));
        }
    }
    assert(element->SetProperty("font-family", PaintFontFamily));
    document->Show();
    context.Update();
    assert(element->GetComputedValues().font_face_handle() != 0);
    auto& values = const_cast<Style::ComputedValues&>(element->GetComputedValues());
    const auto* parent = &element->GetParentNode()->GetComputedValues();
    const auto* root = &document->GetComputedValues();
    Property low = *element->GetPropertySpecification().GetProperty(PropertyId::Opacity)->GetDefaultValue();
    low.value = .25f;
    Property high = low;
    high.value = .75f;
    const auto start = std::chrono::steady_clock::now();
    for (int n = 0; n < 20000; ++n)
    {
        assert(element->GetStyle()->SetProperty(PropertyId::Opacity, n % 2 ? low : high));
        element->GetStyle()->ComputeValues(values, parent, root, false, 1.f, {128, 128});
    }
    const double elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
    document->Close();
    context.Update();
    return elapsed;
}
int main()
{
    assert(urui_surface_create(128, 128, &g_test_surface) == 1);
    auto* context = GetContext(0);
    assert(context);
    assert(LoadFontFace(FontPath(), PaintFontFamily, Style::FontStyle::Normal, Style::FontWeight::Normal));
    Contracts(*context);
    auto* delayed = context->LoadDocumentFromMemory(
        "<!doctype html><html><head></head><body><div id='target' style='font-family:paint-delayed-family;'></div></body></html>",
        "/delayed-paint-font.html");
    assert(delayed);
    delayed->Show();
    context->Update();
    auto* target = delayed->GetElementById("target");
    assert(target->GetComputedValues().font_face_handle() == 0);
    assert(LoadFontFace(FontPath(), "paint-delayed-family", Style::FontStyle::Normal, Style::FontWeight::Normal));
    assert(target->SetProperty("opacity", ".5"));
    context->Update();
    assert(target->GetComputedValues().font_face_handle() != 0);
    delayed->Close();
    context->Update();
    double sparse[3];
    double dense[3];
    for (int n = 0; n < 3; ++n)
    {
        sparse[n] = UpdateCost(*context, false);
        dense[n] = UpdateCost(*context, true);
    }
    std::sort(sparse, sparse + 3);
    std::sort(dense, dense + 3);
    const auto ratio = dense[1] / sparse[1];
    std::cout << "opacity dense/sparse ratio=" << ratio << std::endl;
    // 独立绘制属性的更新不应随无关局部样式数量线性增长。
    assert(ratio < 2.0);
    urui_surface_destroy(g_test_surface);
}
