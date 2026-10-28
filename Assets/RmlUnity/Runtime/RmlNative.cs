using System;
using System.Runtime.InteropServices;
namespace RmlUnity {
 [StructLayout(LayoutKind.Sequential)] internal struct VertexNative {
  public float x,y,u,v; public byte r,g,b,a;
 }
 [StructLayout(LayoutKind.Sequential)] internal struct CommandNative {
  public ulong geometry,texture; public float tx,ty;
  public int clip_enabled,clip_x,clip_y,clip_w,clip_h;
 }
 internal static class RmlNative {
  const string Dll="RmlUnityBridge";
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_create(int w,int h,[MarshalAs(UnmanagedType.LPUTF8Str)]string font);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_destroy();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int rui_load_document([MarshalAs(UnmanagedType.LPUTF8Str)]string rml);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_resize(int w,int h);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_mouse_move(int x,int y);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_mouse_button(int b,int down);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_mouse_wheel(float dy);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_update();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_render();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void rui_set_text([MarshalAs(UnmanagedType.LPUTF8Str)]string id,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
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
  internal static string LastError(){var p=rui_last_error();return p==IntPtr.Zero?"":Marshal.PtrToStringAnsi(p);}
 }
}
