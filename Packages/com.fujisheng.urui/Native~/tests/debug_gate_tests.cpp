#include "render_plan.h"
#include <cassert>
#include <iostream>

int main()
{
    int diagnosticEvaluations = 0;
    URUI_DEBUG_CODE(++diagnosticEvaluations;)

    RenderSerial serial;
    RenderPlan plan(serial);
    plan.Enable(true);
    std::vector<UruiCommand> commands;
    std::vector<std::array<float, 16>> matrices;
    UruiCommand command{};
    command.geometry = 1;
    std::array<float, 16> matrix{};

    auto render = [&]()
    {
        plan.Begin(commands, matrices);
        commands.push_back(command);
        matrices.push_back(matrix);
        plan.Finish(commands, matrices);
        return plan.Snapshot(commands.size());
    };

    const auto first = render();
    const auto unchanged = render();
    assert(first.flags == 1 && unchanged.commands == first.commands);

#if defined(URUI_DEBUG)
    assert(diagnosticEvaluations == 1 && unchanged.retained_bytes > 0);
    assert(unchanged.compare_bytes == sizeof(UruiCommand) + sizeof(matrix));
#else
    // Disabled instrumentation must not evaluate its arguments or count work.
    assert(diagnosticEvaluations == 0 && unchanged.compare_bytes == 0 && unchanged.retained_bytes == 0);
#endif

    command.tx = 10;
    const auto changed = render();
    // Exact version tracking is functional, so the debug gate cannot disable it.
    assert(changed.commands != unchanged.commands && changed.epoch == unchanged.epoch);
    std::cout << "DEBUG_GATE_CONTRACT_PASS\n";
}
