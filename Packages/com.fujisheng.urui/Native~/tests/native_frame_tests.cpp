#include "native_frame.h"
#include <cassert>
#include <thread>
#include <iostream>
#include <limits>
using namespace urui_frame;
static std::array<float, 16> Identity()
{
    return {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
}
int main()
{
    static_assert(sizeof(UruiNativeFramePacket) == (sizeof(void*) == 8 ? 128 : 120) &&
                      sizeof(UruiNativeMeshView) == 16 + 2 * sizeof(void*),
                  "ABI");
    const UruiVertex vs[] = {{0, 0, 0, 0, 255, 0, 0, 128}, {8, 0, 1, 0, 255, 0, 0, 128}, {0, 8, 0, 1, 255, 0, 0, 128}};
    const int ix[] = {0, 1, 2};
    auto resolve = [&](uint64_t id) { return id == 1 ? urui_prepared::View{vs, 3, ix, 3} : urui_prepared::View{}; };
    auto texture = [](uint64_t id) { return id <= 2; };
    std::vector<UruiCommand> commands(3);
    for (auto& c : commands)
    {
        c.geometry = 1;
        c.texture = 1;
    }
    std::vector<std::array<float, 16>> matrices(3, Identity());
    UruiFrameStamp s{};
    s.flags = 1;
    s.epoch = 1;
    s.commands = 1;
    s.resources = 1;
    s.count = 3;
    auto numeric = Identity();
    numeric[0] = -.6485595703125f;
    numeric[4] = -.9876708984375f;
    numeric[12] = -.3780517578125f;
    float mathWorld[16];
    Value mathValue{};
    assert(Prepare(numeric.data(), .5301513671875f, -.074951171875f, mathWorld, mathValue, 1) == 1);
    uint32_t mathBits;
    std::memcpy(&mathBits, mathWorld + 12, 4);
    assert(mathBits == 0xbf25da1e);
    assert(Prepare(numeric.data(), .5301513671875f, -.074951171875f, mathWorld, mathValue, 2) == 1);
    std::memcpy(&mathBits, mathWorld + 12, 4);
    assert(mathBits == 0xbf25da1d);
    Session session;
    UruiNativeFramePacket a{};
    assert(session.Acquire(s, commands, matrices, resolve, texture, a) == 1 && a.version == 1 && a.count == 3 &&
           a.draw_count == 1 && a.affine_count == 3 && a.fallback_count == 0);
#if !defined(URUI_DEBUG)
    assert(a.retained_bytes == 0 && a.built_bytes == 0 && a.built_batches == 0 && a.reused_batches == 0);
#endif
    auto first = session.Get(a.lease);
    assert(first && first->draws[0].count == 3);
    auto plan = first->geometry;
    auto batch = plan->batches[0];
    assert(batch->indices.size() == 9 && batch->vertices[3].slot == 1 && batch->indices[3] == 3);
    auto firstValue = first->values[0];
    commands[0].tx = 7;
    s.commands++;
    UruiNativeFramePacket b{};
    assert(session.Acquire(s, commands, matrices, resolve, texture, b) == 1 && b.plan == a.plan && b.frame != a.frame &&
           b.built_batches == 0);
    assert(session.Get(b.lease)->values[0].data[2] == 7 && std::memcmp(&firstValue, &first->values[0], 32) == 0);
    UruiNativeFramePacket c{}, d{}, e{};
    assert(session.Acquire(s, commands, matrices, resolve, texture, c) == 1 && c.frame == b.frame);
    assert(session.Acquire(s, commands, matrices, resolve, texture, d) == 1);
    e.session = 999;
    assert(session.Acquire(s, commands, matrices, resolve, texture, e) == -5 && e.session == 0);
    assert(!session.Get(a.lease + 123));
    assert(session.Release(a.lease) == 1 && session.Release(a.lease) == -2);
    first.reset();
    assert(session.Release(b.lease) == 1 && session.Release(c.lease) == 1 && session.Release(d.lease) == 1);
    commands[1].texture = 2;
    s.commands++;
    assert(session.Acquire(s, commands, matrices, resolve, texture, a) == 1 && a.draw_count == 3);
    assert(session.Get(a.lease)->draws[0].texture == 1 && session.Get(a.lease)->draws[1].texture == 2 &&
           session.Get(a.lease)->draws[2].texture == 1);
    auto immutable = session.Get(a.lease);
    uint64_t prior = a.frame;
    matrices[1][3] = 0.25f;
    s.commands++;
    assert(session.Acquire(s, commands, matrices, resolve, texture, b) == 1 && b.fallback_count == 1 &&
           session.Get(b.lease)->draws[1].kind == 1);
    assert(immutable->id == prior && immutable->worlds[1][3] == 0);
    session.Release(b.lease);
    matrices[0][0] = std::numeric_limits<float>::quiet_NaN();
    s.commands++;
    assert(session.Acquire(s, commands, matrices, resolve, texture, b) == -1 && b.session == 0 &&
           immutable->id == prior);
    matrices[0] = Identity();
    commands[0].geometry = 999;
    s.commands++;
    assert(session.Acquire(s, commands, matrices, resolve, texture, b) == -2 && b.session == 0);
    commands[0].geometry = 1;
    commands[0].texture = 99;
    s.commands++;
    assert(session.Acquire(s, commands, matrices, resolve, texture, b) == -2);
    commands[0].texture = 1;
    int wrong = 0;
    std::thread other(
        [&]
        {
            UruiNativeFramePacket f{};
            wrong = session.Acquire(s, commands, matrices, resolve, texture, f);
        });
    other.join();
    assert(wrong == -3);
    assert(session.Release(a.lease) == 1);
    immutable.reset();
    s.epoch++;
    s.resources++;
    s.commands++;
    assert(session.Acquire(s, commands, matrices, resolve, texture, a) == 1 && a.epoch == s.epoch &&
           a.plan != plan->id);
    assert(session.Close() == -5);
    assert(session.Get(a.lease));
    assert(session.Acquire(s, commands, matrices, resolve, texture, b) == -5);
    assert(session.Release(a.lease) == 1 && session.Close() == 1);
    Session bounded(100);
    assert(bounded.Acquire(s, commands, matrices, resolve, texture, a) == -4 && a.session == 0);
    Session empty;
    UruiFrameStamp z{};
    z.flags = 1;
    z.epoch = 2;
    z.count = 0;
    std::vector<UruiCommand> zero;
    std::vector<std::array<float, 16>> zm;
    assert(empty.Acquire(z, zero, zm, resolve, texture, a) == 1 && a.count == 0 && a.draw_count == 0);
    empty.Release(a.lease);
    assert(empty.Close() == 1);
    Session recycled;
    uint64_t oldFrame = 0;
    for (int tick = 1; tick <= 1000; tick++)
    {
        commands[0].tx = float(tick);
        s.commands++;
        assert(recycled.Acquire(s, commands, matrices, resolve, texture, a) == 1);
        assert(a.frame != oldFrame);
        oldFrame = a.frame;
        assert(recycled.Release(a.lease) == 1);
        assert(recycled.StorageCount() <= 2 && recycled.Retained() < 1 << 20);
    }
    assert(recycled.Close() == 1);
    Session invalid;
    const int badIx[] = {0, 1, 4};
    auto bad = [&](uint64_t) { return urui_prepared::View{vs, 3, badIx, 3}; };
    assert(invalid.Acquire(s, commands, matrices, bad, texture, a) == -1 && a.session == 0);
    // Precise five-storage/shared-old-plan pressure reproducer from review.
    Session pressure;
    std::vector<UruiVertex> big(500000);
    const int tri[] = {0, 1, 2};
    auto heavy = [&](uint64_t) { return urui_prepared::View{big.data(), big.size(), tri, 3}; };
    std::vector<UruiCommand> one(1);
    one[0].geometry = 1;
    std::vector<std::array<float, 16>> om(1, Identity());
    UruiFrameStamp ps{};
    ps.epoch = 99;
    ps.flags = 1;
    ps.count = 1;
    std::array<UruiNativeFramePacket, 5> held{};
    for (int j = 0; j < 4; j++)
    {
        one[0].tx = float(j);
        ps.commands++;
        assert(pressure.Acquire(ps, one, om, heavy, texture, held[j]) == 1);
    }
    pressure.Release(held[3].lease);
    one[0].tx = 4;
    ps.commands++;
    assert(pressure.Acquire(ps, one, om, heavy, texture, held[4]) == 1 && pressure.StorageCount() == 5);
    pressure.Release(held[1].lease);
    pressure.Release(held[2].lease);
    pressure.Release(held[4].lease);
    for (int j = 1; j < 4; j++)
    {
        one[0].geometry = uint64_t(j + 1);
        ps.commands++;
        assert(pressure.Acquire(ps, one, om, heavy, texture, held[j]) == 1);
    }
    for (int j = 0; j < 4; j++)
        pressure.Release(held[j].lease);
    one[0].geometry = 5;
    ps.commands++;
    assert(pressure.Acquire(ps, one, om, heavy, texture, a) == 1);
    assert(pressure.Retained() < 40u * 1024 * 1024);
    pressure.Release(a.lease);
    assert(pressure.Close() == 1);
    std::cout << "NATIVE_FRAME_TEST_PASS ABI ordered_affine_and_fallback immutable_leases "
                 "stale_thread_budget_atomicity_epoch_pool_1000\n";
}
