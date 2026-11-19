#pragma once
#include "render_plan_protocol.h"
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>

namespace URUI::RenderPlan
{
    static_assert(sizeof(URUIPlanVertexStream) == 24 && sizeof(URUIPlanAttribute) == 16 &&
                      sizeof(URUIPlanExternalGeometry) == 200,
                  "external mesh input ABI");
    inline float ExternalHalf(uint16_t half)
    {
        const uint32_t sign = uint32_t(half & 0x8000) << 16;
        uint32_t exponent = (half >> 10) & 31;
        uint32_t mantissa = half & 1023;
        uint32_t bits;
        if (!exponent)
        {
            if (!mantissa)
            {
                bits = sign;
            }
            else
            {
                int shift = 0;
                while (!(mantissa & 1024))
                {
                    mantissa <<= 1;
                    shift++;
                }
                bits = sign | uint32_t(127 - 14 - shift) << 23 | (mantissa & 1023) << 13;
            }
        }
        else
        {
            bits = sign | ((exponent == 31 ? 255 : exponent + 112) << 23) | (mantissa << 13);
        }
        float value;
        std::memcpy(&value, &bits, sizeof(value));
        return value;
    }
    inline bool ExternalAttributeValid(const URUIPlanExternalGeometry& geometry, const URUIPlanAttribute& attribute,
                                       uint32_t minimum, uint32_t maximum, uint32_t formats)
    {
        if (!attribute.dimension)
        {
            return minimum == 0;
        }
        if (attribute.dimension < minimum || attribute.dimension > maximum ||
            attribute.stream >= geometry.stream_count || attribute.format > 31 || !(formats & (1u << attribute.format)))
        {
            return false;
        }
        const auto& stream = geometry.streams[attribute.stream];
        const uint32_t scalar = attribute.format == URUI_PLAN_FLOAT32   ? 4
                                : attribute.format == URUI_PLAN_FLOAT16 ? 2
                                                                        : 1;
        const uint64_t end = uint64_t(attribute.offset) + uint64_t(attribute.dimension) * scalar;
        return stream.data && stream.stride && end <= stream.stride &&
               (!geometry.vertex_count || (uint64_t(geometry.vertex_count) - 1) * stream.stride + end <= stream.bytes);
    }
    inline bool ValidateExternalLayout(const URUIPlanExternalGeometry& geometry)
    {
        if (!geometry.geometry || !geometry.version || geometry.vertex_count > 0x1000000 ||
            geometry.index_count > 0x3000000 || geometry.stream_count > 4)
        {
            return false;
        }
        if (!ExternalAttributeValid(geometry, geometry.position, 2, 3, 1u << URUI_PLAN_FLOAT32) ||
            !ExternalAttributeValid(geometry, geometry.color, 0, 4, 1u << URUI_PLAN_UNORM8) ||
            (geometry.color.dimension && geometry.color.dimension != 4) ||
            !ExternalAttributeValid(geometry, geometry.uv, 0, 4,
                                    (1u << URUI_PLAN_FLOAT32) | (1u << URUI_PLAN_FLOAT16)) ||
            geometry.uv.dimension == 1)
        {
            return false;
        }
        if (geometry.index_stride != 2 && geometry.index_stride != 4)
        {
            return false;
        }
        const uint64_t end = (uint64_t(geometry.index_start) + geometry.index_count) * geometry.index_stride;
        return (!geometry.index_count || geometry.indices) && end <= geometry.index_bytes &&
               geometry.index_count % 3 == 0;
    }
    inline uint32_t ExternalIndex(const URUIPlanExternalGeometry& geometry, uint32_t index)
    {
        if (index >= geometry.index_count)
        {
            throw std::out_of_range("external index range");
        }
        const uint64_t offset = (uint64_t(geometry.index_start) + index) * geometry.index_stride;
        if (!geometry.indices || (geometry.index_stride != 2 && geometry.index_stride != 4) ||
            offset + geometry.index_stride > geometry.index_bytes)
        {
            throw std::invalid_argument("external index storage");
        }
        const auto* input = static_cast<const uint8_t*>(geometry.indices) + offset;
        uint32_t value = 0;
        if (geometry.index_stride == 2)
        {
            uint16_t small;
            std::memcpy(&small, input, 2);
            value = small;
        }
        else
        {
            std::memcpy(&value, input, 4);
        }
        const int64_t result = int64_t(value) + geometry.base_vertex;
        if (result < 0 || uint64_t(result) >= geometry.vertex_count)
        {
            throw std::out_of_range("external vertex index");
        }
        return uint32_t(result);
    }
    inline bool ValidateExternalGeometry(const URUIPlanExternalGeometry& geometry)
    {
        if (!ValidateExternalLayout(geometry))
        {
            return false;
        }
        try
        {
            for (uint32_t n = 0; n < geometry.index_count; n++)
            {
                ExternalIndex(geometry, n);
            }
            return true;
        }
        catch (const std::exception&)
        {
            return false;
        }
    }
    inline const uint8_t* ExternalAttribute(const URUIPlanExternalGeometry& geometry,
                                            const URUIPlanAttribute& attribute, uint32_t vertex)
    {
        const auto& stream = geometry.streams[attribute.stream];
        return static_cast<const uint8_t*>(stream.data) + uint64_t(vertex) * stream.stride + attribute.offset;
    }
    // The compiler validates the layout once while freezing the MeshData view.
    inline URUIPlanVertex ReadExternalVertex(const URUIPlanExternalGeometry& geometry, uint32_t vertex)
    {
        if (vertex >= geometry.vertex_count)
        {
            throw std::out_of_range("external vertex layout");
        }
        URUIPlanVertex result{};
        float position[3]{};
        std::memcpy(position, ExternalAttribute(geometry, geometry.position, vertex),
                    geometry.position.dimension * sizeof(float));
        result.x = position[0];
        result.y = position[1];
        result.z = position[2];
        result.r = result.g = result.b = result.a = 255;
        if (geometry.color.dimension)
        {
            std::memcpy(&result.r, ExternalAttribute(geometry, geometry.color, vertex), 4);
        }
        float uv[2]{};
        if (geometry.uv.dimension)
        {
            const auto* input = ExternalAttribute(geometry, geometry.uv, vertex);
            if (geometry.uv.format == URUI_PLAN_FLOAT32)
            {
                std::memcpy(uv, input, sizeof(uv));
            }
            else
            {
                uint16_t values[2];
                std::memcpy(values, input, sizeof(values));
                uv[0] = ExternalHalf(values[0]);
                uv[1] = ExternalHalf(values[1]);
            }
        }
        result.u = uv[0];
        result.v = uv[1];
        if (!std::isfinite(result.x) || !std::isfinite(result.y) || !std::isfinite(result.z) ||
            !std::isfinite(result.u) || !std::isfinite(result.v))
        {
            throw std::invalid_argument("nonfinite external geometry");
        }
        return result;
    }
    inline URUIPlanVertex ExternalVertex(const URUIPlanExternalGeometry& geometry, uint32_t vertex)
    {
        if (!ValidateExternalLayout(geometry))
        {
            throw std::invalid_argument("external vertex layout");
        }
        return ReadExternalVertex(geometry, vertex);
    }
}
