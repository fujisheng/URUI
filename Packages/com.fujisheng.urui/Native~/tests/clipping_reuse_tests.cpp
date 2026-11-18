#include "test_document.h"
#include <URUI/Core.h>
#include <URUI/Core/ElementUtilities.h>
#include <URUI/Core/RenderManager.h>
#include <URUI/Core/ElementInstancer.h>
#include <algorithm>
#include <cassert>
#include <chrono>
#include <iostream>
#include <string>
#include <vector>

class ClipMutationElement final : public URUI::Element
{
public:
    static int mode;
    static bool armed, observed;
    static URUI::Rectanglei result;
    static size_t masks;
    explicit ClipMutationElement(const URUI::String& tag) : URUI::Element(tag) {}
    void OnRender() override
    {
        if (GetId() == "observe")
        {
            const auto& state = GetContext()->GetRenderManager().GetState();
            result = state.scissor_region;
            masks = state.clip_mask_list.size();
            observed = true;
        }
        if (GetId() != "mutate" || !armed) return;
        armed = false;
        auto* p = GetParentNode();
        switch (mode)
        {
        case 1: p->SetOffset({30, 0}, p->GetOffsetParent()); break;
        case 2: p->SetBox(URUI::Box({40, 30})); break;
        case 3: p->SetClipArea(URUI::BoxArea::Content); break;
        case 4: p->SetScrollableOverflowRectangle({160, 140}, false); break;
        case 5: p->GetParentNode()->SetScrollTop(10); break;
        case 6:
            assert(p->SetProperty("overflow", "visible"));
            GetOwnerDocument()->UpdateDocument(); break;
        case 7:
            assert(p->SetProperty("width", "40px"));
            GetOwnerDocument()->UpdateDocument(); break;
        case 8:
            assert(p->SetProperty("border-radius", "8px"));
            GetOwnerDocument()->UpdateDocument(); break;
        case 9:
            assert(p->SetProperty("transform", "translate(5px,0px)"));
            GetOwnerDocument()->UpdateDocument(); assert(GetContext()->SynchronizeGeometry()); break;
        }
    }
};
int ClipMutationElement::mode = 0;
bool ClipMutationElement::armed = false, ClipMutationElement::observed = false;
URUI::Rectanglei ClipMutationElement::result;
size_t ClipMutationElement::masks = 0;

static void RenderMutation(int mode)
{
    assert(urui_create(640, 480) == 1);
    URUI::ElementInstancerGeneric<ClipMutationElement> instancer;
    URUI::Factory::RegisterElementInstancer("urui-clip-probe", &instancer);
    const std::string css = mode == 3 ? "#p{box-sizing:border-box;padding:10px;border:5px solid transparent;}" :
        mode == 4 ? "#p{overflow:auto;scrollbar-width:none;}" : "";
    const std::string html = "<!doctype html><html><head><style>body{margin:0;}#outer{position:relative;width:200px;height:150px;overflow:hidden;}#p{position:relative;width:80px;height:70px;overflow:hidden;}urui-clip-probe{display:block;position:absolute;width:20px;height:20px;}" + css +
        "</style></head><body><section id='outer'><section id='p'><urui-clip-probe id='mutate'></urui-clip-probe><urui-clip-probe id='observe'></urui-clip-probe></section></section></body></html>";
    LoadTestDocument(html.c_str(), "/clip-render-mutation.html");
    urui_update();
    if (mode == 5) URUI::GetContext("urui")->GetDocument(0)->GetElementById("outer")->SetScrollableOverflowRectangle({400, 300}, false);
    ClipMutationElement::mode = mode;
    ClipMutationElement::observed = false;
    ClipMutationElement::armed = true;
    urui_render();
    assert(ClipMutationElement::observed && !ClipMutationElement::armed);
    const auto expected = mode == 1 ? URUI::Rectanglei::FromPositionSize({30, 0}, {80, 70}) :
        mode == 2 ? URUI::Rectanglei::FromSize({40, 30}) :
        mode == 3 ? URUI::Rectanglei::FromPositionSize({15, 15}, {50, 40}) :
        mode == 5 ? URUI::Rectanglei::FromSize({80, 60}) :
        mode == 6 || mode == 9 ? URUI::Rectanglei::FromSize({200, 150}) :
        mode == 7 ? URUI::Rectanglei::FromSize({40, 70}) : URUI::Rectanglei::FromSize({80, 70});
    std::cout << "mutation=" << mode << " expected=" << expected.Left() << ',' << expected.Top() << ',' << expected.Width() << ',' << expected.Height()
              << " got=" << ClipMutationElement::result.Left() << ',' << ClipMutationElement::result.Top() << ',' << ClipMutationElement::result.Width() << ',' << ClipMutationElement::result.Height() << std::endl;
    assert(ClipMutationElement::result == expected);
    if (mode == 8 || mode == 9) assert(ClipMutationElement::masks == 1);
    urui_destroy();
}

