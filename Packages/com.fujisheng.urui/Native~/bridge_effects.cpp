#include "bridge_runtime.h"
#include "Engine/Source/Core/ElementStyle.h"
#include "Engine/Source/Core/TransformState.h"
#include <URUI/Core/ElementUtilities.h>
#include <algorithm>
#include <cmath>
#include <cstring>

namespace URUI::Bridge
{
    namespace
    {
        void CopyEffectLayout(URUI::Element* element, float* output)
        {
            const auto& box = element->GetBox();
            const auto position = box.GetPosition(URUI::BoxArea::Content) - box.GetPosition(URUI::BoxArea::Border);
            const auto size = box.GetSize(URUI::BoxArea::Content);
            output[0] = position.x;
            output[1] = position.y;
            output[2] = size.x;
            output[3] = size.y;
            const auto offset = element->GetAbsoluteOffset(URUI::BoxArea::Border);
            auto matrix = URUI::Matrix4f::Translate(offset.x, offset.y, 0);
            if (const auto* state = element->GetTransformState())
            {
                if (const auto* transform = state->GetTransform())
                {
                    matrix = *transform * matrix;
                }
            }
            const auto* m = matrix.data();
            // z=0 平面到文档的完整单应矩阵，保留透视 w，不能用三个投影点近似为仿射变换。
            const float homography[16] = {m[0], m[1], 0, m[3], m[4],  m[5],  0, m[7],
                                          0,    0,    1, 0,    m[12], m[13], 0, m[15]};
            std::copy(homography, homography + 16, output + 4);
        }

        int SequenceSampleImpl(SurfaceContext& surface, URUI::ElementDocument* document, uint64_t token, uint64_t owner,
                               const URUIAnimationSample* samples, int count, int mode)
        {
            if (!owner || count < 0 || count > 256 || (count && !samples) || mode < 0 || mode > 3 ||
                (mode == 3 && count))
            {
                return URUI_ERR_INVALID_ARG;
            }
            auto* element = surface.elementReferences.Resolve(token, document);
            if (!element)
            {
                return URUI_ERR_NOT_READY;
            }
            auto* style = element->GetStyle();
            if (!count)
            {
                if (mode == 3)
                {
                    return style->SetSequenceValues(owner, URUI::PropertyDictionary{}) ? URUI_OK
                                                                                       : URUI_ERR_STALE_REVISION;
                }
                if (mode != 0)
                {
                    style->RemoveSequence(owner);
                }
                return URUI_OK;
            }
            URUI::PropertyIdSet properties;
            for (int index = 0; index < count; ++index)
            {
                const auto& sample = samples[index];
                if (!sample.name || !sample.easing || !std::isfinite(sample.progress) || sample.progress < 0.f ||
                    sample.progress > 1.f || !element->GetKeyframeProperties(sample.name, properties))
                {
                    return URUI_ERR_VALIDATION;
                }
            }
            URUI::PropertyDictionary captured;
            const URUI::PropertyDictionary* baseline = nullptr;
            if (mode == 2)
            {
                if (!style->PrepareSequenceBaseline(owner, properties, captured))
                {
                    return URUI_ERR_STALE_REVISION;
                }
                baseline = &captured;
            }
            else
            {
                baseline = style->GetSequenceBaseline(owner);
            }
            if (!baseline)
            {
                return URUI_ERR_STALE_REVISION;
            }
            URUI::PropertyDictionary values;
            for (int index = 0; index < count; ++index)
            {
                const auto& sample = samples[index];
                if (!element->SampleKeyframes(sample.name, sample.progress, sample.easing, *baseline, values))
                {
                    return URUI_ERR_VALIDATION;
                }
            }
            if (mode == 0)
            {
                return URUI_OK;
            }
            return (mode == 2 ? style->CaptureSequence(owner, captured) : style->SetSequenceValues(owner, values))
                       ? URUI_OK
                       : URUI_ERR_STALE_REVISION;
        }
    }

