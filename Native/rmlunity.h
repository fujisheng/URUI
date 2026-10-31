#pragma once
#include <stdint.h>
#if defined(_WIN32)
# if defined(RMLUNITY_EXPORTS)
#  define RUI_API __declspec(dllexport)
# else
#  define RUI_API __declspec(dllimport)
# endif
#else
# define RUI_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct { float x,y,u,v; uint8_t r,g,b,a; } RuiVertex;
typedef struct { uint64_t geometry,texture; float tx,ty; int clip_enabled,clip_x,clip_y,clip_w,clip_h; } RuiCommand;
// Nanoseconds accumulated on the Unity main thread since the previous take.
// geometry_compile_ns is nested in render_ns when RmlUi compiles while rendering.
typedef struct {
 uint64_t text_ns,update_ns,render_ns,geometry_compile_ns,command_copy_ns,geometry_copy_ns;
 uint64_t text_settext_ns,text_setinnerrml_ns,text_lookup_ns;
 uint32_t text_calls,geometry_compiles,geometry_vertices,geometry_indices,text_fast_calls,text_fallback_calls,text_lookups;
} RuiProfile;
// Version 2 adds generic texture/touch/DOM/transform symbols while preserving
// RuiVertex, RuiCommand and RuiProfile layouts from version 1.
RUI_API int rui_bridge_abi_version(void);
RUI_API int rui_create(int width,int height,const char* font_path);
RUI_API void rui_destroy(void);
RUI_API int rui_load_document(const char* rml);
RUI_API void rui_unload_document(void);
// Image bytes are top-row-first straight-alpha RGBA8. The bridge
// premultiplies once; Unity GetPixels32 callers must reverse its row order.
// Returns 1 on registration/replacement, negative for invalid/busy.
RUI_API int rui_register_rgba_texture(const char* source,int width,int height,const uint8_t* rgba,int byte_count,uint64_t* handle);
RUI_API int rui_unregister_texture(const char* source);
RUI_API int rui_texture_lifecycle_selftest(void);
RUI_API void rui_resize(int width,int height);
RUI_API void rui_mouse_move(int x,int y);
RUI_API void rui_mouse_button(int button,int pressed);
RUI_API void rui_mouse_wheel(float dy);
// phase: 0=start, 1=move, 2=end, 3=cancel. Coordinates are logical
// top-left pixels; pointer IDs are Unity touch finger IDs, not mouse buttons.
RUI_API int rui_touch_event(int phase,uint64_t pointer_id,float x,float y);
// Return UTF-8 byte count, 0 for no identified hit, negative required capacity.
RUI_API int rui_hit_test_id(float x,float y,char* out,int capacity);
RUI_API int rui_touch_selftest(void);
// ID lookup follows RmlUi GetElementById (first match). Mutations return
// 1 success, 0 missing, negative invalid input or rejected property.
RUI_API int rui_element_exists(const char* id);
// Absolute border-box x,y,w,h followed by clientWidth,scrollWidth.
RUI_API int rui_get_element_box(const char* id,float* out_six);
RUI_API int rui_set_attribute(const char* id,const char* name,const char* value);
RUI_API int rui_set_style(const char* id,const char* name,const char* value);
// Equivalent to translate(xpx,ypx) scale(sx,sy), preserving primitive order,
// transform-origin and normal dirty semantics. Finite zero/negative scale
// supported. Identity is NOT transform:none. Same return codes as SetStyle.
RUI_API int rui_set_translate_scale_2d(const char* id,float x,float y,float sx,float sy);
RUI_API int rui_set_inner_rml(const char* id,const char* markup);
RUI_API int rui_append_element(const char* parent_id,const char* tag,const char* id);
// Diagnostic actual DOM child oracle, all DOM children in sibling order.
// count returns -1 for invalid/missing; id returns length, -required capacity,
// or -1 invalid/missing/index. No payload written on failure.
RUI_API int rui_get_child_count(const char* id);
RUI_API int rui_get_child_id_utf8(const char* id,int index,char* out,int capacity);
RUI_API int rui_reorder_element_to_end(const char* id);
RUI_API int rui_remove_element(const char* id);
RUI_API int rui_dom_selftest(void);
// One column-major 4x4 transform per render command. Window-space scissor is
// applied after the transform. Existing RuiCommand ABI remains 48 bytes.
RUI_API int rui_get_command_transforms(float* out,int float_capacity);
RUI_API int rui_transform_selftest(void);
RUI_API void rui_update(void);
RUI_API void rui_render(void);
// Read actual single text child, or empty element. required includes UTF8 NUL.
// 1 success, 0 missing document/element, -1 invalid arguments, -2 structured
// children, -3 insufficient capacity/probe. Payload unchanged on every failure.
// Synchronous context-thread call, no borrowed pointer escapes.
RUI_API int rui_get_attribute_utf8(const char* id,const char* name,char* out,int capacity,int* required);
RUI_API int rui_get_text_utf8(const char* id,char* out,int capacity,int* required);
RUI_API void rui_set_text(const char* id,const char* text);
RUI_API float rui_get_scroll_top(const char* id);
RUI_API int rui_set_pixel_style(const char* id,const char* property,float pixels);
RUI_API int rui_element_count(void);
RUI_API int rui_command_count(void);
RUI_API int rui_get_commands(RuiCommand* out,int capacity);
RUI_API int rui_get_geometry_info(uint64_t id,int* v,int* i);
RUI_API int rui_copy_geometry(uint64_t id,RuiVertex* v,int vc,int* i,int ic);
RUI_API int rui_get_texture_info(uint64_t id,int* w,int* h,int* bytes);
RUI_API int rui_copy_texture(uint64_t id,uint8_t* out,int capacity);
RUI_API int rui_geometry_exists(uint64_t id);
RUI_API int rui_texture_exists(uint64_t id);
RUI_API int rui_pop_event(char* out,int capacity);
RUI_API const char* rui_last_error(void);
RUI_API int rui_profile_take(RuiProfile* out);
typedef struct RuiApiProfile { uint64_t style_ns,attribute_ns,style_calls,attribute_calls; } RuiApiProfile;
RUI_API void rui_api_profile_enable(int enabled);
RUI_API int rui_api_profile_take(RuiApiProfile* out);
// Optional diagnostic build. mode0 off,1 stage timers,2 also exact inline
// equality for supported representations. No mutation is skipped.
// Optional exact retained-plan tracking. Native Update/Render are never skipped.
// flags: 1 complete/supported stamp; 2 unsupported optional renderer operation;
// otherwise 0 (disabled/incomplete/exhausted). Counts/bytes are diagnostics only.
typedef struct RuiFrameStamp { uint64_t epoch,commands,resources,retained_bytes,compare_bytes; uint32_t count,flags; } RuiFrameStamp;
RUI_API int rui_render_plan_version(void);
RUI_API int rui_render_plan_enable(int enabled);
RUI_API int rui_render_plan_stamp(RuiFrameStamp* out);
// Optional synchronous, caller-owned prepared core data. Keys retain painter
// order; mode 1 contains final world/affine values, mode 0 contains the original
// matrix and tx/ty in value[0..1] for the client's existing full-math fallback.
// No pointer escapes this call. All capacities/stamp/thread checks precede writes.
typedef struct RuiCoreKey { uint64_t geometry,texture; int32_t clip_enabled,clip_x,clip_y,clip_w,clip_h; uint32_t mode; } RuiCoreKey;
typedef struct RuiCoreValue { float data[8]; } RuiCoreValue;
typedef struct RuiCoreSummary { uint32_t ready,fallback; uint64_t bytes; } RuiCoreSummary;
RUI_API int rui_core_frame_api_version(void);
RUI_API int rui_core_frame_copy(const RuiFrameStamp* expected,RuiCoreKey* keys,RuiCoreValue* values,float* worlds,int capacity,RuiCoreSummary* info);
// Explicit weak OBJECT references, not live ID selectors. Acquire uses fresh ID
// semantics; rename does not retarget. Unload/destroy invalidates all tokens.
// Single context thread only (-3 for foreign-thread operations); dispose tokens.
// Budget: 65536 outstanding tokens. Invalid/stale writes return 0, bad numeric -1.
RUI_API uint64_t rui_element_ref_acquire(const char* id);
RUI_API int rui_element_ref_valid(uint64_t token);
RUI_API int rui_element_ref_release(uint64_t token);
RUI_API int rui_element_ref_count(void);
RUI_API int rui_element_ref_set_translate_scale_2d(uint64_t token,float x,float y,float sx,float sy);
typedef struct RuiTypedProbe { uint64_t total_ns,lookup_ns,construct_ns,apply_ns,calls,misses,rejects,clock_pairs; } RuiTypedProbe;
// Off=0; timed=1; count-only=2. Snapshot clears counters, never stores element pointers.
RUI_API int rui_typed_probe_mode(int mode);
RUI_API int rui_typed_probe_take(RuiTypedProbe* out);
typedef struct RuiStyleProbe { uint64_t lookup_ns,parse_ns,apply_ns,compare_ns,calls,parsed_calls,rejected_calls,properties,comparable,equal_properties,equal_calls,unsupported_calls; } RuiStyleProbe;
RUI_API int rui_style_probe_mode(int mode);
RUI_API int rui_style_probe_take(RuiStyleProbe* out);
typedef struct RuiLookupProfile { uint64_t lookup_ns,style_lookup_ns,queries,style_queries,nodes_visited,max_frontier,inline_calls,overflow_calls; } RuiLookupProfile;
// 0 off,1 timing,2 timing+operation counts. Optional build, never per-event log.
RUI_API int rui_lookup_profile_enable(int mode);
RUI_API int rui_lookup_profile_take(RuiLookupProfile* out);
RUI_API int rui_lookup_inline_enable(int enabled);
// Isolated native regression, aftercreate and before loading user documents.
RUI_API int rui_lookup_queue_selftest(void);
#ifdef __cplusplus
}
#endif

