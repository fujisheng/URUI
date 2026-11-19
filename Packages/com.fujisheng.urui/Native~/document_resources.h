#pragma once

#ifdef __cplusplus
extern "C"
{
#endif
    URUI_API int urui_document_resource_api_version(void);
    URUI_API int urui_resolve_resource_url(const char* base, const char* source, char* output, int capacity);
    // A hidden managed candidate may replace a prior native identity without
    // claiming another logical document's URI namespace.
    URUI_API int urui_document_set_replacement(URUIDocumentHandle candidate, URUIDocumentHandle previous);
    // Sources are canonical URLs; byte buffers are copied before returning.
    URUI_API int urui_document_register_resource(URUIDocumentHandle owner, const char* source, const uint8_t* bytes,
                                                 int length);
    // 输入是未预乘 RGBA8；复制和绘制使用按行排列的预乘 RGBA8，与纹理协议保持一致。
    URUI_API int urui_document_register_rgba_texture(URUIDocumentHandle owner, const char* source, int width,
                                                     int height, const uint8_t* pixels, int length, uint64_t* handle);
    URUI_API int urui_document_register_external_texture(URUIDocumentHandle owner, const char* source, int width,
                                                         int height, uint64_t resource);
    // Immutable top-left source-pixel region. Source texture must already be registered to this generation.
    URUI_API int urui_document_register_sprite(URUIDocumentHandle owner, const char* name, const char* image_source,
                                               float x, float y, float width, float height, float display_scale);
    URUI_API int urui_document_unregister_texture(URUIDocumentHandle owner, const char* source);
    URUI_API int urui_document_preload_image(URUIDocumentHandle owner, const char* source, int* width, int* height);
    URUI_API int urui_document_release_image(URUIDocumentHandle owner, const char* source);
#ifdef __cplusplus
}
#endif
