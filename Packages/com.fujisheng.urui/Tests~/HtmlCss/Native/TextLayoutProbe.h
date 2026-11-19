#pragma once

struct URUIValidationTextLine
{
    float x;
    float y;
    float width;
    float height;
    float baseline;
};
#if defined(_WIN32)
#define URUI_VALIDATION_API __declspec(dllexport)
#else
#define URUI_VALIDATION_API __attribute__((visibility("default")))
#endif

// 仅验证构建链接的旁路模块；不分发到 Runtime 插件或公开托管 API。
extern "C" URUI_VALIDATION_API int urui_validation_text_lines(const char* source_url, const char* id,
                                                              URUIValidationTextLine* output, int capacity);
