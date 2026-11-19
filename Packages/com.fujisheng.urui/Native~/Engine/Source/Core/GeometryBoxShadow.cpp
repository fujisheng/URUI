#include "GeometryBoxShadow.h"
#include "../../Include/URUI/Core/Box.h"
#include "../../Include/URUI/Core/CompiledFilterShader.h"
#include "../../Include/URUI/Core/DecorationTypes.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/Geometry.h"
#include "../../Include/URUI/Core/Math.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/RenderManager.h"
#include "ElementStyle.h"
#include <algorithm>
#include <cmath>

namespace URUI
{

    BoxShadowGeometryInfo GeometryBoxShadow::Resolve(Element* element, const CornerSizes& border_radius,
                                                     ColourbPremultiplied background_color,
                                                     const Array<ColourbPremultiplied, 4>& border_colors, float opacity)
    {
        URUI_ZoneScoped;

        // Find the box-shadow texture dimension and offset required to cover all box-shadows and element boxes combined.
        Vector2f element_offset_in_texture;
        Vector2i texture_dimensions;

        const Property* p_box_shadow =
            element->GetStyle()->GetLocalPropertyWithResolvedVariables(PropertyId::BoxShadow);
        URUI_ASSERT(p_box_shadow->value.GetType() == Variant::BOXSHADOWLIST);
        BoxShadowList shadow_list = p_box_shadow->value.Get<BoxShadowList>();

        // Resolve all lengths to px units.
        for (BoxShadow& shadow : shadow_list)
        {
            shadow.blur_radius = NumericValue(element->ResolveLength(shadow.blur_radius), Unit::PX);
            shadow.spread_distance = NumericValue(element->ResolveLength(shadow.spread_distance), Unit::PX);
            shadow.offset_x = NumericValue(element->ResolveLength(shadow.offset_x), Unit::PX);
            shadow.offset_y = NumericValue(element->ResolveLength(shadow.offset_y), Unit::PX);
        }

        {
            Vector2f extend_min;
            Vector2f extend_max;

            // Extend the render-texture to encompass box-shadow blur and spread.
            for (const BoxShadow& shadow : shadow_list)
            {
                if (!shadow.inset)
                {
                    const float extend = 1.5f * shadow.blur_radius.number + shadow.spread_distance.number;
                    const Vector2f offset = {shadow.offset_x.number, shadow.offset_y.number};
                    extend_min = Math::Min(extend_min, offset - Vector2f(extend));
                    extend_max = Math::Max(extend_max, offset + Vector2f(extend));
                }
            }

            Rectanglef texture_region;

            // Extend the render-texture further to cover all the element's boxes.
            for (int i = 0; i < element->GetNumBoxes(); i++)
            {
                const RenderBox box = element->GetRenderBox(BoxArea::Border, i);
                texture_region =
                    texture_region.Join(Rectanglef::FromPositionSize(box.GetBorderOffset(), box.GetFillSize()));
            }

            texture_region = texture_region.Extend(-extend_min, extend_max);
            Math::ExpandToPixelGrid(texture_region);

            element_offset_in_texture = -texture_region.TopLeft();
            texture_dimensions = Vector2i(texture_region.Size());
        }

        // Since we can reuse textures across multiple box shadows with the same properties,
        // we need to copy the element's box shadow list and the background and border geometry.
        RenderBoxList padding_render_boxes{};
        RenderBoxList border_render_boxes{};

        for (int i = 0; i < element->GetNumBoxes(); i++)
        {
            padding_render_boxes.push_back(element->GetRenderBox(BoxArea::Padding, i));
            border_render_boxes.push_back(element->GetRenderBox(BoxArea::Border, i));
        }

        // Finally, create cache information
        BoxShadowGeometryInfo geometry_info;
        geometry_info.background_color = background_color;
        geometry_info.border_colors = border_colors;
        geometry_info.border_radius = border_radius;
        geometry_info.texture_dimensions = texture_dimensions;
        geometry_info.element_offset_in_texture = element_offset_in_texture;
        geometry_info.padding_render_boxes = std::move(padding_render_boxes);
        geometry_info.border_render_boxes = std::move(border_render_boxes);
        geometry_info.shadow_list = std::move(shadow_list);
        geometry_info.opacity = opacity;
        return geometry_info;
    }

