#pragma once

// Document-owned element protocol v2. The event packet contains its document identity.
#ifdef __cplusplus
extern "C"
{
#endif
    URUI_API int urui_element_api_version(void);
    // 独立的附加变换协议，不改变既有 ABI/Element 数据布局。
    URUI_API int urui_transform_api_version(void);
    // 32 个列主序浮点数：本地变换矩阵、仅显式旋转原语的矩阵。
    // 1 成功；2 含无法区分旋转/缩放的 matrix 原语（第一个矩阵仍有效）。
    URUI_API int urui_element_local_transform(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity);
    // UTF-8 snapshots: 1 success, 0 absent, -3 probe/insufficient capacity.
    URUI_API int urui_element_style_read(UruiDocumentHandle document_handle, uint64_t token, int local, const char* property, char* out, int capacity, int* required);
    // 16 floats: layout, local content rect, transformed world AABB, scroll x/y/width/height.
    URUI_API int urui_element_box(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity);
    // 只读刷新：有效位1、挂载位2；out为相对布局的x/y/width/height，不计算世界边界。
    URUI_API int urui_element_refresh_state(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity);
    // Content viewport after padding and scrollbar exclusion: x/y/width/height.
    URUI_API int urui_element_viewport(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity);
    URUI_API int urui_element_point(UruiDocumentHandle document_handle, uint64_t token, int to_world, float x, float y, float* out);
    URUI_API uint64_t urui_element_pick(UruiDocumentHandle document_handle, float x, float y);
    URUI_API int urui_element_focus(UruiDocumentHandle document_handle, uint64_t token, int focus);
    URUI_API uint64_t urui_focused_element(UruiDocumentHandle document_handle);
    URUI_API int urui_element_scroll(UruiDocumentHandle document_handle, uint64_t token, float x, float y);
    URUI_API int urui_control_value_read(UruiDocumentHandle document_handle, uint64_t token, char* out, int capacity, int* required);
    URUI_API int urui_control_value_write(UruiDocumentHandle document_handle, uint64_t token, const char* value);
    typedef struct UruiRangeInfo { float minimum, maximum, step, step_base; int flags; } UruiRangeInfo;
    URUI_API int urui_control_range_info(UruiDocumentHandle document_handle, uint64_t token, UruiRangeInfo* info);
    URUI_API int urui_select_option_count(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_select_option_read(UruiDocumentHandle document_handle, uint64_t token, int index, char* out, int capacity, int* required);
    URUI_API int urui_select_option_add(UruiDocumentHandle document_handle, uint64_t token, const char* value);
    URUI_API int urui_select_options_clear(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_select_selection(UruiDocumentHandle document_handle, uint64_t token, int index);
    typedef struct UruiElementEvent
    {
        UruiDocumentHandle document;
        uint64_t target, current;
        int32_t type, phase, pointer, button, buttons, modifiers, key, kind;
        float x, y, dx, dy;
        const char* text;
    } UruiElementEvent;
    typedef int (*UruiElementCallback)(const UruiElementEvent* event);
    URUI_API int urui_set_event_callback(UruiElementCallback callback);
    URUI_API int urui_document_listen(UruiDocumentHandle document_handle);
    URUI_API int urui_element_listen(UruiDocumentHandle document_handle, uint64_t token, int type, int capture, int enabled);
    URUI_API int urui_pointer_event(int pointer, int kind, int phase, float x, float y, int button, int buttons, float dx, float dy, int modifiers);
    URUI_API int urui_pointer_capture(UruiDocumentHandle document_handle, int pointer, uint64_t token);
    URUI_API uint64_t urui_pointer_captured(UruiDocumentHandle document_handle, int pointer);
    URUI_API int urui_input_reset(void);
    URUI_API int urui_cancel_pointers(UruiDocumentHandle document_handle, uint64_t owner);
    URUI_API int urui_key_event(int key, int pressed, int modifiers);
    URUI_API int urui_text_input(const char* text);
    // operation 0 preview, 1 commit, 2 cancel; text is UTF-8.
    URUI_API int urui_text_composition(const char* text, int operation);
    URUI_API int urui_text_selection(int* start, int* end, int write);
    URUI_API int urui_clipboard_write(const char* text);
    URUI_API int urui_clipboard_read(char* out, int capacity, int* required);
    // Called outside URUI dispatch: reclaim detached listeners/deferred context teardown.
    URUI_API int urui_process_deferred_operations(void);
#ifdef __cplusplus
}
#endif
