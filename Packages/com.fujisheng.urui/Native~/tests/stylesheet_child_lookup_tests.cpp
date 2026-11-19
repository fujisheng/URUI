#include "urui.h"
#include "StyleSheetNode.h"
#include <URUI/Core/Property.h>
#include <algorithm>
#include <cassert>
#include <chrono>
#include <iostream>

using namespace URUI;

static CompoundSelector Selector(int index)
{
    CompoundSelector result;
    result.class_names.push_back("rule-" + std::to_string(index));
    return result;
}

static void IdentityAndCopies()
{
    StyleSheetNode root;
    for (int index = 0; index < 128; index++)
    {
        root.GetOrCreateChildNode(Selector(index));
    }
    const auto selector = Selector(91);
    auto* first = root.GetOrCreateChildNode(selector);
    auto moved = selector;
    assert(root.GetOrCreateChildNode(std::move(moved)) == first);
    PropertyDictionary properties;
    properties.SetProperty(PropertyId::Width, Property(37.f, Unit::PX));
    first->ImportProperties(properties, 0);
    auto copy = root.DeepCopy();
    auto* copied = copy->GetOrCreateChildNode(selector);
    assert(copied != first);
    const auto* width = copied->GetProperties().GetProperty(PropertyId::Width);
    assert(width && width->Get<float>() == 37.f);
    auto* appended = copy->GetOrCreateChildNode(Selector(500));
    assert(copy->GetOrCreateChildNode(Selector(500)) == appended);

    // 候选特征相同不代表选择器相等；结构伪类仍须完整比较。
    auto nth = Selector(91);
    nth.structural_selectors.emplace_back(StructuralSelectorType::Nth_Child, 2, 1);
    auto other = nth;
    other.structural_selectors[0].b = 0;
    auto* odd = root.GetOrCreateChildNode(nth);
    auto* even = root.GetOrCreateChildNode(other);
    assert(odd != even && odd != first);
    assert(root.GetOrCreateChildNode(nth) == odd);
    assert(root.GetOrCreateChildNode(other) == even);

    auto one = Selector(91);
    auto two = Selector(91);
    one.structural_selectors.emplace_back(StructuralSelectorType::Not, MakeShared<SelectorTree>(), Specificity{});
    two.structural_selectors.emplace_back(StructuralSelectorType::Not, MakeShared<SelectorTree>(), Specificity{});
    assert(root.GetOrCreateChildNode(one) != root.GetOrCreateChildNode(two));
}

static double ConstructAndFind(int count)
{
    StyleSheetNode root;
    const auto start = std::chrono::steady_clock::now();
    for (int index = 0; index < count; index++)
    {
        root.GetOrCreateChildNode(Selector(index));
    }
    for (int index = count - 1; index >= 0; index--)
    {
        root.GetOrCreateChildNode(Selector(index));
    }
    return std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
}

int main()
{
    assert(urui_create(100, 100) == 1);
    IdentityAndCopies();
    ConstructAndFind(200);
    double small[3];
    double large[3];
    for (int repeat = 0; repeat < 3; repeat++)
    {
        small[repeat] = ConstructAndFind(1500);
        large[repeat] = ConstructAndFind(6000);
    }
    std::sort(std::begin(small), std::end(small));
    std::sort(std::begin(large), std::end(large));
    const auto ratio = large[1] / small[1];
    std::cout << "Stylesheet child lookup growth 1500 -> 6000: " << ratio << std::endl;
    assert(ratio < 9 && "Selector table construction and lookup must not grow quadratically");
    urui_destroy();
}
