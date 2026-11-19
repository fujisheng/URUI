#include "../render_plan_compiler.h"
#include <cassert>
#include <cstring>
#include <iostream>

using namespace URUI::RenderPlan;
static const std::array<float, 16> identity{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
static const URUIVertex quad[] = {{0, 0, 0, 0, 255, 255, 255, 128},
                                  {8, 0, 1, 0, 255, 255, 255, 128},
                                  {8, 8, 1, 1, 255, 255, 255, 128},
                                  {0, 8, 0, 1, 255, 255, 255, 128}};
static const int indices[] = {0, 1, 2, 0, 2, 3};
static const URUIVertex large_quad[] = {{0, 0, 0, 0, 255, 255, 255, 128},
                                        {128, 0, 1, 0, 255, 255, 255, 128},
                                        {128, 128, 1, 1, 255, 255, 255, 128},
                                        {0, 128, 0, 1, 255, 255, 255, 128}};
static GeometrySource Resolve(uint64_t id)
{
    return id ? GeometrySource{id == 2 ? large_quad : quad, 4, indices, 6} : GeometrySource{};
}

struct Fixture
{
    Session session;
    URUICommand commands[3]{};
    std::array<float, 16> matrices[3]{identity, identity, identity};
    URUILayerCommand layers[8]{};
    const float positions[12]{0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0};
    const uint16_t triangles[6]{0, 1, 2, 0, 2, 3};
    URUIPlanExternalGeometry geometry{};
    URUIPlanExternalDraw draw{};
    URUIPaintCommand paint{600, 750, 2, 0};
    URUIPlanExternalBinding external{2, 8};
    URUIPlanBindings bindings{};
    Fixture()
    {
        session.Viewport(128, 128);
        for (auto& command : commands)
        {
            command.geometry = 1;
            command.tx = 10;
            command.ty = 20;
        }
        commands[1].tx = 13;
        commands[2].tx = commands[2].ty = 0;
        layers[0].kind = 1;
        layers[0].target = 1;
        layers[1].kind = 1;
        layers[1].target = 2;
        layers[1].command_index = 2;
        for (int n = 2; n < 8; n++)
        {
            layers[n].command_index = 3;
        }
        layers[2].kind = 3;
        layers[2].source = 2;
        layers[2].target = 3;
        layers[3].kind = 2;
        layers[3].source = 2;
        layers[3].target = 1;
        layers[4].kind = 6;
        layers[4].source = layers[4].target = 1;
        layers[4].mask = 3;
        layers[4].blend = 1;
        layers[5].kind = 4;
        layers[5].source = 1;
        layers[6].kind = 2;
        layers[6].source = 1;
        layers[7].kind = 5;
        layers[7].source = 3;
        geometry.geometry = 500;
        geometry.version = 1;
        geometry.vertex_count = 4;
        geometry.index_count = 6;
        geometry.stream_count = 1;
        geometry.streams[0] = {positions, sizeof(positions), 12, 0};
        geometry.position = {0, 0, URUI_PLAN_FLOAT32, 3};
        geometry.indices = triangles;
        geometry.index_bytes = sizeof(triangles);
        geometry.index_stride = 2;
        draw.command = 2;
        draw.geometry_index = 0;
        draw.provider = 950;
        draw.program = 850;
        draw.flags = 4;
        std::copy(identity.begin(), identity.end(), draw.parameter.matrix);
        draw.parameter.matrix[0] = 16;
        draw.parameter.matrix[5] = 12;
        draw.parameter.matrix[12] = 10;
        draw.parameter.matrix[13] = 20;
        draw.parameter.uv[0] = 16;
        draw.parameter.uv[1] = 12;
        bindings.frame_flags = 1 | 2 | 4 | 32 | 64 | 128;
        bindings.externals = &external;
        bindings.external_count = 1;
        bindings.external_geometry = &geometry;
        bindings.external_geometry_count = 1;
        bindings.external_draws = &draw;
        bindings.external_draw_count = 1;
    }
    uint64_t Frame(bool folded, std::array<float, 4> clip = {10, 20, 26, 32})
    {
        URUIPlanInputPacket input{};
        URUIPlanPreparePacket ready{};
        URUIRenderPlanPacket output{};
        assert(session.Begin(1, commands, matrices, 3, &paint, 1, layers, 8, input) == 1);
        std::array<URUILayerCommand, 8> original{};
        std::copy_n(input.layers, 8, original.begin());
        assert(session.Resolve(input.lease, nullptr, Resolve, ready, &bindings) == 1 && ready.ready);
        uint64_t source_version = 0;
        for (uint32_t n = 0; n < ready.region_count; n++)
        {
            if (ready.regions[n].layer == 1)
            {
                source_version = ready.regions[n].version;
            }
        }
        assert(source_version);
        assert(session.Compile(input.lease, bindings, Resolve, output) == 1);
        assert(std::memcmp(input.layers, original.data(), sizeof(original)) == 0);
        if (folded)
        {
            std::cout << "hard rectangle target requests=" << output.target_count << std::endl;
            // 保留子树源层；只删遮罩和自合成 scratch，不能将重叠图元直接写到父层。
            assert(output.target_count == 1 && output.targets[0].layer == 1 && output.targets[0].slot == 0);
            int layer_operations = 0;
            bool final_clip = false;
            for (uint32_t n = 0; n < output.operation_count; n++)
            {
                const auto& operation = output.operations[n];
                if (operation.kind != URUI_PLAN_LAYER)
                {
                    continue;
                }
                layer_operations++;
                if (operation.layer_index == 5)
                {
                    assert((operation.reason & 2) != 0);
                    std::cout << "clip=" << operation.bounds[0] << "," << operation.bounds[1] << ","
                              << operation.bounds[2] << "," << operation.bounds[3] << std::endl;
                    assert(std::equal(clip.begin(), clip.end(), operation.bounds));
                    final_clip = true;
                }
                else
                {
                    assert(operation.layer_index == 0 || operation.layer_index == 6);
                }
            }
            assert(layer_operations == 3 && final_clip);
        }
        else
        {
            bool source = false;
            bool mask = false;
            bool self_mask = false;
            for (uint32_t n = 0; n < output.target_count; n++)
            {
                const auto& target = output.targets[n];
                source |= target.layer == 1 && target.slot == 0;
                mask |= (target.layer == 2 || target.layer == 3) && target.slot == 0;
            }
            // 未折叠的软遮罩仍保留独立 mask 和自遮罩操作；fetch 后端无需写回 scratch。
            assert(source && mask);
            for (uint32_t n = 0; n < output.operation_count; n++)
            {
                if (output.operations[n].kind == URUI_PLAN_LAYER)
                {
                    assert((output.operations[n].reason & 2) == 0);
                    self_mask |= output.operations[n].layer_index == 4;
                }
            }
            assert(self_mask);
        }
        assert(session.Release(output.lease) == 1 && session.Release(input.lease) == 1);
        return source_version;
    }
};
static void NestedCacheVersion(bool single_leaf)
{
    Fixture value;
    std::vector<URUICommand> commands(value.commands, value.commands + 3);
    commands.emplace_back();
    commands[3].geometry = 2;
    std::vector<std::array<float, 16>> matrices(4, identity);
    auto paint = value.paint;
    auto draw = value.draw;
    auto external = value.external;
    auto bindings = value.bindings;
    bindings.externals = &external;
    bindings.external_draws = &draw;
    std::array<URUILayerCommand, 11> layers{};
    layers[0].kind = 1;
    layers[0].target = 4;
    std::copy_n(value.layers, 8, layers.begin() + 1);
    layers[6].target = 4;
    layers[7].target = 4;
    layers[9].kind = 4;
    layers[9].source = 4;
    layers[9].command_index = 4;
    layers[10].kind = 2;
    layers[10].source = 4;
    layers[10].command_index = 4;
    if (single_leaf)
    {
        commands.erase(commands.begin() + 1);
        matrices.erase(matrices.begin() + 1);
        paint.command_index = 1;
        draw.command = 1;
        external.command = 1;
        for (auto& layer : layers)
        {
            if (layer.command_index >= 2)
            {
                layer.command_index--;
            }
        }
    }
    auto versions = [&]()
    {
        URUIPlanInputPacket input{};
        URUIPlanPreparePacket ready{};
        URUIRenderPlanPacket output{};
        assert(value.session.Begin(1, commands.data(), matrices.data(), uint32_t(commands.size()), &paint, 1,
                                   layers.data(), 11, input) == 1);
        assert(value.session.Resolve(input.lease, nullptr, Resolve, ready, &bindings) == 1 && ready.ready);
        std::array<uint64_t, 2> result{};
        for (uint32_t n = 0; n < ready.region_count; n++)
        {
            const auto& region = ready.regions[n];
            if (region.layer == 1)
            {
                result[0] = region.version;
            }
            if (region.layer == 4)
            {
                result[1] = region.version;
            }
        }
        assert(result[1] && (single_leaf || result[0]));
        assert(value.session.Compile(input.lease, bindings, Resolve, output) == 1);
        assert(output.target_count == (single_leaf ? 1u : 2u));
        assert(value.session.Release(output.lease) == 1 && value.session.Release(input.lease) == 1);
        return result;
    };
    const auto first = versions();
    draw.parameter.matrix[12] = 11;
    const auto moved = versions();
    // 父域由后续宽兄弟固定；纯裁剪变化不改源层像素，却必须使父层的合成缓存失效。
    std::cout << "nested single=" << single_leaf << " source=" << first[0] << "->" << moved[0] << " parent=" << first[1]
              << "->" << moved[1] << std::endl;
    assert(first[0] == moved[0] && first[1] != moved[1]);
}
int main()
{
    {
        Fixture prepared;
        URUIPlanInputPacket input{};
        URUIPlanPreparePacket ready{};
        URUIRenderPlanPacket output{};
        assert(prepared.session.Begin(1, prepared.commands, prepared.matrices, 3, &prepared.paint, 1, prepared.layers,
                                      8, input) == 1);
        assert(prepared.session.Resolve(input.lease, nullptr, Resolve, ready, &prepared.bindings) == 1 && ready.ready);
        auto withdrawn = prepared.bindings;
        withdrawn.frame_flags &= ~1u;
        const auto result = prepared.session.Compile(input.lease, withdrawn, Resolve, output);
        std::cout << "withdrawn root pixel-grid compile result=" << result << std::endl;
        assert(result < 0 && output.lease == 0);
        // 失败不能消耗输入租约；恢复原证明后仍可正常编译和释放。
        assert(prepared.session.Compile(input.lease, prepared.bindings, Resolve, output) == 1);
        assert(output.target_count == 1);
        assert(prepared.session.Release(output.lease) == 1 && prepared.session.Release(input.lease) == 1);
    }
    Fixture basic;
    basic.Frame(true);
    Fixture clipped;
    clipped.layers[5].flags = 1;
    clipped.layers[5].clip_x = 12;
    clipped.layers[5].clip_y = 18;
    clipped.layers[5].clip_w = 7;
    clipped.layers[5].clip_h = 12;
    clipped.commands[2].flags = 1;
    clipped.commands[2].clip_x = 11;
    clipped.commands[2].clip_y = 23;
    clipped.commands[2].clip_w = 13;
    clipped.commands[2].clip_h = 8;
    clipped.Frame(true, {12, 23, 19, 30});
    Fixture flipped;
    flipped.draw.parameter.matrix[0] = -16;
    flipped.draw.parameter.matrix[12] = 26;
    flipped.Frame(true);
    Fixture feather;
    feather.draw.parameter.tint[1] = .00001f;
    feather.Frame(false);
    Fixture half;
    half.draw.parameter.matrix[12] = 10.5f;
    half.Frame(false);
    Fixture rotated;
    rotated.draw.parameter.matrix[1] = .25f;
    rotated.Frame(false);
    Fixture perspective;
    perspective.draw.parameter.matrix[3] = .01f;
    perspective.Frame(false);
    Fixture blend;
    blend.layers[5].blend = 1;
    blend.Frame(false);
    Fixture scaled_output;
    scaled_output.bindings.frame_flags &= ~1u;
    scaled_output.Frame(false);
    Fixture cached;
    const auto raw_version = cached.Frame(true);
    cached.draw.parameter.matrix[12] = 11;
    assert(cached.Frame(true, {11, 20, 27, 32}) == raw_version);
    cached.bindings.frame_flags &= ~1u;
    assert(cached.Frame(false) != raw_version);
    Fixture warm;
    for (int n = 0; n < 64; n++)
    {
        warm.draw.parameter.matrix[12] = float(10 + n % 2);
        warm.Frame(true, {float(10 + n % 2), 20, float(26 + n % 2), 32});
    }
    const auto before = warm.session.Allocations();
    for (int n = 0; n < 32; n++)
    {
        warm.draw.parameter.matrix[12] = float(10 + n % 2);
        warm.Frame(true, {float(10 + n % 2), 20, float(26 + n % 2), 32});
    }
    const auto after = warm.session.Allocations();
    assert(before.allocations == after.allocations && before.frees == after.frees && before.bytes == after.bytes);
    NestedCacheVersion(false);
    NestedCacheVersion(true);
    std::cout << "binary mask target reduction, immutable input, exact clip and conservative raster fallbacks passed\n";
}
