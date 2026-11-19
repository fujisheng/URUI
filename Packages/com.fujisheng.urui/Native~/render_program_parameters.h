#pragma once
#include "urui.h"
#ifdef __cplusplus
extern "C"
{
#endif
    // Fixed float-component destinations, independent of a shader or Effect package.
    // Registration uses the document root token, so pointer reuse after reload cannot revive old bindings.
    // Kinds: 1 number, 2 resolved px, 4 encoded straight RGBA (four components),
    // 8 mapped keyword or nonzero opaque keyword identity, 16 opaque CSS image
    // identity (zero means absent/none; relative URLs include declaration source),
    // 32 tagged pair: (number, 0) or (mapped keyword value, 1).
    // Flag256: absent custom property supplies zero; a present invalid value still fails.
    typedef struct
    {
        const char* text;
        float value;
        uint32_t reserved;
    } URUIProgramKeyword;
    typedef struct
    {
        const char* property;
        uint32_t offset;
        uint32_t kind;
        int32_t origin_offset;
        uint32_t keyword_count;
        const URUIProgramKeyword* keywords;
    } URUIProgramField;
    URUI_API int urui_program_parameter_api_version(void);
    URUI_API int urui_program_layout_register(URUIDocumentHandle owner, uint64_t exemplar, const char* name,
                                              const URUIProgramField* fields, uint32_t field_count,
                                              uint32_t float4_count, uint64_t* output);
    URUI_API int urui_program_layout_values(URUIDocumentHandle owner, uint64_t element, uint64_t program, float* values,
                                            uint32_t float4_count);
    URUI_API int urui_program_layout_release(URUIDocumentHandle owner, uint64_t program);
#ifdef __cplusplus
}
#endif
