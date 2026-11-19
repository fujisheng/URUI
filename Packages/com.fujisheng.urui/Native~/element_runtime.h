#pragma once

// Document-owned element protocol v3. The event packet contains its document identity.
#ifdef __cplusplus
extern "C"
{
#endif
    URUI_API int urui_element_api_version(void);
    // Process-wide invalidation revision, including native destruction and token release.
    // Atomic read; valid during teardown and from any thread. No element pointers escape.
    URUI_API uint64_t urui_element_ref_revision(void);
    // 当前布局/挂载及 token 有效性的变化证明；仅在 Context 所属线程读取。
    // 输出16字节，无回调；失败时清零。1成功，-1空输出，-2无上下文，-3错误线程。
    typedef struct URUIElementRefreshStamp
    {
        uint64_t mutation;
        uint64_t references;
    } URUIElementRefreshStamp;
    URUI_API int urui_element_refresh_stamp(URUIElementRefreshStamp* out);
    // 独立的附加变换协议，不改变既有 ABI/Element 数据布局。
    URUI_API int urui_transform_api_version(void);
    // 32 个列主序浮点数：本地变换矩阵、仅显式旋转原语的矩阵。
    // 1 成功；2 含无法区分旋转/缩放的 matrix 原语（第一个矩阵仍有效）。
    URUI_API int urui_element_local_transform(URUIDocumentHandle document_handle, uint64_t token, float* out,
                                              int capacity);
    // UTF-8 snapshots: 1 success, 0 absent, -3 probe/insufficient capacity.
    URUI_API int urui_element_style_read(URUIDocumentHandle document_handle, uint64_t token, int local,
                                         const char* property, char* out, int capacity, int* required);
    // 16 floats: layout, local content rect, transformed world AABB, scroll x/y/width/height.
    URUI_API int urui_element_box(URUIDocumentHandle document_handle, uint64_t token, float* out, int capacity);
    // 只读刷新：有效位1、挂载位2；out为相对布局的x/y/width/height，不计算世界边界。
    URUI_API int urui_element_refresh_state(URUIDocumentHandle document_handle, uint64_t token, float* out,
                                            int capacity);
    typedef struct URUIElementRefreshState
    {
        int32_t state;
        float layout[4];
    } URUIElementRefreshState;
    // Synchronous read-only batch, no callbacks. 1 success; invalid tokens have state=0.
    // Count <=65536; output zeroed on context/document/failure after argument validation.
    URUI_API int urui_element_refresh_states(URUIDocumentHandle owner, const uint64_t* tokens,
                                             URUIElementRefreshState* out, uint32_t count);
    // Content viewport after padding and scrollbar exclusion: x/y/width/height.
    URUI_API int urui_element_viewport(URUIDocumentHandle document_handle, uint64_t token, float* out, int capacity);
    URUI_API int urui_element_point(URUIDocumentHandle document_handle, uint64_t token, int to_world, float x, float y,
                                    float* out);
    URUI_API uint64_t urui_element_pick(URUIDocumentHandle document_handle, float x, float y);
    URUI_API int urui_element_focus(URUIDocumentHandle document_handle, uint64_t token, int focus);
    URUI_API uint64_t urui_focused_element(URUIDocumentHandle document_handle);
    URUI_API int urui_element_scroll(URUIDocumentHandle document_handle, uint64_t token, float x, float y);
    URUI_API int urui_control_value_read(URUIDocumentHandle document_handle, uint64_t token, char* out, int capacity,
                                         int* required);
    URUI_API int urui_control_value_write(URUIDocumentHandle document_handle, uint64_t token, const char* value);
    typedef struct URUIRangeInfo
    {
        float minimum;
        float maximum;
        float step;
        float step_base;
        int flags;
    } URUIRangeInfo;
    URUI_API int urui_control_range_info(URUIDocumentHandle document_handle, uint64_t token, URUIRangeInfo* info);
    URUI_API int urui_select_option_count(URUIDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_select_option_read(URUIDocumentHandle document_handle, uint64_t token, int index, char* out,
                                         int capacity, int* required);
    URUI_API int urui_select_option_add(URUIDocumentHandle document_handle, uint64_t token, const char* value);
    URUI_API int urui_select_options_clear(URUIDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_select_selection(URUIDocumentHandle document_handle, uint64_t token, int index);
    typedef struct URUIElementEvent
    {
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
    URUI_API int urui_set_event_callback(URUIElementCallback callback);
    URUI_API int urui_document_listen(URUIDocumentHandle document_handle);
    URUI_API int urui_element_event_interest(URUIDocumentHandle owner, uint64_t token, uint32_t mask, uint64_t version);
    URUI_API int urui_element_listen(URUIDocumentHandle document_handle, uint64_t token, int type, int capture,
                                     int enabled);
    URUI_API int urui_pointer_poll(int pointer, int kind, float x, float y, int buttons, int modifiers);
    URUI_API int urui_pointer_event(int pointer, int kind, int phase, float x, float y, int button, int buttons,
                                    float dx, float dy, int modifiers);
    URUI_API int urui_pointer_capture(URUIDocumentHandle document_handle, int pointer, uint64_t token);
    URUI_API uint64_t urui_pointer_captured(URUIDocumentHandle document_handle, int pointer);
    URUI_API int urui_input_reset(void);
    URUI_API int urui_cancel_pointers(URUIDocumentHandle document_handle, uint64_t owner);
    URUI_API int urui_key_event(int key, int pressed, int modifiers);
    URUI_API int urui_text_input(const char* text);
    // operation 0 preview, 1 commit, 2 cancel; text is UTF-8.
    URUI_API int urui_text_composition(const char* text, int operation);
    URUI_API int urui_text_selection(int* start, int* end, int write);
    URUI_API uint64_t urui_clipboard_version();
    URUI_API int urui_clipboard_write(const char* text);
    URUI_API int urui_clipboard_read(char* out, int capacity, int* required);
    // Called outside URUI dispatch: reclaim detached listeners/deferred context teardown.
    URUI_API int urui_process_deferred_operations(void);
#ifdef __cplusplus
}
#endif
