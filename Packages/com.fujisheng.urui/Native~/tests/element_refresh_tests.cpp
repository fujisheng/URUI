#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <thread>
#include <URUI/Core.h>
#include <iostream>
class OffsetUpdateElement : public URUI::Element
{
    int updates = 0;

public:
    explicit OffsetUpdateElement(const URUI::String& tag) : Element(tag)
    {
    }
    void OnUpdate() override
    {
        Element::OnUpdate();
        SetOffset({float(++updates * 7), 19}, GetOffsetParent());
    }
};
static void NativeUpdateOffsets()
{
    assert(urui_surface_create(128, 128, &g_test_surface) == 1);
    URUI::ElementInstancerGeneric<OffsetUpdateElement> instancer;
    URUI::Factory::RegisterElementInstancer("urui-offset-update", &instancer);
    auto owner = LoadTestDocument(g_test_surface, 
        "<!doctype html><html><head></head><body><urui-offset-update id='moving'></urui-offset-update></body></html>",
        "/refresh-native-update.html");
    urui_surface_update(g_test_surface);
    urui_surface_update(g_test_surface);
    auto token = urui_element_ref_acquire(g_test_surface, owner, "moving");
    for (int n = 0; n < 3; n++)
    {
        URUIElementRefreshStamp before{};
        URUIElementRefreshStamp after{};
        float previous[4]{};
        float next[4]{};
        assert(urui_element_refresh_stamp(g_test_surface, &before) == 1 && urui_element_refresh_state(g_test_surface, owner, token, previous, 4) == 3);
        urui_surface_update(g_test_surface);
        assert(urui_element_refresh_stamp(g_test_surface, &after) == 1 && urui_element_refresh_state(g_test_surface, owner, token, next, 4) == 3);
        assert(next[0] != previous[0] && after.mutation != before.mutation);
    }
    urui_surface_destroy(g_test_surface);
}
int main()
{
    static_assert(sizeof(URUIElementRefreshState) == 20);
    assert(urui_surface_create(128, 128, &g_test_surface) == 1);
    auto a = LoadTestDocument(g_test_surface, 
        "<!doctype html><html><head><style>body{margin:0;}div{width:12px;height:9px;}</style></head><body><div id='a'></div><div id='b'></div></body></html>",
        "/refresh-a.html");
    auto b =
        LoadTestDocument(g_test_surface, "<!doctype html><html><head></head><body><div id='b'></div></body></html>", "/refresh-b.html");
    urui_surface_update(g_test_surface);
    uint64_t tokens[] = {urui_element_ref_acquire(g_test_surface, a, "a"), urui_element_ref_acquire(g_test_surface, a, "b"),
                         urui_element_ref_acquire(g_test_surface, b, "b"), UINT64_MAX};
    URUIElementRefreshState states[4]{};
    assert(urui_element_refresh_states(g_test_surface, a, tokens, states, 4) == 1);
    for (int i = 0; i < 2; i++)
    {
        float old[4]{};
        assert(states[i].state == urui_element_refresh_state(g_test_surface, a, tokens[i], old, 4));
        for (int n = 0; n < 4; n++)
        {
            assert(states[i].layout[n] == old[n]);
        }
        assert(states[i].layout[2] == 12 && states[i].layout[3] == 9);
    }
    assert(states[2].state == 0 && states[3].state == 0);
    const auto stable_revision = urui_element_ref_revision(g_test_surface);
    assert(stable_revision != 0);
    assert(urui_element_refresh_states(g_test_surface, a, tokens, states, 4) == 1);
    assert(urui_element_ref_revision(g_test_surface) == stable_revision);
    // 读取相对布局不会清除绝对偏移脏标记；连续移动也必须撤销观察证明。
    auto* element = URUI::GetContext(0)->GetDocument(0)->GetElementById("a");
    for (int n = 1; n <= 3; n++)
    {
        URUIElementRefreshStamp before{};
        URUIElementRefreshStamp after{};
        assert(urui_element_refresh_stamp(g_test_surface, &before) == 1);
        element->SetOffset({float(37 * n), float(19 * n)}, element->GetOffsetParent());
        assert(urui_element_refresh_stamp(g_test_surface, &after) == 1);
        assert(after.mutation != before.mutation);
        float layout[4]{};
        assert(urui_element_refresh_state(g_test_surface, a, tokens[0], layout, 4) == 3);
        assert(layout[0] == 37 * n && layout[1] == 19 * n);
        element->SetOffset({float(37 * n), float(19 * n)}, element->GetOffsetParent());
        URUIElementRefreshStamp unchanged{};
        assert(urui_element_refresh_stamp(g_test_surface, &unchanged) == 1);
        assert(unchanged.mutation == after.mutation && unchanged.references == after.references);
    }
    assert(urui_element_detach(g_test_surface, a, tokens[0]) == 1);
    assert(urui_element_refresh_states(g_test_surface, a, tokens, states, 4) == 1);
    assert(states[0].state == 1);
    int foreign = 0;
    std::thread thread([&] { foreign = urui_element_refresh_states(g_test_surface, a, tokens, states, 4); });
    thread.join();
    assert(foreign == -3);
    for (const auto& s : states)
    {
        assert(s.state == 0);
        for (float v : s.layout)
        {
            assert(v == 0);
        }
    }
    assert(urui_element_refresh_states(g_test_surface, a, nullptr, nullptr, 0) == 1);
    assert(urui_element_refresh_states(g_test_surface, a, nullptr, states, 4) == -1);
    assert(urui_element_refresh_states(g_test_surface, UINT64_MAX, tokens, states, 4) == -2);
    for (const auto& s : states)
    {
        assert(s.state == 0);
    }
    auto revision = urui_element_ref_revision(g_test_surface);
    assert(urui_element_ref_release(g_test_surface, a, tokens[1]) == 1);
    assert(urui_element_ref_revision(g_test_surface) != revision);
    revision = urui_element_ref_revision(g_test_surface);
    assert(urui_element_destroy(g_test_surface, a, tokens[0]) == 1);
    assert(urui_element_ref_revision(g_test_surface) != revision);
    uint64_t foreign_revision = 0;
    std::thread reader([&] { foreign_revision = urui_element_ref_revision(g_test_surface); });
    reader.join();
    assert(foreign_revision == urui_element_ref_revision(g_test_surface));
    urui_surface_destroy(g_test_surface);
    assert(urui_element_refresh_states(g_test_surface, a, tokens, states, 4) == -2);
    NativeUpdateOffsets();
    std::cout << "batched element refresh matches individual state and rejects invalid ownership/thread\n";
}