static double Measure(int depth)
{
    assert(urui_create(2048, 2048) == 1);
    std::string html = "<!doctype html><html><head><style>body{margin:0;}.clip{position:relative;width:1024px;height:1024px;overflow:hidden;}.leaf{position:absolute;width:1px;height:1px;background:green;}</style></head><body>";
    for (int index = 0; index < depth; ++index) html += "<div class='clip'>";
    for (int index = 0; index < 2000; ++index) html += "<div class='leaf'></div>";
    for (int index = 0; index < depth; ++index) html += "</div>";
    html += "</body></html>";
    LoadTestDocument(html.c_str(), "/clip-complexity.html");
    urui_update();
    auto* context = URUI::GetContext("urui");
    // 本测试验证一次原生遍历中的裁剪复用，不能被跨帧整段录制复用绕过。
    auto render_changed_frame = [context]() { context->DirtyNativeRendering(); urui_render(); };
    for (int iteration = 0; iteration < 30; ++iteration) render_changed_frame();
    std::vector<double> times;
    for (int block = 0; block < 9; ++block)
    {
        const auto begin = std::chrono::steady_clock::now();
        for (int iteration = 0; iteration < 100; ++iteration) render_changed_frame();
        times.push_back(std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count() / 100);
    }
    std::sort(times.begin(), times.end());
    assert(urui_command_count() == 2000);
    std::vector<UruiCommand> commands(2000);
    assert(urui_get_commands(commands.data(), 2000) == 2000);
    // 两个夹具的实际裁剪和绘制负载相同，额外嵌套不能按兄弟数重复支付祖先遍历。
    for (const auto& command : commands) assert((command.flags & 1) && command.clip_w == 1024 && command.clip_h == 1024);
    urui_destroy();
    return times[times.size() / 2];
}

