#pragma once
#include <stdint.h>
#include "debug_config.h"
#if defined(_WIN32)
#if defined(URUI_EXPORTS)
#define URUI_API __declspec(dllexport)
#else
#define URUI_API __declspec(dllimport)
#endif
#else
#define URUI_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C"
{
#endif
    typedef struct
    {
        float x, y, u, v;
        uint8_t r, g, b, a;
    } UruiVertex;
    typedef struct
    {
        uint64_t geometry, texture;
        float tx, ty;
        int clip_enabled, clip_x, clip_y, clip_w, clip_h;
    } UruiCommand;
    // URUI bridge ABI version 2. UruiVertex is 20 bytes; UruiCommand is 48 bytes.
    URUI_API int urui_bridge_abi_version(void);
    URUI_API int urui_create(int width, int height, const char* font_path);
    URUI_API void urui_destroy(void);
    URUI_API int urui_load_document(const char* rml);
    URUI_API void urui_unload_document(void);
    // Image bytes are top-row-first straight-alpha RGBA8. The bridge
    // premultiplies once; Unity GetPixels32 callers must reverse its row order.
    // Returns 1 on registration/replacement, negative for invalid/busy.
    URUI_API int urui_register_rgba_texture(const char* source, int width, int height, const uint8_t* rgba,
                                            int byte_count, uint64_t* handle);
    URUI_API int urui_unregister_texture(const char* source);
    URUI_API void urui_resize(int width, int height);
    URUI_API void urui_mouse_move(int x, int y);
    URUI_API void urui_mouse_button(int button, int pressed);
    URUI_API void urui_mouse_wheel(float dy);
    // phase: 0=start, 1=move, 2=end, 3=cancel. Coordinates are logical
    // top-left pixels; pointer IDs are Unity touch finger IDs, not mouse buttons.
    URUI_API int urui_touch_event(int phase, uint64_t pointer_id, float x, float y);
    // Return UTF-8 byte count, 0 for no identified hit, negative required capacity.
    URUI_API int urui_hit_test_id(float x, float y, char* out, int capacity);
    // ID lookup follows RmlUi GetElementById (first match). Mutations return
    // 1 success, 0 missing, negative invalid input or rejected property.
    URUI_API int urui_element_exists(const char* id);
    // Absolute border-box x,y,w,h followed by clientWidth,scrollWidth.
    URUI_API int urui_get_element_box(const char* id, float* out_six);
    URUI_API int urui_set_attribute(const char* id, const char* name, const char* value);
    URUI_API int urui_set_style(const char* id, const char* name, const char* value);
    // Equivalent to translate(xpx,ypx) scale(sx,sy), preserving primitive order,
    // transform-origin and normal dirty semantics. Finite zero/negative scale
    // supported. Identity is NOT transform:none. Same return codes as SetStyle.
    URUI_API int urui_set_translate_scale_2d(const char* id, float x, float y, float sx, float sy);
    URUI_API int urui_set_inner_rml(const char* id, const char* markup);
    URUI_API int urui_append_element(const char* parent_id, const char* tag, const char* id);
    // Diagnostic actual DOM child oracle, all DOM children in sibling order.
    // count returns -1 for invalid/missing; id returns length, -required capacity,
    // or -1 invalid/missing/index. No payload written on failure.
    URUI_API int urui_get_child_count(const char* id);
    URUI_API int urui_get_child_id_utf8(const char* id, int index, char* out, int capacity);
    URUI_API int urui_reorder_element_to_end(const char* id);
    URUI_API int urui_remove_element(const char* id);
    // One column-major 4x4 transform per render command. Window-space scissor is
    // applied after the transform. UruiCommand is 48 bytes.
    URUI_API int urui_get_command_transforms(float* out, int float_capacity);
    URUI_API void urui_update(void);
    URUI_API void urui_render(void);
    // Read actual single text child, or empty element. required includes UTF8 NUL.
    // 1 success, 0 missing document/element, -1 invalid arguments, -2 structured
    // children, -3 insufficient capacity/probe. Payload unchanged on every failure.
    // Synchronous context-thread call, no borrowed pointer escapes.
    URUI_API int urui_get_attribute_utf8(const char* id, const char* name, char* out, int capacity, int* required);
    URUI_API int urui_get_text_utf8(const char* id, char* out, int capacity, int* required);
    URUI_API void urui_set_text(const char* id, const char* text);
    URUI_API float urui_get_scroll_top(const char* id);
    URUI_API int urui_set_pixel_style(const char* id, const char* property, float pixels);
    URUI_API int urui_element_count(void);
    URUI_API int urui_command_count(void);
    URUI_API int urui_get_commands(UruiCommand* out, int capacity);
    URUI_API int urui_get_geometry_info(uint64_t id, int* v, int* i);
    URUI_API int urui_copy_geometry(uint64_t id, UruiVertex* v, int vc, int* i, int ic);
    URUI_API int urui_get_texture_info(uint64_t id, int* w, int* h, int* bytes);
    URUI_API int urui_copy_texture(uint64_t id, uint8_t* out, int capacity);
    URUI_API int urui_geometry_exists(uint64_t id);
    URUI_API int urui_texture_exists(uint64_t id);
    URUI_API int urui_pop_event(char* out, int capacity);
    URUI_API const char* urui_last_error(void);
    // Optional exact retained-plan tracking. Native Update/Render are never skipped.
    // flags: 1 complete/supported stamp; 2 unsupported optional renderer operation;
    // otherwise 0 (disabled/incomplete/exhausted). Counts/bytes are diagnostics only.
    // Retained/compared byte reports are zero unless URUI_DEBUG is enabled.
    typedef struct UruiFrameStamp
    {
        uint64_t epoch, commands, resources, retained_bytes, compare_bytes;
        uint32_t count, flags;
    } UruiFrameStamp;
    URUI_API int urui_render_plan_version(void);
    URUI_API int urui_render_plan_enable(int enabled);
    URUI_API int urui_render_plan_stamp(UruiFrameStamp* out);
    // Optional synchronous, caller-owned prepared core data. Keys retain painter
    // order; mode 1 contains final world/affine values, mode 0 contains the original
    // matrix and tx/ty in value[0..1] for the client's existing full-math fallback.
    // No pointer escapes this call. All capacities/stamp/thread checks precede writes.
    typedef struct UruiCoreKey
    {
        uint64_t geometry, texture;
        int32_t clip_enabled, clip_x, clip_y, clip_w, clip_h;
        uint32_t mode;
    } UruiCoreKey;
    typedef struct UruiCoreValue
    {
        float data[8];
    } UruiCoreValue;
    typedef struct UruiCoreSummary
    {
        uint32_t ready, fallback;
        uint64_t bytes;
    } UruiCoreSummary;
    URUI_API int urui_core_frame_api_version(void);
    URUI_API int urui_core_frame_copy(const UruiFrameStamp* expected, UruiCoreKey* keys, UruiCoreValue* values,
                                      float* worlds, int capacity, UruiCoreSummary* info);
    // Explicit weak OBJECT references, not live ID selectors. Acquire uses fresh ID
    // semantics; rename does not retarget. Unload/destroy invalidates all tokens.
    // Single context thread only (-3 for foreign-thread operations); dispose tokens.
    // Budget: 65536 outstanding tokens. Invalid/stale writes return 0, bad numeric -1.
    URUI_API uint64_t urui_element_ref_acquire(const char* id);
    URUI_API int urui_element_ref_valid(uint64_t token);
    URUI_API int urui_element_ref_release(uint64_t token);
    URUI_API int urui_element_ref_count(void);
    URUI_API int urui_element_ref_set_translate_scale_2d(uint64_t token, float x, float y, float sx, float sy);
#ifdef __cplusplus
}
#endif

