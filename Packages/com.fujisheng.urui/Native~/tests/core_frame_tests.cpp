#include "core_frame.h"
#include <cassert>
#include <cstring>
#include <iostream>
#include <limits>
int main()
{
    float m[16] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 4, -7, 0, 1};
    UruiCommand c{};
    c.geometry = 7;
    c.texture = 9;
    c.tx = 12;
    c.ty = 1;
    c.clip_enabled = 1;
    c.clip_x = -3;
    c.clip_w = 40;
    UruiCoreKey k{};
    UruiCoreValue v{};
    float world[16];
    urui_core::Prepare(c, m, k, v, world);
    assert(k.mode == 1 && world[12] == 16 && world[13] == -6 && v.data[2] == 16 && v.data[6] == -6 && k.geometry == 7 &&
           k.texture == 9 && k.clip_x == -3);
    m[0] = 2;
    urui_core::Prepare(c, m, k, v, world);
    assert(k.mode == 0 && std::memcmp(world, m, 64) == 0 && v.data[0] == 12 && v.data[1] == 1);
    m[0] = 1;
    m[12] = -0.f;
    urui_core::Prepare(c, m, k, v, world);
    assert(k.mode == 0);
    m[12] = std::numeric_limits<float>::denorm_min();
    urui_core::Prepare(c, m, k, v, world);
    assert(k.mode == 0);
    m[12] = std::numeric_limits<float>::max();
    c.tx = m[12];
    urui_core::Prepare(c, m, k, v, world);
    assert(k.mode == 0);
    c.tx = 0;
    c.ty = 0;
    m[12] = m[13] = 0;
    urui_core::Prepare(c, m, k, v, world);
    assert(k.mode == 1);
    c.tx = std::numeric_limits<float>::quiet_NaN();
    urui_core::Prepare(c, m, k, v, world);
    assert(k.mode == 0);
    assert(sizeof(UruiCoreKey) == 40 && sizeof(UruiCoreValue) == 32 && sizeof(UruiCoreSummary) == 16);
    std::cout << "CORE_FRAME_TAGGED_TRANSLATION_PASS\n";
}
