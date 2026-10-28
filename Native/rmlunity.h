#pragma once
#include <stdint.h>
#if defined(_WIN32)
# if defined(RMLUNITY_EXPORTS)
#  define RUI_API __declspec(dllexport)
# else
#  define RUI_API __declspec(dllimport)
# endif
#else
# define RUI_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct { float x,y,u,v; uint8_t r,g,b,a; } RuiVertex;
typedef struct { uint64_t geometry,texture; float tx,ty; int clip_enabled,clip_x,clip_y,clip_w,clip_h; } RuiCommand;
RUI_API int rui_create(int width,int height,const char* font_path);
RUI_API void rui_destroy(void);
RUI_API int rui_load_document(const char* rml);
RUI_API void rui_resize(int width,int height);
RUI_API void rui_mouse_move(int x,int y);
RUI_API void rui_mouse_button(int button,int pressed);
RUI_API void rui_mouse_wheel(float dy);
RUI_API void rui_update(void);
RUI_API void rui_render(void);
RUI_API void rui_set_text(const char* id,const char* text);
RUI_API int rui_command_count(void);
RUI_API int rui_get_commands(RuiCommand* out,int capacity);
RUI_API int rui_get_geometry_info(uint64_t id,int* v,int* i);
RUI_API int rui_copy_geometry(uint64_t id,RuiVertex* v,int vc,int* i,int ic);
RUI_API int rui_get_texture_info(uint64_t id,int* w,int* h,int* bytes);
RUI_API int rui_copy_texture(uint64_t id,uint8_t* out,int capacity);
RUI_API int rui_geometry_exists(uint64_t id);
RUI_API int rui_texture_exists(uint64_t id);
RUI_API int rui_pop_event(char* out,int capacity);
RUI_API const char* rui_last_error(void);
#ifdef __cplusplus
}
#endif
