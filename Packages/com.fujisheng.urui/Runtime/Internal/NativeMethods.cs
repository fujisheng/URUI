using System;
using System.Runtime.InteropServices;
namespace URUI.Internal {
 [StructLayout(LayoutKind.Sequential)] internal struct UruiFrameStampNative {public ulong epoch,commands,resources,retained_bytes,compare_bytes;public uint count,flags;}
 [StructLayout(LayoutKind.Sequential)] internal struct UruiTypedProbeNative {public ulong total_ns,lookup_ns,construct_ns,apply_ns,calls,misses,rejects,clock_pairs;}
 [StructLayout(LayoutKind.Sequential)] internal struct UruiLookupProfileNative {public ulong lookup_ns,style_lookup_ns,queries,style_queries,nodes_visited,max_frontier,inline_calls,overflow_calls;}
 [StructLayout(LayoutKind.Sequential)] internal struct UruiStyleProbeNative {public ulong lookup_ns,parse_ns,apply_ns,compare_ns,calls,parsed_calls,rejected_calls,properties,comparable,equal_properties,equal_calls,unsupported_calls;}
 [StructLayout(LayoutKind.Sequential)] internal struct UruiApiProfileNative {public ulong style_ns,attribute_ns,style_calls,attribute_calls;}
 [StructLayout(LayoutKind.Sequential)] internal struct VertexNative {
  public float x,y,u,v; public byte r,g,b,a;
 }
 [StructLayout(LayoutKind.Sequential)] internal struct CommandNative {
  public ulong geometry,texture; public float tx,ty;
  public int clip_enabled,clip_x,clip_y,clip_w,clip_h;
 }
 [StructLayout(LayoutKind.Sequential)] internal struct UruiProfileNative {
  public ulong text_ns,update_ns,render_ns,geometry_compile_ns,command_copy_ns,geometry_copy_ns;
  public ulong text_settext_ns,text_setinnerrml_ns,text_lookup_ns;
  public uint text_calls,geometry_compiles,geometry_vertices,geometry_indices,text_fast_calls,text_fallback_calls,text_lookups;
 }
 internal static class NativeMethods {
  [DllImport(NativeLibrary.Name,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_bridge_abi_version();
  [DllImport(NativeLibrary.Name,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_element_count();
  const string Dll=NativeLibrary.Name;
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_create(int w,int h,[MarshalAs(UnmanagedType.LPUTF8Str)]string font);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_destroy();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_load_document([MarshalAs(UnmanagedType.LPUTF8Str)]string rml);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_unload_document();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_register_rgba_texture([MarshalAs(UnmanagedType.LPUTF8Str)]string source,int width,int height,[In]byte[] rgba,int byteCount,out ulong handle);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_unregister_texture([MarshalAs(UnmanagedType.LPUTF8Str)]string source);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_texture_lifecycle_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_touch_event(int phase,ulong pointerId,float x,float y);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_hit_test_id(float x,float y,[Out]byte[] id,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_touch_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_element_exists([MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_get_element_box([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[Out]float[] values);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_set_attribute([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_set_style([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_set_translate_scale_2d([MarshalAs(UnmanagedType.LPUTF8Str)]string id,float x,float y,float sx,float sy);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_set_inner_rml([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string markup);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_append_element([MarshalAs(UnmanagedType.LPUTF8Str)]string parent,[MarshalAs(UnmanagedType.LPUTF8Str)]string tag,[MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_remove_element([MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_dom_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_get_command_transforms([Out]float[] transforms,int floatCapacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_transform_selftest();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_resize(int w,int h);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_mouse_move(int x,int y);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_mouse_button(int b,int down);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_mouse_wheel(float dy);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_update();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_render();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_set_text([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern float urui_get_scroll_top([MarshalAs(UnmanagedType.LPUTF8Str)]string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_set_pixel_style([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string property,float pixels);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_command_count();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_get_commands([Out] CommandNative[] commands,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_geometry_exists(ulong id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_texture_exists(ulong id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_get_geometry_info(ulong id,out int v,out int i);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_copy_geometry(ulong id,[Out]VertexNative[] v,int vc,[Out]int[] i,int ic);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_get_texture_info(ulong id,out int w,out int h,out int bytes);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_copy_texture(ulong id,[Out]byte[] data,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_pop_event([Out]byte[] data,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr urui_last_error();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_profile_take(out UruiProfileNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void urui_api_profile_enable(int enabled);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_api_profile_take(out UruiApiProfileNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_render_plan_version();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_render_plan_enable(int enabled);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_render_plan_stamp(out UruiFrameStampNative stamp);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern ulong urui_element_ref_acquire([MarshalAs(UnmanagedType.LPUTF8Str)] string id);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_element_ref_valid(ulong token);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_element_ref_release(ulong token);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_element_ref_count();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_element_ref_set_translate_scale_2d(ulong token,float x,float y,float sx,float sy);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_typed_probe_mode(int mode);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_typed_probe_take(out UruiTypedProbeNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_style_probe_mode(int mode);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_style_probe_take(out UruiStyleProbeNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_lookup_profile_enable(int mode);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_lookup_profile_take(out UruiLookupProfileNative profile);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_lookup_inline_enable(int enabled);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_lookup_queue_selftest();
  internal static string LastError(){var p=urui_last_error();return p==IntPtr.Zero?"":Marshal.PtrToStringAnsi(p);}
 }
}
