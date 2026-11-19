#include "test_document.h"
#include <URUI/Core.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <iostream>
#include <string>

static double Misses(int nodes, int mode)
{
    assert(urui_create(128, 128) == 1);
    std::string html =
        "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;left:0;top:0;width:4px;height:4px;}</style></head><body>";
    for (int n = 0; n < nodes; n++)
    {
        html += "<div></div>";
    }
    html += "</body></html>";
    LoadTestDocument(html.c_str(), "/hit-query.html");
    urui_update();
    auto* context = URUI::GetContext("urui");
    assert(context->AllowsNativeRecordingReuse());
    for (int n = 0; n < 8; n++)
    {
        assert(!context->GetElementAtPoint({300, 300}));
    }
    std::array<double, 5> times{};
    for (auto& time : times)
    {
        auto start = std::chrono::steady_clock::now();
        for (int n = 0; n < 128; n++)
        {
            assert(!context->GetElementAtPoint({300.f + (mode == 1   ? float(n % 3)
                                                         : mode == 2 ? float(n % 2) * .25f
                                                                     : 0.f),
                                                300}));
        }
        time = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - start).count() / 128;
    }
    std::sort(times.begin(), times.end());
    urui_destroy();
    return times[2];
}
static void Changes()
{
    assert(urui_create(128, 128) == 1);
    auto owner = LoadTestDocument(
        "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;width:20px;height:20px;}</style></head><body><div id='a'></div><div id='b'></div></body></html>",
        "/hit-mutations.html");
    urui_update();
    auto* context = URUI::GetContext("urui");
    auto* doc = context->GetDocument(0);
    auto* a = doc->GetElementById("a");
    auto* b = doc->GetElementById("b");
    auto hit = [&] { return context->GetElementAtPoint({5, 5}); };
    assert(hit() == b && hit() == b);
    b->SetEffectState(false, true);
    assert(hit() == a);
    b->SetEffectState(false, false);
    assert(hit() == b);
    b->SetOffset({30, 0}, b->GetOffsetParent());
    assert(hit() == a);
    assert(context->GetElementAtPoint({35, 5}) == b);
    b->SetOffset({0, 0}, b->GetOffsetParent());
    assert(hit() == b);
    assert(context->GetElementAtPoint({5, 5}, b) == a);
    assert(hit() == b);
    b->SetBox(URUI::Box({5.125f, 20}));
    for (int n = 0; n < 3; n++)
    {
        assert(hit() == b);
        assert(context->GetElementAtPoint({5.25f, 5}) == a);
    }

    assert(context->GetElementAtPoint({5, 5}, nullptr, a) == a);
    assert(hit() == b);
    b->SetProperty("visibility", "hidden");
    urui_update();
    assert(hit() == a);
    b->RemoveProperty("visibility");
    urui_update();
    assert(hit() == b);
    auto* parent = b->GetParentNode();
    auto removed = parent->RemoveChild(b);
    assert(hit() == a);
    removed.reset();
    assert(hit() == a);
    auto child = doc->CreateElement("div");
    auto* next = child.get();
    parent->AppendChild(std::move(child));
    urui_update();
    assert(hit() == next);
    doc->Hide();
    assert(hit() != next);
    doc->Show();
    urui_update();
    assert(hit() == next);
    assert(urui_document_close(owner) == 1);
    urui_update();
    assert(hit() != next);
    urui_destroy();
}
class HitProbe : public URUI::Element
{
public:
    static int calls;
    explicit HitProbe(const URUI::String& tag) : Element(tag)
    {
    }
    bool IsPointWithinElement(URUI::Vector2f) override
    {
        return ++calls % 2 == 0;
    }
};
int HitProbe::calls = 0;
static void UnknownHit()
{
    assert(urui_create(128, 128) == 1);
    URUI::ElementInstancerGeneric<HitProbe> instancer;
    URUI::Factory::RegisterElementInstancer("urui-hit-probe", &instancer);
    LoadTestDocument(
        "<!doctype html><html><head></head><body><urui-hit-probe id='probe'></urui-hit-probe></body></html>",
        "/opaque-hit.html");
    urui_update();
    auto* context = URUI::GetContext("urui");
    auto* probe = context->GetDocument(0)->GetElementById("probe");
    HitProbe::calls = 0;
    assert(context->GetElementAtPoint({5, 5}) != probe);
    assert(context->GetElementAtPoint({5, 5}) == probe);
    assert(context->GetElementAtPoint({5, 5}) != probe);
    assert(HitProbe::calls == 3);
    urui_destroy();
}
int main(int argc, char** argv)
{
    if (argc > 1 && std::string(argv[1]) == "changes")
    {
        Changes();
        return 0;
    }
    Changes();
    UnknownHit();
    const auto small = Misses(500, false);
    const auto large = Misses(4000, false);
    const auto moving = Misses(4000, true);
    std::cout << "stable500=" << small << "us stable4000=" << large << "us ratio=" << large / small
              << " moving4000=" << moving << "us" << std::endl;
    assert(large / small < 1.8);
    // 浮点输入分发与整数原生鼠标位置交替查询同一个静止指针。
    const auto pair_small = Misses(500, 2);
    const auto pair_large = Misses(4000, 2);
    std::cout << "paired500=" << pair_small << "us paired4000=" << pair_large << "us ratio=" << pair_large / pair_small
              << std::endl;
    assert(pair_large / pair_small < 1.8);
}
