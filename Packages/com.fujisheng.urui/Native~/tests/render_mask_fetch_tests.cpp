#include "../render_plan_compiler.h"
#include <array>
#include <cassert>
#include <iostream>

using namespace URUI::RenderPlan;
static const URUIVertex vertices[] = {{0, 0, 0, 0, 255, 255, 255, 128},
                                      {8, 0, 1, 0, 255, 255, 255, 128},
                                      {8, 8, 1, 1, 255, 255, 255, 128},
                                      {0, 8, 0, 1, 255, 255, 255, 128}};
static const int indices[] = {0, 1, 2, 0, 2, 3};
static GeometrySource Resolve(uint64_t)
{
    return {vertices, 4, indices, 6};
}

static void Requests(uint32_t flags, int blend, bool expects_scratch)
{
    Session session;
    session.Viewport(64, 64);
    URUICommand draw{};
    draw.geometry = 1;
    std::array<float, 16> matrix = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    URUILayerCommand layers[3]{};
    layers[0].kind = 1;
    layers[0].target = 1;
    layers[1].kind = 6;
    layers[1].source = 1;
    layers[1].blend = blend;
    layers[1].command_index = 1;
    layers[2].kind = 2;
    layers[2].source = 1;
    layers[2].command_index = 1;
    URUIPlanInputPacket input{};
    URUIRenderPlanPacket plan{};
    URUIPlanBindings bindings{};
    bindings.frame_flags = flags;
    assert(session.Begin(1, &draw, &matrix, 1, nullptr, 0, layers, 3, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    bool primary = false;
    bool scratch = false;
    bool second = false;
    bool filter = false;
    for (uint32_t i = 0; i < plan.operation_count; i++)
    {
        filter |= plan.operations[i].kind == URUI_PLAN_LAYER && plan.operations[i].layer_index == 1;
    }
    for (uint32_t i = 0; i < plan.target_count; i++)
    {
        const auto& request = plan.targets[i];
        primary |= request.layer == 1 && request.slot == 0;
        scratch |= request.layer == 1 && request.slot == 1;
        second |= request.layer == 1 && request.slot == 2;
    }
    assert(primary && filter);
    if (scratch != expects_scratch)
    {
        std::cerr << "flags=" << flags << " blend=" << blend << " scratch=" << scratch
                  << " expected=" << expects_scratch << "\n";
    }
    assert(scratch == expects_scratch);
    assert(second == (blend != 1));
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
}
int main()
{
    Requests(0, 1, true);
    Requests(2 | 4 | 8, 1, true);
    Requests(2 | 4 | 8 | 64, 1, false);
    Requests(2 | 8 | 64, 1, false);
    Requests(2 | 4 | 8 | 64, 0, true);
    std::cout << "in-place mask removes only its own scratch target; fallback and custom filters retained\n";
}