// Prepared-batch API v1. Main-context-thread synchronous CPU data only.
// Packed little-endian vertex: position float3/color RGBA8/uv float2/slot float2.
// Slot is the original command ordinal. Index/primitive/painter order preserved.
// copy calls own no output memory and never retain pointers. Release lease only
// AFTER all synchronous copies; Unity GPU object retirement remains caller-owned.
// Status 1 success; -1 invalid, -2 stale/missing, -3 foreign thread, -4 budget,
// -5 busy/closing, -8 allocation/internal failure.
#ifdef __cplusplus
extern "C"
{
#endif
    typedef struct UruiPreparedVertex
    {
        float x, y, z;
        uint8_t r, g, b, a;
        float u, v, slot, pad;
    } UruiPreparedVertex;
    typedef struct UruiPreparedDraw
    {
        uint64_t batch, texture;
        uint32_t slot, count, vertex_count, index_count, kind;
        int32_t clip_enabled, clip_x, clip_y, clip_w, clip_h;
    } UruiPreparedDraw;
    // Build/reuse/copy/retained reports are zero unless URUI_DEBUG is enabled.
    typedef struct UruiPreparedPacket
    {
        uint64_t lease, session, epoch, plan, resources, retained_bytes, built_bytes;
        uint32_t draw_count, commands, built_batches, reused_batches, native_vertices_copied, native_indices_copied,
            stride, version;
    } UruiPreparedPacket;
    URUI_API int urui_prepared_api_version(void);
    URUI_API uint64_t urui_prepared_create(void);
    // Busy destroy marks session closing; copy/release remain valid until lastlease.
    URUI_API int urui_prepared_destroy(uint64_t session);
    URUI_API int urui_prepared_acquire(uint64_t session, const UruiFrameStamp* expected, const uint8_t* affine_mask,
                                       int count, UruiPreparedPacket* packet);
    URUI_API int urui_prepared_copy_draws(uint64_t session, uint64_t lease, UruiPreparedDraw* out, int capacity);
    URUI_API int urui_prepared_copy_batch(uint64_t session, uint64_t lease, uint64_t batch,
                                          UruiPreparedVertex* vertices, int vertex_capacity, uint32_t* indices,
                                          int index_capacity);
    URUI_API int urui_prepared_release(uint64_t session, uint64_t lease);
#ifdef __cplusplus
}
#endif

