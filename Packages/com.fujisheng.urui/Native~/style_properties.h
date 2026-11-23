#pragma once
#include <stdint.h>
#include "urui.h"

#ifdef __cplusplus
extern "C"
{
#endif
    typedef struct URUIStyleValue
    {
        int32_t name_offset;
        int32_t kind;
        float x;
        float y;
        float z;
        float w;
        int32_t text_offset;
        int32_t source_offset;
    } URUIStyleValue;

    typedef struct URUIFrozenStyleHandle
    {
        uint64_t slot;
        uint64_t version;
    } URUIFrozenStyleHandle;

    URUI_API int urui_style_api_version(void);
    URUI_API int urui_style_frozen_api_version(void);
    // kind bits: 1 number, 2 resolved px, 4 RGBA, 8 keyword, 16 image.
    // kind>>8 origin: 0 initial, 1 UA, 2 author, 3 animation, 4 important, 5 transition.
    // Returns URUI_OK, URUI_ERR_BUFFER_TOO_SMALL (-5), or URUI_ERR_STALE_REVISION (-9).
    URUI_API int urui_element_style_snapshot(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                             uint64_t expected_revision, URUIStyleValue* values, int capacity,
                                             char* names, int names_capacity, uint64_t* revision, int* value_count,
                                             int* name_bytes);
    URUI_API int urui_style_snapshot_freeze(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                            const URUIFrozenStyleHandle* previous, URUIFrozenStyleHandle* output);
    URUI_API int urui_style_snapshot_release(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                             const URUIFrozenStyleHandle* snapshot);
    URUI_API int urui_style_snapshot_copy(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                          const URUIFrozenStyleHandle* snapshot, uint64_t expected_revision,
                                          URUIStyleValue* values, int capacity, char* names, int names_capacity,
                                          uint64_t* revision, int* value_count, int* name_bytes);
    // snapshot build / string read / typed copy / retained nodes.
    URUI_API int urui_style_diagnostics(URUISurfaceHandle surface, uint64_t* values, int capacity);
#ifdef __cplusplus
}
#endif
