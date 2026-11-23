#pragma once

// Document-owned element runtime & interaction protocol v4 (C ABI v13).
#ifdef __cplusplus
extern "C"
{
#endif
    enum URUIElementStateFlag
    {
        URUI_STATE_VALID = 1,
        URUI_STATE_ATTACHED = 2,
        URUI_STATE_VISIBLE_IN_HIERARCHY = 4,
        URUI_STATE_DISABLED_IN_HIERARCHY = 8
    };

    typedef struct URUIElementRefreshStamp
    {
        uint64_t mutation;
        uint64_t references;
    } URUIElementRefreshStamp;

    typedef struct URUIElementRefreshState
    {
        int32_t state;
        float layout[4];
    } URUIElementRefreshState;

    typedef struct URUIElementSpatialSnapshot
    {
        float layout[4];
        float content[4];
        float world_bounds[4];
        float scroll[4];
        float viewport[4];
        float local_matrix[16];
        float rotation_matrix[16];
        int32_t state_flags;
        int32_t separate_rotation;
    } URUIElementSpatialSnapshot;

    typedef struct URUIRangeInfo
    {
        float minimum;
        float maximum;
        float step;
        float step_base;
        int flags;
    } URUIRangeInfo;

    typedef struct URUIElementEvent
    {
        URUISurfaceHandle surface;
        URUIDocumentHandle document;
        uint64_t target;
        uint64_t current;
        int32_t type;
        int32_t phase;
        int32_t pointer;
        int32_t button;
        int32_t buttons;
        int32_t modifiers;
        int32_t key;
        int32_t kind;
        float x;
        float y;
        float dx;
        float dy;
        const char* text;
    } URUIElementEvent;

    typedef int (*URUIElementCallback)(const URUIElementEvent* event);

    URUI_API int urui_element_api_version(void);
    URUI_API int urui_transform_api_version(void);
    URUI_API uint64_t urui_element_ref_revision(URUISurfaceHandle surface);
    URUI_API int urui_element_refresh_stamp(URUISurfaceHandle surface, URUIElementRefreshStamp* out);
    URUI_API int urui_element_state_flags(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_element_spatial_snapshot(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                               URUIElementSpatialSnapshot* out);
    URUI_API int urui_element_local_transform(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                              float* out, int capacity);
    URUI_API int urui_element_style_read(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int local,
                                         const char* property, char* out, int capacity, int* required);
    URUI_API int urui_element_box(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, float* out,
                                  int capacity);
    URUI_API int urui_element_refresh_state(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                            float* out, int capacity);
    URUI_API int urui_element_refresh_states(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                             const uint64_t* tokens, URUIElementRefreshState* out, uint32_t count);
    URUI_API int urui_element_viewport(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, float* out,
                                       int capacity);
    URUI_API int urui_element_point(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int to_world,
                                    float x, float y, float* out);
    URUI_API uint64_t urui_element_pick(URUISurfaceHandle surface, URUIDocumentHandle owner, float x, float y);
    URUI_API int urui_element_focus(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int focus);
    URUI_API uint64_t urui_focused_element(URUISurfaceHandle surface, URUIDocumentHandle owner);
    URUI_API int urui_element_scroll(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, float x,
                                     float y);

    URUI_API int urui_control_value_read(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, char* out,
                                         int capacity, int* required);
    URUI_API int urui_control_value_write(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                          const char* value);
    URUI_API int urui_control_range_info(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                         URUIRangeInfo* info);
    URUI_API int urui_select_option_count(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_select_option_read(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int index,
                                         char* out, int capacity, int* required);
    URUI_API int urui_select_option_add(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                        const char* value);
    URUI_API int urui_select_options_clear(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_select_selection(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int index);

    // Events & Input (per-Surface).
    URUI_API int urui_set_event_callback(URUISurfaceHandle surface, URUIElementCallback callback);
    URUI_API int urui_document_listen(URUISurfaceHandle surface, URUIDocumentHandle owner);
    URUI_API int urui_element_event_interest(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token,
                                             uint32_t mask, uint64_t version);
    URUI_API int urui_element_listen(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token, int type,
                                     int capture, int enabled);
    URUI_API int urui_pointer_poll(URUISurfaceHandle surface, int pointer, int kind, float x, float y, int buttons,
                                   int modifiers);
    URUI_API int urui_pointer_event(URUISurfaceHandle surface, int pointer, int kind, int phase, float x, float y,
                                    int button, int buttons, float dx, float dy, int modifiers);
    URUI_API int urui_pointer_capture(URUISurfaceHandle surface, URUIDocumentHandle owner, int pointer, uint64_t token);
    URUI_API uint64_t urui_pointer_captured(URUISurfaceHandle surface, URUIDocumentHandle owner, int pointer);
    URUI_API int urui_input_reset(URUISurfaceHandle surface);
    URUI_API int urui_cancel_pointers(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token);
    URUI_API int urui_key_event(URUISurfaceHandle surface, int key, int pressed, int modifiers);
    URUI_API int urui_text_input(URUISurfaceHandle surface, const char* text);
    URUI_API int urui_text_composition(URUISurfaceHandle surface, const char* text, int operation);
    URUI_API int urui_text_selection(URUISurfaceHandle surface, int* start, int* end, int write);
    URUI_API uint64_t urui_clipboard_version(URUISurfaceHandle surface);
    URUI_API int urui_clipboard_write(URUISurfaceHandle surface, const char* text);
    URUI_API int urui_clipboard_read(URUISurfaceHandle surface, char* out, int capacity, int* required);
    URUI_API int urui_process_deferred_operations(URUISurfaceHandle surface);
#ifdef __cplusplus
}
#endif