// Optional incremental CPU copy. Caller base token asserts BOTH output arrays
// contain that complete immutable batch and have not been modified. Unknown,
// missing or incompatible base always falls back to a complete copy. No commit
// acknowledgement: a lease always retains the complete final payload for retry.
#ifdef __cplusplus
extern "C"
{
#endif
    // Byte/range reports are debug-only; base_batch and mode are always valid.
    typedef struct UruiPreparedDeltaInfo
    {
        uint64_t base_batch, full_bytes, delta_bytes, copied_bytes;
        uint32_t mode, vertex_ranges, index_ranges, reserved;
    } UruiPreparedDeltaInfo;
    URUI_API int urui_prepared_delta_api_version(void);
    URUI_API int urui_prepared_delta_enable(uint64_t session, int enabled);
    URUI_API int urui_prepared_copy_incremental(uint64_t session, uint64_t lease, uint64_t batch, uint64_t base_batch,
                                                UruiPreparedVertex* vertices, int vertex_capacity, uint32_t* indices,
                                                int index_capacity, UruiPreparedDeltaInfo* info);
#ifdef __cplusplus
}
#endif

// Final-frame view API v1, native-width pointers. Views are READ ONLY and
// remain valid until release; neither allocator nor GPU ownership is transferred.
#ifdef __cplusplus
extern "C"
{
#endif
    // Build/reuse/retained reports are debug-only. Affine/fallback counts remain
    // production protocol fields and always sum to count.
    typedef struct UruiNativeFramePacket
    {
        uint64_t session, lease, epoch, commands, resources, plan, frame, retained_bytes, built_bytes;
        const UruiPreparedDraw* draws;
        const float* values;
        const float* worlds;
        uint32_t count, draw_count, built_batches, reused_batches, affine_count, fallback_count, version, flags;
    } UruiNativeFramePacket;
    typedef struct UruiNativeMeshView
    {
        const UruiPreparedVertex* vertices;
        const uint32_t* indices;
        uint32_t vertex_count, index_count, vertex_stride, index_stride;
    } UruiNativeMeshView;
    URUI_API int urui_native_frame_api_version(void);
    URUI_API uint64_t urui_native_frame_create(void);
    // Arithmetic 1 = rounded binary32 operations (IL2CPP), 2 = wide intermediate
    // operations before storing float matrix fields (observed Mono Editor).
    URUI_API uint64_t urui_native_frame_create_mode(uint32_t arithmetic);
    URUI_API int urui_native_frame_acquire(uint64_t session, UruiNativeFramePacket* packet);
    URUI_API int urui_native_frame_mesh(uint64_t session, uint64_t lease, uint64_t batch, UruiNativeMeshView* view);
    URUI_API int urui_native_frame_release(uint64_t session, uint64_t lease);
    URUI_API int urui_native_frame_destroy(uint64_t session);
#ifdef __cplusplus
}
#endif

// Caller-owned arithmetic preparation used for renderer capability matching.
#ifdef __cplusplus
extern "C"
{
#endif
    URUI_API int urui_native_frame_prepare_matrix(const float* matrix, float tx, float ty, float* world,
                                                  float* parameter);
    URUI_API int urui_native_frame_prepare_matrix_mode(const float* matrix, float tx, float ty, uint32_t arithmetic,
                                                       float* world, float* parameter);
#ifdef __cplusplus
}
#endif

#if defined(URUI_DEBUG)
#include "Debug/urui_debug.h"
#endif
