#pragma once
#include <stdint.h>
typedef uint64_t UruiDocumentHandle;
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
#include "element_runtime.h"
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
        // Command protocol2: bit0 scissor, bit1 repeat X, bit2 repeat Y.
        int flags, clip_x, clip_y, clip_w, clip_h;
    } UruiCommand;
    typedef struct
    {
        uint64_t shader, element;
        int command_index, reserved;
    } UruiPaintCommand;
    typedef struct
    {
        int command_index, kind;
        uint64_t source, target, mask;
        int blend, flags, clip_x, clip_y, clip_w, clip_h;
    } UruiLayerCommand;
    // Bridge ABI 9 adds the leased universal render-plan protocol; element event v2 remains 80 bytes.
    URUI_API int urui_bridge_abi_version(void);
    URUI_API int urui_command_protocol_version(void);
    // Surface bootstrap only: family0 serif,1 sans-serif,2 monospace; style0 normal,1 italic.
    URUI_API int urui_surface_set_default_font_face(int family, const uint8_t* bytes, int length, int face_index, int style, int weight);
    // 隔离作者校验；不得占用已有 context。0=通过，正值=诊断数，-2=已有活动 context，-3=缓冲区不足。
    URUI_API int urui_validate_stylesheet(const char* uri, const char* source, char* output, int capacity, int* required);
    // Texture protocol v1; ABI4 preserves the existing vertex/command layouts.
    URUI_API int urui_texture_protocol_version(void);
    // 独立同步图片解析协议；失败被 URUI 缓存到 context 结束，不表示异步 pending。
    // 成功返回高位唯一 resource_id 与尺寸；每次成功对应一次 release（包括无效响应）。
    // 回调中不能重入文档 API；destroy/unload 延迟至 process_deferred_operations。
    typedef int (*UruiImageResolver)(UruiDocumentHandle document, const char* source, uint64_t* resource_id, uint64_t* source_identity, int* width, int* height);
    typedef void (*UruiImageRelease)(uint64_t resource_id);
    URUI_API int urui_image_resolver_version(void);
    // context 线程、未加载文档且尚无动态图片时配置；两个回调必须同时为空或非空。
    URUI_API int urui_set_image_resolver(UruiImageResolver resolve, UruiImageRelease release);
    URUI_API int urui_ui_clock_api_version(void);
    URUI_API int urui_set_ui_time(double now);
    URUI_API int urui_create(int width, int height);
    URUI_API void urui_destroy(void);
    // Surface-wide hit/focus metadata; no implicit document is selected.
    URUI_API UruiDocumentHandle urui_surface_element_owner(uint64_t token);
    URUI_API UruiDocumentHandle urui_surface_focused_document(void);
    URUI_API uint64_t urui_surface_pick(float x, float y, UruiDocumentHandle* owner);
    // A pending identity owns staged document state before HTML is published.
    // Result codes: 1 success, -1 arguments/ownership, -2 expired, -3 thread,
    // -4 budget, -5 callback/reentrancy, -8 internal, -9 strict HTML validation.
    URUI_API int urui_document_create(UruiDocumentHandle* result);
    URUI_API int urui_document_load_html(UruiDocumentHandle owner, const char* html, const char* source_url);
    // 源文件只在同步编译调用内借用；资产保存类型化结构，不含原生地址。
    typedef struct { const char* uri; const uint8_t* bytes; int length; } UruiCompileResource;
    // 1 成功；-3 输出容量不足（required 为所需字节数）；其余遵循文档错误约定。
    URUI_API int urui_compile_document(const char* html, const char* source_url, int fragment,
        const UruiCompileResource* resources, int resource_count, uint8_t* output, int capacity, int* required);
    URUI_API int urui_document_load_compiled(UruiDocumentHandle owner, const uint8_t* bytes, int length);
    URUI_API int urui_compile_template(const uint8_t* parent, int parent_length, const char* html, const char* source_url,
        uint8_t* output, int capacity, int* required);
    URUI_API int urui_element_begin_compiled_template(UruiDocumentHandle owner, uint64_t element, const uint8_t* bytes, int length);
    typedef struct {
        uint64_t identity, revision;
        uint32_t local_id, end;
        int flags, root_match;
    } UruiCompiledNodeInfo;
    // flags: bit0 拓扑有效，bit1 id 索引有效，bit2 class 索引有效，bit3 动态片段宿主。
    // query_kind: 0 只读元数据，1 id，2 class；root_match 仅用于动态宿主自身。
    URUI_API int urui_element_compiled_info(UruiDocumentHandle owner, uint64_t element, int query_kind, const char* key, UruiCompiledNodeInfo* info);
    URUI_API int urui_element_compiled_tokens(UruiDocumentHandle owner, uint64_t element, uint64_t identity, uint64_t revision,
        uint64_t* output, int capacity);
    URUI_API int urui_compiled_document_metadata(const uint8_t* bytes, int length, char* output, int capacity, int* required);
    URUI_API int urui_document_close(UruiDocumentHandle owner);
    URUI_API int urui_document_set_presentation(UruiDocumentHandle owner, int layer, int order, int visible, int modal);
    URUI_API uint64_t urui_document_root(UruiDocumentHandle owner);
    URUI_API uint64_t urui_document_body(UruiDocumentHandle owner);
    URUI_API int urui_element_set_inner_html(UruiDocumentHandle owner, uint64_t element, const char* html, const char* source_url);
    URUI_API int urui_element_begin_inner_html(UruiDocumentHandle owner, uint64_t element, const char* html, const char* source_url);
    URUI_API int urui_element_end_inner_html(UruiDocumentHandle owner, uint64_t element, int commit);
    // 追加独立资源中的 CSS，不生成或修改 HTML；仅文档所属线程且不在回调中可调用。
    // before_author: 0追加覆盖；1作为默认规则置于作者样式之前。
    URUI_API int urui_document_add_stylesheet(UruiDocumentHandle document_handle, const char* css, const char* source_url, int before_author);
    URUI_API void urui_resize(int width, int height);
    // Return UTF-8 byte count, 0 for no identified hit, negative required capacity.
    URUI_API int urui_hit_test_id(UruiDocumentHandle document_handle, float x, float y, char* out, int capacity);
    // ID lookup follows URUI GetElementById (first match). Mutations return
    // 1 success, 0 missing, negative invalid input or rejected property.
    URUI_API int urui_element_exists(UruiDocumentHandle document_handle, const char* id);
    // Absolute border-box x,y,w,h followed by clientWidth,scrollWidth.
    URUI_API int urui_get_element_box(UruiDocumentHandle document_handle, const char* id, float* out_six);
    URUI_API int urui_set_attribute(UruiDocumentHandle document_handle, const char* id, const char* name, const char* value);
    URUI_API int urui_set_style(UruiDocumentHandle document_handle, const char* id, const char* name, const char* value);
    // Equivalent to translate(xpx,ypx) scale(sx,sy), preserving primitive order,
    // transform-origin and normal dirty semantics. Finite zero/negative scale
    // supported. Identity is NOT transform:none. Same return codes as SetStyle.
    URUI_API int urui_set_translate_scale_2d(UruiDocumentHandle document_handle, const char* id, float x, float y, float sx, float sy);
    URUI_API int urui_set_inner_html(UruiDocumentHandle document_handle, const char* id, const char* markup);
    URUI_API int urui_append_element(UruiDocumentHandle document_handle, const char* parent_id, const char* tag, const char* id);
    // Diagnostic actual DOM child oracle, all DOM children in sibling order.
    // count returns -1 for invalid/missing; id returns length, -required capacity,
    // or -1 invalid/missing/index. No payload written on failure.
    URUI_API int urui_get_child_count(UruiDocumentHandle document_handle, const char* id);
    URUI_API int urui_get_child_id_utf8(UruiDocumentHandle document_handle, const char* id, int index, char* out, int capacity);
    URUI_API int urui_reorder_element_to_end(UruiDocumentHandle document_handle, const char* id);
    URUI_API int urui_remove_element(UruiDocumentHandle document_handle, const char* id);
    // One column-major 4x4 transform per render command. Window-space scissor is
    // applied after the transform. UruiCommand is 48 bytes.
    URUI_API int urui_get_command_transforms(float* out, int float_capacity);
    URUI_API void urui_update(void);
    // 同步已经更新的布局/变换，不录制绘制命令。仅context线程、回调外；1成功，负值拒绝。
    URUI_API int urui_synchronize_geometry(void);
    URUI_API void urui_render(void);
    // Read actual single text child, or empty element. required includes UTF8 NUL.
    // 1 success, 0 missing document/element, -1 invalid arguments, -2 structured
    // children, -3 insufficient capacity/probe. Payload unchanged on every failure.
    // Synchronous context-thread call, no borrowed pointer escapes.
    URUI_API int urui_get_attribute_utf8(UruiDocumentHandle document_handle, const char* id, const char* name, char* out, int capacity, int* required);
    URUI_API int urui_get_text_utf8(UruiDocumentHandle document_handle, const char* id, char* out, int capacity, int* required);
    URUI_API void urui_set_text(UruiDocumentHandle document_handle, const char* id, const char* text);
    URUI_API float urui_get_scroll_top(UruiDocumentHandle document_handle, const char* id);
    URUI_API int urui_set_pixel_style(UruiDocumentHandle document_handle, const char* id, const char* property, float pixels);
    URUI_API int urui_element_count(UruiDocumentHandle document_handle);
    URUI_API int urui_command_count(void);
    URUI_API int urui_get_commands(UruiCommand* out, int capacity);
    // Ordered external paint metadata. Shader commands retain the regular geometry/transform/clip ABI.
    URUI_API int urui_get_paint_commands(UruiPaintCommand* out, int capacity);
    // Effect v4: reserved=2 marks a proven leaf image with an ordered standard CSS color chain.
    typedef struct { int kind; float value; } UruiColorFilter;
    // kind: grayscale=0, saturate=1, brightness=2, contrast=3, opacity=4. Query count with null/0.
    URUI_API int urui_paint_color_filters(uint64_t shader, UruiColorFilter* out, int capacity);
    // Before creating documents only. An unknown managed shader contract must leave this disabled.
    URUI_API int urui_set_direct_color_filters(int enabled);
    // Effect v5: captured subtree margins in Surface CSS pixels, applied to actual transformed ink.
    URUI_API int urui_element_effect_expansion(uint64_t filter, float left, float top, float right, float bottom);
    // Conservative output coverage transforms in document coordinates, applied to actual subtree ink.
    URUI_API int urui_element_effect_transforms(uint64_t filter, const float* matrices, int count);
    // Effect v6: content rect + element homography + root content rect + root homography (40 floats).
    // Root references are resolved only inside this document and cached by DOM structure identity.
    URUI_API int urui_effect_layout(UruiDocumentHandle document_handle, uint64_t element, float* out, int capacity, uint64_t* root);
    URUI_API int urui_element_effect_state(UruiDocumentHandle document_handle,uint64_t element,int capture_required,int input_disabled);
    // Before loading HTML: direct image rendering is allowed only when this computed property equals direct_value.
    URUI_API int urui_document_effect_image_condition(UruiDocumentHandle document_handle, const char* effect, const char* property, const char* direct_value);
    // kinds: 1 push, 2 pop/release source, 3 snapshot mask, 4 composite, 5 release mask.
    URUI_API int urui_get_layer_commands(UruiLayerCommand* out, int capacity);
    typedef struct { uint64_t handle, element; float value, sigma, offset_x, offset_y, red, green, blue, alpha; } UruiFilter;
    typedef struct { uint64_t layer; int x, y, width, height; } UruiLayerRegion;
    typedef struct { uint64_t filter, layer; int x, y, width, height; } UruiEffectInputRegion;
    URUI_API int urui_get_effect_input_regions(UruiEffectInputRegion* regions, int capacity);
    URUI_API int urui_animation_api_version(void);
    // Sequence v1. Capture=2 freezes baseline without publication, validate=0, apply=1, clear-values=3.
    // Empty samples release this owner's layer. No time advancement or animation events.
    typedef struct UruiAnimationSample { const char* name; const char* easing; float progress; } UruiAnimationSample;
    URUI_API int urui_sequence_api_version(void);
