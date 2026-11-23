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
        struct RoundedRect
        {
            float cx, cy;
            float hx, hy;
            float r_tl, r_tr, r_br, r_bl;
            bool empty = true;

            static bool FromBox(const RenderBox& box, Vector2f offset, CornerSizes radii, RoundedRect& out_rect)
            {
                const EdgeSizes edges = box.GetBorderWidths();
                const Vector2f origin = box.GetBorderOffset() + Vector2f(edges[3], edges[0]) + offset;
                const Vector2f size = box.GetFillSize();
                out_rect = RoundedRect{};
                if (size.x <= 0.f || size.y <= 0.f)
                {
                    return true;
                }
                const float sum_radius = radii[0] + radii[1] + radii[2] + radii[3];
                float tl = 0.f, tr = 0.f, br = 0.f, bl = 0.f;
                if (sum_radius > 1.f)
                {
                    float scale = 1.f;
                    if (radii[0] + radii[1] > 0.f)
                        scale = std::min(scale, size.x / (radii[0] + radii[1]));
                    if (radii[2] + radii[3] > 0.f)
                        scale = std::min(scale, size.x / (radii[2] + radii[3]));
                    if (radii[0] + radii[3] > 0.f)
                        scale = std::min(scale, size.y / (radii[0] + radii[3]));
                    if (radii[1] + radii[2] > 0.f)
                        scale = std::min(scale, size.y / (radii[1] + radii[2]));
                    const float o_tl = Math::Round(radii[0] * scale);
                    const float o_tr = Math::Round(radii[1] * scale);
                    const float o_br = Math::Round(radii[2] * scale);
                    const float o_bl = Math::Round(radii[3] * scale);
                    const float tl_x = std::max(0.f, o_tl - edges[3]), tl_y = std::max(0.f, o_tl - edges[0]);
                    const float tr_x = std::max(0.f, o_tr - edges[1]), tr_y = std::max(0.f, o_tr - edges[0]);
                    const float br_x = std::max(0.f, o_br - edges[1]), br_y = std::max(0.f, o_br - edges[2]);
                    const float bl_x = std::max(0.f, o_bl - edges[3]), bl_y = std::max(0.f, o_bl - edges[2]);
                    // Asymmetric border widths produce elliptical inner arcs; fall back to polygon rasterization.
                    if (tl_x != tl_y || tr_x != tr_y || br_x != br_y || bl_x != bl_y)
                    {
                        return false;
                    }
                    tl = tl_x;
                    tr = tr_x;
                    br = br_x;
                    bl = bl_x;
                }
                out_rect.cx = origin.x + size.x * 0.5f;
                out_rect.cy = origin.y + size.y * 0.5f;
                out_rect.hx = size.x * 0.5f;
                out_rect.hy = size.y * 0.5f;
                out_rect.r_tl = tl;
                out_rect.r_tr = tr;
                out_rect.r_br = br;
                out_rect.r_bl = bl;
                out_rect.empty = false;
                return true;
            }

            float SignedDistance(float px, float py) const
            {
                const float dx = px - cx;
                const float dy = py - cy;
                const float r = (dy <= 0.f) ? (dx <= 0.f ? r_tl : r_tr) : (dx <= 0.f ? r_bl : r_br);
                const float qx = std::abs(dx) - (hx - r);
                const float qy = std::abs(dy) - (hy - r);
                const float ox = std::max(qx, 0.f);
                const float oy = std::max(qy, 0.f);
                return std::sqrt(ox * ox + oy * oy) + std::min(std::max(qx, qy), 0.f) - r;
            }
        };

        Vector<float> RasterizeRoundedRect(const RoundedRect& rect, Vector2i size)
        {
            Vector<float> result(size_t(size.x) * size.y, 0.f);
            if (rect.empty)
            {
                return result;
            }
            const int left = std::max(0, int(std::floor(rect.cx - rect.hx)) - 1);
            const int top = std::max(0, int(std::floor(rect.cy - rect.hy)) - 1);
            const int right = std::min(size.x, int(std::ceil(rect.cx + rect.hx)) + 1);
            const int bottom = std::min(size.y, int(std::ceil(rect.cy + rect.hy)) + 1);
            for (int y = top; y < bottom; ++y)
            {
                float* row = result.data() + size_t(y) * size.x;
                const float py = float(y) + 0.5f;
                for (int x = left; x < right; ++x)
                {
                    const float px = float(x) + 0.5f;
                    const float d = rect.SignedDistance(px, py);
                    if (d <= -0.75f)
                    {
                        row[x] = 1.f;
                    }
                    else if (d < 0.75f)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < 4; ++sy)
                        {
                            const float sub_y = float(y) + (sy + 0.5f) * 0.25f;
                            for (int sx = 0; sx < 4; ++sx)
                            {
                                const float sub_x = float(x) + (sx + 0.5f) * 0.25f;
                                if (rect.SignedDistance(sub_x, sub_y) <= 1e-6f)
                                {
                                    ++hits;
                                }
                            }
                        }
                        row[x] = hits * (1.f / 16.f);
                    }
                }
            }
            return result;
        }

        // 多盒（如折行内联元素）回退路径。
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
            const float inv_two_sigma_sq = -0.5f / (sigma * sigma);
            for (int index = -radius; index <= radius; ++index)
            {
                total += weights[index + radius] = std::exp(float(index * index) * inv_two_sigma_sq);
            }
            const float inv_total = 1.f / total;
            for (auto& weight : weights)
            {
                weight *= inv_total;
            }
            Vector<float> temporary(data.size(), 0.f);
            // Horizontal pass (skip empty rows)
            for (int y = 0; y < size.y; ++y)
            {
                const float* src_row = data.data() + size_t(y) * size.x;
                int min_x = size.x;
                int max_x = -1;
                for (int x = 0; x < size.x; ++x)
                {
                    if (src_row[x] != 0.f)
                    {
                        min_x = x;
                        break;
                    }
                }
                if (min_x >= size.x)
                {
                    continue;
                }
                for (int x = size.x - 1; x >= min_x; --x)
                {
                    if (src_row[x] != 0.f)
                    {
                        max_x = x;
                        break;
                    }
                }
                const int out_min = std::max(0, min_x - radius);
                const int out_max = std::min(size.x - 1, max_x + radius);
                float* dst_row = temporary.data() + size_t(y) * size.x;
                for (int x = out_min; x <= out_max; ++x)
                {
                    const int k0 = std::max(-radius, min_x - x);
                    const int k1 = std::min(radius, max_x - x);
                    float sum = 0.f;
                    for (int tap = k0; tap <= k1; ++tap)
                    {
                        sum += src_row[x + tap] * weights[tap + radius];
                    }
                    dst_row[x] = sum;
                }
            }
            std::fill(data.begin(), data.end(), 0.f);
            // Vertical pass
            for (int y = 0; y < size.y; ++y)
            {
                const int k0 = std::max(-radius, -y);
                const int k1 = std::min(radius, size.y - 1 - y);
                float* dst_row = data.data() + size_t(y) * size.x;
                for (int tap = k0; tap <= k1; ++tap)
                {
                    const float w = weights[tap + radius];
                    const float* src_row = temporary.data() + size_t(y + tap) * size.x;
                    for (int x = 0; x < size.x; ++x)
                    {
                        dst_row[x] += src_row[x] * w;
                    }
                }
            }
        }

        struct NineSlicePlan
        {
            bool collapsed_x = false;
            bool collapsed_y = false;
            int left_cut = 0;
            int right_cut = 0;
            int top_cut = 0;
            int bottom_cut = 0;
            int shrink_x = 0;
            int shrink_y = 0;
            Vector2i small_size{};
        };

        NineSlicePlan PlanNineSlice(const BoxShadowGeometryInfo& info)
        {
            NineSlicePlan plan{};
            plan.small_size = info.texture_dimensions;
            if (info.border_render_boxes.size() != 1 || info.padding_render_boxes.size() != 1 || info.shadow_list.empty())
            {
                return plan;
            }
            const auto& border_box = info.border_render_boxes[0];
            const auto& padding_box = info.padding_render_boxes[0];
            const auto orig_size = info.texture_dimensions;
            const auto off = info.element_offset_in_texture;

            float left_need = 0.f;
            float right_need = 0.f;
            float top_need = 0.f;
            float bottom_need = 0.f;

            auto include_box = [&](const RenderBox& box, const CornerSizes& radii, Vector2f extra_offset, float halo)
            {
                const EdgeSizes edges = box.GetBorderWidths();
                const float x0 = box.GetBorderOffset().x + edges[3] + off.x + extra_offset.x;
                const float y0 = box.GetBorderOffset().y + edges[0] + off.y + extra_offset.y;
                const float x1 = x0 + box.GetFillSize().x;
                const float y1 = y0 + box.GetFillSize().y;
                const float r_left = std::max(radii[0], radii[3]);
                const float r_right = std::max(radii[1], radii[2]);
                const float r_top = std::max(radii[0], radii[1]);
                const float r_bottom = std::max(radii[2], radii[3]);
                left_need = std::max(left_need, x0 + r_left + halo + 2.f);
                right_need = std::max(right_need, float(orig_size.x) - x1 + r_right + halo + 2.f);
                top_need = std::max(top_need, y0 + r_top + halo + 2.f);
                bottom_need = std::max(bottom_need, float(orig_size.y) - y1 + r_bottom + halo + 2.f);
            };

            for (const auto& shadow : info.shadow_list)
            {
                const float sigma = 0.5f * shadow.blur_radius.number;
                if (!std::isfinite(sigma) || sigma < 0.f || sigma > 8192.f)
                {
                    return plan;
                }
                const float halo = sigma >= 0.25f ? std::ceil(3.f * sigma) : 0.f;
                const float spread = shadow.spread_distance.number;
                const float signed_spread = shadow.inset ? -spread : spread;
                const RenderBox& base = shadow.inset ? padding_box : border_box;
                include_box(base, info.border_radius, Vector2f(0.f), 1.f);

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
                RenderBox sbox = base;
                sbox.SetFillSize(Math::Max(sbox.GetFillSize() + Vector2f(2.f * signed_spread), Vector2f(0.f)));
                sbox.SetBorderRadius(radii);
                sbox.SetBorderOffset(sbox.GetBorderOffset() - Vector2f(signed_spread));
                include_box(sbox, radii, Vector2f(shadow.offset_x.number, shadow.offset_y.number), halo);
            }

            plan.left_cut = int(std::ceil(std::max(0.f, left_need)));
            plan.right_cut = int(std::ceil(std::max(0.f, right_need)));
            plan.top_cut = int(std::ceil(std::max(0.f, top_need)));
            plan.bottom_cut = int(std::ceil(std::max(0.f, bottom_need)));

            constexpr int kMinCenter = 2;
            if (plan.left_cut + plan.right_cut + kMinCenter < orig_size.x &&
                border_box.GetFillSize().x > float(orig_size.x - (plan.left_cut + plan.right_cut + kMinCenter)) + 2.f &&
                padding_box.GetFillSize().x > float(orig_size.x - (plan.left_cut + plan.right_cut + kMinCenter)) + 2.f)
            {
                plan.shrink_x = orig_size.x - (plan.left_cut + plan.right_cut + kMinCenter);
                plan.small_size.x = orig_size.x - plan.shrink_x;
                plan.collapsed_x = true;
            }
            if (plan.top_cut + plan.bottom_cut + kMinCenter < orig_size.y &&
                border_box.GetFillSize().y > float(orig_size.y - (plan.top_cut + plan.bottom_cut + kMinCenter)) + 2.f &&
                padding_box.GetFillSize().y > float(orig_size.y - (plan.top_cut + plan.bottom_cut + kMinCenter)) + 2.f)
            {
                plan.shrink_y = orig_size.y - (plan.top_cut + plan.bottom_cut + kMinCenter);
                plan.small_size.y = orig_size.y - plan.shrink_y;
                plan.collapsed_y = true;
            }
            return plan;
        }

        bool RasterizeShadows(const CallbackTextureInterface& texture, const BoxShadowGeometryInfo& info,
                              const NineSlicePlan& plan)
        {
            const auto size = plan.small_size;
            constexpr size_t max_pixels = 16u * 1024u * 1024u;
            if (size.x <= 0 || size.y <= 0 || size.x > 8192 || size.y > 8192 || size_t(size.x) * size.y > max_pixels)
            {
                return false;
            }
            Vector<byte> pixels(size_t(size.x) * size.y * 4, 0);
            const Vector2f shrink(float(plan.shrink_x), float(plan.shrink_y));
            const bool single_box = info.border_render_boxes.size() == 1 && info.padding_render_boxes.size() == 1;
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
                const auto& boxes = shadow.inset ? info.padding_render_boxes : info.border_render_boxes;

                CornerSizes shadow_radii = info.border_radius;
                for (auto& radius : shadow_radii)
                {
                    float factor = shadow.inset ? -1.f : 1.f;
                    if (radius < spread)
                    {
                        const float ratio = radius / spread - 1.f;
                        factor *= 1.f + ratio * ratio * ratio;
                    }
                    radius = std::max(radius + factor * spread, 0.f);
                }

                Vector<float> clip;
                Vector<float> mask;
                bool used_sdf = false;
                if (single_box)
                {
                    RenderBox clip_box = boxes[0];
                    clip_box.SetFillSize(Math::Max(clip_box.GetFillSize() - shrink, Vector2f(0.f)));
                    RenderBox shadow_box = clip_box;
                    shadow_box.SetFillSize(Math::Max(shadow_box.GetFillSize() + Vector2f(2.f * signed_spread), Vector2f(0.f)));
                    shadow_box.SetBorderOffset(shadow_box.GetBorderOffset() - Vector2f(signed_spread));
                    RoundedRect clip_rect{}, shadow_rect{};
                    if (RoundedRect::FromBox(clip_box, info.element_offset_in_texture, info.border_radius, clip_rect) &&
                        RoundedRect::FromBox(shadow_box,
                                             info.element_offset_in_texture + displacement + Vector2f(float(halo)),
                                             shadow_radii, shadow_rect))
                    {
                        clip = RasterizeRoundedRect(clip_rect, size);
                        mask = RasterizeRoundedRect(shadow_rect, work_size);
                        used_sdf = true;
                    }
                }
                if (!used_sdf)
                {
                    Mesh clip_mesh;
                    Mesh shadow_mesh;
                    for (auto box : boxes)
                    {
                        if (single_box)
                        {
                            box.SetFillSize(Math::Max(box.GetFillSize() - shrink, Vector2f(0.f)));
                        }
                        MeshUtilities::GenerateBackground(clip_mesh, box, ColourbPremultiplied(255));
                        box.SetFillSize(Math::Max(box.GetFillSize() + Vector2f(2 * signed_spread), Vector2f(0)));
                        box.SetBorderRadius(shadow_radii);
                        box.SetBorderOffset(box.GetBorderOffset() - Vector2f(signed_spread));
                        if (box.GetFillSize().x > 0 && box.GetFillSize().y > 0)
                        {
                            MeshUtilities::GenerateBackground(shadow_mesh, box, ColourbPremultiplied(255));
                        }
                    }
                    clip = Rasterize(clip_mesh, size, info.element_offset_in_texture);
                    mask = Rasterize(shadow_mesh, work_size,
                                     info.element_offset_in_texture + displacement + Vector2f(float(halo)));
                }
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
        NineSlicePlan full_plan{};
        full_plan.small_size = info.texture_dimensions;
        out_shadow_texture = render_manager.MakeCallbackTexture([&info, full_plan](const CallbackTextureInterface& texture)
                                                                { return RasterizeShadows(texture, info, full_plan); });
    }

    void GeometryBoxShadow::GenerateShadow(CallbackTexture& out_shadow_texture, Geometry& out_shadow_geometry,
                                           Geometry& out_background_border_geometry, RenderManager& render_manager,
                                           const BoxShadowGeometryInfo& info)
    {
        out_background_border_geometry.Release();
        const NineSlicePlan plan = PlanNineSlice(info);
        out_shadow_texture = render_manager.MakeCallbackTexture([&info, plan](const CallbackTextureInterface& texture)
                                                                { return RasterizeShadows(texture, info, plan); });

        Mesh mesh;
        const byte alpha = byte(info.opacity * 255.f);
        const ColourbPremultiplied color(alpha, alpha);
        const Vector2f origin = -info.element_offset_in_texture;
        const Vector2f full(info.texture_dimensions);
        if (!plan.collapsed_x && !plan.collapsed_y)
        {
            MeshUtilities::GenerateQuad(mesh, origin, full, color);
        }
        else
        {
            const float xs[4] = {
                origin.x,
                origin.x + (plan.collapsed_x ? float(plan.left_cut) : 0.f),
                origin.x + full.x - (plan.collapsed_x ? float(plan.right_cut) : 0.f),
                origin.x + full.x,
            };
            const float ys[4] = {
                origin.y,
                origin.y + (plan.collapsed_y ? float(plan.top_cut) : 0.f),
                origin.y + full.y - (plan.collapsed_y ? float(plan.bottom_cut) : 0.f),
                origin.y + full.y,
            };
            const float inv_w = 1.f / float(plan.small_size.x);
            const float inv_h = 1.f / float(plan.small_size.y);
            const float us[4] = {
                0.f,
                plan.collapsed_x ? float(plan.left_cut) * inv_w : 0.f,
                plan.collapsed_x ? float(plan.small_size.x - plan.right_cut) * inv_w : 1.f,
                1.f,
            };
            const float vs[4] = {
                0.f,
                plan.collapsed_y ? float(plan.top_cut) * inv_h : 0.f,
                plan.collapsed_y ? float(plan.small_size.y - plan.bottom_cut) * inv_h : 1.f,
                1.f,
            };
            const int x_start = plan.collapsed_x ? 0 : 1;
            const int x_end = plan.collapsed_x ? 3 : 2;
            const int y_start = plan.collapsed_y ? 0 : 1;
            const int y_end = plan.collapsed_y ? 3 : 2;
            for (int yi = y_start; yi < y_end; ++yi)
            {
                for (int xi = x_start; xi < x_end; ++xi)
                {
                    const Vector2f cell_size(xs[xi + 1] - xs[xi], ys[yi + 1] - ys[yi]);
                    if (cell_size.x > 0.f && cell_size.y > 0.f)
                    {
                        MeshUtilities::GenerateQuad(mesh, {xs[xi], ys[yi]}, cell_size, color, {us[xi], vs[yi]},
                                                    {us[xi + 1], vs[yi + 1]});
                    }
                }
            }
        }
        out_shadow_geometry = render_manager.MakeGeometry(std::move(mesh));
    }
}
