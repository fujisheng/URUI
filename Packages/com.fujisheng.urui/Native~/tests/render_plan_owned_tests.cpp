#include "test_document.h"
#include "render_plan_protocol.h"
#include <atomic>
#include <cassert>
#include <iostream>
#include <thread>

int main()
{
    assert(urui_create(96,96)==1);
    LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}div{width:20px;height:20px;background:red;}</style></head><body><div></div></body></html>","/owned-plan.html");
    urui_update();UruiPlanInputPacket input{};UruiRenderPlanPacket plan{};UruiPlanBindings bindings{};
    assert(urui_plan_begin(&input)==1&&urui_plan_compile(input.lease,&bindings,&plan)==1&&plan.operation_count==1);
    const auto stale=plan.lease,geometry=plan.operations[0].geometry;UruiGeometryView view{};
    assert(urui_plan_geometry(stale,geometry,&view)==1&&view.vertex_count>0);
    urui_destroy();
    // 关闭 Context 后窗口已失效；不能直接解引用保存的指针，也不能把新 Context 当成旧窗口。
    view.vertex_count=999;assert(urui_plan_geometry(stale,geometry,&view)==-2&&!view.vertices&&view.vertex_count==0);
    assert(urui_plan_release(stale)==-2&&urui_plan_release(input.lease)==-2);
    std::atomic<bool> stop{false},ready{false};std::atomic<uint32_t> calls{0};
    std::thread foreign([&] {
        ready=true;
        while(!stop.load()) {
            UruiPlanInputPacket rejected{};rejected.lease=999;assert(urui_plan_begin(&rejected)==-3&&!rejected.lease&&!rejected.commands);
            UruiGeometryView rejected_mesh{};rejected_mesh.vertex_count=999;
            assert(urui_plan_geometry(stale,geometry,&rejected_mesh)==-3&&!rejected_mesh.vertices&&!rejected_mesh.vertex_count);
            assert(urui_plan_release(stale)==-3);calls++;
        }
    });
    while(!ready.load())std::this_thread::yield();
    for(int context=0;context<200;context++) {
        assert(urui_create(96,96)==1);
        for(int frame=0;frame<5;frame++) {
            assert(urui_plan_begin(&input)==1&&input.protocol==URUI_RENDER_PLAN_PROTOCOL&&input.lease!=stale);
            assert(urui_plan_compile(input.lease,&bindings,&plan)==1&&plan.protocol==URUI_RENDER_PLAN_PROTOCOL&&plan.lease!=stale);
            assert(urui_plan_geometry(stale,geometry,&view)==-2&&!view.vertices);
            assert(urui_plan_release(plan.lease)==1&&urui_plan_release(input.lease)==1);
        }
        urui_destroy();std::this_thread::yield();
    }
    stop=true;foreign.join();assert(calls>0);
    std::cout<<"render-plan real ABI: 200 context recreations / 1000 lease cycles; concurrent foreign calls "<<calls<<"; stale outputs zero\n";
}
