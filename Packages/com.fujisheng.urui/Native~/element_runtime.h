#pragma once

// Additive element protocol. Existing ABI3 data layouts and entry points remain intact.
#ifdef __cplusplus
extern "C"
{
#endif
    URUI_API int urui_element_api_version(void);
    // UTF-8 snapshots: 1 success, 0 absent, -3 probe/insufficient capacity.
    URUI_API int urui_element_style_read(uint64_t token, int local, const char* property, char* out, int capacity, int* required);
    // 16 floats: layout, local content rect, transformed world AABB, scroll x/y/width/height.
    URUI_API int urui_element_box(uint64_t token, float* out, int capacity);
    // Content viewport after padding and scrollbar exclusion: x/y/width/height.
    URUI_API int urui_element_viewport(uint64_t token, float* out, int capacity);
    URUI_API int urui_element_point(uint64_t token, int to_world, float x, float y, float* out);
    URUI_API uint64_t urui_element_pick(float x, float y);
    URUI_API int urui_element_focus(uint64_t token, int focus);
    URUI_API uint64_t urui_focused_element(void);
    URUI_API int urui_element_scroll(uint64_t token, float x, float y);
    URUI_API int urui_control_value_read(uint64_t token, char* out, int capacity, int* required);
    URUI_API int urui_control_value_write(uint64_t token, const char* value);
    URUI_API int urui_select_option_count(uint64_t token);
    URUI_API int urui_select_option_read(uint64_t token, int index, char* out, int capacity, int* required);
    URUI_API int urui_select_option_add(uint64_t token, const char* value);
    URUI_API int urui_select_options_clear(uint64_t token);
    URUI_API int urui_select_selection(uint64_t token, int index);
    typedef struct UruiElementEvent
    {
        uint64_t target, current;
        int32_t type, phase, pointer, button, buttons, modifiers, key, kind;
        float x, y, dx, dy;
        const char* text;
    } UruiElementEvent;
    typedef int (*UruiElementCallback)(const UruiElementEvent* event);
    URUI_API int urui_set_event_callback(UruiElementCallback callback);
    URUI_API int urui_document_listen(void);
    URUI_API int urui_element_listen(uint64_t token, int type, int capture, int enabled);
    URUI_API int urui_pointer_event(int pointer, int kind, int phase, float x, float y, int button, int buttons, float dx, float dy, int modifiers);
    URUI_API int urui_pointer_capture(int pointer, uint64_t token);
    URUI_API uint64_t urui_pointer_captured(int pointer);
    URUI_API int urui_input_reset(void);
    URUI_API int urui_cancel_pointers(uint64_t owner);
    URUI_API int urui_key_event(int key, int pressed, int modifiers);
    URUI_API int urui_text_input(const char* text);
    // operation 0 preview, 1 commit, 2 cancel; text is UTF-8.
    URUI_API int urui_text_composition(const char* text, int operation);
    URUI_API int urui_text_selection(int* start, int* end, int write);
    URUI_API int urui_clipboard_write(const char* text);
    URUI_API int urui_clipboard_read(char* out, int capacity, int* required);
    // Called outside RmlUi dispatch: reclaim detached listeners/deferred context teardown.
    URUI_API int urui_process_deferred_operations(void);
#ifdef __cplusplus
}
#endif
