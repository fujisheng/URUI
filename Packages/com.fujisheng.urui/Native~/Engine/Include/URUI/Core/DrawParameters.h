#pragma once
#include "Colour.h"
#include "Vector2.h"

namespace URUI
{
    // Uniform draw state is independent of immutable local mesh topology.
    struct DrawParameters
    {
        ColourbPremultiplied tint{255, 255, 255, 255};
        Vector2f uv_scale{1, 1};
        Vector2f uv_offset{0, 0};
    };
}
