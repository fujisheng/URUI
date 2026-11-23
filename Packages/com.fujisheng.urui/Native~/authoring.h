#pragma once

#ifdef __cplusplus
extern "C"
{
#endif
    // Stateless strict HTML parsing and owned JSON diagnostics/node inventory.
    URUI_API int urui_validate_html(const char* source, const char* source_url, int fragment, char* output,
                                    int capacity, int* required);
    URUI_API int urui_authoring_protocol_version(void);
#ifdef __cplusplus
}
#endif
