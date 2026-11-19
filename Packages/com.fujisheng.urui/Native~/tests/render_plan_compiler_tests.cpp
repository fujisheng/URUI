#include "../render_plan_compiler.h"
#include <cassert>
#include <thread>
#include <iostream>
#include <cstdlib>

// Independent heap probe: do not trust only the planner's own accounting when
// checking that a helper/standard-library control block has not escaped it.
static thread_local bool watch_heap = false;
static thread_local uint64_t heap_allocations = 0, heap_frees = 0, heap_bytes = 0;
void* operator new(std::size_t bytes)
{
    void* value = std::malloc(bytes ? bytes : 1);
    if (!value)
    {
        throw std::bad_alloc();
    }
    if (watch_heap)
    {
        heap_allocations++;
        heap_bytes += bytes;
    }
    return value;
}
void operator delete(void* value) noexcept
{
    if (value && watch_heap)
    {
        heap_frees++;
    }
    std::free(value);
}
void operator delete(void* value, std::size_t) noexcept
{
    ::operator delete(value);
}
void* operator new[](std::size_t bytes)
{
    return ::operator new(bytes);
}
void operator delete[](void* value) noexcept
{
    ::operator delete(value);
}
void operator delete[](void* value, std::size_t) noexcept
{
    ::operator delete(value);
}

