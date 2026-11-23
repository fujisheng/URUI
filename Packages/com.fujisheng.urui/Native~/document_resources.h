#pragma once

#ifdef __cplusplus
extern "C"
{
#endif
    URUI_API int urui_document_resource_api_version(void);
    // Stateless canonical URL resolver. Returns URUI_OK or URUI_ERR_BUFFER_TOO_SMALL with *required.
    URUI_API int urui_resolve_resource_url(const char* base, const char* source, char* output, int capacity,
                                           int* required);
    URUI_API int urui_document_set_replacement(URUISurfaceHandle surface, URUIDocumentHandle candidate,
                                               URUIDocumentHandle previous);
    URUI_API int urui_document_register_resource(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                 const char* source, const uint8_t* bytes, int length);
    URUI_API int urui_document_register_rgba_texture(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                     const char* source, int width, int height, const uint8_t* pixels,
                                                     int length, uint64_t* handle);
    URUI_API int urui_document_register_external_texture(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                         const char* source, int width, int height, uint64_t resource);
    URUI_API int urui_document_register_sprite(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* name,
                                               const char* image_source, float x, float y, float width, float height,
                                               float display_scale);
    URUI_API int urui_document_unregister_texture(URUISurfaceHandle surface, URUIDocumentHandle owner,
                                                  const char* source);
    URUI_API int urui_document_preload_image(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* source,
                                             int* width, int* height);
    URUI_API int urui_document_release_image(URUISurfaceHandle surface, URUIDocumentHandle owner, const char* source);
#ifdef __cplusplus
}
#endif