    namespace
    {
        // 阴影缓存使用实际 RGBA 资源，避免依赖 Unity 桥接层未实现的 SaveLayerAsTexture/模板缓冲接口。
        // 复用现有圆角网格计算轮廓，每次缓存生成时采样；相同样式与尺寸仍共享不可变纹理。
        Vector<float> Rasterize(const Mesh& mesh, Vector2i size, Vector2f offset)
        {
            Vector<uint16_t> coverage(size_t(size.x) * size.y, 0);
            auto cross = [](Vector2f a, Vector2f b) { return a.x * b.y - a.y * b.x; };
            for (size_t triangle = 0; triangle + 2 < mesh.indices.size(); triangle += 3)
            {
                const Vector2f a = mesh.vertices[mesh.indices[triangle]].position + offset;
                const Vector2f b = mesh.vertices[mesh.indices[triangle + 1]].position + offset;
                const Vector2f c = mesh.vertices[mesh.indices[triangle + 2]].position + offset;
                const float area = cross(b - a, c - a);
                if (std::abs(area) < 1e-7f)
                {
                    continue;
                }
                const int left = std::max(0, int(std::floor(std::min({a.x, b.x, c.x}))));
                const int top = std::max(0, int(std::floor(std::min({a.y, b.y, c.y}))));
                const int right = std::min(size.x, int(std::ceil(std::max({a.x, b.x, c.x}))));
                const int bottom = std::min(size.y, int(std::ceil(std::max({a.y, b.y, c.y}))));
                const float direction = area > 0 ? 1.f : -1.f;
                for (int y = top; y < bottom; ++y)
                {
                    for (int x = left; x < right; ++x)
                    {
                        auto& mask = coverage[size_t(y) * size.x + x];
                        if (mask == 0xffff)
                        {
                            continue;
                        }
                        for (int sy = 0; sy < 4; ++sy)
                        {
                            for (int sx = 0; sx < 4; ++sx)
                            {
                                const uint16_t bit = uint16_t(1u << (sy * 4 + sx));
                                if (mask & bit)
                                {
                                    continue;
                                }
                                const Vector2f point{x + (sx + .5f) * .25f, y + (sy + .5f) * .25f};
                                if (direction * cross(b - a, point - a) >= -1e-6f &&
                                    direction * cross(c - b, point - b) >= -1e-6f &&
                                    direction * cross(a - c, point - c) >= -1e-6f)
                                {
                                    mask |= bit;
                                }
                            }
                        }
                    }
                }
            }
            Vector<float> result(coverage.size());
            for (size_t index = 0; index < coverage.size(); ++index)
            {
                uint16_t value = coverage[index];
                int count = 0;
                while (value)
                {
                    value &= uint16_t(value - 1);
                    ++count;
                }
                result[index] = count / 16.f;
            }
            return result;
        }

        void Blur(Vector<float>& data, Vector2i size, float sigma, int radius)
        {
            if (radius == 0)
            {
                return;
            }
            Vector<float> weights(2 * radius + 1);
            float total = 0;
            for (int index = -radius; index <= radius; ++index)
            {
                total += weights[index + radius] = std::exp(-.5f * index * index / (sigma * sigma));
            }
            for (auto& weight : weights)
            {
                weight /= total;
            }
            Vector<float> temporary(data.size());
            for (int axis = 0; axis < 2; ++axis)
            {
                for (int y = 0; y < size.y; ++y)
                {
                    for (int x = 0; x < size.x; ++x)
                    {
                        float sum = 0;
                        for (int tap = -radius; tap <= radius; ++tap)
                        {
                            const int px = x + (axis == 0 ? tap : 0);
                            const int py = y + (axis == 1 ? tap : 0);
                            if (px >= 0 && py >= 0 && px < size.x && py < size.y)
                            {
                                sum += data[size_t(py) * size.x + px] * weights[tap + radius];
                            }
                        }
                        temporary[size_t(y) * size.x + x] = sum;
                    }
                }
                data.swap(temporary);
            }
        }

