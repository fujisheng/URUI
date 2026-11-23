#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "render_plan_protocol.h"
#include <atomic>
#include <cassert>
#include <iostream>
#include <thread>

int main()
{
    assert(urui_surface_create(96, 96, &g_test_surface) == 1);
    LoadTestDocument(g_test_surface, 
        "<!doctype html><html><head><style>body{margin:0;}div{width:20px;height:20px;background:red;}</style></head><body><div></div></body></html>",
        "/owned-plan.html");
    urui_surface_update(g_test_surface);
    URUIPlanInputPacket input{};
    URUIRenderPlanPacket plan{};
    URUIPlanBindings bindings{};
    assert(urui_plan_begin(g_test_surface, &input) == 1 && urui_plan_compile(g_test_surface, input.lease, &bindings, &plan) == 1 &&
           plan.operation_count == 1);
    const auto stale = plan.lease;
    const auto geometry = plan.operations[0].geometry;
    URUIGeometryView view{};
    assert(urui_plan_geometry(g_test_surface, stale, geometry, &view) == 1 && view.vertex_count > 0);
    urui_surface_destroy(g_test_surface);
    // 关闭 Context 后窗口已失效；不能直接解引用保存的指针，也不能把新 Context 当成旧窗口。
    view.vertex_count = 999;
    assert(urui_plan_geometry(g_test_surface, stale, geometry, &view) == -2 && !view.vertices && view.vertex_count == 0);
    assert(urui_plan_release(g_test_surface, stale) == -2 && urui_plan_release(g_test_surface, input.lease) == -2);
    std::atomic<URUISurfaceHandle> live_surface{0};
    std::atomic<bool> stop{false}, ready{false};
    std::atomic<uint32_t> calls{0};
    std::atomic<uint32_t> wrong_thread_calls{0};
    std::thread foreign(
        [&]
        {
            ready = true;
            while (!stop.load())
            {
                const URUISurfaceHandle target = live_surface.load(std::memory_order_acquire);
                URUIPlanInputPacket rejected{};
                rejected.lease = 999;
                const int s1 = urui_plan_begin(target, &rejected);
                assert((s1 == URUI_ERR_WRONG_THREAD || s1 == URUI_ERR_NOT_READY) && !rejected.lease &&
                       !rejected.commands);
                if (s1 == URUI_ERR_WRONG_THREAD)
                {
                    wrong_thread_calls++;
                }
                URUIGeometryView rejected_mesh{};
                rejected_mesh.vertex_count = 999;
                const int s2 = urui_plan_geometry(target, stale, geometry, &rejected_mesh);
                assert((s2 == URUI_ERR_WRONG_THREAD || s2 == URUI_ERR_NOT_READY) && !rejected_mesh.vertices &&
                       !rejected_mesh.vertex_count);
                const int s3 = urui_plan_release(target, stale);
                assert(s3 == URUI_ERR_WRONG_THREAD || s3 == URUI_ERR_NOT_READY);
                calls++;
            }
        });
    while (!ready.load())
    {
        std::this_thread::yield();
    }
    for (int context = 0; context < 200; context++)
    {
        assert(urui_surface_create(96, 96, &g_test_surface) == 1);
        live_surface.store(g_test_surface, std::memory_order_release);
        for (int frame = 0; frame < 5; frame++)
        {
            assert(urui_plan_begin(g_test_surface, &input) == 1 && input.protocol == URUI_RENDER_PLAN_PROTOCOL && input.lease != stale);
            assert(urui_plan_compile(g_test_surface, input.lease, &bindings, &plan) == 1 &&
                   plan.protocol == URUI_RENDER_PLAN_PROTOCOL && plan.lease != stale);
            assert(urui_plan_geometry(g_test_surface, stale, geometry, &view) == -2 && !view.vertices);
            assert(urui_plan_release(g_test_surface, plan.lease) == 1 && urui_plan_release(g_test_surface, input.lease) == 1);
        }
        urui_surface_destroy(g_test_surface);
        std::this_thread::yield();
    }
    stop = true;
    foreign.join();
    assert(calls > 0);
    std::cout << "render-plan real ABI: 200 context recreations / 1000 lease cycles; concurrent foreign calls " << calls
              << "; stale outputs zero\n";
}
