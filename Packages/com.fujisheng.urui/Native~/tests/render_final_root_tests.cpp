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
static void Check(uint32_t flags, bool shared, int callback = -1)
{
    Session session;
    session.Viewport(256, 256);
    URUICommand commands[2]{};
    commands[0].geometry = commands[1].geometry = 1;
    std::array<float, 16> matrix = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    std::array<float, 16> matrices[2] = {matrix, matrix};
    URUIPaintCommand paint{1, 1, 0, 0};
    URUIPlanExternalBinding external{0, uint32_t(callback)};
    URUIPlanInputPacket input{};
    URUIRenderPlanPacket plan{};
    URUIPlanBindings bindings{};
    bindings.frame_flags = flags;
    if (callback >= 0)
    {
        bindings.externals = &external;
        bindings.external_count = 1;
    }
    if (callback == 2)
    {
        bindings.frame_flags &= ~32u;
    }
    assert(session.Begin(7, commands, matrices, 2, callback >= 0 || callback == -2 ? &paint : nullptr,
                         callback >= 0 || callback == -2 ? 1 : 0, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    int opaque = 0;
    for (uint32_t n = 0; n < plan.operation_count; n++)
    {
        if (plan.operations[n].kind == URUI_PLAN_EXTERNAL || plan.operations[n].kind == URUI_PLAN_IMAGE)
        {
            opaque++;
        }
    }
    if (callback >= 0)
    {
        assert(opaque == ((callback & 5) ? 1 : 0));
    }
    int kind = 0;
    for (uint32_t n = 0; n < plan.target_count; n++)
    {
        const auto& target = plan.targets[n];
        if (target.layer == 0 && target.slot == -1)
        {
            kind = target.kind;
        }
    }
    std::cout << "flags=" << flags << " callback=" << callback << " targetKind=" << kind
              << " expected=" << (shared ? 7 : 2) << std::endl;
    assert(kind == (shared ? 7u : 2u));
    assert(session.Release(input.lease) == 1);
    assert(session.Release(plan.lease) == 1);
}
int main()
{
    const uint32_t flags = 1 | 2 | 8 | 16 | 32 | 256 | 512 | 1024;
    Check(flags, false, -2);
    Check(flags, true);
    Check(flags & ~1024u, false);
    Check(flags & ~512u, false);
    Check(flags & ~1u, false);
    Check(flags & ~256u, false);
    Check(flags, true, 0);
    Check(flags, true, 2);
    for (int callback : {1, 4})
    {
        Check(flags, false, callback);
    }
    std::cout << "final root proof and conservative boundaries passed\n";
}
