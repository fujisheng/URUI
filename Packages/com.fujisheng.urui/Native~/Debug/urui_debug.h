#pragma once
#include "../urui.h"

// Diagnostic ABI is available only when both managed and native builds opt in.
#if defined(URUI_DEBUG)
#ifdef __cplusplus
extern "C"
{
#endif
    // Nanoseconds accumulated on the Unity main thread since the previous take.
    // geometry_compile_ns is nested in render_ns when RmlUi compiles while rendering.
    typedef struct
    {
        uint64_t text_ns, update_ns, render_ns, geometry_compile_ns, command_copy_ns, geometry_copy_ns;
        uint64_t text_settext_ns, text_setinnerrml_ns, text_lookup_ns;
        uint32_t text_calls, geometry_compiles, geometry_vertices, geometry_indices, text_fast_calls,
            text_fallback_calls, text_lookups;
    } UruiProfile;
    typedef struct UruiApiProfile
    {
        uint64_t style_ns, attribute_ns, style_calls, attribute_calls;
    } UruiApiProfile;
    typedef struct UruiTypedProbe
    {
        uint64_t total_ns, lookup_ns, construct_ns, apply_ns, calls, misses, rejects, clock_pairs;
    } UruiTypedProbe;
    typedef struct UruiStyleProbe
    {
        uint64_t lookup_ns, parse_ns, apply_ns, compare_ns, calls, parsed_calls, rejected_calls, properties, comparable,
            equal_properties, equal_calls, unsupported_calls;
    } UruiStyleProbe;
    typedef struct UruiLookupProfile
    {
        uint64_t lookup_ns, style_lookup_ns, queries, style_queries, nodes_visited, max_frontier, inline_calls,
            overflow_calls;
    } UruiLookupProfile;

    URUI_API int urui_profile_take(UruiProfile* out);
    URUI_API void urui_api_profile_enable(int enabled);
    URUI_API int urui_api_profile_take(UruiApiProfile* out);
    URUI_API int urui_typed_probe_mode(int mode);
    URUI_API int urui_typed_probe_take(UruiTypedProbe* out);
    URUI_API int urui_style_probe_mode(int mode);
    URUI_API int urui_style_probe_take(UruiStyleProbe* out);
    URUI_API int urui_lookup_profile_enable(int mode);
    URUI_API int urui_lookup_profile_take(UruiLookupProfile* out);
    URUI_API int urui_lookup_inline_enable(int enabled);
    URUI_API int urui_lookup_queue_selftest(void);
    URUI_API int urui_texture_lifecycle_selftest(void);
    URUI_API int urui_touch_selftest(void);
    URUI_API int urui_dom_selftest(void);
    URUI_API int urui_transform_selftest(void);
#ifdef __cplusplus
}
#endif
#endif
