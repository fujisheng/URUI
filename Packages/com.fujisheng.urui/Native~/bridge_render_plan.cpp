#include "bridge_runtime.h"
#include <algorithm>
#include <cmath>
#include <cstring>

namespace URUI::Bridge
{
    namespace
    {
        int PlanAccess(URUISurfaceHandle surfaceHandle, SurfaceContext*& surface) noexcept
        {
            const int status = LookupSurface(surfaceHandle, surface, true, true);
            if (status != URUI_OK)
            {
                return status;
            }
            if (!surface->context || !surface->documentRegistry)
            {
                return URUI_ERR_NOT_READY;
            }
            surface->renderer.drawPlan.Invalidate(surface->renderer.geometryEpoch);
            return URUI_OK;
        }

        URUI::RenderPlan::GeometrySource ResolvePlanGeometry(const SurfaceRenderer& renderer, uint64_t id) noexcept
        {
            const auto found = renderer.meshes.find(id);
            if (found == renderer.meshes.end())
            {
                return {};
            }
            const auto& mesh = found->second;
            return {mesh.vertices.data(), mesh.vertices.size(), mesh.indices.data(), mesh.indices.size(),
                    mesh.boundsValid ? mesh.bounds.data() : nullptr};
        }
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_plan_begin(URUISurfaceHandle surfaceHandle, URUIPlanInputPacket* out)
    try
    {
        if (!out)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *out = {};
        SurfaceContext* surface = nullptr;
        int status = PlanAccess(surfaceHandle, surface);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        status = RenderSurfaceImpl(*surface);
        if (status != URUI_OK)
        {
            return status;
        }
        auto& render = surface->renderer;
        if (!render.layerValid)
        {
            return URUI_ERR_INTERNAL;
        }
        auto& compiler = render.drawPlan;
        const auto size = surface->context->GetDimensions();
        compiler.Viewport(static_cast<uint32_t>(size.x), static_cast<uint32_t>(size.y));

        // Build paint commands directly into compiler scratch without an extra API round-trip.
        const size_t paintTotal = render.paintCommands.size();
        auto* paints = compiler.PaintScratch(paintTotal);
        uint32_t count = 0;
        for (const auto& entry : render.paintCommands)
        {
            const auto shader = render.paintShaders.find(entry.shader);
            if (shader == render.paintShaders.end() || !shader->second.element)
            {
                continue;
            }
            const auto token = surface->elementReferences.Track(shader->second.element.get(),
                                                                shader->second.element->GetOwnerDocument());
            if (!token)
            {
                return URUI_ERR_BUDGET;
            }
            paints[count++] = {entry.shader, token, entry.command_index, shader->second.kind};
        }
        if (count != paintTotal)
        {
            return URUI_ERR_INTERNAL;
        }

        const auto& commands = render.commands;
        if (render.commandUniforms.size() != commands.size())
        {
            return URUI_ERR_INTERNAL;
        }
        status = compiler.Begin(render.geometryEpoch, commands.data(), render.commandTransforms.data(),
                                static_cast<uint32_t>(commands.size()), paints, count, render.layerCommands.data(),
                                static_cast<uint32_t>(render.layerCommands.size()), *out, render.commandUniforms.data());
        if (status != URUI_OK)
        {
            return status;
        }
        auto fail = [&](int error)
        {
            compiler.Release(out->lease);
            *out = {};
            return error;
        };
        uint32_t colors = 0;
        for (uint32_t n = 0; n < count; ++n)
        {
            const uint32_t kind = paints[n].reserved;
            if (kind == 1)
            {
                const auto owner = surface->documentRegistry->OwnerOf(paints[n].element);
                if (!owner)
                {
                    return fail(URUI_ERR_NOT_READY);
                }
                status = compiler.SetImageDocument(out->lease, static_cast<uint32_t>(paints[n].command_index), owner);
                if (status != URUI_OK)
                {
                    return fail(status);
                }
            }
            else if (kind == 2)
            {
                const auto found = render.paintShaders.find(paints[n].shader);
                if (found == render.paintShaders.end() || found->second.colorFilters.empty() ||
                    found->second.colorFilters.size() > 16)
                {
                    return fail(URUI_ERR_NOT_READY);
                }
                colors += static_cast<uint32_t>(found->second.colorFilters.size());
            }
        }
        if (colors > 0)
        {
            auto* colorFields = compiler.ColorScratch(colors);
            uint32_t written = 0;
            for (uint32_t n = 0; n < count; ++n)
            {
                if (paints[n].reserved == 2)
                {
                    const auto& filters = render.paintShaders.find(paints[n].shader)->second.colorFilters;
                    for (const auto& filter : filters)
                    {
                        colorFields[written++] = {static_cast<uint32_t>(paints[n].command_index),
                                                  static_cast<uint32_t>(filter.kind), filter.value};
                    }
                }
            }
            status = compiler.SetColorFilters(out->lease, colorFields, written);
            if (status != URUI_OK)
            {
                return fail(status);
            }
        }
        uint32_t effects = 0;
        for (const auto& layer : render.layerCommands)
        {
            if (layer.kind == URUI_LAYER_APPLY_MASK && !layer.blend)
            {
                ++effects;
            }
        }
        if (effects > 0)
        {
            auto* metadata = compiler.EffectScratch(effects);
            uint32_t written = 0;
            for (uint32_t n = 0; n < render.layerCommands.size(); ++n)
            {
                const auto& layer = render.layerCommands[n];
                if (layer.kind != URUI_LAYER_APPLY_MASK || layer.blend)
                {
                    continue;
                }
                const auto found = render.filters.find(layer.mask);
                if (found == render.filters.end())
                {
                    return fail(URUI_ERR_NOT_READY);
                }
                const auto& filter = found->second;
                uint64_t element = 0;
                if (filter.element)
                {
                    element =
                        surface->elementReferences.Track(filter.element.get(), filter.element->GetOwnerDocument());
                    if (!element)
                    {
                        return fail(URUI_ERR_BUDGET);
                    }
                }
                const uint32_t kind = filter.element                 ? 3u
                                      : filter.name == "blur"        ? 1u
                                      : filter.name == "drop-shadow" ? 2u
                                                                     : 0u;
                auto& data = metadata[written++];
                data = {n,
                        filter.document,
                        element,
                        layer.mask,
                        kind,
                        filter.data.sigma,
                        filter.data.offset_x,
                        filter.data.offset_y,
                        filter.data.value};
                data.cacheable = filter.name == "grayscale" || filter.name == "saturate" ||
                                 filter.name == "brightness" || filter.name == "contrast" ||
                                 filter.name == "opacity";
                if (element)
                {
                    const auto owner = surface->documentRegistry->OwnerOf(element);
                    if (!owner || (filter.document && filter.document != owner))
                    {
                        return fail(URUI_ERR_NOT_READY);
                    }
                    data.document = owner;
                    float layout[40]{};
                    uint64_t root = 0;
                    status = EffectLayoutImpl(*surface, owner, element, layout, 40, &root);
                    if (status != URUI_OK)
                    {
                        return fail(status);
                    }
                    std::copy_n(layout + 4, 16, data.projection.data());
                }
            }
            status = compiler.SetLayerEffects(out->lease, metadata, written);
            if (status != URUI_OK)
            {
                return fail(status);
            }
        }
        return URUI_OK;
    }
    catch (const std::bad_alloc&)
    {
        if (out && out->lease)
        {
            if (auto* s = FindSurfaceInternal(surfaceHandle))
            {
                s->renderer.drawPlan.Release(out->lease);
            }
        }
        if (out)
        {
            *out = {};
        }
        return URUI_ERR_BUDGET;
    }
    catch (...)
    {
        if (out && out->lease)
        {
            if (auto* s = FindSurfaceInternal(surfaceHandle))
            {
                s->renderer.drawPlan.Release(out->lease);
            }
        }
        if (out)
        {
            *out = {};
        }
        return URUI_ERR_INTERNAL;
    }

