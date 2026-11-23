#pragma once
#include <stdint.h>
#include "urui.h"

#ifdef __cplusplus
extern "C"
{
#endif
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
    URUI_API int urui_program_layout_register(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t exemplar,
                                              const char* name, const URUIProgramField* fields, uint32_t field_count,
                                              uint32_t float4_count, uint64_t* output);
    URUI_API int urui_program_layout_values(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element,
                                            uint64_t program, float* values, uint32_t float4_count);
    URUI_API int urui_program_layout_release(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t program);
#ifdef __cplusplus
}
#endif
