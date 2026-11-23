#pragma once
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/ID.h"
#include "../../Include/URUI/Core/Mesh.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/PropertyIdSet.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
#include <utility>

// Image geometry is independent of the Unity host and application binding layer.
namespace URUI::Images
{
    constexpr float pi = 3.14159265358979323846f;

    inline float Number(URUI::Element& element, URUI::PropertyId id, float fallback)
    {
        const auto* property = element.GetProperty(id);
        const float value = property ? property->Get<float>() : fallback;
        return std::isfinite(value) ? value : fallback;
    }

    inline bool Changed(const URUI::PropertyIdSet& changed)
    {
        return changed.Contains(URUI::PropertyId::ObjectFit) ||
               changed.Contains(URUI::PropertyId::ImageSliceTop) ||
               changed.Contains(URUI::PropertyId::ImageSliceRight) ||
               changed.Contains(URUI::PropertyId::ImageSliceBottom) ||
               changed.Contains(URUI::PropertyId::ImageSliceLeft) ||
               changed.Contains(URUI::PropertyId::ImageFillMode) ||
               changed.Contains(URUI::PropertyId::ImageFillOrigin) ||
               changed.Contains(URUI::PropertyId::ImageFillClockwise) ||
               changed.Contains(URUI::PropertyId::ImageFillAmount) ||
               changed.Contains(URUI::PropertyId::ImageFillCenter);
    }

