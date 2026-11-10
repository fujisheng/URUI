#pragma once
#include <stdint.h>
#ifdef __cplusplus
#include <string>
namespace URUI { class Element; }
namespace urui_styles {
using ElementResolver = URUI::Element* (*)(uint64_t, uint64_t);
void Begin(ElementResolver resolver);
void End();
void Prune();
void RecordStringRead();
bool ComputedKeyword(URUI::Element* element, const char* name, std::string& value);
}
extern "C" {
#endif
    typedef struct UruiStyleValue { int32_t name_offset, kind; float x, y, z, w; int32_t text_offset, source_offset; } UruiStyleValue;
    URUI_API int urui_style_api_version(void);
    // kind 位：1 number, 2 已解析 px, 4 RGBA, 8 keyword, 16 image。图像 x=0/1/2 表示 none/url/gradient；文本与来源分别由 offset 指向 names。
    // style v4: kind>>8 为来源：0 initial,1 UA,2 author,3 animation,4 important,5 transition；低八位类型布局不变。
    // -3 容量探测，-4 版本变化；所有值及名称均复制到调用者缓冲区。
    URUI_API int urui_element_style_snapshot(UruiDocumentHandle document_handle, uint64_t token, uint64_t expected_revision, UruiStyleValue* values, int capacity,
        char* names, int names_capacity, uint64_t* revision, int* value_count, int* name_bytes);
    // snapshot build / string read / typed copy / retained nodes.
    URUI_API int urui_style_diagnostics(uint64_t* values, int capacity);
#ifdef __cplusplus
}
#endif