    int urui_plan_resolve(URUISurfaceHandle surfaceHandle, uint64_t input_lease, const URUIPlanFeedback* feedbacks,
                          uint32_t feedback_count, const URUIPlanBindings* resources, URUIPlanPreparePacket* out)
    try
    {
        if (!out)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *out = {};
        SurfaceContext* surface = nullptr;
        const int status = PlanAccess(surfaceHandle, surface);
        if (status != URUI_OK)
        {
            return status;
        }
        const auto& renderer = surface->renderer;
        return surface->renderer.drawPlan.Resolve(
            input_lease, feedbacks, feedback_count,
            [&renderer](uint64_t id) { return ResolvePlanGeometry(renderer, id); }, *out, resources);
    }
    catch (...)
    {
        if (out)
        {
            *out = {};
        }
        return URUI_ERR_INTERNAL;
    }

    int urui_plan_compile(URUISurfaceHandle surfaceHandle, uint64_t input_lease, const URUIPlanBindings* bindings,
                          URUIRenderPlanPacket* out)
    try
    {
        if (!out || !bindings)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *out = {};
        SurfaceContext* surface = nullptr;
        const int status = PlanAccess(surfaceHandle, surface);
        if (status != URUI_OK)
        {
            return status;
        }
        const auto& renderer = surface->renderer;
        return surface->renderer.drawPlan.Compile(
            input_lease, *bindings, [&renderer](uint64_t id) { return ResolvePlanGeometry(renderer, id); }, *out);
    }
    catch (...)
    {
        if (out)
        {
            *out = {};
        }
        return URUI_ERR_INTERNAL;
    }

