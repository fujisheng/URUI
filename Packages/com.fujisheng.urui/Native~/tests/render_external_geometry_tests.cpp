#include "../render_external_geometry.h"
#include <cassert>
#include <array>
#include <iostream>

int main()
{
    struct Position
    {
        float x;
        float y;
        float z;
        float padding;
    };
    struct Attributes
    {
        uint8_t color[4];
        uint16_t uv[2];
        float padding;
    };
    Position positions[]{{1, 2, 3, 99}, {4, 5, 6, 99}, {7, 8, 9, 99}, {10, 11, 12, 99}};
    Attributes attributes[]{{{1, 2, 3, 4}, {0, 0x3c00}, 99},
                            {{5, 6, 7, 8}, {0x3800, 0}, 99},
                            {{9, 10, 11, 12}, {0x3c00, 0x3800}, 99},
                            {{13, 14, 15, 16}, {0, 0}, 99}};
    uint16_t indices[]{99, 0, 1, 2};
    URUIPlanExternalGeometry input{};
    input.geometry = 101;
    input.version = 1;
    input.vertex_count = 4;
    input.stream_count = 2;
    input.streams[0] = {positions, sizeof(positions), sizeof(Position), 0};
    input.streams[1] = {attributes, sizeof(attributes), sizeof(Attributes), 0};
    input.position = {0, 0, URUI_PLAN_FLOAT32, 3};
    input.color = {1, 0, URUI_PLAN_UNORM8, 4};
    input.uv = {1, 4, URUI_PLAN_FLOAT16, 2};
    input.indices = indices;
    input.index_bytes = sizeof(indices);
    input.index_stride = 2;
    input.index_start = 1;
    input.index_count = 3;
    input.base_vertex = 1;
    assert(URUI::RenderPlan::ValidateExternalGeometry(input));
    assert(URUI::RenderPlan::ExternalHalf(1) == std::ldexp(1.f, -24) && URUI::RenderPlan::ExternalHalf(0x8000) == 0.f);
    auto vertex = URUI::RenderPlan::ExternalVertex(input, 1);
    assert(vertex.x == 4 && vertex.y == 5 && vertex.z == 6 && vertex.r == 5 && vertex.a == 8 && vertex.u == .5f &&
           vertex.v == 0);
    assert(URUI::RenderPlan::ExternalIndex(input, 0) == 1 && URUI::RenderPlan::ExternalIndex(input, 2) == 3);
    auto invalid = input;
    invalid.index_count = 4;
    assert(!URUI::RenderPlan::ValidateExternalGeometry(invalid));
    invalid = input;
    invalid.base_vertex = 2;
    assert(!URUI::RenderPlan::ValidateExternalGeometry(invalid));
    invalid = input;
    invalid.position.offset = 8;
    assert(!URUI::RenderPlan::ValidateExternalGeometry(invalid));
    invalid = input;
    invalid.streams[1].bytes = 8;
    assert(!URUI::RenderPlan::ValidateExternalGeometry(invalid));
    invalid = input;
    invalid.index_stride = 1;
    assert(!URUI::RenderPlan::ValidateExternalGeometry(invalid));
    invalid = input;
    invalid.color.format = URUI_PLAN_FLOAT32;
    assert(!URUI::RenderPlan::ValidateExternalGeometry(invalid));
    invalid = input;
    invalid.uv.dimension = 1;
    assert(!URUI::RenderPlan::ValidateExternalGeometry(invalid));
    uint32_t wide[]{3, 2, 1};
    input.indices = wide;
    input.index_bytes = sizeof(wide);
    input.index_start = 0;
    input.index_stride = 4;
    input.base_vertex = -1;
    assert(URUI::RenderPlan::ValidateExternalGeometry(input) && URUI::RenderPlan::ExternalIndex(input, 2) == 0);
    for (int n = 0; n < 10000; n++)
    {
        const auto copy = URUI::RenderPlan::ExternalVertex(input, n % 4);
        assert(copy.parameter == 0 && copy.texture == 0);
    }
    std::cout << "external geometry layout/range contracts passed\n";
}
