#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <URUI/Core/Elements/ElementFormControlInput.h>
#include <cassert>
#include <cmath>
#include <fstream>
#include <iostream>
#include <iterator>
int main(int argc, char** argv)
{
    assert(argc == 2 && urui_surface_create(400, 400, &g_test_surface) == 1);
    std::ifstream input(argv[1]);
    const URUI::String html((std::istreambuf_iterator<char>(input)), {});
    assert(!html.empty());
    auto* document = URUI::GetContext(0)->LoadDocumentFromMemory(html, "ui/range.html");
    assert(document);
    const float expected[] = {50, 20, 6, 10, 2.25f, 50, 4, 1.5f};
    for (int i = 0; i < 8; ++i)
    {
        auto* range =
            urui_dynamic_cast<URUI::ElementFormControlInput*>(document->GetElementById("range" + std::to_string(i)));
        assert(range);
        const auto actual = std::stof(range->GetValue());
        std::cerr << "range" << i << ": " << actual << " expected " << expected[i] << '\n';
        assert(std::isfinite(actual) && std::abs(actual - expected[i]) < .0001f);
    }
    auto* range = urui_dynamic_cast<URUI::ElementFormControlInput*>(document->GetElementById("range6"));
    range->SetValue("5");
    assert(std::stof(range->GetValue()) == 4 && range->GetAttribute<URUI::String>("value", "") == "4");
    range->SetValue("6");
    assert(std::stof(range->GetValue()) == 7 && range->GetAttribute<URUI::String>("value", "") == "4");
    auto clone = range->Clone();
    auto* cloned = urui_dynamic_cast<URUI::ElementFormControlInput*>(clone.get());
    assert(cloned && std::stof(cloned->GetValue()) == 7 && cloned->GetAttribute<URUI::String>("value", "") == "4");
    range->SetAttribute("max", "5");
    assert(std::stof(range->GetValue()) == 4);
    clone.reset();
    auto* context = URUI::GetContext(0);
    auto* axis = context->LoadDocumentFromMemory(
        "<!doctype html><html><head></head><body><input id='vertical' type='range' data-urui-orientation='vertical'><input id='unsupported' type='range' orientation='vertical'></body></html>",
        "ui/direction.html");
    assert(axis);
    axis->Show();
    context->Update();
    auto dimensions = axis->GetElementById("vertical")->GetBox().GetSize();
    assert(dimensions.x == 16 && dimensions.y == 256);
    dimensions = axis->GetElementById("unsupported")->GetBox().GetSize();
    assert(dimensions.x == 256 && dimensions.y == 16);
    axis->GetElementById("vertical")->SetAttribute("data-urui-orientation", "horizontal");
    context->Update();
    dimensions = axis->GetElementById("vertical")->GetBox().GetSize();
    assert(dimensions.x == 256 && dimensions.y == 16);
    auto* numbers = context->LoadDocumentFromMemory(
        "<!doctype html><html><head></head><body><input id='number' type='number' value='1.25'></body></html>",
        "ui/number.html");
    assert(numbers);
    auto* number = urui_dynamic_cast<URUI::ElementFormControlInput*>(numbers->GetElementById("number"));
    assert(number && number->GetValue() == "1.25");
    number->SetValue("4");
    assert(number->GetValue() == "4" && number->GetAttribute<URUI::String>("value", "") == "1.25");
    number->SetAttribute("value", "100");
    assert(number->GetValue() == "4");
    auto number_clone = number->Clone();
    auto* cloned_number = urui_dynamic_cast<URUI::ElementFormControlInput*>(number_clone.get());
    assert(cloned_number && cloned_number->GetValue() == "4" &&
           cloned_number->GetAttribute<URUI::String>("value", "") == "100");
    number_clone.reset();
    urui_surface_destroy(g_test_surface);
    std::cout << "Standard range defaults, bounds, step/any, authored step base and cloning passed\n";
}
