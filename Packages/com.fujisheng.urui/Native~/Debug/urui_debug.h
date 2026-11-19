#pragma once
#include "../urui.h"

// Diagnostic ABI is available only when both managed and native builds opt in.
#if defined(URUI_DEBUG)
#ifdef __cplusplus
extern "C"
{
#endif
    // Nanoseconds accumulated on the Unity main thread since the previous take.
    // geometry_compile_ns is nested in render_ns when URUI compiles while rendering.
    typedef struct
    {
        uint64_t text_ns;
        uint64_t update_ns;
        uint64_t render_ns;
        uint64_t geometry_compile_ns;
        uint64_t command_copy_ns;
        uint64_t geometry_copy_ns;
        uint64_t text_settext_ns;
        uint64_t text_setinnerhtml_ns;
        uint64_t text_lookup_ns;
        uint32_t text_calls, geometry_compiles, geometry_vertices, geometry_indices, text_fast_calls,
            text_fallback_calls, text_lookups;
    } URUIProfile;
    typedef struct URUIApiProfile
    {
        uint64_t style_ns;
        uint64_t attribute_ns;
        uint64_t style_calls;
        uint64_t attribute_calls;
    } URUIApiProfile;
    typedef struct URUITypedProbe
    {
        uint64_t total_ns;
        uint64_t lookup_ns;
        uint64_t construct_ns;
        uint64_t apply_ns;
        uint64_t calls;
        uint64_t misses;
        uint64_t rejects;
        uint64_t clock_pairs;
    } URUITypedProbe;
    typedef struct URUIStyleProbe
    {
        uint64_t lookup_ns, parse_ns, apply_ns, compare_ns, calls, parsed_calls, rejected_calls, properties, comparable,
            equal_properties, equal_calls, unsupported_calls;
    } URUIStyleProbe;
    typedef struct URUILookupProfile
    {
        uint64_t lookup_ns, style_lookup_ns, queries, style_queries, nodes_visited, max_frontier, inline_calls,
            overflow_calls;
    } URUILookupProfile;

    URUI_API int urui_profile_take(URUIProfile* out);
    URUI_API void urui_api_profile_enable(int enabled);
    URUI_API int urui_api_profile_take(URUIApiProfile* out);
    URUI_API int urui_typed_probe_mode(int mode);
    URUI_API int urui_typed_probe_take(URUITypedProbe* out);
    URUI_API int urui_style_probe_mode(int mode);
    URUI_API int urui_style_probe_take(URUIStyleProbe* out);
    URUI_API int urui_lookup_profile_enable(int mode);
    URUI_API int urui_lookup_profile_take(URUILookupProfile* out);
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