// Additive prepared-batch API v1. Main-context-thread synchronous CPU data only.
// Packed little-endian vertex: position float3/color RGBA8/uv float2/slot float2.
// Slot is the original command ordinal. Index/primitive/painter order preserved.
// copy calls own no output memory and never retain pointers. Release lease only
// AFTER all synchronous copies; Unity GPU object retirement remains caller-owned.
// Status 1 success; -1 invalid, -2 stale/missing, -3 foreign thread, -4 budget,
// -5 busy/closing, -8 allocation/internal failure. Old bridge ABI remains v2.
#ifdef __cplusplus
extern "C" {
#endif
typedef struct RuiPreparedVertex {float x,y,z;uint8_t r,g,b,a;float u,v,slot,pad;} RuiPreparedVertex;
typedef struct RuiPreparedDraw {uint64_t batch,texture;uint32_t slot,count,vertex_count,index_count,kind;int32_t clip_enabled,clip_x,clip_y,clip_w,clip_h;} RuiPreparedDraw;
typedef struct RuiPreparedPacket {uint64_t lease,session,epoch,plan,resources,retained_bytes,built_bytes;uint32_t draw_count,commands,built_batches,reused_batches,native_vertices_copied,native_indices_copied,stride,version;} RuiPreparedPacket;
RUI_API int rui_prepared_api_version(void);
RUI_API uint64_t rui_prepared_create(void);
// Busy destroy marks session closing; copy/release remain valid until lastlease.
RUI_API int rui_prepared_destroy(uint64_t session);
RUI_API int rui_prepared_acquire(uint64_t session,const RuiFrameStamp* expected,const uint8_t* affine_mask,int count,RuiPreparedPacket* packet);
RUI_API int rui_prepared_copy_draws(uint64_t session,uint64_t lease,RuiPreparedDraw* out,int capacity);
RUI_API int rui_prepared_copy_batch(uint64_t session,uint64_t lease,uint64_t batch,RuiPreparedVertex* vertices,int vertex_capacity,uint32_t* indices,int index_capacity);
RUI_API int rui_prepared_release(uint64_t session,uint64_t lease);
#ifdef __cplusplus
}
#endif