static void Semantics()
{
    assert(urui_create(640, 480) == 1);
    auto owner = LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}section{position:absolute;left:10px;top:15px;width:80px;height:70px;overflow:hidden;}#p{border-radius:8px;}div{position:absolute;width:20px;height:20px;background:green;}#a2{left:5px;}</style></head><body><section id='p'><div id='a1'></div><div id='a2'></div></section><section id='q'><div id='b'></div></section></body></html>", "/clip-state.html");
    urui_update();
    auto* context = URUI::GetContext("urui");
    auto& manager = context->GetRenderManager();
    auto* document = context->GetDocument(0);
    auto* a1 = document->GetElementById("a1");
    auto* a2 = document->GetElementById("a2");
    auto* b = document->GetElementById("b");
    auto expected = URUI::Rectanglei::FromPositionSize({10, 15}, {80, 70});
    manager.PrepareRender({640, 480});
    assert(URUI::ElementUtilities::SetClippingRegion(a1));
    assert(manager.GetScissorRegion() == expected);
    assert(manager.GetState().clip_mask_list.size() == 1);
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(manager.GetScissorRegion() == expected);

    // 相同矩形、不同祖先必须更新圆角遮罩，不能仅凭 scissor 相同复用。
    assert(URUI::ElementUtilities::SetClippingRegion(b));
    assert(manager.GetScissorRegion() == expected);
    assert(manager.GetState().clip_mask_list.empty());
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(manager.GetState().clip_mask_list.size() == 1);

    manager.DisableClipMask();
    assert(URUI::ElementUtilities::SetClippingRegion(a1));
    assert(manager.GetState().clip_mask_list.size() == 1);
    const auto original_mask = manager.GetState().clip_mask_list.front();
    manager.SetClipMask(URUI::ClipMaskGeometryList{});
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(manager.GetState().clip_mask_list.size() == 1);
    manager.SetClipMask(URUI::ClipMaskOperation::Set, original_mask.geometry, original_mask.absolute_offset + URUI::Vector2f(2, 3));
    assert(URUI::ElementUtilities::SetClippingRegion(a1));
    assert(manager.GetState().clip_mask_list.front().absolute_offset == original_mask.absolute_offset);
    manager.SetScissorRegion(URUI::Rectanglei::FromSize({1, 1}));
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(manager.GetScissorRegion() == expected);
    assert(URUI::ElementUtilities::SetClippingRegion(a2, true));
    assert(manager.GetScissorRegion() == URUI::Rectanglei::FromPositionSize({15, 15}, {20, 20}));
    assert(URUI::ElementUtilities::SetClippingRegion(a1));
    assert(manager.GetScissorRegion() == expected);

    manager.SetViewport({50, 50});
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(manager.GetScissorRegion() == URUI::Rectanglei::FromPositionSize({10, 15}, {40, 35}));
    manager.ResetState();
    assert(urui_set_style(owner, "p", "width", "60px") == 1);
    assert(urui_set_style(owner, "p", "border-radius", "0px") == 1);
    urui_update();
    manager.PrepareRender({640, 480});
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(manager.GetScissorRegion() == URUI::Rectanglei::FromPositionSize({10, 15}, {60, 70}));
    assert(manager.GetState().clip_mask_list.empty());
    manager.AbortRender();
    // 异常终止后，帧外读取也不能把默认状态误当成上一份裁剪结果。
    assert(URUI::ElementUtilities::SetClippingRegion(a1));
    assert(manager.GetScissorRegion() == URUI::Rectanglei::FromPositionSize({10, 15}, {60, 70}));
    manager.ResetState();

    assert(a2->SetProperty("--urui-clip", "none"));
    urui_update();
    manager.PrepareRender({640, 480});
    assert(URUI::ElementUtilities::SetClippingRegion(a1));
    assert(manager.GetScissorRegion().Valid());
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(!manager.GetScissorRegion().Valid());
    assert(manager.GetState().clip_mask_list.empty());
    manager.ResetState();
    assert(a2->SetProperty("--urui-clip", "1"));
    urui_update();
    manager.PrepareRender({640, 480});
    assert(URUI::ElementUtilities::SetClippingRegion(a1));
    assert(manager.GetScissorRegion().Valid());
    assert(URUI::ElementUtilities::SetClippingRegion(a2));
    assert(!manager.GetScissorRegion().Valid());
    manager.ResetState();
    urui_destroy();
}

int main(int argc, char** argv)
{
    assert(argc == 2);
    if (std::string(argv[1]).find("mutation-") == 0) RenderMutation(std::stoi(std::string(argv[1]).substr(9)));
    else if (std::string(argv[1]) == "semantics") Semantics();
    else
    {
        const double shallow = Measure(1), deep = Measure(24);
        std::cout << "clip complexity shallow=" << shallow << "ms deep=" << deep << "ms ratio=" << deep / shallow << std::endl;
        assert(deep / shallow < 1.8);
    }
}