// Current-document style/layout synchronization only: no clock, animation advance or input/business update.
URUI_API int urui_sequence_layout(UruiDocumentHandle document_handle);
URUI_API int urui_keyframes_count(UruiDocumentHandle document_handle, uint64_t token, const char* name);
    URUI_API int urui_keyframes_properties(UruiDocumentHandle document_handle, uint64_t token, const char* name, char* output, int capacity, int* required);
    URUI_API int urui_sequence_sample(UruiDocumentHandle document_handle, uint64_t token, uint64_t owner, const UruiAnimationSample* samples, int count, int mode);
    URUI_API uint64_t urui_animation_next_identity(void);
    URUI_API int urui_animation_current_event(UruiDocumentHandle document_handle, char* name,int capacity,uint64_t* identity,int* iterations);
    URUI_API int urui_effect_api_version(void);
    URUI_API int urui_effect_ink_bounds(int command_index,float x,float y,float width,float height);
    URUI_API int urui_element_visible_in_hierarchy(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_filter_api_version(void);
    URUI_API UruiDocumentHandle urui_filter_document(uint64_t handle);
    URUI_API int urui_filter_info(uint64_t handle, UruiFilter* result, char* name, int capacity);
    URUI_API int urui_get_layer_regions(UruiLayerRegion* regions, int capacity);

    // Returns required UTF-8 size including NUL, 0 for expired shader, -1 for invalid/thread call.
    URUI_API int urui_paint_shader_name(uint64_t shader, char* out, int capacity);
    URUI_API int urui_get_geometry_info(uint64_t id, int* v, int* i);
    URUI_API int urui_copy_geometry(uint64_t id, UruiVertex* v, int vc, int* i, int ic);
    // kind: 0 missing, 1 owned RGBA/generated, 2 borrowed external metadata.
    URUI_API int urui_get_texture_kind(uint64_t id);
    // Borrowed metadata reports its dimensions and zero pixel bytes.
    URUI_API int urui_get_texture_info(uint64_t id, int* w, int* h, int* bytes);
    // Borrowed textures return -2 with an explicit error; no output is written.
    URUI_API int urui_copy_texture(uint64_t id, uint8_t* out, int capacity);
    URUI_API int urui_geometry_exists(uint64_t id);
    URUI_API int urui_texture_exists(uint64_t id);
    URUI_API int urui_pop_event(UruiDocumentHandle document_handle, char* out, int capacity);
    URUI_API const char* urui_last_error(void);
    // Document-owned element identities. Query is self-inclusive DOM-only DFS.
    // Query returns required count without writes when capacity is insufficient.
    URUI_API uint64_t urui_element_create(UruiDocumentHandle document_handle, const char* tag);
    // 深拷贝作者子树，返回由文档持有的 detached 根；不解析生成的标记字符串。
    URUI_API uint64_t urui_element_clone(UruiDocumentHandle document_handle, uint64_t token);
    // 类型化动态数值：unit 0=number,1=px,2=percent。不会构造或解析样式文本。
    URUI_API int urui_element_number(UruiDocumentHandle document_handle, uint64_t token, const char* property, float value, int unit);
    URUI_API int urui_element_color(UruiDocumentHandle document_handle, uint64_t token, const char* property, int red, int green, int blue, int alpha);
    // 12 floats: translation xyz, rotation axis xyz + degrees, scale xyz, origin xy percent.
    URUI_API int urui_element_trs(UruiDocumentHandle document_handle, uint64_t token, const float* values, int count);
    // parent/find return UINT64_MAX for failure, zero for no parent/no match.
    // UINT64_MAX is reserved and is never a node identity.
    URUI_API uint64_t urui_element_parent(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_element_child_count(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API uint64_t urui_element_child_at(UruiDocumentHandle document_handle, uint64_t token, int index);
    URUI_API uint64_t urui_element_find(UruiDocumentHandle document_handle, uint64_t token, const char* name, const char* class_name);
    URUI_API int urui_element_query(UruiDocumentHandle document_handle, uint64_t token, const char* name, const char* class_name, uint64_t* out, int capacity);
    // read fields: 0=name, 1=tag, 2=plain text, 3=attribute, 4=inner HTML. UTF8 required includes NUL.
    // 1 success; 0 absent attribute; -1 invalid; -2 structured text; -3 capacity probe.
    URUI_API int urui_element_read(UruiDocumentHandle document_handle, uint64_t token, int field, const char* name, char* out, int capacity, int* required);
    // write fields: 0=name, 1=plain text, 2=attribute, 3=attached inner HTML, 4=inline style.
    URUI_API int urui_element_write(UruiDocumentHandle document_handle, uint64_t token, int field, const char* name, const char* value);
    // erase fields: 0=attribute, 1=inline style. Class action: -1=query, 0=remove, 1=add.
    URUI_API int urui_element_erase(UruiDocumentHandle document_handle, uint64_t token, int field, const char* name);
    URUI_API int urui_element_class(UruiDocumentHandle document_handle, uint64_t token, const char* name, int action);
    // Insert validates before moving; -1 index means append. Detach/Clear retain nodes.
    URUI_API int urui_element_insert(UruiDocumentHandle document_handle, uint64_t parent, uint64_t child, int index);
    URUI_API int urui_element_detach(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_element_clear(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_element_destroy(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_element_attached(UruiDocumentHandle document_handle, uint64_t token);
    // Actual weak click target, never an ID lookup. Removed/unloaded targets are discarded.
    URUI_API int urui_element_pop_click(UruiDocumentHandle document_handle, uint64_t* target);

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
    URUI_API int urui_render_plan_stamp(UruiFrameStamp* out);
    // Explicit weak OBJECT references, not live ID selectors. Acquire uses fresh ID
    // semantics; rename does not retarget. Unload/destroy invalidates all tokens.
    // Single context thread only (-3 for foreign-thread operations). Managed nodes
    // retain canonical identities until destruction/unload; raw release invalidates a token.
    // Budget: 65536 outstanding tokens. Invalid/stale writes return 0, bad numeric -1.
    URUI_API uint64_t urui_element_ref_acquire(UruiDocumentHandle document_handle, const char* id);
    URUI_API int urui_element_ref_valid(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_element_ref_release(UruiDocumentHandle document_handle, uint64_t token);
    URUI_API int urui_element_ref_count(UruiDocumentHandle document_handle);
    URUI_API int urui_element_ref_set_translate_scale_2d(UruiDocumentHandle document_handle, uint64_t token, float x, float y, float sx, float sy);
#ifdef __cplusplus
}
#endif

#include "document_resources.h"

#if defined(URUI_DEBUG)
#include "Debug/urui_debug.h"
#endif

#include "authoring.h"

#include "render_plan_protocol.h"
