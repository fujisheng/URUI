#pragma once
#include <stdint.h>
typedef uint64_t URUISurfaceHandle;
typedef uint64_t URUIDocumentHandle;
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
    // Unified status codes across the entire C ABI v13.
    // Positive (> 0): success (URUI_OK = 1, or count/specialized status where documented).
    // Zero (0): absent / false / empty / no-op.
    // Negative (< 0): explicit error class; never encodes -required.
    enum URUIStatus
    {
        URUI_OK = 1,
        URUI_ABSENT = 0,
        URUI_ERR_INVALID_ARG = -1,
        URUI_ERR_NOT_READY = -2,
        URUI_ERR_WRONG_THREAD = -3,
        URUI_ERR_REENTRANCY = -4,
        URUI_ERR_BUFFER_TOO_SMALL = -5,
        URUI_ERR_BUDGET = -6,
        URUI_ERR_VALIDATION = -7,
        URUI_ERR_INTERNAL = -8,
        URUI_ERR_STALE_REVISION = -9,
        URUI_ERR_STRUCTURED = -10
    };

    typedef struct
    {
        float x;
        float y;
        float u;
        float v;
        uint8_t r;
        uint8_t g;
        uint8_t b;
        uint8_t a;
    } URUIVertex;

    typedef struct
    {
        uint64_t geometry;
        uint64_t texture;
        float tx;
        float ty;
        // Command protocol 3: bit0 scissor, bit1 repeat X, bit2 repeat Y.
        int flags;
        int clip_x;
        int clip_y;
        int clip_w;
        int clip_h;
        int reserved;
    } URUICommand;

    typedef struct
    {
        uint64_t shader;
        uint64_t element;
        int command_index;
        int reserved;
    } URUIPaintCommand;

    enum URUILayerCommandKind
    {
        URUI_LAYER_PUSH = 1,
        URUI_LAYER_POP = 2,
        URUI_LAYER_SAVE_MASK = 3,
        URUI_LAYER_COMPOSITE = 4,
        URUI_LAYER_RELEASE_MASK = 5,
        URUI_LAYER_APPLY_MASK = 6
    };

    typedef struct
    {
        int command_index;
        int kind;
        uint64_t source;
        uint64_t target;
        uint64_t mask;
        int blend;
        int flags;
        int clip_x;
        int clip_y;
        int clip_w;
        int clip_h;
    } URUILayerCommand;

    typedef struct
    {
        int kind;
        float value;
    } URUIColorFilter;

    typedef struct
    {
        uint64_t handle;
        uint64_t element;
        float value;
        float sigma;
        float offset_x;
        float offset_y;
        float red;
        float green;
        float blue;
        float alpha;
    } URUIFilter;

    typedef struct
    {
        const char* uri;
        const uint8_t* bytes;
        int length;
    } URUICompileResource;

    typedef struct
    {
        uint64_t identity;
        uint64_t revision;
        uint32_t local_id;
        uint32_t end;
        int flags;
        int root_match;
    } URUICompiledNodeInfo;

    typedef struct URUIAnimationSample
    {
        const char* name;
        const char* easing;
        float progress;
    } URUIAnimationSample;

    typedef struct URUISequenceBatchItem
    {
        uint64_t token;
        uint64_t owner;
        uint32_t sample_offset;
        uint32_t sample_count;
        int32_t mode;
        int32_t reserved;
    } URUISequenceBatchItem;

    typedef int (*URUIImageResolver)(URUISurfaceHandle surface, URUIDocumentHandle document, const char* source,
                                     uint64_t* resource_id, uint64_t* source_identity, int* width, int* height);
    typedef void (*URUIImageRelease)(URUISurfaceHandle surface, uint64_t resource_id);

    // Protocol version queries (stateless).
    URUI_API int urui_bridge_abi_version(void);
    URUI_API int urui_command_protocol_version(void);
    URUI_API int urui_texture_protocol_version(void);
    URUI_API int urui_image_resolver_version(void);
    URUI_API int urui_ui_clock_api_version(void);
    URUI_API int urui_render_plan_version(void);
    URUI_API int urui_animation_api_version(void);
    URUI_API int urui_sequence_api_version(void);
    URUI_API int urui_effect_api_version(void);
    URUI_API int urui_filter_api_version(void);

    // Surface lifecycle & error inspection.
    URUI_API int urui_surface_create(int width, int height, URUISurfaceHandle* out_surface);
    URUI_API int urui_surface_destroy(URUISurfaceHandle surface);
    URUI_API int urui_surface_last_error(URUISurfaceHandle surface, char* out, int capacity, int* required);
    URUI_API int urui_last_error_copy(char* out, int capacity, int* required);

    // Surface bootstrap & configuration.
    URUI_API int urui_surface_set_default_font_face(URUISurfaceHandle surface, int family, const uint8_t* bytes,
                                                    int length, int face_index, int style, int weight);
    URUI_API int urui_surface_set_image_resolver(URUISurfaceHandle surface, URUIImageResolver resolve,
                                                 URUIImageRelease release);
    URUI_API int urui_surface_set_ui_time(URUISurfaceHandle surface, double now);
    URUI_API int urui_surface_set_direct_color_filters(URUISurfaceHandle surface, int enabled);

    // Surface frame lifecycle & queries.
    URUI_API int urui_surface_resize(URUISurfaceHandle surface, int width, int height);
    URUI_API int urui_surface_update(URUISurfaceHandle surface);
    URUI_API int urui_surface_synchronize_geometry(URUISurfaceHandle surface);
    URUI_API int urui_surface_render(URUISurfaceHandle surface);
    URUI_API uint64_t urui_surface_layout_version(URUISurfaceHandle surface);
    URUI_API URUIDocumentHandle urui_surface_element_owner(URUISurfaceHandle surface, uint64_t token);
    URUI_API URUIDocumentHandle urui_surface_focused_document(URUISurfaceHandle surface);
    URUI_API uint64_t urui_surface_pick(URUISurfaceHandle surface, float x, float y, URUIDocumentHandle* owner);

    // Document lifecycle & stylesheets.
    URUI_API int urui_document_create(URUISurfaceHandle surface, URUIDocumentHandle* result);
    URUI_API int urui_document_load_html(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* html,
                                         const char* source_url);
    URUI_API int urui_document_load_compiled(URUISurfaceHandle surface, URUIDocumentHandle owner, const uint8_t* bytes,
                                             int length);
    URUI_API int urui_document_close(URUISurfaceHandle surface, URUIDocumentHandle owner);
    URUI_API int urui_document_set_presentation(URUISurfaceHandle surface, URUIDocumentHandle owner, int layer,
                                                int order, int visible, int modal);
    URUI_API int urui_document_add_stylesheet(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* css,
                                              const char* source_url, int before_author);
    URUI_API uint64_t urui_document_root(URUISurfaceHandle surface, URUIDocumentHandle owner);
    URUI_API uint64_t urui_document_body(URUISurfaceHandle surface, URUIDocumentHandle owner);
    URUI_API uint64_t urui_document_layout_version(URUISurfaceHandle surface, URUIDocumentHandle owner);
    URUI_API int urui_document_drain_invalidated_elements(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                          uint64_t* out, int capacity, int* out_total,
                                                          int* out_overflow);

    // Stateless authoring/compiling APIs (safe alongside active Surfaces).
    URUI_API int urui_validate_stylesheet(const char* uri, const char* source, char* output, int capacity,
                                          int* required);
    URUI_API int urui_compile_document(const char* html, const char* source_url, int fragment,
                                       const URUICompileResource* resources, int resource_count, uint8_t* output,
                                       int capacity, int* required);
    URUI_API int urui_compile_template(const uint8_t* parent, int parent_length, const char* html,
                                       const char* source_url, uint8_t* output, int capacity, int* required);
    URUI_API int urui_compiled_document_metadata(const uint8_t* bytes, int length, char* output, int capacity,
                                                 int* required);

    // Fragment & compiled template queries.
    URUI_API int urui_element_set_inner_html(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element,
                                             const char* html, const char* source_url);
    URUI_API int urui_element_begin_inner_html(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element,
                                               const char* html, const char* source_url);
    URUI_API int urui_element_end_inner_html(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element,
                                             int commit);
    URUI_API int urui_element_begin_compiled_template(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                      uint64_t element, const uint8_t* bytes, int length);
    URUI_API int urui_element_compiled_info(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element,
                                            int query_kind, const char* key, URUICompiledNodeInfo* info);
    URUI_API int urui_element_compiled_tokens(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element,
                                              uint64_t identity, uint64_t revision, uint64_t* output, int capacity);

    // Token-based DOM & element operations.
    URUI_API uint64_t urui_element_create(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* tag);
    URUI_API uint64_t urui_element_clone(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_clone_template_content(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                     uint64_t source, uint64_t destination);
    URUI_API int urui_element_number(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                     const char* property, float value, int unit);
    URUI_API int urui_element_color(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                    const char* property, int red, int green, int blue, int alpha);
    URUI_API int urui_element_trs(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                  const float* values, int count);
    // preserve_origin != 0 keeps existing transform-origin and only requires 10 floats (tx,ty,tz,ax,ay,az,deg,sx,sy,sz).
    URUI_API int urui_element_trs_ex(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                     const float* values, int count, int preserve_origin);
    URUI_API uint64_t urui_element_parent(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_child_count(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API uint64_t urui_element_child_at(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                            int index);
    URUI_API uint64_t urui_element_find(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                        const char* name, const char* class_name);
    URUI_API int urui_element_query(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                    const char* name, const char* class_name, uint64_t* out, int capacity);
    // read fields: 0=name, 1=tag, 2=plain text, 3=attribute, 4=inner HTML.
    URUI_API int urui_element_read(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int field,
                                   const char* name, char* out, int capacity, int* required);
    // write fields: 0=name, 1=plain text, 2=attribute, 3=attached inner HTML, 4=inline style.
    URUI_API int urui_element_write(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int field,
                                    const char* name, const char* value);
    // erase fields: 0=attribute, 1=inline style. Class action: -1=query, 0=remove, 1=add.
    URUI_API int urui_element_erase(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int field,
                                    const char* name);
    URUI_API int urui_element_class(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                    const char* name, int action);
    URUI_API int urui_element_insert(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t parent,
                                     uint64_t child, int index);
    URUI_API int urui_element_insert_before(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t parent,
                                            uint64_t child, uint64_t ref_child);
    URUI_API int urui_element_detach(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_clear(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_destroy(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_attached(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_visible_in_hierarchy(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_pop_click(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t* target);
    URUI_API uint64_t urui_element_ref_acquire(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* id);
    URUI_API int urui_element_ref_valid(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_ref_release(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_ref_set_translate_scale_2d(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                         uint64_t token, float x, float y, float sx, float sy);

    // Effects, Filters & Sequences.
    URUI_API int urui_effect_layout(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element, float* out,
                                    int capacity, uint64_t* root);
    URUI_API int urui_element_effect_state(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t element,
                                           int capture_required, int input_disabled);
    URUI_API int urui_document_effect_image_condition(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                      const char* effect, const char* property,
                                                      const char* direct_value);
    URUI_API URUIDocumentHandle urui_filter_document(URUISurfaceHandle surface, uint64_t handle);
    URUI_API int urui_filter_info(URUISurfaceHandle surface, uint64_t handle, URUIFilter* result, char* name,
                                  int capacity, int* required);
    URUI_API int urui_sequence_layout(URUISurfaceHandle surface, URUIDocumentHandle owner);
    URUI_API int urui_keyframes_count(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                      const char* name);
    URUI_API int urui_keyframes_properties(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                           const char* name, char* output, int capacity, int* required);
    URUI_API int urui_sequence_sample(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                      uint64_t sequence_owner, const URUIAnimationSample* samples, int count, int mode);
    URUI_API int urui_sequence_apply_batch(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                           const URUISequenceBatchItem* items, int item_count,
                                           const URUIAnimationSample* samples, int sample_count);
    URUI_API uint64_t urui_animation_next_identity(void);
    URUI_API int urui_animation_current_event(URUISurfaceHandle surface, URUIDocumentHandle owner, char* name,
                                              int capacity, int* required, uint64_t* identity, int* iterations);

    // Recorded command / resource inspection.
    URUI_API int urui_command_count(URUISurfaceHandle surface);
    URUI_API int urui_get_commands(URUISurfaceHandle surface, URUICommand* out, int capacity);
    URUI_API int urui_get_paint_commands(URUISurfaceHandle surface, URUIPaintCommand* out, int capacity);
    URUI_API int urui_paint_color_filters(URUISurfaceHandle surface, uint64_t shader, URUIColorFilter* out,
                                          int capacity);
    URUI_API int urui_paint_shader_name(URUISurfaceHandle surface, uint64_t shader, char* out, int capacity,
                                        int* required);
    URUI_API int urui_get_layer_commands(URUISurfaceHandle surface, URUILayerCommand* out, int capacity);
    URUI_API int urui_geometry_exists(URUISurfaceHandle surface, uint64_t id);
    URUI_API int urui_get_geometry_info(URUISurfaceHandle surface, uint64_t id, int* v, int* i);
    URUI_API int urui_copy_geometry(URUISurfaceHandle surface, uint64_t id, URUIVertex* v, int vc, int* i, int ic);
    URUI_API int urui_texture_exists(URUISurfaceHandle surface, uint64_t id);
    URUI_API int urui_get_texture_kind(URUISurfaceHandle surface, uint64_t id);
    URUI_API int urui_get_texture_info(URUISurfaceHandle surface, uint64_t id, int* w, int* h, int* bytes);
    URUI_API int urui_copy_texture(URUISurfaceHandle surface, uint64_t id, uint8_t* out, int capacity);
#ifdef __cplusplus
}
#endif

#include "element_runtime.h"
#include "document_resources.h"
#include "style_properties.h"
#include "render_program_parameters.h"
#include "authoring.h"
#include "render_plan_protocol.h"

#if defined(URUI_DEBUG)
#include "Debug/urui_debug.h"
#endif