        bool RasterizeShadows(const CallbackTextureInterface& texture, const BoxShadowGeometryInfo& info)
        {
            const auto size = info.texture_dimensions;
            constexpr size_t max_pixels = 16u * 1024u * 1024u;
            if (size.x <= 0 || size.y <= 0 || size.x > 8192 || size.y > 8192 || size_t(size.x) * size.y > max_pixels)
            {
                return false;
            }
            Vector<byte> pixels(size_t(size.x) * size.y * 4, 0);
            for (int index = int(info.shadow_list.size()) - 1; index >= 0; --index)
            {
                const auto& shadow = info.shadow_list[index];
                const float sigma = .5f * shadow.blur_radius.number;
                if (!std::isfinite(sigma) || sigma < 0 || sigma > 8192)
                {
                    return false;
                }
                const int halo = sigma >= .25f ? int(std::ceil(3.f * sigma)) : 0;
                const Vector2i work_size{size.x + 2 * halo, size.y + 2 * halo};
                if (work_size.x > 8192 || work_size.y > 8192 || size_t(work_size.x) * work_size.y > max_pixels)
                {
                    return false;
                }
                const float spread = shadow.spread_distance.number;
                const float signed_spread = shadow.inset ? -spread : spread;
                const Vector2f displacement{shadow.offset_x.number, shadow.offset_y.number};
                Mesh clip_mesh;
                Mesh shadow_mesh;
                const auto& boxes = shadow.inset ? info.padding_render_boxes : info.border_render_boxes;
                for (auto box : boxes)
                {
                    MeshUtilities::GenerateBackground(clip_mesh, box, ColourbPremultiplied(255));
                    CornerSizes radii = info.border_radius;
                    for (auto& radius : radii)
                    {
                        float factor = shadow.inset ? -1.f : 1.f;
                        if (radius < spread)
                        {
                            const float ratio = radius / spread - 1.f;
                            factor *= 1.f + ratio * ratio * ratio;
                        }
                        radius = std::max(radius + factor * spread, 0.f);
                    }
                    box.SetFillSize(Math::Max(box.GetFillSize() + Vector2f(2 * signed_spread), Vector2f(0)));
                    box.SetBorderRadius(radii);
                    box.SetBorderOffset(box.GetBorderOffset() - Vector2f(signed_spread));
                    if (box.GetFillSize().x > 0 && box.GetFillSize().y > 0)
                    {
                        MeshUtilities::GenerateBackground(shadow_mesh, box, ColourbPremultiplied(255));
                    }
                }
                const auto clip = Rasterize(clip_mesh, size, info.element_offset_in_texture);
                auto mask = Rasterize(shadow_mesh, work_size,
                                      info.element_offset_in_texture + displacement + Vector2f(float(halo)));
                Blur(mask, work_size, sigma, halo);
                for (int y = 0; y < size.y; ++y)
                {
                    for (int x = 0; x < size.x; ++x)
                    {
                        const size_t pixel = size_t(y) * size.x + x;
                        const float silhouette = mask[size_t(y + halo) * work_size.x + x + halo];
                        const float coverage =
                            shadow.inset ? clip[pixel] * (1 - silhouette) : (1 - clip[pixel]) * silhouette;
                        const float alpha = coverage * (shadow.color.alpha / 255.f);
                        for (int channel = 0; channel < 4; ++channel)
                        {
                            pixels[4 * pixel + channel] = byte(Math::Round(Math::Clamp(
                                coverage * shadow.color[channel] + (1 - alpha) * pixels[4 * pixel + channel], 0.f,
                                255.f)));
                        }
                    }
                }
            }
            return texture.GenerateTexture(pixels, size);
        }
    }

    void GeometryBoxShadow::GenerateTexture(CallbackTexture& out_shadow_texture,
                                            Geometry& out_background_border_geometry, RenderManager& render_manager,
                                            const BoxShadowGeometryInfo& info)
    {
        out_background_border_geometry.Release();
        out_shadow_texture = render_manager.MakeCallbackTexture([&info](const CallbackTextureInterface& texture)
                                                                { return RasterizeShadows(texture, info); });
    }
}