    int urui_plan_geometry(URUISurfaceHandle surfaceHandle, uint64_t plan_lease, uint64_t geometry,
                           URUIGeometryView* out)
    try
    {
        if (!out)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *out = {};
        SurfaceContext* surface = nullptr;
        const int status = PlanAccess(surfaceHandle, surface);
        if (status != URUI_OK)
        {
            return status;
        }
        return surface->renderer.drawPlan.Geometry(plan_lease, geometry, *out);
    }
    catch (...)
    {
        if (out)
        {
            *out = {};
        }
        return URUI_ERR_INTERNAL;
    }

    int urui_plan_image_program(URUISurfaceHandle surfaceHandle, uint64_t input_lease, uint64_t request,
                                uint64_t program, uint64_t owner, uint64_t layout, const float* runtime_rows,
                                uint32_t runtime_count, uint32_t field_count, const float* bound_rows, uint64_t clock)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = PlanAccess(surfaceHandle, surface);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!owner || !layout || !runtime_rows || runtime_count < 2 || runtime_count > 32 || !field_count ||
            field_count > 512)
        {
            return URUI_ERR_INVALID_ARG;
        }
        return surface->renderer.drawPlan.SetImageProgram(
            input_lease, request, program, runtime_count + field_count,
            [&](const URUIPlanPrepareRequest& pending, float* output, uint32_t) -> int
            {
                if (pending.document != owner || surface->documentRegistry->OwnerOf(pending.element) != owner)
                {
                    return URUI_ERR_NOT_READY;
                }
                std::copy_n(runtime_rows, static_cast<size_t>(runtime_count) * 4, output);
                if (bound_rows)
                {
                    // Skip redundant CSS evaluation when managed already supplied bound rows,
                    // while still validating owner, element, and registered layout binding.
                    const int valid =
                        ValidateProgramLayoutBinding(*surface, owner, pending.element, layout, field_count);
                    if (valid != URUI_OK)
                    {
                        return valid;
                    }
                    std::copy_n(bound_rows, static_cast<size_t>(field_count) * 4,
                                output + static_cast<size_t>(runtime_count) * 4);
                    return URUI_OK;
                }
                return FillProgramLayoutValues(*surface, owner, pending.element, layout,
                                               output + static_cast<size_t>(runtime_count) * 4, field_count);
            },
            clock);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_plan_image_geometry(URUISurfaceHandle surfaceHandle, uint64_t input_lease, uint64_t request,
                                 const URUIPlanExternalGeometry* geometry, const float* local_transform)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = PlanAccess(surfaceHandle, surface);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!geometry)
        {
            return URUI_ERR_INVALID_ARG;
        }
        return surface->renderer.drawPlan.SetImageGeometry(input_lease, request, *geometry, local_transform);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_plan_release(URUISurfaceHandle surfaceHandle, uint64_t lease)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = PlanAccess(surfaceHandle, surface);
        if (status != URUI_OK)
        {
            return status;
        }
        return surface->renderer.drawPlan.Release(lease);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }
}