    inline void Generate(URUI::Element& element, URUI::Mesh& mesh, URUI::Vector2f offset, URUI::Vector2f size,
                         URUI::Vector2f uv0, URUI::Vector2f uv1, URUI::Vector2f source, float density,
                         URUI::ColourbPremultiplied color)
    {
        if (size.x <= 0 || size.y <= 0 || source.x <= 0 || source.y <= 0)
        {
            return;
        }
        const int mode = int(Number(element, URUI::PropertyId::ImageFillMode, 0));
        float left = std::max(0.f, Number(element, URUI::PropertyId::ImageSliceLeft, 0));
        float right = std::max(0.f, Number(element, URUI::PropertyId::ImageSliceRight, 0));
        float top = std::max(0.f, Number(element, URUI::PropertyId::ImageSliceTop, 0));
        float bottom = std::max(0.f, Number(element, URUI::PropertyId::ImageSliceBottom, 0));
        const bool sliced = mode == 0 && left + right + top + bottom > 0;
        const int fit = int(Number(element, URUI::PropertyId::ObjectFit, 0));
        if (!sliced && fit != 0)
        {
            const float scale = fit == 1 ? std::min(size.x / source.x, size.y / source.y)
                                         : std::max(size.x / source.x, size.y / source.y);
            const auto fitted = source * scale;
            if (fit == 1)
            {
                offset += (size - fitted) * .5f;
                size = fitted;
            }
            else
            {
                const URUI::Vector2f crop(size.x / fitted.x, size.y / fitted.y);
                const auto oldUv = uv0;
                uv0 += (uv1 - uv0) * (URUI::Vector2f(1, 1) - crop) * .5f;
                uv1 = oldUv + (uv1 - oldUv) * (URUI::Vector2f(1, 1) + crop) * .5f;
            }
        }
        auto quad = [&](URUI::Vector2f a, URUI::Vector2f b, URUI::Vector2f ta, URUI::Vector2f tb)
        {
            if (b.x > a.x && b.y > a.y)
            {
                URUI::MeshUtilities::GenerateQuad(mesh, a, b - a, color, ta, tb);
            }
        };
        if (sliced)
        {
            const float sx = left + right > source.x ? source.x / (left + right) : 1;
            const float sy = top + bottom > source.y ? source.y / (top + bottom) : 1;
            left *= sx;
            right *= sx;
            top *= sy;
            bottom *= sy;
            const float dx = std::min(density, left + right > 0 ? size.x / (left + right) : density);
            const float dy = std::min(density, top + bottom > 0 ? size.y / (top + bottom) : density);
            const float xs[] = {offset.x, offset.x + left * dx, offset.x + size.x - right * dx, offset.x + size.x};
            const float ys[] = {offset.y, offset.y + top * dy, offset.y + size.y - bottom * dy, offset.y + size.y};
            const float us[] = {uv0.x, uv0.x + (uv1.x - uv0.x) * left / source.x,
                                uv1.x - (uv1.x - uv0.x) * right / source.x, uv1.x};
            const float vs[] = {uv0.y, uv0.y + (uv1.y - uv0.y) * top / source.y,
                                uv1.y - (uv1.y - uv0.y) * bottom / source.y, uv1.y};
            const bool fill_center = Number(element, URUI::PropertyId::ImageFillCenter, 1) >= .5f;
            for (int y = 0; y < 3; ++y)
            {
                for (int x = 0; x < 3; ++x)
                {
                    if (x == 1 && y == 1 && !fill_center)
                    {
                        continue;
                    }
                    quad({xs[x], ys[y]}, {xs[x + 1], ys[y + 1]}, {us[x], vs[y]}, {us[x + 1], vs[y + 1]});
                }
            }
            return;
        }
        const float amount = std::clamp(Number(element, URUI::PropertyId::ImageFillAmount, 1), 0.f, 1.f);
        if (mode == 0 || amount >= 1)
        {
            quad(offset, offset + size, uv0, uv1);
            return;
        }
        if (amount < .001f)
        {
            return;
        }
        const int origin = int(std::clamp(Number(element, URUI::PropertyId::ImageFillOrigin, 0), 0.f, 3.f));
        if (mode == 1 || mode == 2)
        {
            const bool reverse = mode == 1 ? origin == 1 : origin != 1;
            URUI::Vector2f a(0, 0), b(1, 1);
            if (mode == 1)
            {
                if (reverse)
                {
                    a.x = 1 - amount;
                }
                else
                {
                    b.x = amount;
                }
            }
            else
            {
                if (reverse)
                {
                    a.y = 1 - amount;
                }
                else
                {
                    b.y = amount;
                }
            }
            quad(offset + a * size, offset + b * size, uv0 + a * (uv1 - uv0), uv0 + b * (uv1 - uv0));
            return;
        }
        const bool clockwise = Number(element, URUI::PropertyId::ImageFillClockwise, 1) >= .5f;
        const URUI::Vector2f corners[] = {{0, 1}, {0, 0}, {1, 0}, {1, 1}};
        URUI::Vector2f center(.5f, .5f), angularScale(1, 1);
        float start = 0;
        float extent = 2 * pi;
        if (mode == 3)
        {
            center = corners[origin];
            start = -pi * .5f + origin * pi * .5f;
            extent = pi * .5f;
        }
        else if (mode == 4)
        {
            const URUI::Vector2f centers[] = {{.5f, 1}, {0, .5f}, {.5f, 0}, {1, .5f}};
            center = centers[origin];
            start = pi + origin * pi * .5f;
            extent = pi;
            // Each half is a normalized quadrant, preserving the angular metric
            // of a half-rectangle rather than that of the whole image.
            angularScale = origin % 2 == 0 ? URUI::Vector2f(.5f, 1) : URUI::Vector2f(1, .5f);
        }
        else
        {
            start = pi * .5f - origin * pi * .5f;
        }
        if (!clockwise && mode != 5)
        {
            start += extent;
        }
        const float direction = clockwise ? 1.f : -1.f;
        const float sweep = extent * amount;
        auto boundary = [&](float angle)
        {
            const URUI::Vector2f ray(std::cos(angle) * angularScale.x, std::sin(angle) * angularScale.y);
            float length = std::numeric_limits<float>::infinity();
            if (ray.x > 1e-6f)
            {
                length = std::min(length, (1 - center.x) / ray.x);
            }
            if (ray.x < -1e-6f)
            {
                length = std::min(length, -center.x / ray.x);
            }
            if (ray.y > 1e-6f)
            {
                length = std::min(length, (1 - center.y) / ray.y);
            }
            if (ray.y < -1e-6f)
            {
                length = std::min(length, -center.y / ray.y);
            }
            return center + ray * length;
        };
        std::array<std::pair<float, URUI::Vector2f>, 6> points;
        size_t point_count = 0;
        points[point_count++] = {0.f, boundary(start)};
        points[point_count++] = {sweep, boundary(start + direction * sweep)};
        for (const auto& corner : corners)
        {
            const auto delta = corner - center;
            if (delta.x == 0 && delta.y == 0)
            {
                continue;
            }
            float angle = std::fmod(
                direction * (std::atan2(delta.y / angularScale.y, delta.x / angularScale.x) - start) + 4 * pi, 2 * pi);
            if (angle > 1e-5f && angle < sweep - 1e-5f && point_count < points.size())
            {
                points[point_count++] = {angle, corner};
            }
        }
        std::sort(points.begin(), points.begin() + point_count,
                  [](const auto& a, const auto& b) { return a.first < b.first; });
        const int first = int(mesh.vertices.size());
        auto vertex = [&](URUI::Vector2f point) {
            mesh.vertices.push_back({offset + point * size, color, uv0 + point * (uv1 - uv0)});
        };
        vertex(center);
        for (size_t i = 0; i < point_count; ++i)
        {
            vertex(points[i].second);
        }
        for (size_t i = 1; i < point_count; ++i)
        {
            mesh.indices.insert(mesh.indices.end(), {first, first + int(i), first + int(i) + 1});
        }
    }
}
