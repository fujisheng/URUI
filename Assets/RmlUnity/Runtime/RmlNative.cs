using System;
using System.Runtime.InteropServices;
namespace RmlUnity {
 [StructLayout(LayoutKind.Sequential)] public struct RuiFrameStampNative {public ulong epoch,commands,resources,retained_bytes,compare_bytes;public uint count,flags;}
 [StructLayout(LayoutKind.Sequential)] public struct RuiTypedProbeNative {public ulong total_ns,lookup_ns,construct_ns,apply_ns,calls,misses,rejects,clock_pairs;}
 [StructLayout(LayoutKind.Sequential)] public struct RuiLookupProfileNative {public ulong lookup_ns,style_lookup_ns,queries,style_queries,nodes_visited,max_frontier,inline_calls,overflow_calls;}
 [StructLayout(LayoutKind.Sequential)] public struct RuiStyleProbeNative {public ulong lookup_ns,parse_ns,apply_ns,compare_ns,calls,parsed_calls,rejected_calls,properties,comparable,equal_properties,equal_calls,unsupported_calls;}
 [StructLayout(LayoutKind.Sequential)] public struct RuiApiProfileNative {public ulong style_ns,attribute_ns,style_calls,attribute_calls;}
 [StructLayout(LayoutKind.Sequential)] internal struct VertexNative {
  public float x,y,u,v; public byte r,g,b,a;
 }
 [StructLayout(LayoutKind.Sequential)] public struct CommandNative {
  public ulong geometry,texture; public float tx,ty;
  public int clip_enabled,clip_x,clip_y,clip_w,clip_h;
 }
 [StructLayout(LayoutKind.Sequential)] public struct RuiProfileNative {
  public ulong text_ns,update_ns,render_ns,geometry_compile_ns,command_copy_ns,geometry_copy_ns;
  public ulong text_settext_ns,text_setinnerrml_ns,text_lookup_ns;
  public uint text_calls,geometry_compiles,geometry_vertices,geometry_indices,text_fast_calls,text_fallback_calls,text_lookups;
 }
 internal static class RmlNative {
  [DllImport("RmlUnityBridge",CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_bridge_abi_version();
  [DllImport("RmlUnityBridge",CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_element_count();
  const string Dll="RmlUnityBridge";
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_create(int w,int h,[MarshalAs(UnmanagedType.LPUTF8Str)]string font);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_destroy();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_load_document([MarshalAs(UnmanagedType.LPUTF8Str)]string rml);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_unload_document();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_register_rgba_texture([MarshalAs(UnmanagedType.LPUTF8Str)]string source,int width,int height,[In]byte[] rgba,int byteCount,out ulong handle);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_unregister_texture([MarshalAs(UnmanagedType.LPUTF8Str)]string source);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_texture_lifecycle_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_touch_event(int phase,ulong pointerId,float x,float y);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_hit_test_id(float x,float y,[Out]byte[] id,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_touch_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_element_exists([MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_get_element_box([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[Out]float[] values);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_set_attribute([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_set_style([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_set_translate_scale_2d([MarshalAs(UnmanagedType.LPUTF8Str)]string id,float x,float y,float sx,float sy);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_set_inner_rml([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string markup);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_append_element([MarshalAs(UnmanagedType.LPUTF8Str)]string parent,[MarshalAs(UnmanagedType.LPUTF8Str)]string tag,[MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_remove_element([MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_dom_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_get_command_transforms([Out]float[] transforms,int floatCapacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_transform_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_resize(int w,int h);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_mouse_move(int x,int y);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_mouse_button(int b,int down);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_mouse_wheel(float dy);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_update();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_render();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_set_text([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern float rui_get_scroll_top([MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_set_pixel_style([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string property,float pixels);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_command_count();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_get_commands([Out] CommandNative[] commands,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_geometry_exists(ulong id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_texture_exists(ulong id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_get_geometry_info(ulong id,out int v,out int i);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_copy_geometry(ulong id,[Out]VertexNative[] v,int vc,[Out]int[] i,int ic);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_get_texture_info(ulong id,out int w,out int h,out int bytes);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_copy_texture(ulong id,[Out]byte[] data,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_pop_event([Out]byte[] data,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr rui_last_error();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_profile_take(out RuiProfileNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_api_profile_enable(int enabled);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_api_profile_take(out RuiApiProfileNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_render_plan_version();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_render_plan_enable(int enabled);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_render_plan_stamp(out RuiFrameStampNative stamp);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern ulong rui_element_ref_acquire([MarshalAs(UnmanagedType.LPUTF8Str)] string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_element_ref_valid(ulong token);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_element_ref_release(ulong token);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_element_ref_count();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_element_ref_set_translate_scale_2d(ulong token,float x,float y,float sx,float sy);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_typed_probe_mode(int mode);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_typed_probe_take(out RuiTypedProbeNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_style_probe_mode(int mode);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_style_probe_take(out RuiStyleProbeNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_lookup_profile_enable(int mode);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_lookup_profile_take(out RuiLookupProfileNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_lookup_inline_enable(int enabled);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_lookup_queue_selftest();
  internal static string LastError(){var p=rui_last_error();return p==IntPtr.Zero?"":Marshal.PtrToStringAnsi(p);}
 }
}
