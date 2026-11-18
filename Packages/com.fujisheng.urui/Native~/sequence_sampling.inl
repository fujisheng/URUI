// All calls are synchronous on the context owner thread; no pointer or event escapes.
#include "Engine/Source/Core/ElementStyle.h"
extern "C"
{
    int urui_sequence_api_version(void) { return 1; }
    int urui_keyframes_count(UruiDocumentHandle document_handle, uint64_t token, const char* name)
    try
    {
        auto* element = urui_interaction::Owner() && name ? urui_elements::Get(document_handle, token) : nullptr;
        return element ? element->GetKeyframeCount(name) : -1;
    }
    catch (...) { return -8; }
    int urui_sequence_layout(UruiDocumentHandle document_handle)
    try
    {
        auto* document = urui_interaction::Owner() ? urui_elements::Document(document_handle) : nullptr;
        // Deliberately omit Element::Update: sampling must not advance natural animations or dispatch input.
        return document && document->SynchronizeSequenceLayout() ? 1 : -1;
    }
    catch (...) { return -8; }
    int urui_keyframes_properties(UruiDocumentHandle document_handle, uint64_t token, const char* name,
        char* output, int capacity, int* required)
    try
    {
        if (!urui_interaction::Owner() || !name) return -1;
        auto* element = urui_elements::Get(document_handle, token);
        URUI::PropertyIdSet properties;
        if (!element || !element->GetKeyframeProperties(name, properties)) return -2;
        URUI::String names;
        for (auto id : properties) { names += element->GetPropertySpecification().GetPropertyName(id); names += '\n'; }
        return urui_interaction::Copy(names, output, capacity, required);
    }
    catch (...) { return -8; }

    int urui_sequence_sample(UruiDocumentHandle document_handle, uint64_t token, uint64_t owner,
        const UruiAnimationSample* samples, int count, int mode)
    try
    {
        if (!urui_interaction::Owner() || !owner || count < 0 || count > 256 || (count && !samples) || mode < 0 || mode > 3 || (mode == 3 && count)) return -1;
        auto* element = urui_elements::Get(document_handle, token);
        if (!element) return -2;
        auto* style = element->GetStyle();
        if (!count)
        {
            if (mode == 3) return style->SetSequenceValues(owner, URUI::PropertyDictionary{}) ? 1 : -4;
            if (mode != 0) style->RemoveSequence(owner);
            return 1;
        }
        URUI::PropertyIdSet properties;
        for (int index = 0; index < count; ++index)
        {
            const auto& sample = samples[index];
            if (!sample.name || !sample.easing || !std::isfinite(sample.progress) || sample.progress < 0 || sample.progress > 1 ||
                !element->GetKeyframeProperties(sample.name, properties)) return -3;
        }
        URUI::PropertyDictionary captured;
        const URUI::PropertyDictionary* baseline = nullptr;
        if (mode == 2)
        {
            if (!style->PrepareSequenceBaseline(owner, properties, captured)) return -4;
            baseline = &captured;
        }
        else baseline = style->GetSequenceBaseline(owner);
        if (!baseline) return -4;
        URUI::PropertyDictionary values;
        for (int index = 0; index < count; ++index)
        {
            const auto& sample = samples[index];
            if (!element->SampleKeyframes(sample.name, sample.progress, sample.easing, *baseline, values)) return -3;
        }
        if (mode == 0) return 1;
        return (mode == 2 ? style->CaptureSequence(owner, captured) : style->SetSequenceValues(owner, values)) ? 1 : -4;
    }
    catch (...) { return -8; }
}