// Optional incremental CPU copy. Caller base token asserts BOTH output arrays
// contain that complete immutable batch and have not been modified. Unknown,
// missing or incompatible base always falls back to a complete copy. No commit
// acknowledgement: a lease always retains the complete final payload for retry.
#ifdef __cplusplus
extern "C" {
#endif
typedef struct RuiPreparedDeltaInfo {uint64_t base_batch,full_bytes,delta_bytes,copied_bytes;uint32_t mode,vertex_ranges,index_ranges,reserved;} RuiPreparedDeltaInfo;
RUI_API int rui_prepared_delta_api_version(void);
RUI_API int rui_prepared_delta_enable(uint64_t session,int enabled);
RUI_API int rui_prepared_copy_incremental(uint64_t session,uint64_t lease,uint64_t batch,uint64_t base_batch,RuiPreparedVertex* vertices,int vertex_capacity,uint32_t* indices,int index_capacity,RuiPreparedDeltaInfo* info);
#ifdef __cplusplus
}
#endif

// Additive final-frame view API v1, 64-bit pointers. Views are READ ONLY and
// remain valid until release; neither allocator nor GPU ownership is transferred.
#ifdef __cplusplus
extern "C" {
#endif
typedef struct RuiNativeFramePacket {
 uint64_t session,lease,epoch,commands,resources,plan,frame,retained_bytes,built_bytes;
 const RuiPreparedDraw* draws;const float* values;const float* worlds;
 uint32_t count,draw_count,built_batches,reused_batches,affine_count,fallback_count,version,flags;
} RuiNativeFramePacket;
typedef struct RuiNativeMeshView {const RuiPreparedVertex* vertices;const uint32_t* indices;uint32_t vertex_count,index_count,vertex_stride,index_stride;} RuiNativeMeshView;
RUI_API int rui_native_frame_api_version(void);
RUI_API uint64_t rui_native_frame_create(void);
// Arithmetic 1 = rounded binary32 operations (IL2CPP), 2 = wide intermediate
// operations before storing float matrix fields (observed Mono Editor).
RUI_API uint64_t rui_native_frame_create_mode(uint32_t arithmetic);
RUI_API int rui_native_frame_acquire(uint64_t session,RuiNativeFramePacket* packet);
RUI_API int rui_native_frame_mesh(uint64_t session,uint64_t lease,uint64_t batch,RuiNativeMeshView* view);
RUI_API int rui_native_frame_release(uint64_t session,uint64_t lease);
RUI_API int rui_native_frame_destroy(uint64_t session);
// Numeric contract probe; synchronous pointers, no retention. 1 affine, 2 full,
// -1 nonfinite input/result. Output is zero on rejected inputs.
RUI_API int rui_native_frame_prepare_matrix(const float* matrix,float tx,float ty,float* world,float* parameter);
RUI_API int rui_native_frame_prepare_matrix_mode(const float* matrix,float tx,float ty,uint32_t arithmetic,float* world,float* parameter);
#ifdef __cplusplus
}
#endif
