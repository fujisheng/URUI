#include "test_document.h"
#include <URUI/Core.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string>

static const char* Markup =
    "<!doctype html><html><head><style>body{margin:0;}div{width:10px;height:10px;}.wide{width:70px;}</style></head><body><div id='box'></div></body></html>";
static void Warm(int count = 16)
{
    for (int n = 0; n < count; n++)
    {
        urui_update();
    }
}
static double Idle(int nodes, bool hidden_input)
{
    assert(urui_create(128, 128) == 1 && urui_set_ui_time(0) == 1);
    std::string html =
        "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;width:4px;height:4px;}</style></head><body>";
    for (int n = 0; n < nodes; n++)
    {
        html += "<div></div>";
    }
    html += "</body></html>";
    LoadTestDocument(html.c_str(), "/idle-many.html");
    auto* context = URUI::GetContext("urui");
    if (hidden_input)
    {
        LoadTestDocument("<!doctype html><html><head></head><body><input type='text'></body></html>",
                         "/hidden-input.html");
        context->GetDocument(1)->Hide();
    }
    Warm();
    std::array<double, 5> times{};
    for (auto& time : times)
    {
        const auto start = std::chrono::steady_clock::now();
        for (int n = 0; n < 128; n++)
        {
            urui_update();
        }
        time = std::chrono::duration<double, std::micro>(std::chrono::steady_clock::now() - start).count() / 128;
    }
    std::sort(times.begin(), times.end());
    urui_destroy();
    return times[2];
}
static void Mutations()
{
    assert(urui_create(128, 128) == 1 && urui_set_ui_time(0) == 1);
    LoadTestDocument(Markup, "/idle-mutations.html");
    auto* context = URUI::GetContext("urui");
    auto* doc = context->GetDocument(0);
    auto* box = doc->GetElementById("box");
    Warm();
    assert(box->GetOffsetWidth() == 10);
    box->SetClass("wide", true);
    urui_update();
    assert(box->GetOffsetWidth() == 70);
    Warm();
    box->SetClass("wide", false);
    urui_update();
    assert(box->GetOffsetWidth() == 10);
    Warm();
    box->SetProperty("width", "30px");
    urui_update();
    assert(box->GetOffsetWidth() == 30);
    Warm();
    box->RemoveProperty("width");
    urui_update();
    assert(box->GetOffsetWidth() == 10);
    Warm();
    auto child = doc->CreateElement("div");
    auto* added = child.get();
    box->AppendChild(std::move(child));
    urui_update();
    assert(added->GetOffsetWidth() == 10);
    Warm();
    added->SetProperty("width", "42px");
    urui_update();
    assert(added->GetOffsetWidth() == 42);
    Warm();
    auto removed = box->RemoveChild(added);
    removed.reset();
    urui_update();
    assert(box->GetNumChildren() == 0);
    Warm();
    box->SetOffset({30, 12}, box->GetOffsetParent());
    urui_update();
    assert(box->GetAbsoluteOffset().x == 30);
    urui_destroy();
}
class EndListener : public URUI::EventListener
{
public:
    int count = 0;
    URUI::ElementDocument* document = nullptr;
    void ProcessEvent(URUI::Event&) override
    {
        count++;
        // 动画结束时重入只读文档更新，不能让外层发布尚未完成的证明。
        if (document)
        {
            document->UpdateDocument();
        }
    }
};
static void DelayedAnimation(bool hidden)
{
    assert(urui_create(128, 128) == 1 && urui_set_ui_time(0) == 1);
    LoadTestDocument(Markup, "/idle-animation.html");
    auto* doc = URUI::GetContext("urui")->GetDocument(0);
    auto* box = doc->GetElementById("box");
    EndListener ended;
    ended.document = doc;
    box->AddEventListener("animationend", &ended);
    Warm();
    if (hidden)
    {
        doc->Hide();
    }
    assert(box->Animate("width", URUI::Property(70.f, URUI::Unit::PX), 1.f, URUI::Tween{}, 1, false, .5f));
    Warm();
    assert(ended.count == 0);
    assert(urui_set_ui_time(.25) == 1);
    Warm();
    assert(ended.count == 0);
    assert(urui_set_ui_time(.75) == 1);
    urui_update();
    assert(box->GetOffsetWidth() > 10 && box->GetOffsetWidth() < 70);
    assert(urui_set_ui_time(2) == 1);
    urui_update();
    assert(ended.count == 1);
    Warm();
    assert(ended.count == 1);
    urui_destroy();
}
static bool throw_tween;
static int tween_calls;
static float ThrowingTween(float value)
{
    tween_calls++;
    if (throw_tween)
    {
        throw_tween = false;
        throw std::runtime_error("idle update probe");
    }
    return value;
}
static void FailedUpdate()
{
    assert(urui_create(128, 128) == 1 && urui_set_ui_time(0) == 1);
    LoadTestDocument(Markup, "/idle-failed.html");
    auto* context = URUI::GetContext("urui");
    auto* box = context->GetDocument(0)->GetElementById("box");
    Warm();
    assert(box->Animate("width", URUI::Property(70.f, URUI::Unit::PX), 1.f, URUI::Tween(ThrowingTween), 1, false));
    throw_tween = true;
    tween_calls = 0;
    assert(urui_set_ui_time(.25) == 1);
    bool caught = false;
    try
    {
        context->Update();
    }
    catch (const std::runtime_error&)
    {
        caught = true;
    }
    assert(caught && tween_calls == 1);
    assert(urui_set_ui_time(.5) == 1);
    assert(context->Update());
    assert(tween_calls > 1);
    urui_destroy();
}
class TickProbe : public URUI::Element
{
public:
    static int calls;
    explicit TickProbe(const URUI::String& tag) : Element(tag)
    {
    }
    void OnUpdate() override
    {
        calls++;
    }
};
int TickProbe::calls = 0;
static void UnknownUpdate()
{
    assert(urui_create(128, 128) == 1 && urui_set_ui_time(0) == 1);
    URUI::ElementInstancerGeneric<TickProbe> instancer;
    URUI::Factory::RegisterElementInstancer("urui-tick-probe", &instancer);
    LoadTestDocument("<!doctype html><html><head></head><body><urui-tick-probe></urui-tick-probe></body></html>",
                     "/idle-unknown.html");
    auto* doc = URUI::GetContext("urui")->GetDocument(0);
    Warm();
    TickProbe::calls = 0;
    Warm(31);
    assert(TickProbe::calls == 31);
    doc->Hide();
    TickProbe::calls = 0;
    Warm(31);
    assert(TickProbe::calls == 31);
    urui_destroy();
}
static void NestedDocumentUpdate()
{
    assert(urui_create(128, 128) == 1 && urui_set_ui_time(0) == 1);
    URUI::ElementInstancerGeneric<TickProbe> instancer;
    URUI::Factory::RegisterElementInstancer("urui-tick-probe", &instancer);
    LoadTestDocument(Markup, "/idle-outer.html");
    auto* context = URUI::GetContext("urui");
    auto* outer = context->GetDocument(0);
    auto* middle = context->CreateDocument();
    auto* inner = context->CreateDocument();
    inner->AppendChild(inner->CreateElement("urui-tick-probe"));
    middle->AppendChild(context->GetRootElement()->RemoveChild(inner));
    outer->AppendChild(context->GetRootElement()->RemoveChild(middle));
    Warm();
    TickProbe::calls = 0;
    Warm(31);
    assert(TickProbe::calls == 31);
    inner->Hide();
    TickProbe::calls = 0;
    Warm(31);
    assert(TickProbe::calls == 31);
    outer->Hide();
    TickProbe::calls = 0;
    Warm(31);
    assert(TickProbe::calls == 31);
    urui_destroy();
}
int main(int argc, char** argv)
{
    Mutations();
    DelayedAnimation(false);
    DelayedAnimation(true);
    FailedUpdate();
    UnknownUpdate();
    NestedDocumentUpdate();
    if (argc > 1 && std::string(argv[1]) == "semantics")
    {
        return 0;
    }
    const auto small = Idle(500, false);
    const auto large = Idle(4000, false);
    std::cout << "idle500=" << small << "us idle4000=" << large << "us ratio=" << large / small << std::endl;
    assert(large / small < 1.8);
    const auto mixed_small = Idle(500, true);
    const auto mixed_large = Idle(4000, true);
    std::cout << "hidden-input500=" << mixed_small << "us hidden-input4000=" << mixed_large
              << "us ratio=" << mixed_large / mixed_small << std::endl;
    assert(mixed_large / mixed_small < 1.8);
}