using namespace URUI::RenderPlan;
static const std::array<float, 16> identity{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
static const URUIVertex quad[] = {{0, 0, 0, 0, 255, 255, 255, 255},
                                  {8, 0, 1, 0, 255, 255, 255, 255},
                                  {8, 8, 1, 1, 255, 255, 255, 255},
                                  {0, 8, 0, 1, 255, 255, 255, 255}};
static const int triangles[] = {0, 1, 2, 0, 2, 3};
static GeometrySource Resolve(uint64_t id)
{
    return id ? GeometrySource{quad, 4, triangles, 6} : GeometrySource{};
}

static std::array<float, 4> Position(const URUIPlanVertex& vertex, const URUIPlanParameter& parameter)
{
    const auto* m = parameter.matrix;
    const bool anchored = (uint32_t(parameter.metadata[0]) & 512u) != 0;
    const float x = vertex.x - (anchored ? m[8] : 0);
    const float y = vertex.y - (anchored ? m[9] : 0);
    std::array<float, 4> result{};
    for (int row = 0; row < 4; row++)
    {
        result[row] = m[row] * x + m[4 + row] * y + m[12 + row];
    }
    return result;
}

static void TerminalMaskStorageRegression()
{
    URUICommand draws[4]{};
    std::array<float, 16> transforms[4]{identity, identity, identity, identity};
    for (int n = 0; n < 4; n++)
    {
        draws[n].geometry = 1;
        draws[n].tx = n == 3 ? 400.f : float(n * 2000);
    }
    transforms[3][0] = 5;
    std::vector<URUILayerCommand> layers(8);
    layers[0].kind = 1;
    layers[0].target = 1;
    layers[1].kind = 1;
    layers[1].target = 2;
    layers[1].command_index = 3;
    layers[2].kind = 3;
    layers[2].source = 2;
    layers[2].target = 3;
    layers[2].command_index = 4;
    layers[3].kind = 2;
    layers[3].source = 2;
    layers[3].target = 1;
    layers[3].command_index = 4;
    layers[4].kind = 6;
    layers[4].source = layers[4].target = 1;
    layers[4].mask = 3;
    layers[4].blend = 1;
    layers[4].command_index = 4;
    layers[5].kind = 4;
    layers[5].source = 1;
    layers[5].command_index = 4;
    layers[6].kind = 2;
    layers[6].source = 1;
    layers[6].command_index = 4;
    layers[7].kind = 5;
    layers[7].source = 3;
    layers[7].command_index = 4;
    auto region = [](const URUIPlanPreparePacket& packet) -> const URUIPlanLayerRegion&
    {
        for (uint32_t n = 0; n < packet.region_count; n++)
        {
            if (packet.regions[n].layer == 1)
            {
                return packet.regions[n];
            }
        }
        throw std::runtime_error("missing masked region");
    };
    Session storage;
    storage.Viewport(2160, 1080);
    URUIPlanInputPacket input{};
    URUIPlanInputPacket second{};
    URUIPlanPreparePacket ready{};
    URUIPlanPreparePacket moved{};
    assert(storage.Begin(301, draws, transforms, 4, nullptr, 0, layers.data(), uint32_t(layers.size()), input) == 1);
    assert(storage.Resolve(input.lease, nullptr, Resolve, ready) == 1 && ready.ready);
    assert(region(ready).bounds[0] == 1999 && region(ready).bounds[2] == 42);
    const auto version = region(ready).version;
    draws[3].tx = 404;
    assert(storage.Begin(301, draws, transforms, 4, nullptr, 0, layers.data(), uint32_t(layers.size()), second) == 1);
    assert(storage.Resolve(second.lease, nullptr, Resolve, moved) == 1 && moved.ready);
    assert(region(moved).bounds[0] == 2019 && region(moved).bounds[2] == 42 && region(moved).version != version);
    assert(region(ready).bounds[0] == 1999 && region(ready).version == version);
    assert(storage.Release(input.lease) == 1 && storage.Release(second.lease) == 1);
    draws[3].tx = 400;
    // 邻域滤镜必须保留遮罩外输入，不能为缩小目标而裁掉会进入可见区的像素。
    auto filtered = layers;
    URUILayerCommand blur{};
    blur.kind = 6;
    blur.source = blur.target = 1;
    blur.mask = UINT64_MAX;
    blur.command_index = 3;
    filtered.insert(filtered.begin() + 1, blur);
    LayerEffect effect{1, 0, 0, UINT64_MAX, 1, 2, 0, 0, 0};
    assert(storage.Begin(302, draws, transforms, 4, nullptr, 0, filtered.data(), uint32_t(filtered.size()), input) ==
           1);
    assert(storage.SetLayerEffects(input.lease, &effect, 1) == 1);
    assert(storage.Resolve(input.lease, nullptr, Resolve, ready) == 1 && ready.ready && region(ready).bounds[2] > 4000);
    assert(storage.Release(input.lease) == 1);
    // 提前保存的消费者仍需完整内容，不能被后面的最终遮罩收紧。
    auto saved = layers;
    URUILayerCommand copy{};
    copy.kind = 3;
    copy.source = 1;
    copy.target = 4;
    copy.command_index = 3;
    saved.insert(saved.begin() + 1, copy);
    URUILayerCommand release{};
    release.kind = 5;
    release.source = 4;
    release.command_index = 4;
    saved.push_back(release);
    assert(storage.Begin(303, draws, transforms, 4, nullptr, 0, saved.data(), uint32_t(saved.size()), input) == 1);
    assert(storage.Resolve(input.lease, nullptr, Resolve, ready) == 1 && ready.ready &&
           region(ready).bounds[2] == 4008);
    assert(storage.Release(input.lease) == 1);
    // 任意回调的目标域没有只读几何证明。
    URUIPaintCommand callback{1, 1, 0, 0};
    assert(storage.Begin(304, draws, transforms, 4, &callback, 1, layers.data(), uint32_t(layers.size()), input) == 1);
    assert(storage.Resolve(input.lease, nullptr, Resolve, ready) == 1 && ready.ready &&
           region(ready).bounds[2] >= 4008);
    assert(storage.Release(input.lease) == 1);
}

static void RetiredEpochStorageRegression()
{
    Session storage;
    URUICommand draw{};
    draw.geometry = 1;
    URUIPlanBindings bindings{};
    uint64_t first = 0;
    uint64_t last = 0;
    for (uint64_t epoch = 401; epoch <= 433; epoch++)
    {
        URUIPlanInputPacket input{};
        URUIRenderPlanPacket plan{};
        storage.Invalidate(epoch);
        assert(storage.Begin(epoch, &draw, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(storage.Compile(input.lease, bindings, Resolve, plan) == 1);
        if (epoch == 401)
        {
            first = plan.geometry_bytes;
        }
        last = plan.geometry_bytes;
        assert(storage.Release(input.lease) == 1 && storage.Release(plan.lease) == 1);
    }
    assert(last <= first * 2);
    Session pinned;
    URUIPlanInputPacket input{};
    URUIRenderPlanPacket held{};
    URUIGeometryView view{};
    assert(pinned.Begin(440, &draw, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
    assert(pinned.Compile(input.lease, bindings, Resolve, held) == 1);
    const auto geometry = held.operations[0].geometry;
    assert(pinned.Release(input.lease) == 1);
    pinned.Invalidate(450);
    assert(pinned.Geometry(held.lease, geometry, view) == 1 && view.vertex_count == 4 && view.vertices[1].x == 8);
    URUIPlanInputPacket pending[3]{};
    for (auto& item : pending)
    {
        assert(pinned.Begin(450, &draw, &identity, 1, nullptr, 0, nullptr, 0, item) == 1);
    }
    assert(pinned.Begin(499, &draw, &identity, 1, nullptr, 0, nullptr, 0, input) == -5);
    URUIPlanPreparePacket prepared{};
    assert(pinned.Resolve(pending[0].lease, nullptr, Resolve, prepared) == 1);
    for (auto& item : pending)
    {
        assert(pinned.Release(item.lease) == 1);
    }
    assert(pinned.Begin(499, nullptr, &identity, 1, nullptr, 0, nullptr, 0, input) == -1);
    assert(pinned.Geometry(held.lease, geometry, view) == 1 && view.vertices[1].x == 8);
    assert(pinned.Release(held.lease) == 1);
    pinned.Invalidate(500);
    URUILayerCommand layers[3]{};
    layers[0].kind = 1;
    layers[0].target = 1;
    layers[1].kind = 2;
    layers[1].source = 1;
    layers[2].kind = 4;
    layers[2].source = 1;
    assert(pinned.Begin(500, nullptr, nullptr, 0, nullptr, 0, layers, 3, input) == 1);
    assert(pinned.Compile(input.lease, bindings, Resolve, held) == 1);
    view.vertex_count = 123;
    assert(pinned.Geometry(held.lease, 0, view) == -2 && !view.vertex_count && !view.vertices && !view.indices);
    assert(pinned.Release(input.lease) == 1 && pinned.Release(held.lease) == 1);
}

static void SharedRasterPassRegression()
{
    // 真实颜色域交错不能重排；超过16个subpass须在已解析的线性边界分段。
    for (const uint32_t groups : {1u, 3u, 8u, 9u, 17u})
    {
        for (const bool capable : {false, true})
        {
            Session session;
            session.Viewport(128, 128);
            std::vector<URUICommand> draws(groups * 2);
            std::vector<std::array<float, 16>> transforms(draws.size(), identity);
            for (uint32_t n = 0; n < draws.size(); n++)
            {
                draws[n].geometry = 1;
                draws[n].texture = n % 2 ? 202 : 101;
            }
            URUIPlanTextureBinding textures[2] = {{101, 1, 0, 1}, {202, 1, 1, 1}};
            URUIPlanBindings bindings{};
            bindings.textures = textures;
            bindings.texture_count = 2;
            bindings.frame_flags = 1 | 8 | 16 | 64 | (capable ? 256 : 0);
            URUIPlanInputPacket input{};
            URUIRenderPlanPacket plan{};
            assert(session.Begin(198, draws.data(), transforms.data(), uint32_t(draws.size()), nullptr, 0, nullptr, 0,
                                 input) == 1);
            assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
            assert(plan.operation_count == groups * 2 + 2);
            uint32_t starts = 0;
            uint32_t ends = 0;
            uint32_t total_groups = 0;
            for (uint32_t n = 0; n < plan.operation_count; n++)
            {
                const auto& op = plan.operations[n];
                if (op.reason & 0x20000000u)
                {
                    starts++;
                    total_groups += ((op.reason >> 24) & 7) + 1;
                    assert(op.kind == URUI_PLAN_GEOMETRY && op.textures[0] == 101);
                }
                if (op.reason & 0x10000000u)
                {
                    ends++;
                    assert(op.kind == URUI_PLAN_GEOMETRY && op.textures[0] == 202);
                }
            }
            std::cout << "shared groups=" << groups << " capable=" << capable << " starts=" << starts
                      << " ends=" << ends << std::endl;
            assert(starts == (capable ? (groups + 7) / 8 : 0) && ends == starts);
            assert(total_groups == (capable ? groups : 0));
            bool encoded = false;
            for (uint32_t n = 0; n < plan.target_count; n++)
            {
                if (plan.targets[n].slot == -1)
                {
                    encoded = true;
                    assert(plan.targets[n].kind == (capable ? 7u : 2u));
                }
            }
            assert(encoded);
            assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
        }
    }
    {
        Session callbacks;
        callbacks.Viewport(128, 128);
        URUICommand draws[6]{};
        std::array<float, 16> transforms[6] = {identity, identity, identity, identity, identity, identity};
        for (uint32_t n = 0; n < 6; n++)
        {
            draws[n].geometry = 1;
            draws[n].texture = n % 2 ? 202 : 101;
        }
        URUIPaintCommand paint{11, 22, 2, 0};
        URUIPlanExternalBinding callback{2, 1};
        URUIPlanTextureBinding textures[2] = {{101, 1, 0, 1}, {202, 1, 1, 1}};
        URUIPlanBindings bindings{};
        bindings.frame_flags = 1 | 8 | 16 | 64 | 256;
        bindings.textures = textures;
        bindings.texture_count = 2;
        bindings.externals = &callback;
        bindings.external_count = 1;
        URUIPlanInputPacket input{};
        URUIRenderPlanPacket plan{};
        assert(callbacks.Begin(199, draws, transforms, 6, &paint, 1, nullptr, 0, input) == 1);
        assert(callbacks.Compile(input.lease, bindings, Resolve, plan) == 1);
        bool inside = false;
        uint32_t spans = 0;
        for (uint32_t n = 0; n < plan.operation_count; n++)
        {
            const auto& op = plan.operations[n];
            if (op.reason & 0x20000000u)
            {
                assert(!inside);
                inside = true;
                spans++;
            }
            if (op.kind == URUI_PLAN_EXTERNAL)
            {
                assert(!inside);
            }
            if (op.reason & 0x10000000u)
            {
                assert(inside);
                inside = false;
            }
        }
        assert(!inside && spans == 2);
        assert(callbacks.Release(plan.lease) == 1 && callbacks.Release(input.lease) == 1);
    }
    {
        Session offscreen;
        offscreen.Viewport(128, 128);
        URUICommand draws[3]{};
        std::array<float, 16> transforms[3] = {identity, identity, identity};
        for (auto& draw : draws)
        {
            draw.geometry = 1;
            draw.texture = 101;
        }
        URUIPaintCommand paint{11, 22, 1, 0};
        URUIPlanExternalBinding callback{1, 4};
        URUIPlanTextureBinding texture{101, 1, 0, 1};
        URUIPlanBindings bindings{};
        bindings.frame_flags = 1 | 8 | 16 | 64 | 256;
        bindings.textures = &texture;
        bindings.texture_count = 1;
        bindings.externals = &callback;
        bindings.external_count = 1;
        URUIPlanInputPacket input{};
        URUIRenderPlanPacket plan{};
        assert(offscreen.Begin(200, draws, transforms, 3, &paint, 1, nullptr, 0, input) == 1);
        assert(offscreen.Compile(input.lease, bindings, Resolve, plan) == 1);
        // 离屏回调不应迫使父层提前解析到LDR，再编码会改变低alpha的量化边界。
        for (uint32_t n = 0; n < plan.operation_count; n++)
        {
            assert(!(plan.operations[n].reason & 0x20000000u));
        }
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            if (plan.targets[n].slot == -1)
            {
                assert(plan.targets[n].kind == 2);
            }
        }
        assert(offscreen.Release(plan.lease) == 1 && offscreen.Release(input.lease) == 1);
    }
    {
        Session final_encoded;
        final_encoded.Viewport(128, 128);
        URUICommand draws[3]{};
        std::array<float, 16> transforms[3] = {identity, identity, identity};
        for (uint32_t n = 0; n < 3; n++)
        {
            draws[n].geometry = 1;
            draws[n].texture = n == 1 ? 202 : 101;
        }
        URUIPlanTextureBinding textures[2] = {{101, 1, 0, 1}, {202, 1, 1, 1}};
        URUIPlanBindings bindings{};
        bindings.frame_flags = 1 | 8 | 16 | 64 | 256;
        bindings.textures = textures;
        bindings.texture_count = 2;
        URUIPlanInputPacket input{};
        URUIRenderPlanPacket plan{};
        assert(final_encoded.Begin(201, draws, transforms, 3, nullptr, 0, nullptr, 0, input) == 1);
        assert(final_encoded.Compile(input.lease, bindings, Resolve, plan) == 1);
        // 最后仍处于编码域时，必须保留旧的直接解析；不能为了共享而新增整屏复制。
        for (uint32_t n = 0; n < plan.operation_count; n++)
        {
            assert(!(plan.operations[n].reason & 0x20000000u));
        }
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            if (plan.targets[n].slot == -1)
            {
                assert(plan.targets[n].kind == 2);
            }
        }
        assert(final_encoded.Release(plan.lease) == 1 && final_encoded.Release(input.lease) == 1);
    }
}
int main()
{
    SharedRasterPassRegression();
    watch_heap = true;
    auto* positive = ::operator new(4096);
    ::operator delete(positive);
    watch_heap = false;
    assert(heap_allocations == 1 && heap_frees == 1 && heap_bytes == 4096);
    heap_allocations = heap_frees = heap_bytes = 0;
    TerminalMaskStorageRegression();
    RetiredEpochStorageRegression();
    static_assert(sizeof(URUIPlanVertex) == 32);
    static_assert(sizeof(URUIPlanParameter) == 128);
    Session session;
    URUICommand commands[3]{};
    std::array<float, 16> matrices[3]{identity, identity, identity};
    for (int n = 0; n < 3; n++)
    {
        commands[n].geometry = 1;
        commands[n].texture = 7;
        commands[n].tx = float(n * 16);
    }
    URUIPlanInputPacket input{};
    assert(session.Begin(1, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == 1);
    URUIPlanBindings bindings{};
    URUIRenderPlanPacket plan{};
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    assert(plan.operation_count == 1 && plan.operations[0].kind == URUI_PLAN_GEOMETRY);
    auto batch = plan.operations[0].geometry;
    URUIGeometryView mesh{};
    assert(session.Geometry(plan.lease, batch, mesh) == 1 && mesh.vertex_count == 12);
    assert(mesh.vertices[4].x == 16 && mesh.vertices[4].parameter == 1);
    const auto old_parameter = plan.parameters[1].matrix[12];
    assert(mesh.vertices[4].x + old_parameter == 16);
    assert(session.Release(input.lease) == 1);
    commands[1].tx = 17;
    assert(session.Begin(1, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == 1);
    URUIRenderPlanPacket second{};
    assert(session.Compile(input.lease, bindings, Resolve, second) == 1);
    assert(second.operations[0].geometry == batch);
    assert(Position(mesh.vertices[4], second.parameters[1])[0] == 17 &&
           Position(mesh.vertices[4], plan.parameters[1])[0] == 16);
    assert(session.Release(second.lease) == 1 && session.Release(input.lease) == 1);
    assert(session.Release(plan.lease) == 1);
    assert(session.Geometry(plan.lease, batch, mesh) == -2 && mesh.vertices == nullptr);

    // Canonical batches keep a stable initial layout origin. Later layout and
    // CSS transforms remain delta parameters, including a full projective row.
    {
        Session anchored;
        URUICommand draw{};
        draw.geometry = 1;
        draw.tx = 16;
        draw.ty = -4;
        auto matrix = identity;
        URUIPlanInputPacket in{};
        URUIRenderPlanPacket a{};
        URUIRenderPlanPacket b{};
        URUIGeometryView vertices{};
        assert(anchored.Begin(72, &draw, &matrix, 1, nullptr, 0, nullptr, 0, in) == 1);
        assert(anchored.Compile(in.lease, bindings, Resolve, a) == 1);
        assert(anchored.Geometry(a.lease, a.operations[0].geometry, vertices) == 1);
        assert(vertices.vertices[0].x == 16 && vertices.vertices[0].y == -4);
        assert(a.parameters[0].matrix[12] == 0 && a.parameters[0].matrix[13] == 0 &&
               (a.operations[0].reason & 0x40000000u));
        assert(anchored.Release(in.lease) == 1);
        draw.tx = 17;
        matrix[0] = 2;
        matrix[3] = .001f;
        assert(anchored.Begin(72, &draw, &matrix, 1, nullptr, 0, nullptr, 0, in) == 1);
        assert(anchored.Compile(in.lease, bindings, Resolve, b) == 1);
        assert(b.operations[0].geometry == a.operations[0].geometry && !(b.operations[0].reason & 0x40000000u));
        const auto* m = b.parameters[0].matrix;
        const auto& v = vertices.vertices[0];
        const auto world = Position(v, b.parameters[0]);
        assert(world[0] == 34 && std::abs(world[3] - (matrix[3] * 17 + 1)) < .000001f);
        assert(a.parameters[0].matrix[12] == 0);
        assert(anchored.Release(a.lease) == 1 && anchored.Release(b.lease) == 1 && anchored.Release(in.lease) == 1);
    }

    // 大布局坐标经父变换回到屏幕时，必须保留源顶点亚像素与乘法精度。
    {
        for (bool fractional : {true, false})
        {
            Session precision;
            URUICommand draw{};
            draw.geometry = 1;
            draw.tx = 16777216.f;
            auto matrix = identity;
            URUIVertex points[4];
            std::copy_n(quad, 4, points);
            if (fractional)
            {
                for (auto& point : points)
                {
                    point.x += .25f;
                }
            }
            auto resolve = [&](uint64_t id) { return id ? GeometrySource{points, 4, triangles, 6} : GeometrySource{}; };
            URUIPlanInputPacket in{};
            URUIRenderPlanPacket initial{};
            URUIRenderPlanPacket moved{};
            URUIGeometryView vertices{};
            assert(precision.Begin(73, &draw, &matrix, 1, nullptr, 0, nullptr, 0, in) == 1);
            assert(precision.Compile(in.lease, bindings, resolve, initial) == 1);
            assert(precision.Release(in.lease) == 1);
            matrix[0] = fractional ? 1.f : 1.1f;
            matrix[12] = -matrix[0] * draw.tx;
            assert(precision.Begin(73, &draw, &matrix, 1, nullptr, 0, nullptr, 0, in) == 1);
            assert(precision.Compile(in.lease, bindings, resolve, moved) == 1);
            assert(initial.operations[0].geometry == moved.operations[0].geometry);
            assert(precision.Geometry(moved.lease, moved.operations[0].geometry, vertices) == 1);
            const auto& parameter = moved.parameters[0];
            const auto* m = parameter.matrix;
            const bool anchored = (uint32_t(parameter.metadata[0]) & 512u) != 0;
            for (uint32_t vertex = 0; vertex < 4; vertex++)
            {
                const auto& v = vertices.vertices[vertex];
                const float local = v.x - (anchored ? m[8] : 0);
                const float actual = m[0] * local + m[12];
                const float expected = matrix[0] * points[vertex].x + (matrix[12] + matrix[0] * draw.tx);
                if (std::abs(actual - expected) > .00001f)
                {
                    std::cerr << "coordinate precision: fractional=" << fractional << " vertex=" << vertex
                              << " expected=" << expected << " actual=" << actual << "\n";
                    assert(false);
                }
            }
            assert(precision.Release(in.lease) == 1 && precision.Release(moved.lease) == 1 &&
                   precision.Release(initial.lease) == 1);
        }
    }

    // A stable nine-texture stream must not rebuild its spatial dependency graph.
    {
        Session cached;
        URUICommand draws[18]{};
        std::array<float, 16> transforms[18];
        URUIPlanTextureBinding resources[9]{};
        for (int n = 0; n < 18; n++)
        {
            draws[n].geometry = 1;
            draws[n].texture = n % 9 + 1;
            draws[n].tx = float(n * 16);
            transforms[n] = identity;
        }
        for (int n = 0; n < 9; n++)
        {
            resources[n] = {uint64_t(n + 1), 1, 0, 1};
        }
        URUIPlanBindings inputs{};
        inputs.textures = resources;
        inputs.texture_count = 9;
        auto compile = [&](uint64_t generation, bool overlap = false)
        {
            URUIPlanInputPacket in{};
            URUIRenderPlanPacket out{};
            assert(cached.Begin(generation, draws, transforms, 18, nullptr, 0, nullptr, 0, in) == 1);
            assert(cached.Compile(in.lease, inputs, Resolve, out) == 1);
            if (overlap)
            {
                int position = 0;
                int early = -1;
                int late = -1;
                for (uint32_t op = 0; op < out.operation_count; op++)
                {
                    URUIGeometryView geometry{};
                    assert(cached.Geometry(out.lease, out.operations[op].geometry, geometry) == 1);
                    for (uint32_t v = 0; v < geometry.vertex_count; v += 4, position++)
                    {
                        if (geometry.vertices[v].parameter == 8)
                        {
                            early = position;
                        }
                        if (geometry.vertices[v].parameter == 9)
                        {
                            late = position;
                        }
                    }
                }
                assert(early >= 0 && early < late);
            }
            assert(cached.Release(out.lease) == 1 && cached.Release(in.lease) == 1);
        };
        compile(77);
        const auto builds = cached.SortBuilds();
        assert(builds > 0);
        compile(77);
        assert(cached.SortBuilds() == builds);
        draws[8].tx = draws[9].tx;
        compile(77, true);
        assert(cached.SortBuilds() == builds + 1);
        transforms[17][12] = .5f;
        compile(77, true);
        assert(cached.SortBuilds() == builds + 2);
        resources[8].domain = 1;
        compile(77, true);
        assert(cached.SortBuilds() == builds + 3);
        compile(78, true);
        assert(cached.SortBuilds() == builds + 4);
    }

    // Parameter-only changes must not scan every retired canonical batch.
    {
        Session lookup;
        URUICommand draw{};
        draw.texture = 7;
        auto matrix = identity;
        URUIPlanUniform uniform{{1, 1, 1, 1}, {1, 1, 0, 0}};
        URUIPlanBindings inputs{};
        auto compile = [&](uint64_t source, float tint, uint64_t generation = 83)
        {
            draw.geometry = source;
            uniform.tint[0] = tint;
            URUIPlanInputPacket input{};
            URUIRenderPlanPacket output{};
            assert(lookup.Begin(generation, &draw, &matrix, 1, nullptr, 0, nullptr, 0, input, &uniform) == 1);
            assert(lookup.Compile(input.lease, inputs, Resolve, output) == 1);
            const auto id = output.operations[0].geometry;
            assert(output.parameters[0].tint[0] == tint);
            assert(lookup.Release(input.lease) == 1 && lookup.Release(output.lease) == 1);
            return id;
        };
        for (uint64_t id = 1; id <= 512; id++)
        {
            compile(id, 1);
        }
        const auto stable = compile(17, .5f);
        const auto before = lookup.GeometrySearches();
        for (int n = 0; n < 64; n++)
        {
            assert(compile(17, n % 2 ? .5f : 1.f) == stable);
        }
        const auto probes = lookup.GeometrySearches() - before;
        std::cout << "stable geometry lookup probes: " << probes << " / 64 requests\n";
        if (probes > 128)
        {
            std::cerr << "stable geometry lookup probes: " << probes << ", expected <=128\n";
            assert(false);
        }
        assert(compile(513, 1) != stable);
        assert(compile(17, .5f) == stable);
        assert(compile(17, 1) == stable);
        assert(compile(17, .5f, 84) != stable);
    }

    // Independent corner oracle for signed affine scales and shears.
    {
        Session affine;
        URUICommand draw{};
        draw.geometry = 1;
        draw.tx = .25f;
        draw.ty = -.75f;
        for (int sample = 0; sample < 64; sample++)
        {
            auto matrix = identity;
            matrix[0] = (sample & 1 ? -1.f : 1.f) * (sample + 1) * .125f;
            matrix[1] = -.5f;
            matrix[4] = .125f;
            matrix[5] = (sample & 2 ? -1.f : 1.f) * 2.25f;
            matrix[12] = float(sample - 32);
            URUIPlanInputPacket in{};
            URUIRenderPlanPacket out{};
            assert(affine.Begin(81, &draw, &matrix, 1, nullptr, 0, nullptr, 0, in) == 1);
            assert(affine.Compile(in.lease, bindings, Resolve, out) == 1);
            assert(out.operation_count == 1);
            const auto* m = out.parameters[0].matrix;
            double x0 = INFINITY;
            double y0 = INFINITY;
            double x1 = -INFINITY;
            double y1 = -INFINITY;
            URUIGeometryView baked{};
            assert(affine.Geometry(out.lease, out.operations[0].geometry, baked) == 1);
            for (uint32_t vertex = 0; vertex < baked.vertex_count; vertex++)
            {
                const auto& v = baked.vertices[vertex];
                const bool anchored = (uint32_t(out.parameters[0].metadata[0]) & 512u) != 0;
                const float local_x = v.x - (anchored ? m[8] : 0);
                const float local_y = v.y - (anchored ? m[9] : 0);
                const double x = double(m[0]) * local_x + double(m[4]) * local_y + m[12],
                             y = double(m[1]) * local_x + double(m[5]) * local_y + m[13];
                x0 = std::min(x0, x);
                y0 = std::min(y0, y);
                x1 = std::max(x1, x);
                y1 = std::max(y1, y);
            }
            const float expected[4] = {float(x0), float(y0), float(x1), float(y1)};
            for (int n = 0; n < 4; n++)
            {
                assert(out.operations[0].bounds[n] == expected[n]);
            }
            assert(affine.Release(out.lease) == 1 && affine.Release(in.lease) == 1);
        }
    }

    // Reuse exact ordinary inputs while keeping independent immutable output leases.
    {
        Session reuse;
        URUICommand draw{};
        draw.geometry = 1;
        draw.texture = 7;
        auto matrix = identity;
        URUIPlanUniform uniform{{1, 1, 1, 1}, {1, 1, 0, 0}};
        URUIPlanTextureBinding texture{7, 1, 0, 1};
        URUIPlanBindings inputs{};
        inputs.textures = &texture;
        inputs.texture_count = 1;
        auto run = [&](uint64_t generation, URUIRenderPlanPacket& output, auto resolve)
        {
            URUIPlanInputPacket input{};
            assert(reuse.Begin(generation, &draw, &matrix, 1, nullptr, 0, nullptr, 0, input, &uniform) == 1);
            const auto result = reuse.Compile(input.lease, inputs, resolve, output);
            assert(reuse.Release(input.lease) == 1);
            return result;
        };
        URUIRenderPlanPacket a{};
        URUIRenderPlanPacket b{};
        assert(run(91, a, Resolve) == 1);
        assert(run(91, b, Resolve) == 1);
        assert(reuse.OrdinaryReuses() == 1 && a.lease != b.lease && a.parameters != b.parameters &&
               a.parameters[0].tint[0] == 1);
        assert(reuse.Release(b.lease) == 1);
        texture.version = 2;
        assert(run(91, b, Resolve) == 1 && reuse.OrdinaryReuses() == 1);
        assert(reuse.Release(b.lease) == 1);
        uniform.tint[0] = .5f;
        assert(run(91, b, Resolve) == 1 && reuse.OrdinaryReuses() == 1 && b.parameters[0].tint[0] == .5f &&
               a.parameters[0].tint[0] == 1);
        assert(reuse.Release(b.lease) == 1);
        assert(run(91, b, Resolve) == 1 && reuse.OrdinaryReuses() == 2);
        assert(reuse.Release(b.lease) == 1);
        auto expired = [](uint64_t) { return GeometrySource{}; };
        assert(run(91, b, expired) == -2 && b.lease == 0);
        assert(run(92, b, Resolve) == 1 && reuse.OrdinaryReuses() == 2);
        assert(reuse.Release(b.lease) == 1 && reuse.Release(a.lease) == 1);
    }

    // Warm up both input/output slots, then prove native allocator traffic itself is zero.
    for (int n = 0; n < 300; n++)
    {
        commands[1].tx = float(n % 3);
        assert(session.Begin(1, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == 1);
        assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
        assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    }
    auto before = session.Allocations();
    for (int n = 0; n < 32; n++)
    {
        watch_heap = true;
        commands[1].tx = float(n % 5);
        assert(session.Begin(1, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == 1);
        assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
        assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
        watch_heap = false;
    }
    auto after = session.Allocations();
    assert(before.allocations == after.allocations && before.frees == after.frees && before.bytes == after.bytes);
    assert(heap_allocations == 0 && heap_frees == 0 && heap_bytes == 0);

    // Parameter textures contain values, not document-owned resource identities.
    // Exact equal values can be shared across document epochs while geometry stays isolated.
    assert(session.Begin(10, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    const auto immutable_parameters = plan.parameter_version;
    const auto immutable_page = plan.parameter_pages[0];
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    assert(session.Begin(11, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    assert(plan.parameter_version == immutable_parameters && plan.parameter_pages[0] == immutable_page);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);

    // Exact external slot and every layer boundary survive compilation.
    URUIPaintCommand paint{11, 22, 1, 0};
    URUILayerCommand layer[2]{};
    layer[0].command_index = 2;
    layer[0].kind = 1;
    layer[0].target = 1;
    layer[1].command_index = 3;
    layer[1].kind = 2;
    layer[1].source = 1;
    assert(session.Begin(1, commands, matrices, 3, &paint, 1, layer, 2, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    assert(plan.operation_count == 5);
    assert(plan.operations[1].kind == URUI_PLAN_EXTERNAL && plan.operations[1].command_index == 1);
    assert(plan.operations[2].kind == URUI_PLAN_LAYER && plan.operations[2].layer_index == 0);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);

    // An empty prepared callback is transparent to native batching; preprocessing
    // plus DrawOriginal stays an ordered external operation followed by geometry.
    URUIPlanExternalBinding external{1, 0};
    bindings.externals = &external;
    bindings.external_count = 1;
    assert(session.Begin(1, commands, matrices, 3, &paint, 1, nullptr, 0, input) == 1);
    assert(input.command_layers[0] == 0 && input.command_layers[2] == 0);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    assert(plan.operation_count == 1 && plan.operations[0].count == 2);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    // replacement writes cannot also request original geometry.
    external.flags = 3;
    assert(session.Begin(1, commands, matrices, 3, &paint, 1, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == -1 && !plan.lease);
    assert(session.Release(input.lease) == 1);
    bindings.externals = nullptr;
    bindings.external_count = 0;

    // Neighborhood filter input ignores only its own inherited output clip;
    // the final composite still clips, and replacing metadata restores the input.
    URUICommand clipped = commands[0];
    clipped.flags = 1;
    clipped.clip_w = clipped.clip_h = 10;
    URUILayerCommand halo[4]{};
    halo[0].kind = 1;
    halo[0].target = 1;
    halo[1].kind = 2;
    halo[1].source = 1;
    halo[1].command_index = 1;
    halo[2].kind = 6;
    halo[2].source = 1;
    halo[2].mask = 4;
    halo[2].command_index = 1;
    halo[3].kind = 4;
    halo[3].source = 1;
    halo[3].flags = 1;
    halo[3].clip_w = halo[3].clip_h = 10;
    halo[3].command_index = 1;
    LayerEffect halo_effect{2, 0, 0, 4, 1, 1, 0, 0, 0};
    assert(session.Begin(1, &clipped, &identity, 1, nullptr, 0, halo, 4, input) == 1);
    assert(session.SetLayerEffects(input.lease, &halo_effect, 1) == 1);
    assert(input.commands[0].flags == 0 && clipped.flags == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1 &&
           (uint32_t(plan.parameters[0].metadata[0]) & 1) == 0);
    assert(session.Release(plan.lease) == 1);
    halo_effect.kind = 0;
    assert(session.SetLayerEffects(input.lease, &halo_effect, 1) == 1 && input.commands[0].flags == 1);
    assert(session.Release(input.lease) == 1);

    // Point-filter programs use the same native batch and a fixed GPU field
    // layout. Changing a value never changes local vertices or batch identity.
    URUIPaintCommand point_paints[2]{{1, 10, 0, 2}, {2, 11, 2, 2}};
    ColorFilterInput point_values[2]{{0, 2, .5f}, {2, 0, 1.f}};
    assert(session.Begin(1, commands, matrices, 3, point_paints, 2, nullptr, 0, input) == 1);
    assert(session.SetColorFilters(input.lease, point_values, 2) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    assert(plan.operation_count == 1 && plan.operations[0].count == 3);
    assert(plan.extra_parameter_count == 4 && plan.extra_parameters[5] == .5f);
    assert(plan.parameters[0].metadata[1] == 1 && plan.parameters[0].metadata[2] == 24 &&
           plan.parameters[0].metadata[3] == 0);
    const auto filter_batch = plan.operations[0].geometry;
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    point_values[0].value = .75f;
    assert(session.Begin(1, commands, matrices, 3, point_paints, 2, nullptr, 0, input) == 1);
    assert(session.SetColorFilters(input.lease, point_values, 2) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1 && plan.operations[0].geometry == filter_batch);
    assert(plan.extra_parameters[5] == .75f);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);

    for (int n = 0; n < 300; n++)
    {
        point_values[0].value = float(n % 3) * .25f;
        assert(session.Begin(1, commands, matrices, 3, point_paints, 2, nullptr, 0, input) == 1);
        assert(session.SetColorFilters(input.lease, point_values, 2) == 1);
        assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
        assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    }
    before = session.Allocations();
    heap_allocations = heap_frees = heap_bytes = 0;
    for (int n = 0; n < 32; n++)
    {
        watch_heap = true;
        point_values[0].value = float(n % 3) * .25f;
        assert(session.Begin(1, commands, matrices, 3, point_paints, 2, nullptr, 0, input) == 1);
        assert(session.SetColorFilters(input.lease, point_values, 2) == 1);
        assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
        assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
        watch_heap = false;
    }
    after = session.Allocations();
    assert(before.allocations == after.allocations && before.frees == after.frees && before.bytes == after.bytes);
    assert(heap_allocations == 0 && heap_frees == 0 && heap_bytes == 0);

    // Only a proven non-overlapping leaf quad plus one normalized texture mask
    // can eliminate the isolated group. Scaled/unknown domains retain it.
    URUICommand masked[2]{commands[0], commands[0]};
    masked[1].texture = 8;
    URUILayerCommand mask_layers[8]{};
    mask_layers[0].kind = 1;
    mask_layers[0].target = 1;
    mask_layers[1].kind = 1;
    mask_layers[1].target = 2;
    mask_layers[1].command_index = 1;
    mask_layers[2].kind = 3;
    mask_layers[2].source = 2;
    mask_layers[2].target = 3;
    mask_layers[2].command_index = 2;
    mask_layers[3].kind = 2;
    mask_layers[3].source = 2;
    mask_layers[3].target = 1;
    mask_layers[3].command_index = 2;
    mask_layers[4].kind = 6;
    mask_layers[4].source = mask_layers[4].target = 1;
    mask_layers[4].mask = 3;
    mask_layers[4].blend = 1;
    mask_layers[4].command_index = 2;
    mask_layers[5].kind = 4;
    mask_layers[5].source = 1;
    mask_layers[5].command_index = 2;
    mask_layers[6].kind = 2;
    mask_layers[6].source = 1;
    mask_layers[6].command_index = 2;
    mask_layers[7].kind = 5;
    mask_layers[7].source = 3;
    mask_layers[7].command_index = 2;
    URUIPlanTextureBinding mask_textures[2]{{7, 1, 0, 1}, {8, 1, 0, 1}};
    URUIPlanBindings mask_bindings{};
    mask_bindings.textures = mask_textures;
    mask_bindings.texture_count = 2;
    mask_bindings.frame_flags = 9;
    URUIPlanPreparePacket mask_ready{};
    assert(session.Begin(1, masked, matrices, 2, nullptr, 0, mask_layers, 8, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1 && mask_ready.ready);
    assert(mask_ready.region_count == 0 && mask_ready.layer_index_count == 0);
    assert(session.Compile(input.lease, mask_bindings, Resolve, plan) == 1);
    assert(plan.operation_count == 1 && plan.operations[0].kind == URUI_PLAN_GEOMETRY && plan.operations[0].count == 1);
    assert(plan.operations[0].mask_texture == 8 && (int(plan.parameters[0].metadata[0]) & 256));
    assert(plan.target_count == 1 && plan.targets[0].layer == 0 && plan.targets[0].slot == -1);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    // 降为根几何的遮罩仍须保留屏幕读取语义，不能按原始离屏 layer 跳过。
    mask_bindings.frame_flags = 1 | 8 | 16;
    assert(session.Begin(1, masked, matrices, 2, nullptr, 0, mask_layers, 8, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1 && mask_ready.ready);
    assert(mask_ready.target_flags & 1);
    assert(session.Compile(input.lease, mask_bindings, Resolve, plan) == 1 && plan.operation_count == 3);
    assert(plan.operations[0].kind == URUI_PLAN_ROOT_COPY && plan.operations[2].kind == URUI_PLAN_ROOT_PRESENT);
    assert(plan.operations[0].reason & 1);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    mask_bindings.frame_flags = 0;
    assert(session.Begin(1, masked, matrices, 2, nullptr, 0, mask_layers, 8, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1 &&
           mask_ready.region_count == 3);
    assert(session.Compile(input.lease, mask_bindings, Resolve, plan) == 1 && plan.operation_count > 1);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);

    // Saved masks may alias only a source that remains immutable until release.
    URUILayerCommand alias_layers[9]{};
    std::copy_n(mask_layers, 2, alias_layers);
    alias_layers[2].kind = 6;
    alias_layers[2].source = 2;
    alias_layers[2].mask = 900;
    alias_layers[2].command_index = 2;
    std::copy_n(mask_layers + 2, 6, alias_layers + 3);
    assert(session.Begin(1, masked, matrices, 2, nullptr, 0, alias_layers, 9, input) == 1);
    LayerEffect mask_effect{2, 3, 99, 900, 3, 0, 0, 0, 0};
    assert(session.SetLayerEffects(input.lease, &mask_effect, 1) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1);
    for (int step = 0; !mask_ready.ready && step < 8; step++)
    {
        const auto request = mask_ready.requests[0];
        URUIPlanFeedback response{};
        response.request = request.request;
        response.kind = request.kind;
        if (request.kind == URUI_PLAN_ACQUIRE_CAPTURE)
        {
            response.resource = 901;
            response.source_version = 1;
            response.pixel_width = response.pixel_height = 32;
            response.ink[0] = -8;
            response.ink[1] = -8;
            response.ink[2] = response.ink[3] = 32;
        }
        else if (request.kind == URUI_PLAN_PREPARE_ELEMENT)
        {
            std::copy_n(request.source_ink, 4, response.ink);
        }
        assert(session.Resolve(input.lease, &response, Resolve, mask_ready, &mask_bindings) == 1);
    }
    assert(mask_ready.ready);
    const URUIPlanLayerRegion* saved_mask = nullptr;
    const URUIPlanLayerRegion* mask_source = nullptr;
    for (uint32_t n = 0; n < mask_ready.region_count; n++)
    {
        if (mask_ready.regions[n].layer == 3)
        {
            saved_mask = mask_ready.regions + n;
        }
        if (mask_ready.regions[n].layer == 2)
        {
            mask_source = mask_ready.regions + n;
        }
    }
    assert(saved_mask && mask_source &&
           std::memcmp(saved_mask->bounds, mask_source->bounds, sizeof(saved_mask->bounds)) == 0);
    assert((saved_mask->flags & 8) && saved_mask->reserved == 2);
    assert(session.Release(input.lease) == 1);
    // A write after saving requires a distinct snapshot, even with the same bounds.
    std::copy_n(mask_layers, 3, alias_layers);
    alias_layers[3].kind = 6;
    alias_layers[3].source = 2;
    alias_layers[3].mask = 902;
    alias_layers[3].command_index = 2;
    std::copy_n(mask_layers + 3, 5, alias_layers + 4);
    assert(session.Begin(1, masked, matrices, 2, nullptr, 0, alias_layers, 9, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1 && mask_ready.ready);
    for (uint32_t n = 0; n < mask_ready.region_count; n++)
    {
        if (mask_ready.regions[n].layer == 3)
        {
            assert(!(mask_ready.regions[n].flags & 8));
        }
    }
    assert(session.Release(input.lease) == 1);

    mask_bindings.frame_flags = 1;
    std::array<float, 16> edge_matrices[2]{identity, identity};
    edge_matrices[1][12] = .5f;
    assert(session.Begin(1, masked, edge_matrices, 2, nullptr, 0, mask_layers, 8, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1 &&
           mask_ready.region_count == 3);
    assert(session.Release(input.lease) == 1);

    URUICommand mask_groups[8]{};
    std::array<float, 16> group_matrices[8];
    URUILayerCommand group_layers[32]{};
    for (int leaf = 0; leaf < 4; leaf++)
    {
        mask_groups[leaf * 2] = commands[0];
        mask_groups[leaf * 2 + 1] = commands[0];
        mask_groups[leaf * 2].tx = mask_groups[leaf * 2 + 1].tx = float(leaf * 16);
        mask_groups[leaf * 2 + 1].texture = 8 + leaf % 2;
        group_matrices[leaf * 2] = group_matrices[leaf * 2 + 1] = identity;
        for (int operation = 0; operation < 8; operation++)
        {
            auto copy = mask_layers[operation];
            copy.command_index += leaf * 2;
            if (copy.source)
            {
                copy.source += leaf * 3;
            }
            if (copy.target)
            {
                copy.target += leaf * 3;
            }
            if (copy.mask)
            {
                copy.mask += leaf * 3;
            }
            group_layers[leaf * 8 + operation] = copy;
        }
    }
    URUIPlanTextureBinding group_textures[3]{{7, 1, 0, 1}, {8, 1, 0, 1}, {9, 1, 0, 1}};
    mask_bindings.textures = group_textures;
    mask_bindings.texture_count = 3;
    assert(session.Begin(1, mask_groups, group_matrices, 8, nullptr, 0, group_layers, 32, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1 &&
           mask_ready.region_count == 0);
    assert(session.Compile(input.lease, mask_bindings, Resolve, plan) == 1 && plan.operation_count == 2);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    for (auto& command : mask_groups)
    {
        command.tx = 0;
    }
    assert(session.Begin(1, mask_groups, group_matrices, 8, nullptr, 0, group_layers, 32, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, mask_ready, &mask_bindings) == 1);
    assert(session.Compile(input.lease, mask_bindings, Resolve, plan) == 1 && plan.operation_count == 4);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);

    // Expired, cross-thread and epoch mismatch reject access and zero the output.
    assert(session.Compile(input.lease, bindings, Resolve, plan) == -2 && !plan.lease);
    assert(session.Begin(2, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == 1);
    int threaded = 0;
    std::thread other([&] { threaded = session.Compile(input.lease, bindings, Resolve, plan); });
    other.join();
    assert(threaded == -3 && !plan.lease);
    session.Invalidate(3);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == -2 && !plan.lease);
    assert(session.Release(input.lease) == 1);

    URUIPlanInputPacket held[4]{};
    for (auto& item : held)
    {
        assert(session.Begin(3, commands, matrices, 3, nullptr, 0, nullptr, 0, item) == 1);
    }
    assert(session.Begin(3, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == -5 && !input.lease);
    for (auto& item : held)
    {
        assert(session.Release(item.lease) == 1);
    }
    Session tiny(128, 128);
    assert(tiny.Begin(1, commands, matrices, 3, nullptr, 0, nullptr, 0, input) == -4 && !input.lease);

    // Extension callbacks still receive effective source colors/UV, even though
    // the upstream native topology is now white with unit UV.
    URUIPaintCommand mapped_image{1, 2, 0, 1};
    URUIPlanUniform uniform{{.5f, .25f, .125f, .5f}, {.25f, .5f, .125f, .25f}};
    assert(session.Begin(4, commands, &identity, 1, &mapped_image, 1, nullptr, 0, input, &uniform) == 1);
    URUIPlanPreparePacket source_request{};
    assert(session.Resolve(input.lease, nullptr, Resolve, source_request) == 1);
    const auto source_geometry = source_request.requests[0].geometry;
    assert(session.Geometry(input.lease, source_geometry, mesh) == 1);
    assert(mesh.vertices[0].r == 128 && mesh.vertices[0].g == 64 && mesh.vertices[0].a == 128);
    assert(mesh.vertices[1].u == .375f && mesh.vertices[0].v == .25f);
    assert(session.Release(input.lease) == 1);
    assert(session.Geometry(input.lease, source_geometry, mesh) == -2 && !mesh.vertices);

    // 长会话留下旧 epoch 批次时，图片准备和公开源视图也必须能逐出未租用缓存。
    {
        Session pressure(2048, 1024 * 1024);
        URUICommand draw{};
        draw.geometry = 1;
        draw.texture = 7;
        URUIPlanBindings resources{};
        URUIPlanInputPacket in{};
        URUIRenderPlanPacket output{};
        for (uint64_t epoch = 200; epoch < 240; epoch++)
        {
            assert(pressure.Begin(epoch, &draw, &identity, 1, nullptr, 0, nullptr, 0, in) == 1);
            assert(pressure.Compile(in.lease, resources, Resolve, output) == 1);
            assert(pressure.Release(output.lease) == 1 && pressure.Release(in.lease) == 1);
        }
        URUIVertex vertices[16];
        int indices[24];
        for (int n = 0; n < 16; n++)
        {
            vertices[n] = quad[n % 4];
        }
        for (int n = 0; n < 24; n++)
        {
            indices[n] = triangles[n % 6] + (n / 6) * 4;
        }
        auto larger = [&](uint64_t) { return GeometrySource{vertices, 16, indices, 24}; };
        URUIPaintCommand image{1, 2, 0, 1};
        URUIPlanPreparePacket request{};
        URUIGeometryView source{};
        assert(pressure.Begin(241, &draw, &identity, 1, &image, 1, nullptr, 0, in) == 1);
        assert(pressure.Resolve(in.lease, nullptr, larger, request) == 1 && request.count == 1);
        assert(pressure.Geometry(in.lease, request.requests[0].geometry, source) == 1 && source.vertex_count == 16);
        assert(pressure.Release(in.lease) == 1);
        Session leased(2048, 1024 * 1024);
        URUICommand occupied[6];
        std::array<float, 16> occupied_matrices[6];
        for (int n = 0; n < 6; n++)
        {
            occupied[n] = draw;
            occupied[n].tx = float(n * 16);
            occupied_matrices[n] = identity;
        }
        assert(leased.Begin(250, occupied, occupied_matrices, 6, nullptr, 0, nullptr, 0, in) == 1);
        assert(leased.Compile(in.lease, resources, Resolve, output) == 1);
        assert(leased.Release(in.lease) == 1);
        URUIGeometryView held{};
        assert(leased.Geometry(output.lease, output.operations[0].geometry, held) == 1 && held.vertex_count == 24);
        assert(leased.Begin(251, &draw, &identity, 1, &image, 1, nullptr, 0, in) == 1);
        assert(leased.Resolve(in.lease, nullptr, larger, request) == 1);
        const auto identity_before = request.requests[0].geometry;
        assert(leased.Geometry(in.lease, identity_before, source) == -4 && !source.vertices);
        assert(held.vertices[0].r == 255 && held.vertices[20].x == 80);
        assert(leased.Release(output.lease) == 1);
        assert(leased.Geometry(in.lease, identity_before, source) == 1 && source.vertex_count == 16);
        assert(leased.Release(in.lease) == 1);
    }

    // 未知 painter 在任何有限局部网格之外也可画出像素；隔离组必须保守覆盖可见目标。
    {
        Session unknown;
        unknown.Viewport(64, 64);
        URUICommand draw{};
        draw.geometry = 1;
        draw.tx = 16;
        draw.ty = 16;
        URUIPaintCommand painter{11, 22, 0, 0};
        URUILayerCommand group[3]{};
        group[0].kind = 1;
        group[0].target = 1;
        group[1].kind = 2;
        group[1].source = 1;
        group[1].command_index = 1;
        group[2].kind = 4;
        group[2].source = 1;
        group[2].command_index = 1;
        URUIPlanInputPacket in{};
        URUIPlanPreparePacket ready{};
        assert(unknown.Begin(260, &draw, &identity, 1, &painter, 1, group, 3, in) == 1);
        assert(unknown.Resolve(in.lease, nullptr, Resolve, ready) == 1 && ready.ready && ready.region_count == 1);
        const auto* bounds = ready.regions[0].bounds;
        assert(bounds[0] <= 0 && bounds[1] <= 0 && bounds[0] + bounds[2] >= 64 && bounds[1] + bounds[3] >= 64);
        assert(unknown.Release(in.lease) == 1);
    }

    // Image ink is ready before a child capture; its output then enlarges the parent.
    // A real capture binding is required before PrepareElement, exactly once per slot.
    URUICommand content = commands[0];
    URUIPaintCommand image{1, 2, 0, 1};
    URUILayerCommand nested[8]{};
    nested[0].kind = 1;
    nested[0].target = 1;
    nested[1].kind = 1;
    nested[1].target = 2;
    nested[2].kind = 2;
    nested[2].source = 2;
    nested[2].target = 1;
    nested[2].command_index = 1;
    nested[3].kind = 6;
    nested[3].source = 2;
    nested[3].mask = 100;
    nested[3].command_index = 1;
    nested[4].kind = 4;
    nested[4].source = 2;
    nested[4].target = 1;
    nested[4].command_index = 1;
    nested[5].kind = 2;
    nested[5].source = 1;
    nested[5].command_index = 1;
    nested[6].kind = 6;
    nested[6].source = 1;
    nested[6].mask = 101;
    nested[6].command_index = 1;
    nested[7].kind = 4;
    nested[7].source = 1;
    nested[7].command_index = 1;
    assert(session.Begin(4, &content, &identity, 1, &image, 1, nested, 8, input) == 1);
    LayerEffect effects[2]{{3, 3, 10, 100, 3, 0, 0, 0, 0}, {6, 3, 11, 101, 3, 0, 0, 0, 0}};
    assert(session.SetLayerEffects(input.lease, effects, 2) == 1);
    URUIPlanPreparePacket prepare{};
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare) == 1 && prepare.count == 1);
    assert(prepare.requests[0].kind == URUI_PLAN_PREPARE_IMAGE);
    URUIPlanFeedback feedback{};
    auto reply = [&](uint32_t kind)
    {
        feedback.request = prepare.requests[0].request;
        feedback.kind = kind;
        assert(session.Resolve(input.lease, &feedback, Resolve, prepare) == 1);
    };
    feedback.ink[0] = -4;
    feedback.ink[1] = -4;
    feedback.ink[2] = 16;
    feedback.ink[3] = 16;
    reply(URUI_PLAN_PREPARE_IMAGE);
    assert(prepare.requests[0].kind == URUI_PLAN_ELEMENT_LAYOUT);
    reply(URUI_PLAN_ELEMENT_LAYOUT);
    assert(prepare.requests[0].kind == URUI_PLAN_ELEMENT_LAYOUT);
    reply(URUI_PLAN_ELEMENT_LAYOUT);
    assert(prepare.requests[0].kind == URUI_PLAN_ACQUIRE_CAPTURE && prepare.requests[0].capture[2] == 16);
    // A null/placeholder Texture cannot stand in for the real source.
    feedback.request = prepare.requests[0].request;
    feedback.kind = URUI_PLAN_ACQUIRE_CAPTURE;
    assert(session.Resolve(input.lease, &feedback, Resolve, prepare) == -1 && !prepare.requests);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare) == 1);
    feedback.resource = 500;
    feedback.source_version = 9;
    feedback.pixel_width = 32;
    feedback.pixel_height = 32;
    feedback.ink[0] = -12;
    feedback.ink[1] = -12;
    feedback.ink[2] = 32;
    feedback.ink[3] = 32;
    feedback.uv[0] = .25f;
    feedback.uv[1] = .25f;
    feedback.uv[2] = .5f;
    feedback.uv[3] = .5f;
    reply(URUI_PLAN_ACQUIRE_CAPTURE);
    assert(prepare.requests[0].kind == URUI_PLAN_PREPARE_ELEMENT && prepare.requests[0].resource == 500);
    assert(prepare.requests[0].pixel_width == 32 && prepare.requests[0].uv[2] == .5f);
    assert(prepare.requests[0].capture[0] == -12 && prepare.requests[0].capture[2] == 32);
    feedback.ink[0] = -8;
    feedback.ink[1] = -8;
    feedback.ink[2] = 24;
    feedback.ink[3] = 24;
    reply(URUI_PLAN_PREPARE_ELEMENT);
    assert(prepare.requests[0].kind == URUI_PLAN_ACQUIRE_CAPTURE && prepare.requests[0].capture[2] == 24);
    feedback.resource = 501;
    feedback.ink[0] = -12;
    feedback.ink[1] = -12;
    feedback.ink[2] = 32;
    feedback.ink[3] = 32;
    reply(URUI_PLAN_ACQUIRE_CAPTURE);
    assert(prepare.requests[0].resource == 501);
    reply(URUI_PLAN_PREPARE_ELEMENT);
    assert(prepare.ready == 1 && prepare.count == 0);
    assert(prepare.region_count == 2);
    for (uint32_t n = 0; n < prepare.region_count; n++)
    {
        assert(prepare.regions[n].bounds[2] >= 32);
    }
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    // Element ink is local to its element, while parent captures are in window space.
    auto shifted = identity;
    shifted[12] = 100;
    effects[0].projection = shifted;
    assert(session.Begin(9, &content, &shifted, 1, &image, 1, nested, 8, input) == 1);
    assert(session.SetLayerEffects(input.lease, effects, 2) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare) == 1);
    for (int steps = 0; !prepare.ready && steps < 16; steps++)
    {
        const auto request = prepare.requests[0];
        feedback = {};
        feedback.request = request.request;
        feedback.kind = request.kind;
        if (request.kind == URUI_PLAN_PREPARE_IMAGE)
        {
            feedback.ink[0] = -4;
            feedback.ink[1] = -4;
            feedback.ink[2] = 16;
            feedback.ink[3] = 16;
        }
        else if (request.kind == URUI_PLAN_ACQUIRE_CAPTURE)
        {
            if (request.program == 101)
            {
                assert(request.capture[0] == 92 && request.capture[2] == 24);
            }
            feedback.resource = 1000 + steps;
            feedback.source_version = 1;
            feedback.pixel_width = uint32_t(request.capture[2]);
            feedback.pixel_height = uint32_t(request.capture[3]);
            std::copy_n(request.capture, 4, feedback.ink);
        }
        else if (request.kind == URUI_PLAN_PREPARE_ELEMENT)
        {
            if (request.program == 100)
            {
                feedback.ink[0] = -8;
                feedback.ink[1] = -8;
                feedback.ink[2] = 24;
                feedback.ink[3] = 24;
            }
            else
            {
                std::copy_n(request.source_ink, 4, feedback.ink);
            }
        }
        assert(session.Resolve(input.lease, &feedback, Resolve, prepare) == 1);
    }
    assert(prepare.ready);
    assert(session.Release(input.lease) == 1);
    effects[0].projection = identity;
    // Preparing many sibling effects must visit each frozen primitive once,
    // rather than replaying the prefix for every capture request.
    std::vector<URUICommand> siblings(64, content);
    std::vector<std::array<float, 16>> sibling_matrices(64, identity);
    std::vector<URUILayerCommand> sibling_layers(64 * 4);
    std::vector<LayerEffect> sibling_effects(64);
    for (uint32_t n = 0; n < 64; n++)
    {
        const uint64_t id = n + 1;
        auto* layer = sibling_layers.data() + n * 4;
        layer[0].kind = 1;
        layer[0].target = id;
        layer[0].command_index = int(n);
        layer[1].kind = 6;
        layer[1].source = id;
        layer[1].mask = 1000 + n;
        layer[1].command_index = int(n + 1);
        layer[2].kind = 4;
        layer[2].source = id;
        layer[2].command_index = int(n + 1);
        layer[3].kind = 2;
        layer[3].source = id;
        layer[3].command_index = int(n + 1);
        sibling_effects[n] = {n * 4 + 1, 3, 100 + n, 1000 + n, 3, 0, 0, 0, 0};
    }
    assert(session.Begin(10, siblings.data(), sibling_matrices.data(), 64, nullptr, 0, sibling_layers.data(), 256,
                         input) == 1);
    assert(session.SetLayerEffects(input.lease, sibling_effects.data(), 64) == 1);
    uint32_t visits = 0;
    auto counted = [&](uint64_t id)
    {
        visits++;
        return Resolve(id);
    };
    assert(session.Resolve(input.lease, nullptr, counted, prepare) == 1);
    for (uint32_t steps = 0; !prepare.ready && steps < 256; steps++)
    {
        const auto request = prepare.requests[0];
        feedback = {};
        feedback.request = request.request;
        feedback.kind = request.kind;
        if (request.kind == URUI_PLAN_ACQUIRE_CAPTURE)
        {
            feedback.resource = 2000 + steps;
            feedback.source_version = 1;
            feedback.pixel_width = uint32_t(request.capture[2]);
            feedback.pixel_height = uint32_t(request.capture[3]);
            std::copy_n(request.capture, 4, feedback.ink);
        }
        else if (request.kind == URUI_PLAN_PREPARE_ELEMENT)
        {
            std::copy_n(request.source_ink, 4, feedback.ink);
        }
        assert(session.Resolve(input.lease, &feedback, counted, prepare) == 1);
    }
    assert(prepare.ready && visits == 64);
    assert(session.Resolve(input.lease, nullptr, counted, prepare) == 1 && prepare.ready && visits == 64);
    assert(session.Release(input.lease) == 1);
    // Exact per-layer dependencies: a dynamic child invalidates its ancestors,
    // while an unrelated sibling retains its content version.
    URUILayerCommand dependency_layers[10]{};
    dependency_layers[0].kind = 1;
    dependency_layers[0].target = 1;
    dependency_layers[1].kind = 1;
    dependency_layers[1].target = 2;
    dependency_layers[2].kind = 6;
    dependency_layers[2].source = 2;
    dependency_layers[2].mask = 800;
    dependency_layers[2].command_index = 1;
    dependency_layers[3].kind = 4;
    dependency_layers[3].source = 2;
    dependency_layers[3].target = 1;
    dependency_layers[3].command_index = 1;
    dependency_layers[4].kind = 2;
    dependency_layers[4].source = 2;
    dependency_layers[4].target = 1;
    dependency_layers[4].command_index = 1;
    dependency_layers[5].kind = 1;
    dependency_layers[5].target = 3;
    dependency_layers[5].command_index = 1;
    dependency_layers[6].kind = 6;
    dependency_layers[6].source = 3;
    dependency_layers[6].mask = 801;
    dependency_layers[6].command_index = 2;
    dependency_layers[7].kind = 4;
    dependency_layers[7].source = 3;
    dependency_layers[7].target = 1;
    dependency_layers[7].command_index = 2;
    dependency_layers[8].kind = 2;
    dependency_layers[8].source = 3;
    dependency_layers[8].target = 1;
    dependency_layers[8].command_index = 2;
    dependency_layers[9].kind = 2;
    dependency_layers[9].source = 1;
    dependency_layers[9].command_index = 2;
    LayerEffect dependency_effects[2]{{2, 3, 0, 800, 1, 1, 0, 0, 0}, {6, 3, 0, 801, 1, 1, 0, 0, 0}};
    URUICommand dependency_commands[2]{commands[0], commands[0]};
    dependency_commands[1].texture = 8;
    URUIPlanTextureBinding dependency_textures[2]{{7, 1, 0, 1}, {8, 1, 0, 1}};
    URUIPlanBindings dependency_bindings{};
    dependency_bindings.textures = dependency_textures;
    dependency_bindings.texture_count = 2;
    auto dependencies = [&]()
    {
        assert(session.Begin(11, dependency_commands, matrices, 2, nullptr, 0, dependency_layers, 10, input) == 1);
        assert(session.SetLayerEffects(input.lease, dependency_effects, 2) == 1);
        assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &dependency_bindings) == 1 && prepare.ready);
        std::array<uint64_t, 4> versions{};
        for (uint32_t n = 0; n < prepare.region_count; n++)
        {
            versions[prepare.regions[n].layer] = prepare.regions[n].version;
        }
        return versions;
    };
    const auto first_versions = dependencies();
    assert(first_versions[1] && first_versions[2] && first_versions[3]);
    assert(session.Release(input.lease) == 1);
    assert(dependencies() == first_versions);
    assert(session.Release(input.lease) == 1);
    dependency_textures[0].version++;
    const auto changed_versions = dependencies();
    assert(changed_versions[1] != first_versions[1] && changed_versions[2] != first_versions[2] &&
           changed_versions[3] == first_versions[3]);
    URUIPlanLayerBinding cached_sibling{3, changed_versions[3], 1, 0};
    dependency_bindings.layer_bindings = &cached_sibling;
    dependency_bindings.layer_binding_count = 1;
    assert(session.Compile(input.lease, dependency_bindings, Resolve, plan) == 1);
    uint32_t geometry_draws = 0;
    uint32_t skipped_filters = 0;
    for (uint32_t n = 0; n < plan.operation_count; n++)
    {
        const auto& op = plan.operations[n];
        if (op.kind == URUI_PLAN_GEOMETRY)
        {
            geometry_draws++;
        }
        if (op.kind == URUI_PLAN_LAYER && op.layer_index == 6 && (op.reason & 1))
        {
            skipped_filters++;
        }
    }
    assert(geometry_draws == 1 && skipped_filters == 1);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    dependency_bindings.layer_bindings = nullptr;
    dependency_bindings.layer_binding_count = 0;
    dependency_textures[0].domain = 1;
    const auto live_versions = dependencies();
    assert(session.Release(input.lease) == 1);
    const auto live_again = dependencies();
    assert(live_versions[1] != live_again[1] && live_versions[2] != live_again[2] && live_versions[3] == live_again[3]);
    assert(session.Release(input.lease) == 1);
    dependency_textures[0].domain = 0;
    const auto stable_versions = dependencies();
    assert(session.Release(input.lease) == 1);
    URUILayerCommand moved_layers[10];
    std::copy_n(dependency_layers, 10, moved_layers);
    for (auto& layer : moved_layers)
    {
        if (layer.source)
        {
            layer.source += 10;
        }
        if (layer.target)
        {
            layer.target += 10;
        }
    }
    assert(session.Begin(11, dependency_commands, matrices, 2, nullptr, 0, moved_layers, 10, input) == 1);
    assert(session.SetLayerEffects(input.lease, dependency_effects, 2) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &dependency_bindings) == 1 && prepare.ready);
    for (uint32_t n = 0; n < prepare.region_count; n++)
    {
        assert(prepare.regions[n].version == stable_versions[prepare.regions[n].layer - 10]);
    }
    assert(session.Release(input.lease) == 1);
    for (uint32_t n = 0; n < 300; n++)
    {
        dependency_commands[0].tx = float(n);
        dependencies();
        assert(session.Release(input.lease) == 1);
    }
    const auto dependency_before = session.Allocations();
    heap_allocations = heap_frees = heap_bytes = 0;
    watch_heap = true;
    for (uint32_t n = 300; n < 332; n++)
    {
        dependency_commands[0].tx = float(n);
        dependencies();
        assert(session.Release(input.lease) == 1);
    }
    watch_heap = false;
    const auto dependency_after = session.Allocations();
    assert(dependency_before.allocations == dependency_after.allocations &&
           dependency_before.frees == dependency_after.frees && dependency_before.bytes == dependency_after.bytes);
    assert(!heap_allocations && !heap_frees && !heap_bytes);
    // Known external meshes join the same ordered compiler while their matrices
    // remain parameter inputs. An unknown painter still separates the runs.
    URUIPlanExternalGeometry external_mesh{};
    external_mesh.geometry = 500;
    external_mesh.version = 1;
    const float external_positions[12]{0, 0, 0, 8, 0, 0, 8, 8, 0, 0, 8, 0};
    const uint16_t external_indices[6]{0, 1, 2, 0, 2, 3};
    external_mesh.vertex_count = 4;
    external_mesh.index_count = 6;
    external_mesh.stream_count = 1;
    external_mesh.streams[0] = {external_positions, sizeof(external_positions), 12, 0};
    external_mesh.position = {0, 0, URUI_PLAN_FLOAT32, 3};
    external_mesh.indices = external_indices;
    external_mesh.index_bytes = sizeof(external_indices);
    external_mesh.index_stride = 2;
    // Analytic rectangle masks carry a typed primitive instead of an opaque
    // callback; the same raster proof can eliminate only a single leaf group.
    const float unit_positions[12]{0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0};
    auto analytic_geometry = external_mesh;
    analytic_geometry.geometry = 550;
    analytic_geometry.streams[0].data = unit_positions;
    URUIPlanExternalDraw analytic_draw{};
    analytic_draw.command = 1;
    analytic_draw.geometry_index = 0;
    analytic_draw.provider = 950;
    analytic_draw.program = 850;
    analytic_draw.flags = 4;
    std::copy(identity.begin(), identity.end(), analytic_draw.parameter.matrix);
    analytic_draw.parameter.matrix[0] = analytic_draw.parameter.matrix[5] = 8;
    analytic_draw.parameter.tint[0] = 1;
    analytic_draw.parameter.tint[1] = 2;
    analytic_draw.parameter.tint[2] = 3;
    analytic_draw.parameter.tint[3] = 4;
    analytic_draw.parameter.uv[0] = analytic_draw.parameter.uv[1] = 8;
    URUIPaintCommand analytic_paint{600, 750, 1, 0};
    URUIPlanExternalBinding analytic_provider{1, 8};
    auto analytic_bindings = mask_bindings;
    analytic_bindings.frame_flags = 9;
    analytic_bindings.externals = &analytic_provider;
    analytic_bindings.external_count = 1;
    analytic_bindings.external_geometry = &analytic_geometry;
    analytic_bindings.external_geometry_count = 1;
    analytic_bindings.external_draws = &analytic_draw;
    analytic_bindings.external_draw_count = 1;
    auto analytic_frame = [&]()
    {
        assert(session.Begin(12, masked, matrices, 2, &analytic_paint, 1, mask_layers, 8, input) == 1);
        assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &analytic_bindings) == 1 && prepare.ready &&
               prepare.region_count == 0);
        assert(session.Compile(input.lease, analytic_bindings, Resolve, plan) == 1 && plan.operation_count == 1 &&
               plan.operations[0].kind == URUI_PLAN_GEOMETRY);
        assert(plan.operations[0].mask_texture == 0 && (int(plan.parameters[0].metadata[0]) & 256));
        assert(plan.extra_parameter_count == 8 && plan.extra_parameters[8] == analytic_draw.parameter.tint[0] &&
               plan.extra_parameters[11] == 4);
        assert(plan.target_count == 1 && plan.targets[0].layer == 0 && plan.targets[0].slot == -1);
        const auto geometry = plan.operations[0].geometry;
        assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
        return geometry;
    };
    const auto analytic_batch = analytic_frame();
    for (uint32_t n = 0; n < 300; n++)
    {
        analytic_draw.parameter.tint[0] = float(n % 3);
        assert(analytic_frame() == analytic_batch);
    }
    const auto analytic_before = session.Allocations();
    heap_allocations = heap_frees = heap_bytes = 0;
    watch_heap = true;
    for (uint32_t n = 0; n < 32; n++)
    {
        analytic_draw.parameter.tint[0] = float(n % 3);
        assert(analytic_frame() == analytic_batch);
    }
    watch_heap = false;
    const auto analytic_after = session.Allocations();
    assert(analytic_before.allocations == analytic_after.allocations && analytic_before.frees == analytic_after.frees &&
           analytic_before.bytes == analytic_after.bytes);
    assert(!heap_allocations && !heap_frees && !heap_bytes);
    analytic_draw.parameter.matrix[1] = .25f;
    assert(session.Begin(12, masked, matrices, 2, &analytic_paint, 1, mask_layers, 8, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &analytic_bindings) == 1 &&
           prepare.region_count == 3);
    assert(session.Compile(input.lease, analytic_bindings, Resolve, plan) == 1 && plan.operation_count > 1);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    analytic_draw.parameter.matrix[1] = 0;
    URUICommand provider_commands[2]{commands[0], commands[0]};
    URUIPaintCommand provider_paints[2]{{600, 700, 0, 0}, {600, 701, 1, 0}};
    URUIPlanExternalDraw external_draws[4]{};
    for (uint32_t n = 0; n < 4; n++)
    {
        auto& draw = external_draws[n];
        draw.command = n / 2;
        draw.geometry_index = 0;
        draw.provider = 900 + n / 2;
        draw.program = 800;
        std::copy(identity.begin(), identity.end(), draw.parameter.matrix);
        draw.parameter.matrix[12] = float(n * 16);
        std::fill_n(draw.parameter.tint, 4, 1);
        draw.parameter.uv[0] = draw.parameter.uv[1] = 1;
    }
    URUIPlanExternalBinding provider_bindings[2]{{0, 8}, {1, 8}};
    URUIPlanBindings external_bindings{};
    external_bindings.externals = provider_bindings;
    external_bindings.external_count = 2;
    external_bindings.external_geometry = &external_mesh;
    external_bindings.external_geometry_count = 1;
    external_bindings.external_draws = external_draws;
    external_bindings.external_draw_count = 4;
    assert(session.Begin(12, provider_commands, matrices, 2, provider_paints, 2, nullptr, 0, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &external_bindings) == 1 && prepare.ready);
    assert(session.Compile(input.lease, external_bindings, Resolve, plan) == 1);
    assert(plan.operation_count == 1 && plan.operations[0].kind == URUI_PLAN_EXTERNAL_GEOMETRY &&
           plan.operations[0].program == 800);
    assert(session.Geometry(plan.lease, plan.operations[0].geometry, mesh) == 1 && mesh.vertex_count == 16 &&
           mesh.index_count == 24);
    assert(mesh.vertices[4].x == 0 && mesh.vertices[4].parameter == 3 && plan.parameters[3].matrix[12] == 16);
    const auto external_batch = plan.operations[0].geometry;
    external_draws[0].parameter.matrix[12] = 5;
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    assert(session.Begin(12, provider_commands, matrices, 2, provider_paints, 2, nullptr, 0, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &external_bindings) == 1);
    assert(session.Compile(input.lease, external_bindings, Resolve, plan) == 1 &&
           plan.operations[0].geometry == external_batch);
    assert(plan.parameters[2].matrix[12] == 5);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    // One unknown shader range cannot disable native grouping of its neighbors,
    // and cannot be merged through just because its material token matches.
    external_draws[1].flags = 1;
    assert(session.Begin(12, provider_commands, matrices, 2, provider_paints, 2, nullptr, 0, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &external_bindings) == 1 && prepare.ready);
    assert(session.Compile(input.lease, external_bindings, Resolve, plan) == 1 && plan.operation_count == 3);
    assert(plan.operations[0].kind == URUI_PLAN_EXTERNAL_GEOMETRY && plan.operations[1].kind == 7 &&
           plan.operations[1].layer_index == 1 && plan.operations[2].kind == URUI_PLAN_EXTERNAL_GEOMETRY);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    external_draws[1].flags = 0;
    // Provider matrices use element-local coordinates, matching painter semantics.
    auto translated_matrix = identity;
    translated_matrix[12] = 7;
    auto translated_command = provider_commands[0];
    translated_command.tx = 3;
    auto local_draw = external_draws[0];
    std::copy(identity.begin(), identity.end(), local_draw.parameter.matrix);
    local_draw.parameter.matrix[12] = 1;
    auto local_bindings = external_bindings;
    local_bindings.external_count = 1;
    local_bindings.external_draws = &local_draw;
    local_bindings.external_draw_count = 1;
    assert(session.Begin(12, &translated_command, &translated_matrix, 1, provider_paints, 1, nullptr, 0, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &local_bindings) == 1);
    assert(session.Compile(input.lease, local_bindings, Resolve, plan) == 1 && plan.parameters[1].matrix[12] == 11);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    // A provider cannot be introduced only after capture regions are frozen.
    assert(session.Begin(12, provider_commands, matrices, 2, provider_paints, 2, nullptr, 0, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare) == 1 && prepare.ready);
    assert(session.Compile(input.lease, external_bindings, Resolve, plan) == -1 && !plan.lease);
    assert(session.Release(input.lease) == 1);
    // Generic 3D source positions must contribute through the complete matrix;
    // 2D consumers declare a two-component position channel instead.
    float depth_positions[12]{0, 0, 2, 8, 0, 2, 8, 8, 2, 0, 8, 2};
    auto depth_geometry = external_mesh;
    depth_geometry.geometry = 501;
    depth_geometry.streams[0].data = depth_positions;
    auto depth_draw = external_draws[0];
    depth_draw.command = 0;
    std::copy(identity.begin(), identity.end(), depth_draw.parameter.matrix);
    depth_draw.parameter.matrix[8] = 2;
    depth_draw.parameter.matrix[10] = 0;
    URUILayerCommand depth_layers[2]{};
    depth_layers[0].kind = 1;
    depth_layers[0].target = 1;
    depth_layers[1].kind = 2;
    depth_layers[1].source = 1;
    depth_layers[1].command_index = 1;
    auto depth_bindings = external_bindings;
    depth_bindings.external_count = 1;
    depth_bindings.external_geometry = &depth_geometry;
    depth_bindings.external_draws = &depth_draw;
    depth_bindings.external_draw_count = 1;
    assert(session.Begin(12, provider_commands, matrices, 1, provider_paints, 1, depth_layers, 2, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &depth_bindings) == 1 && prepare.ready);
    assert(prepare.region_count == 1 && prepare.regions[0].bounds[0] == 4 && prepare.regions[0].bounds[2] == 8);
    const auto stable_external_version = prepare.regions[0].version;
    assert(prepare.regions[0].flags & 16);
    assert(session.Release(input.lease) == 1);
    assert(session.Begin(12, provider_commands, matrices, 1, provider_paints, 1, depth_layers, 2, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &depth_bindings) == 1 &&
           prepare.regions[0].version == stable_external_version);
    assert(session.Release(input.lease) == 1);
    depth_draw.program++;
    assert(session.Begin(12, provider_commands, matrices, 1, provider_paints, 1, depth_layers, 2, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &depth_bindings) == 1 &&
           prepare.regions[0].version != stable_external_version);
    assert(session.Release(input.lease) == 1);
    depth_draw.flags = 2;
    assert(session.Begin(12, provider_commands, matrices, 1, provider_paints, 1, depth_layers, 2, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &depth_bindings) == 1 &&
           !(prepare.regions[0].flags & 16));
    assert(session.Release(input.lease) == 1);
    depth_draw.flags = 0;
    // Stable source slots with changing vertex payloads reuse native backing
    // capacity; immutable versions held by another output lease remain pinned.
    float moving_positions[12]{0, 0, 0, 8, 0, 0, 8, 8, 0, 0, 8, 0};
    auto moving_geometry = external_mesh;
    moving_geometry.geometry = 502;
    moving_geometry.streams[0].data = moving_positions;
    auto moving_draw = external_draws[0];
    moving_draw.command = 0;
    auto moving_bindings = external_bindings;
    moving_bindings.external_count = 1;
    moving_bindings.external_geometry = &moving_geometry;
    moving_bindings.external_draws = &moving_draw;
    moving_bindings.external_draw_count = 1;
    auto move_frame = [&](uint32_t n)
    {
        moving_positions[0] = float(n % 3);
        moving_geometry.version = uint64_t(n) + 1;
        assert(session.Begin(13, provider_commands, matrices, 1, provider_paints, 1, nullptr, 0, input) == 1);
        assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &moving_bindings) == 1);
        assert(session.Compile(input.lease, moving_bindings, Resolve, plan) == 1);
        assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    };
    for (uint32_t n = 0; n < 300; n++)
    {
        move_frame(n);
    }
    moving_geometry.version = 301;
    moving_positions[0] = 9;
    assert(session.Begin(13, provider_commands, matrices, 1, provider_paints, 1, nullptr, 0, input) == 1);
    assert(session.Resolve(input.lease, nullptr, Resolve, prepare, &moving_bindings) == 1);
    URUIRenderPlanPacket pinned_payload{};
    assert(session.Compile(input.lease, moving_bindings, Resolve, pinned_payload) == 1);
    URUIGeometryView pinned_vertices{};
    assert(session.Geometry(pinned_payload.lease, pinned_payload.operations[0].geometry, pinned_vertices) == 1);
    assert(pinned_vertices.vertices[0].x == 9 && session.Release(input.lease) == 1);
    move_frame(301);
    const auto moving_before = session.Allocations();
    heap_allocations = heap_frees = heap_bytes = 0;
    watch_heap = true;
    for (uint32_t n = 302; n < 334; n++)
    {
        move_frame(n);
    }
    watch_heap = false;
    const auto moving_after = session.Allocations();
    assert(moving_after.allocations == moving_before.allocations && moving_after.frees == moving_before.frees &&
           moving_after.bytes == moving_before.bytes);
    assert(!heap_allocations && !heap_frees && !heap_bytes);
    assert(pinned_vertices.vertices[0].x == 9 && session.Release(pinned_payload.lease) == 1);
    // All sizes use the same semantic compiler; unique identifiers are generated
    // independently of UI/business names. Nine interleaved disjoint textures need two groups.
    for (uint32_t count : {10u, 100u, 1000u, 5000u})
    {
        for (uint32_t textures : {1u, 8u, 9u})
        {
            std::vector<URUICommand> many(count);
            std::vector<std::array<float, 16>> transforms(count, identity);
            for (uint32_t n = 0; n < count; n++)
            {
                many[n].geometry = 1;
                many[n].texture = 100 + n % textures;
                many[n].tx = float(n % 100 * 16);
                many[n].ty = float(n / 100 * 16);
            }
            assert(session.Begin(5, many.data(), transforms.data(), count, nullptr, 0, nullptr, 0, input) == 1);
            assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
            assert(plan.operation_count == (textures + 7) / 8);
            auto parameter_version = plan.parameter_version;
            auto operationVersion = plan.version;
            assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
            assert(session.Begin(5, many.data(), transforms.data(), count, nullptr, 0, nullptr, 0, input) == 1);
            assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
            assert(plan.version == operationVersion && plan.parameter_version == parameter_version);
            assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
        }
    }
    // Budget pressure reclaims only unpublished geometry; an earlier leased mesh
    // remains byte-for-byte readable while topology changes many times.
    Session bounded(2048, 1024 * 1024);
    content.geometry = 1;
    assert(bounded.Begin(1, &content, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
    URUIRenderPlanPacket pinned{};
    assert(bounded.Compile(input.lease, bindings, Resolve, pinned) == 1);
    assert(bounded.Release(input.lease) == 1);
    const auto pinned_geometry = pinned.operations[0].geometry;
    for (uint32_t n = 2; n < 100; n++)
    {
        content.geometry = n;
        assert(bounded.Begin(1, &content, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(bounded.Compile(input.lease, bindings, Resolve, plan) == 1);
        assert(bounded.Release(plan.lease) == 1 && bounded.Release(input.lease) == 1);
        assert(bounded.Geometry(pinned.lease, pinned_geometry, mesh) == 1 && mesh.vertices[0].r == 255);
    }
    assert(bounded.Release(pinned.lease) == 1);
    // Transparent overlap is a dependency even when the texture palette overflows.
    std::vector<URUICommand> overlap(27, content);
    std::vector<std::array<float, 16>> overlap_matrices(27, identity);
    for (size_t n = 0; n < overlap.size(); n++)
    {
        overlap[n].geometry = 1;
        overlap[n].texture = 1 + n % 9;
        overlap[n].tx = 0;
    }
    assert(session.Begin(6, overlap.data(), overlap_matrices.data(), 27, nullptr, 0, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    uint32_t expected = 0;
    for (uint32_t n = 0; n < plan.operation_count; n++)
    {
        assert(session.Geometry(plan.lease, plan.operations[n].geometry, mesh) == 1);
        for (uint32_t v = 0; v < mesh.vertex_count; v += 4)
        {
            assert(mesh.vertices[v].parameter == float(expected++));
        }
    }
    assert(expected == 27);
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
    overlap_matrices[0][0] = std::numeric_limits<float>::quiet_NaN();
    assert(session.Begin(6, overlap.data(), overlap_matrices.data(), 27, nullptr, 0, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == -1 && !plan.lease);
    assert(session.Release(input.lease) == 1);

    // Vary the number of real groups without shrinking any warmed planner storage.
    commands[0].geometry = commands[1].geometry = commands[2].geometry = 1;
    for (int n = 0; n < 332; n++)
    {
        watch_heap = n >= 300;
        auto saved = session.Allocations();
        bool external = n % 2 != 0;
        assert(session.Begin(7, commands, matrices, 3, external ? &paint : nullptr, external ? 1 : 0, nullptr, 0,
                             input) == 1);
        assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
        assert(plan.operation_count == uint32_t(external ? 3 : 1));
        assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1);
        if (n >= 300)
        {
            auto actual = session.Allocations();
            assert(actual.allocations == saved.allocations && actual.frees == saved.frees &&
                   actual.bytes == saved.bytes);
        }
        watch_heap = false;
    }
    assert(heap_allocations == 0 && heap_frees == 0 && heap_bytes == 0);
    // An exact row version changes only for the row containing the edited parameter.
    std::vector<URUICommand> paged(100, content);
    std::vector<std::array<float, 16>> paged_matrices(100, identity);
    assert(session.Begin(8, paged.data(), paged_matrices.data(), 100, nullptr, 0, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, pinned) == 1);
    assert(pinned.parameter_page_count == 4);
    assert(session.Release(input.lease) == 1);
    paged[65].tx = 3;
    assert(session.Begin(8, paged.data(), paged_matrices.data(), 100, nullptr, 0, nullptr, 0, input) == 1);
    assert(session.Compile(input.lease, bindings, Resolve, plan) == 1);
    for (uint32_t page = 0; page < 4; page++)
    {
        assert((plan.parameter_pages[page] == pinned.parameter_pages[page]) == (page != 2));
    }
    assert(session.Release(plan.lease) == 1 && session.Release(input.lease) == 1 && session.Release(pinned.lease) == 1);
    // Final targets derive from actual operations, not every layer/placeholder command.
    {
        Session targets;
        URUIPlanBindings resources{};
        resources.frame_flags = 8;
        assert(targets.Begin(9, commands, matrices, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(targets.Compile(input.lease, resources, Resolve, plan) == 1);
        assert(plan.target_count == 1 && plan.targets[0].layer == 0 && plan.targets[0].slot == -1 &&
               plan.targets[0].kind == 2);
        assert(plan.targets[0].first_operation == 0 && plan.targets[0].last_operation == 0);
        assert(targets.Release(plan.lease) == 1 && targets.Release(input.lease) == 1);
        URUILayerCommand opacity_layers[3]{};
        opacity_layers[0].kind = 1;
        opacity_layers[0].target = 1;
        opacity_layers[1].kind = 6;
        opacity_layers[1].source = 1;
        opacity_layers[1].blend = 1;
        opacity_layers[1].command_index = 1;
        opacity_layers[2].kind = 2;
        opacity_layers[2].source = 1;
        opacity_layers[2].command_index = 1;
        resources.frame_flags = 0;
        assert(targets.Begin(9, commands, matrices, 1, nullptr, 0, opacity_layers, 3, input) == 1);
        assert(targets.Compile(input.lease, resources, Resolve, plan) == 1);
        assert(plan.target_count == 2);
        bool primary = false;
        bool scratch = false;
        for (uint32_t i = 0; i < plan.target_count; i++)
        {
            const auto& request = plan.targets[i];
            assert(request.layer == 1);
            primary |= request.slot == 0;
            scratch |= request.slot == 1;
            assert(request.slot != 2);
            assert(request.first_operation <= request.last_operation);
        }
        assert(primary && scratch);
        assert(targets.Release(plan.lease) == 1 && targets.Release(input.lease) == 1);
    }
    // 颜色附件可读取时，CSS over 不再申请离屏背景；表面采样仍保留原有读回。
    {
        Session fetch;
        fetch.Viewport(128, 128);
        URUICommand draw{};
        draw.geometry = 1;
        URUILayerCommand layers[4]{};
        layers[0].kind = 1;
        layers[0].target = 1;
        layers[1].kind = 2;
        layers[1].source = 1;
        layers[1].command_index = 1;
        layers[2].kind = 4;
        layers[2].source = 1;
        layers[2].command_index = 1;
        layers[3].kind = 5;
        layers[3].source = 1;
        layers[3].command_index = 1;
        URUIPlanBindings resources{};
        resources.frame_flags = 2 | 16 | 64;
        assert(fetch.Begin(810, &draw, &identity, 1, nullptr, 0, layers, 4, input) == 1);
        assert(fetch.Compile(input.lease, resources, Resolve, plan) == 1 && plan.operation_count > 0);
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            assert(plan.targets[n].kind != 2 && plan.targets[n].kind != 4 && plan.targets[n].kind != 5 &&
                   plan.targets[n].kind != 6);
        }
        assert(fetch.Release(plan.lease) == 1 && fetch.Release(input.lease) == 1);
        // 没有能力声明时仍需 CSS 背景，不把后端能力变成用户配置。
        resources.frame_flags = 2 | 16;
        assert(fetch.Begin(811, &draw, &identity, 1, nullptr, 0, layers, 4, input) == 1);
        assert(fetch.Compile(input.lease, resources, Resolve, plan) == 1);
        bool backdrop = false;
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            backdrop |= plan.targets[n].kind == 4;
        }
        assert(backdrop && fetch.Release(plan.lease) == 1 && fetch.Release(input.lease) == 1);
        URUILayerCommand surface_layers[5]{};
        surface_layers[0].kind = 1;
        surface_layers[0].target = 1;
        surface_layers[1].kind = 4;
        surface_layers[1].target = 1;
        surface_layers[2].kind = 2;
        surface_layers[2].source = 1;
        surface_layers[2].command_index = 1;
        surface_layers[3] = layers[2];
        surface_layers[4] = layers[3];
        // 显式表面读取发生在创建目标后、几何前。
        resources.frame_flags = 2 | 16 | 64;
        assert(fetch.Begin(812, &draw, &identity, 1, nullptr, 0, surface_layers, 5, input) == 1);
        assert(fetch.Compile(input.lease, resources, Resolve, plan) == 1);
        bool capture = false;
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            capture |= plan.targets[n].kind == 6;
        }
        assert(capture && fetch.Release(plan.lease) == 1 && fetch.Release(input.lease) == 1);
    }
    // 普通图元只在实际高精度目标上直写；8位根目标仍保留编码累积。
    {
        Session precision;
        precision.Viewport(128, 128);
        URUICommand draw{};
        draw.geometry = 1;
        for (int high = 0; high < 2; high++)
        {
            URUIPlanBindings resources{};
            resources.frame_flags = 2 | 8 | 16 | 64 | (high ? 128 : 0);
            assert(precision.Begin(820 + high, &draw, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
            assert(precision.Compile(input.lease, resources, Resolve, plan) == 1);
            bool encoded = false;
            bool root = false;
            for (uint32_t n = 0; n < plan.target_count; n++)
            {
                encoded |= plan.targets[n].kind == 2;
                root |= plan.targets[n].kind == 5;
            }
            assert(encoded == !high && root == !high);
            assert(precision.Release(plan.lease) == 1 && precision.Release(input.lease) == 1);
        }
    }
    // A rejected unpublished large input must not strand high-water storage
    // that permanently prevents a much smaller valid input from compiling.
    {
        const auto limit = sizeof(URUICommand) * 1024 + 256;
        Session recovery(64u * 1024 * 1024, limit);
        std::vector<URUICommand> oversized(1024, commands[0]);
        std::vector<std::array<float, 16>> large_matrices(1024, identity);
        assert(recovery.Begin(12, oversized.data(), large_matrices.data(), 1024, nullptr, 0, nullptr, 0, input) == -4 &&
               input.lease == 0);
        assert(recovery.Begin(12, commands, matrices, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(recovery.Compile(input.lease, bindings, Resolve, plan) == 1);
        assert(recovery.Release(plan.lease) == 1 && recovery.Release(input.lease) == 1);
    }
    // Fixed image-program rows are captured inside the pending Prepare window.
    // Compatible ordered images share geometry; uniforms never regenerate it.
    {
        Session images;
        URUICommand draws[2]{commands[0], commands[0]};
        draws[1].texture = 9;
        draws[1].tx = 12;
        URUIPaintCommand programs[2]{{31, 51, 0, 1}, {32, 51, 1, 1}};
        URUIPlanPreparePacket request{};
        float field = .25f;
        uint64_t stable_geometry = 0;
        for (int round = 0; round < 332; round++)
        {
            watch_heap = round >= 300;
            auto saved = images.Allocations();
            assert(images.Begin(20, draws, matrices, 2, programs, 2, nullptr, 0, input) == 1);
            assert(images.Resolve(input.lease, nullptr, Resolve, request) == 1);
            while (!request.ready)
            {
                auto pending = request.requests[0];
                auto fill = [&](const URUIPlanPrepareRequest&, float* rows, uint32_t count)
                {
                    assert(count == 2);
                    std::fill_n(rows, 8, 0);
                    rows[4] = field;
                    return 1;
                };
                assert(images.SetImageProgram(input.lease, pending.request + 1, 71, 2, fill) == -2);
                assert(images.SetImageProgram(input.lease, pending.request, 71, 2, fill) == 1);
                assert(images.SetImageProgram(input.lease, pending.request, 71, 2, fill) == -5);
                URUIPlanFeedback feedback{};
                feedback.request = pending.request;
                feedback.kind = pending.kind;
                feedback.ink[2] = feedback.ink[3] = 8;
                assert(images.Resolve(input.lease, &feedback, Resolve, request) == 1);
            }
            assert(images.Compile(input.lease, bindings, Resolve, plan) == 1);
            assert(plan.operation_count == 1 && plan.operations[0].kind == URUI_PLAN_PARAMETER_IMAGE);
            assert(plan.operations[0].program == 71 && plan.operations[0].texture_count == 2);
            assert(plan.extra_parameter_count == 4 && plan.extra_parameters[4] == field &&
                   plan.extra_parameters[12] == field);
            assert(plan.parameters[0].metadata[2] == 16 && plan.parameters[1].metadata[2] == 18);
            if (round)
            {
                assert(plan.operations[0].geometry == stable_geometry);
            }
            else
            {
                stable_geometry = plan.operations[0].geometry;
            }
            assert(images.Release(plan.lease) == 1 && images.Release(input.lease) == 1);
            if (round >= 300)
            {
                auto now = images.Allocations();
                assert(now.allocations == saved.allocations && now.frees == saved.frees && now.bytes == saved.bytes);
            }
            watch_heap = false;
            field += .001f;
        }
        assert(heap_allocations == 0 && heap_frees == 0 && heap_bytes == 0);
    }
    // Native source composition is an observable snapshot operation, not render
    // preparation work. Fixed-program consumers need only topology + uniforms.
    {
        Session deferred;
        URUICommand image = commands[0];
        URUIPaintCommand program{33, 52, 0, 1};
        URUIPlanUniform uniform{{.5f, 1, 1, 1}, {2, 1, .25f, 0}};
        assert(deferred.Begin(22, &image, &identity, 1, &program, 1, nullptr, 0, input, &uniform) == 1);
        URUIPlanPreparePacket request{};
        assert(deferred.Resolve(input.lease, nullptr, Resolve, request) == 1);
        const auto pending = request.requests[0];
        const auto before = deferred.Allocations();
        assert(deferred.Geometry(input.lease, pending.geometry, mesh) == 1);
        const auto observed = deferred.Allocations();
        assert(observed.allocations == before.allocations + 1);
        assert(mesh.vertices[0].r == 128 && mesh.vertices[0].u == .25f);
        const auto* retained = mesh.vertices;
        assert(deferred.Geometry(input.lease, pending.geometry, mesh) == 1 && mesh.vertices == retained);
        const auto repeated = deferred.Allocations();
        assert(repeated.allocations == observed.allocations && repeated.frees == observed.frees);
        assert(deferred.Release(input.lease) == 1);
    }
    // A frozen UI clock is one shared texel, even when many image inputs use it.
    {
        Session clocks;
        URUICommand draws[2]{commands[0], commands[0]};
        URUIPaintCommand programs[2]{{41, 61, 0, 1}, {42, 61, 1, 1}};
        auto capture = [&](float time, URUIRenderPlanPacket& output)
        {
            assert(clocks.Begin(25, draws, matrices, 2, programs, 2, nullptr, 0, input) == 1);
            URUIPlanPreparePacket request{};
            assert(clocks.Resolve(input.lease, nullptr, Resolve, request) == 1);
            while (!request.ready)
            {
                auto pending = request.requests[0];
                auto fill = [&](const URUIPlanPrepareRequest&, float* rows, uint32_t count)
                {
                    assert(count == 3);
                    std::fill_n(rows, 12, 0);
                    rows[6] = time;
                    return 1;
                };
                assert(clocks.SetImageProgram(input.lease, pending.request, 81, 3, fill, 1) == 1);
                URUIPlanFeedback feedback{};
                feedback.request = pending.request;
                feedback.kind = pending.kind;
                feedback.ink[2] = feedback.ink[3] = 8;
                assert(clocks.Resolve(input.lease, &feedback, Resolve, request) == 1);
            }
            assert(clocks.Compile(input.lease, bindings, Resolve, output) == 1 && output.extra_parameter_count == 7);
            assert(output.extra_parameters[6] == 22 && output.extra_parameters[18] == 22 &&
                   output.extra_parameters[24] == time);
            assert(clocks.Release(input.lease) == 1);
        };
        for (int round = 0; round < 332; round++)
        {
            watch_heap = round >= 300;
            const auto before = clocks.Allocations();
            capture(round * .01f, plan);
            assert(clocks.Release(plan.lease) == 1);
            if (round >= 300)
            {
                const auto after = clocks.Allocations();
                assert(after.allocations == before.allocations && after.frees == before.frees &&
                       after.bytes == before.bytes);
            }
            watch_heap = false;
        }
        assert(heap_allocations == 0 && heap_frees == 0 && heap_bytes == 0);
        URUIRenderPlanPacket retained{};
        capture(2.5f, retained);
        capture(3.5f, plan);
        assert(retained.extra_parameters[24] == 2.5f && plan.extra_parameters[24] == 3.5f);
        assert(clocks.Release(retained.lease) == 1 && clocks.Release(plan.lease) == 1);
    }
    // A late, immutable template uses the same native ordered image batching.
    {
        Session templates;
        URUICommand draws[2]{};
        for (auto& draw : draws)
        {
            draw.geometry = 1;
            draw.texture = 7;
        }
        URUIPaintCommand programs[2]{{51, 71, 0, 1}, {52, 71, 1, 1}};
        const float positions[] = {0, 0, 1, 0, 1, 1, 0, 1};
        const uint32_t indices[] = {0, 1, 2, 0, 2, 3};
        URUIPlanExternalGeometry geometry{};
        geometry.geometry = 9001;
        geometry.version = 1;
        geometry.vertex_count = 4;
        geometry.index_count = 6;
        geometry.stream_count = 1;
        geometry.streams[0] = {positions, sizeof(positions), 8, 0};
        geometry.position = {0, 0, 1, 2};
        geometry.indices = indices;
        geometry.index_bytes = sizeof(indices);
        geometry.index_stride = 4;
        uint64_t stable = 0;
        for (int round = 0; round < 332; round++)
        {
            watch_heap = round >= 300;
            const auto before = templates.Allocations();
            assert(templates.Begin(28, draws, matrices, 2, programs, 2, nullptr, 0, input) == 1);
            URUIPlanPreparePacket request{};
            assert(templates.Resolve(input.lease, nullptr, Resolve, request) == 1);
            while (!request.ready)
            {
                auto pending = request.requests[0];
                auto local = identity;
                local[12] = float(4 + pending.slot * 5);
                assert(templates.SetImageGeometry(input.lease, pending.request + 1, geometry, local.data()) == -2);
                assert(templates.SetImageGeometry(input.lease, pending.request, geometry, local.data()) == 1);
                assert(templates.SetImageGeometry(input.lease, pending.request, geometry, local.data()) == -5);
                auto fill = [](const URUIPlanPrepareRequest&, float* rows, uint32_t count)
                {
                    std::fill_n(rows, count * 4, 0);
                    return 1;
                };
                assert(templates.SetImageProgram(input.lease, pending.request, 91, 3, fill) == 1);
                URUIPlanFeedback feedback{};
                feedback.request = pending.request;
                feedback.kind = pending.kind;
                feedback.ink[2] = feedback.ink[3] = 8;
                assert(templates.Resolve(input.lease, &feedback, Resolve, request) == 1);
            }
            assert(templates.Compile(input.lease, bindings, Resolve, plan) == 1 && plan.operation_count == 1 &&
                   plan.operations[0].kind == 8);
            assert(plan.parameters[0].matrix[12] == 4 && plan.parameters[1].matrix[12] == 9);
            assert(templates.Geometry(plan.lease, plan.operations[0].geometry, mesh) == 1 && mesh.vertex_count == 8 &&
                   mesh.vertices[1].x == 1);
            if (round)
            {
                assert(stable == plan.operations[0].geometry);
            }
            else
            {
                stable = plan.operations[0].geometry;
            }
            assert(templates.Release(input.lease) == 1 && templates.Release(plan.lease) == 1);
            if (round >= 300)
            {
                const auto after = templates.Allocations();
                assert(after.allocations == before.allocations && after.frees == before.frees &&
                       after.bytes == before.bytes);
            }
            watch_heap = false;
        }
        assert(heap_allocations == 0 && heap_frees == 0 && heap_bytes == 0);
    }
    // Captured subtrees retain the layer operation, but use the same immutable
    // fixed program/geometry data as images. No managed Record callback is needed.
    {
        Session captured;
        URUILayerCommand layers[4]{};
        layers[0].kind = 1;
        layers[0].target = 1;
        layers[1].kind = 2;
        layers[1].source = 1;
        layers[1].command_index = 1;
        layers[2].kind = 6;
        layers[2].source = 1;
        layers[2].mask = 101;
        layers[2].command_index = 1;
        layers[3].kind = 4;
        layers[3].source = 1;
        layers[3].command_index = 1;
        const float positions[] = {0, 0, 1, 0, 1, 1, 0, 1};
        const uint32_t indices[] = {0, 1, 2, 0, 2, 3};
        URUIPlanExternalGeometry geometry{};
        geometry.geometry = 9901;
        geometry.version = 1;
        geometry.vertex_count = 4;
        geometry.index_count = 6;
        geometry.stream_count = 1;
        geometry.streams[0] = {positions, sizeof(positions), 8, 0};
        geometry.position = {0, 0, 1, 2};
        geometry.indices = indices;
        geometry.index_bytes = sizeof(indices);
        geometry.index_stride = 4;
        LayerEffect effect{2, 3, 10, 101, 3, 0, 0, 0, 0};
        uint64_t stable = 0;
        heap_allocations = heap_frees = heap_bytes = 0;
        for (int round = 0; round < 332; round++)
        {
            watch_heap = round >= 300;
            const auto before = captured.Allocations();
            assert(captured.Begin(38, &content, &identity, 1, nullptr, 0, layers, 4, input) == 1);
            assert(captured.SetLayerEffects(input.lease, &effect, 1) == 1);
            URUIPlanPreparePacket request{};
            assert(captured.Resolve(input.lease, nullptr, Resolve, request) == 1);
            while (!request.ready)
            {
                const auto pending = request.requests[0];
                URUIPlanFeedback answer{};
                answer.request = pending.request;
                answer.kind = pending.kind;
                if (pending.kind == URUI_PLAN_ACQUIRE_CAPTURE)
                {
                    answer.resource = 8001;
                    answer.source_version = 1;
                    answer.pixel_width = answer.pixel_height = 8;
                    std::copy_n(pending.capture, 4, answer.ink);
                }
                else if (pending.kind == URUI_PLAN_PREPARE_ELEMENT)
                {
                    auto local = identity;
                    local[12] = 5;
                    assert(captured.SetImageGeometry(input.lease, pending.request, geometry, local.data()) == 1);
                    auto fill = [&](const URUIPlanPrepareRequest&, float* rows, uint32_t count)
                    {
                        std::fill_n(rows, count * 4, 0);
                        rows[60] = float(round);
                        return 1;
                    };
                    assert(captured.SetImageProgram(input.lease, pending.request, 91, 16, fill) == 1);
                    answer.ink[2] = answer.ink[3] = 8;
                }
                assert(captured.Resolve(input.lease, &answer, Resolve, request) == 1);
            }
            assert(captured.Compile(input.lease, bindings, Resolve, plan) == 1);
            const URUIPlanOperation* operation = nullptr;
            for (uint32_t n = 0; n < plan.operation_count; n++)
            {
                if (plan.operations[n].kind == 5 && plan.operations[n].layer_index == 2)
                {
                    operation = plan.operations + n;
                }
            }
            assert(operation && operation->program == 91 && operation->geometry);
            assert(captured.Geometry(plan.lease, operation->geometry, mesh) == 1 && mesh.vertex_count == 4);
            const auto parameter = uint32_t(mesh.vertices[0].parameter);
            assert(parameter < plan.parameter_count);
            assert(plan.parameters[parameter].matrix[12] == 5);
            assert(plan.extra_parameters[60] == float(round));
            if (round)
            {
                assert(stable == operation->geometry);
            }
            else
            {
                stable = operation->geometry;
            }
            assert(captured.Release(input.lease) == 1 && captured.Release(plan.lease) == 1);
            if (round >= 300)
            {
                const auto after = captured.Allocations();
                assert(after.allocations == before.allocations && after.frees == before.frees &&
                       after.bytes == before.bytes);
            }
            watch_heap = false;
        }
        assert(heap_allocations == 0 && heap_frees == 0 && heap_bytes == 0);
    }
    // 旧数值探针迁到正式参数输出：缩放、负零、极小值及溢出均不再依赖研究版 mode 标签。
    {
        Session numeric;
        URUICommand command{};
        command.geometry = 1;
        command.texture = 7;
        for (float translation : {4.f, -0.f, std::numeric_limits<float>::denorm_min()})
        {
            auto matrix = identity;
            matrix[0] = 2;
            matrix[12] = translation;
            command.tx = 12;
            command.ty = 1;
            assert(numeric.Begin(1, &command, &matrix, 1, nullptr, 0, nullptr, 0, input) == 1);
            assert(numeric.Compile(input.lease, bindings, Resolve, plan) == 1 && plan.parameter_count == 1);
            URUIGeometryView baked{};
            assert(numeric.Geometry(plan.lease, plan.operations[0].geometry, baked) == 1);
            const auto& first = baked.vertices[0];
            const auto* m = plan.parameters[0].matrix;
            assert(std::isfinite(m[12]) && Position(first, plan.parameters[0])[0] == translation + 24.f);
            assert(numeric.Release(plan.lease) == 1 && numeric.Release(input.lease) == 1);
        }
        auto matrix = identity;
        matrix[12] = std::numeric_limits<float>::max();
        command.tx = matrix[12];
        assert(numeric.Begin(1, &command, &matrix, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(numeric.Compile(input.lease, bindings, Resolve, plan) == -1 && !plan.lease && !plan.operations);
        assert(numeric.Release(input.lease) == 1);
    }
    // Screen roots that must be sampled use one native-planned texture; explicit
    // texture roots and streams containing only texture-domain draws need none.
    {
        Session screen;
        URUICommand draw{};
        draw.geometry = 1;
        draw.texture = 7;
        URUIPlanTextureBinding texture{7, 1, 0, 1};
        URUIPlanBindings resources{};
        resources.textures = &texture;
        resources.texture_count = 1;
        resources.frame_flags = 8 | 16;
        URUIPlanPreparePacket ready{};
        assert(screen.Begin(90, &draw, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(screen.Resolve(input.lease, nullptr, Resolve, ready, &resources) == 1 && ready.ready);
        assert(ready.target_flags == 1);
        assert(screen.Compile(input.lease, resources, Resolve, plan) == 1);
        assert(plan.operation_count == 3 && plan.operations[0].kind == 9 &&
               plan.operations[1].kind == URUI_PLAN_GEOMETRY && plan.operations[2].kind == 10);
        bool root = false;
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            if (plan.targets[n].kind == 5)
            {
                const auto& request = plan.targets[n];
                assert(request.layer == 0 && request.slot == -2 && request.first_operation == 0 &&
                       request.last_operation == 2);
                root = true;
            }
        }
        bool full_capture = false;
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            if (plan.targets[n].kind == 6)
            {
                assert(plan.targets[n].slot == -3 && plan.targets[n].layer == 0);
                full_capture = true;
            }
        }
        // 完整根快照在一次标准 Blit 内归一化，不应再申请同尺寸中转目标。
        assert(!full_capture);
        assert(root && screen.Release(plan.lease) == 1 && screen.Release(input.lease) == 1);
        // Identical inputs exercise cached native operations without duplicate copies.
        assert(screen.Begin(90, &draw, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(screen.Compile(input.lease, resources, Resolve, plan) == 1 && plan.operation_count == 3);
        assert(screen.Release(plan.lease) == 1 && screen.Release(input.lease) == 1);
        for (int scenario = 0; scenario < 3; scenario++)
        {
            resources.frame_flags = scenario == 0 ? 8 : 8 | 16;
            texture.domain = scenario == 1 ? 1 : 0;
            assert(screen.Begin(91, &draw, &identity, scenario == 2 ? 0 : 1, nullptr, 0, nullptr, 0, input) == 1);
            assert(screen.Resolve(input.lease, nullptr, Resolve, ready, &resources) == 1 && ready.ready &&
                   ready.target_flags == 0);
            assert(screen.Compile(input.lease, resources, Resolve, plan) == 1);
            for (uint32_t n = 0; n < plan.target_count; n++)
            {
                assert(plan.targets[n].kind != 5);
            }
            for (uint32_t n = 0; n < plan.operation_count; n++)
            {
                assert(plan.operations[n].kind != 9 && plan.operations[n].kind != 10);
            }
            assert(screen.Release(plan.lease) == 1 && screen.Release(input.lease) == 1);
        }
    }
    {
        Session empty_callback;
        URUICommand draw{};
        draw.geometry = 1;
        URUIPaintCommand paint{11, 22, 0, 0};
        URUIPlanBindings resources{};
        resources.frame_flags = 8 | 16;
        URUIPlanPreparePacket ready{};
        assert(empty_callback.Begin(92, &draw, &identity, 1, &paint, 1, nullptr, 0, input) == 1);
        assert(empty_callback.Resolve(input.lease, nullptr, Resolve, ready, &resources) == 1 && ready.ready &&
               ready.target_flags == 1);
        URUIPlanExternalBinding callback{0, 0};
        resources.externals = &callback;
        resources.external_count = 1;
        assert(empty_callback.Compile(input.lease, resources, Resolve, plan) == 1 && plan.operation_count == 0 &&
               plan.target_count == 0);
        assert(empty_callback.Release(plan.lease) == 1 && empty_callback.Release(input.lease) == 1);
    }
    // Only the native plan chooses a proven root region, and raw-paint input
    // contracts cannot silently reintroduce original geometry after preparation.
    {
        Session region;
        region.Viewport(640, 360);
        URUICommand draw{};
        draw.geometry = 1;
        draw.tx = 64;
        draw.ty = 32;
        URUIPlanBindings resources{};
        resources.frame_flags = 1 | 8 | 16 | 32;
        assert(region.Begin(94, &draw, &identity, 1, nullptr, 0, nullptr, 0, input) == 1);
        assert(region.Compile(input.lease, resources, Resolve, plan) == 1);
        const auto& copy = plan.operations[0];
        assert(copy.kind == URUI_PLAN_ROOT_COPY && (copy.reason & 1));
        assert(copy.bounds[0] == 64 && copy.bounds[1] == 32 && copy.bounds[2] == 72 && copy.bounds[3] == 40);
        bool screen_capture = false;
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            if (plan.targets[n].kind == 6)
            {
                assert(plan.targets[n].slot == -3 && plan.targets[n].layer == 0);
                screen_capture = true;
            }
        }
        // 纯几何可直接把屏幕来源的归一化合入编码，无需第三个根目标。
        assert(!screen_capture);
        assert(region.Release(plan.lease) == 1 && region.Release(input.lease) == 1);
        URUIPaintCommand painter{11, 22, 0, 0};
        URUIPlanPreparePacket ready{};
        assert(region.Begin(95, &draw, &identity, 1, &painter, 1, nullptr, 0, input) == 1);
        assert(region.Resolve(input.lease, nullptr, Resolve, ready, &resources) == 1 && ready.ready &&
               ready.target_flags == 0);
        URUIPlanExternalBinding raw{0, 1};
        resources.externals = &raw;
        resources.external_count = 1;
        assert(region.Compile(input.lease, resources, Resolve, plan) == 1 && plan.operation_count == 1 &&
               plan.operations[0].kind == URUI_PLAN_EXTERNAL && plan.target_count == 0);
        assert(region.Release(plan.lease) == 1);
        raw.flags = 2;
        assert(region.Compile(input.lease, resources, Resolve, plan) == -1 && !plan.lease);
        assert(region.Release(input.lease) == 1);
    }
    // A group can take the root snapshot already requested by its composite;
    // it must not add a second full-screen working copy just to read that snapshot.
    {
        Session group;
        group.Viewport(640, 360);
        URUICommand draw{};
        draw.geometry = 1;
        URUILayerCommand layers[4]{};
        layers[0].kind = 1;
        layers[0].target = 1;
        layers[1].kind = 2;
        layers[1].source = 1;
        layers[1].command_index = 1;
        layers[2].kind = 4;
        layers[2].source = 1;
        layers[2].command_index = 1;
        layers[3].kind = 5;
        layers[3].source = 1;
        layers[3].command_index = 1;
        URUIPlanBindings resources{};
        resources.frame_flags = 1 | 2 | 8 | 16 | 32;
        URUIPlanPreparePacket ready{};
        assert(group.Begin(96, &draw, &identity, 1, nullptr, 0, layers, 4, input) == 1);
        assert(group.Resolve(input.lease, nullptr, Resolve, ready, &resources) == 1 && ready.ready &&
               ready.target_flags == 0);
        assert(group.Compile(input.lease, resources, Resolve, plan) == 1);
        bool backdrop = false;
        bool screen_capture = false;
        for (uint32_t n = 0; n < plan.target_count; n++)
        {
            assert(plan.targets[n].kind != 5);
            backdrop |= plan.targets[n].kind == 4;
            screen_capture |= plan.targets[n].kind == 6;
        }
        // 没有根工作目标时，局部背景仍须解析屏幕别名，不能一并删除。
        assert(backdrop && screen_capture && group.Release(plan.lease) == 1 && group.Release(input.lease) == 1);
    }
    std::cout << "render plan compiler contracts passed\n";
}
