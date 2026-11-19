#include "../render_plan_compiler.h"
#include <cassert>
#include <chrono>
#include <iostream>

using namespace URUI::RenderPlan;

// 独立的两阶段参考：先规划完整转换，再按已确定的区间计算范围。
// 它同时钉住输出等价性和新增范围证明不能重复扫描资源的工作预算。
static uint32_t Domain(const Frame& source, const URUIPlanOperation& operation)
{
    if (operation.texture_count)
    {
        for (const auto& texture : source.resources)
        {
            if (texture.texture == operation.textures[0])
            {
                return texture.domain;
            }
        }
    }
    return 0;
}
static void Reference(Frame& source, Frame& target, const URUIPlanBindings& bindings)
{
    const auto flags = source.resourceFlags;
    source.resourceFlags &= ~512u;
    PlanRasterPasses(source, target, bindings);
    source.resourceFlags = flags;
    for (uint32_t first = 0; first < target.operations.size();)
    {
        if (!(target.operations[first].reason & URUI_PLAN_RASTER_BEGIN))
        {
            first++;
            continue;
        }
        uint32_t end = first;
        while (!(target.operations[end].reason & URUI_PLAN_RASTER_END))
        {
            end++;
        }
        for (uint32_t group = first; group <= end;)
        {
            if (target.operations[group].kind != URUI_PLAN_GEOMETRY || Domain(source, target.operations[group]))
            {
                group++;
                continue;
            }
            const auto start = group;
            bool valid = true;
            float bounds[4] = {std::numeric_limits<float>::max(), std::numeric_limits<float>::max(),
                               std::numeric_limits<float>::lowest(), std::numeric_limits<float>::lowest()};
            do
            {
                const auto& item = target.operations[group];
                valid = valid && !(item.reason & 0x80000000u) && item.bounds[2] > item.bounds[0] &&
                        item.bounds[3] > item.bounds[1];
                for (int axis = 0; axis < 4; axis++)
                {
                    valid = valid && std::isfinite(item.bounds[axis]);
                }
                bounds[0] = std::min(bounds[0], item.bounds[0]);
                bounds[1] = std::min(bounds[1], item.bounds[1]);
                bounds[2] = std::max(bounds[2], item.bounds[2]);
                bounds[3] = std::max(bounds[3], item.bounds[3]);
                group++;
            } while (group <= end && target.operations[group].kind == URUI_PLAN_GEOMETRY &&
                     !Domain(source, target.operations[group]));
            if (valid)
            {
                auto& item = target.operations[start];
                item.reason |= URUI_PLAN_RASTER_REGION;
                std::copy_n(bounds, 4, item.bounds);
            }
        }
        first = end + 1;
    }
}
static void Actual(Frame& source, Frame& target, const URUIPlanBindings& bindings)
{
    PlanRasterPasses(source, target, bindings);
}

using Planner = void (*)(Frame&, Frame&, const URUIPlanBindings&);
static volatile uint32_t witness = 0;
static double Measure(Planner planner, Frame& source, Frame& target, const std::vector<URUIPlanOperation>& input)
{
    const auto begin = std::chrono::steady_clock::now();
    for (int n = 0; n < 6; n++)
    {
        target.operations.assign(input.begin(), input.end());
        planner(source, target, {});
        witness = target.operations.front().reason;
        std::atomic_signal_fence(std::memory_order_seq_cst);
    }
    return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count();
}
int main()
{
    AllocationCounters counters;
    Budget budget{&counters, 0, 32 * 1024 * 1024};
    Frame source(&budget), actual(&budget), reference(&budget);
    source.resourceFlags = 1 | 2 | 8 | 16 | 32 | 256 | 512;
    source.createdLayers.resize(1);
    for (uint64_t n = 1; n <= 512; n++)
    {
        source.resources.push_back({n, 1, n == 511 ? 0u : 1u, 1});
    }
    std::vector<URUIPlanOperation> input;
    for (uint32_t n = 0; n < 4096; n++)
    {
        for (uint32_t item = 0; item < 3; item++)
        {
            URUIPlanOperation operation{};
            operation.kind = URUI_PLAN_GEOMETRY;
            operation.texture_count = 1;
            operation.textures[0] = item == 2 ? 512 : 511;
            operation.geometry = n * 3 + item + 1;
            operation.command_index = n * 3 + item;
            operation.count = 6;
            operation.mask_texture = item == 1 ? 1 : 0;
            const float left = float(n % 128 + item * 3);
            operation.bounds[0] = left;
            operation.bounds[1] = 4;
            operation.bounds[2] = left + 2;
            operation.bounds[3] = 8;
            input.push_back(operation);
        }
    }
    actual.operations.assign(input.begin(), input.end());
    Actual(source, actual, {});
    reference.operations.assign(input.begin(), input.end());
    Reference(source, reference, {});
    assert(actual.operations.size() == reference.operations.size());
    assert(std::memcmp(actual.operations.data(), reference.operations.data(),
                       actual.operations.size() * sizeof(URUIPlanOperation)) == 0);
    const auto allocations = counters.allocations;
    const auto retained = budget.retained;
    std::vector<double> actualTimes, referenceTimes;
    for (int round = 0; round < 6; round++)
    {
        if (round % 2)
        {
            referenceTimes.push_back(Measure(Reference, source, reference, input));
            actualTimes.push_back(Measure(Actual, source, actual, input));
        }
        else
        {
            actualTimes.push_back(Measure(Actual, source, actual, input));
            referenceTimes.push_back(Measure(Reference, source, reference, input));
        }
    }
    assert(counters.allocations == allocations && budget.retained == retained);
    std::sort(actualTimes.begin(), actualTimes.end());
    std::sort(referenceTimes.begin(), referenceTimes.end());
    const auto current = (actualTimes[2] + actualTimes[3]) * .5;
    const auto old = (referenceTimes[2] + referenceTimes[3]) * .5;
    std::cout << "identical operations; no warm allocations; actual_ms=" << current << " reference_ms=" << old
              << " ratio=" << current / old << "\n";
    if (current > old * .8)
    {
        std::cerr << "范围规划须消除重复资源扫描，不能把新增CPU工作转嫁给整页。\n";
        return 1;
    }
}