    int EffectLayoutImpl(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t token, float* output, int capacity,
                         uint64_t* root)
    {
        if (!output || capacity < 40 || !root)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* document = ResolveDocument(surface, owner);
        auto* element = ResolveElement(surface, owner, token);
        if (!document || !element || element->GetOwnerDocument() != document ||
            !SurfaceRenderer::EffectAttached(element))
        {
            return URUI_ERR_NOT_READY;
        }
        static const URUI::String rootAttribute = "data-urui-effect-root";
        const auto* attribute = element->GetAttribute(rootAttribute);
        static const URUI::String empty;
        const auto& name = attribute && attribute->GetType() == URUI::Variant::STRING
                               ? attribute->GetReference<URUI::String>()
                               : empty;
        auto& reference = surface.renderer.effectRoots[token];
        const auto structure = document->GetEffectStructureRevision();
        if (reference.element.get() != element || reference.name != name || reference.structure != structure ||
            !reference.root)
        {
            auto* target = name.empty() ? element : URUI::ElementUtilities::GetElementById(document, name);
            if (!target || target->GetOwnerDocument() != document || !SurfaceRenderer::EffectAttached(target))
            {
                target = element;
            }
            reference = {element->GetObserverPtr(), target->GetObserverPtr(), name, structure};
        }
        auto* target = reference.root.get();
        *root = surface.elementReferences.Track(target, document);
        if (!*root)
        {
            return URUI_ERR_BUDGET;
        }
        CopyEffectLayout(element, output);
        CopyEffectLayout(target, output + 20);
        return URUI_OK;
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_effect_api_version(void)
    {
        return 8;
    }

    int urui_filter_api_version(void)
    {
        return 4;
    }

    int urui_sequence_api_version(void)
    {
        return 2;
    }

    int urui_element_effect_state(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                  int capture_required, int input_disabled)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (capture_required < 0 || capture_required > 1 || input_disabled < 0 || input_disabled > 1)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = ResolveElement(*surface, owner, token);
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        element->SetEffectState(capture_required != 0, input_disabled != 0);
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_document_effect_image_condition(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                             const char* effect, const char* property, const char* direct_value)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (state->Element() || !effect || !*effect || !property || property[0] != '-' || property[1] != '-' ||
            !direct_value || !*direct_value)
        {
            return URUI_ERR_INVALID_ARG;
        }
        EnsurePendingResources(*surface, *state)
            ->RegisterEffectImageCondition(effect, URUI::EffectImageCondition{property, direct_value});
        return URUI_OK;
    }
    catch (const std::invalid_argument&)
    {
        return URUI_ERR_INVALID_ARG;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_effect_layout(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, float* output,
                           int capacity, uint64_t* root)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        return EffectLayoutImpl(*surface, owner, token, output, capacity, root);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    URUIDocumentHandle urui_filter_document(URUISurfaceHandle surfaceHandle, uint64_t handle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        const auto found = surface->renderer.filters.find(handle);
        return found == surface->renderer.filters.end() ? 0 : found->second.document;
    }

    int urui_filter_info(URUISurfaceHandle surfaceHandle, uint64_t handle, URUIFilter* result, char* name, int capacity,
                         int* required)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!result || !required || capacity < 0 || (!name && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        const auto found = surface->renderer.filters.find(handle);
        if (found == surface->renderer.filters.end())
        {
            return URUI_ERR_NOT_READY;
        }
        const int needed = static_cast<int>(found->second.name.size()) + 1;
        *required = needed;
        if (capacity < needed)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        *result = found->second.data;
        if (found->second.element)
        {
            auto* element = found->second.element.get();
            result->element = surface->elementReferences.Track(element, element->GetOwnerDocument());
            if (!result->element)
            {
                return URUI_ERR_BUDGET;
            }
        }
        std::memcpy(name, found->second.name.c_str(), static_cast<size_t>(needed));
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_sequence_layout(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        // Deliberately omit Element::Update: sampling must not advance natural animations or dispatch input.
        return document->SynchronizeSequenceLayout() ? URUI_OK : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_keyframes_count(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                             const char* name)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!name)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = ResolveElement(*surface, owner, token);
        return element ? element->GetKeyframeCount(name) : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_keyframes_properties(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                  const char* name, char* output, int capacity, int* required)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!name || !required || capacity < 0 || (!output && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        auto* element = ResolveElement(*surface, owner, token);
        URUI::PropertyIdSet properties;
        if (!element || !element->GetKeyframeProperties(name, properties))
        {
            return URUI_ERR_NOT_READY;
        }
        URUI::String names;
        for (auto id : properties)
        {
            names += element->GetPropertySpecification().GetPropertyName(id);
            names += '\n';
        }
        return EngineRuntime::CopyString(names, output, capacity, required);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_sequence_sample(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                             uint64_t sequence_owner, const URUIAnimationSample* samples, int count, int mode)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        return SequenceSampleImpl(*surface, document, token, sequence_owner, samples, count, mode);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_sequence_apply_batch(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                  const URUISequenceBatchItem* items, int item_count,
                                  const URUIAnimationSample* samples, int sample_count)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (item_count < 0 || item_count > 4096 || (item_count > 0 && !items) || sample_count < 0 ||
            sample_count > 65536 || (sample_count > 0 && !samples))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        for (int index = 0; index < item_count; ++index)
        {
            const auto& item = items[index];
            if (item.reserved != 0 || item.sample_count > 256 ||
                uint64_t(item.sample_offset) + uint64_t(item.sample_count) > uint64_t(sample_count))
            {
                return URUI_ERR_INVALID_ARG;
            }
            const URUIAnimationSample* slice = item.sample_count ? (samples + item.sample_offset) : nullptr;
            const int itemStatus = SequenceSampleImpl(*surface, document, item.token, item.owner, slice,
                                                      static_cast<int>(item.sample_count), item.mode);
            if (itemStatus != URUI_OK)
            {
                return itemStatus;
            }
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }
}
