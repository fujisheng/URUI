using System;
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    /// <summary>
    /// 同步 C ABI 边界。结构字段顺序和宽度必须与 Native~/urui.h 完全一致。
    /// 原生返回的指针只在调用约定允许的窗口内借用，不转交给使用者持有。
    /// </summary>
    internal static partial class NativeMethods
    {
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_bridge_abi_version();

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_count();

        const string Dll = NativeLibrary.Name;

        // 字符串固定按 UTF-8 传入，数组容量由调用者提供；原生端不接管托管资源所有权。
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_create(int w, int h, [MarshalAs(UnmanagedType.LPUTF8Str)] string font);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_destroy();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_load_document([MarshalAs(UnmanagedType.LPUTF8Str)] string rml);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_unload_document();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_register_rgba_texture(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
            int width,
            int height,
            [In] byte[] rgba,
            int byteCount,
            out ulong handle);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_unregister_texture([MarshalAs(UnmanagedType.LPUTF8Str)] string source);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_touch_event(int phase, ulong pointerId, float x, float y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_hit_test_id(float x, float y, [Out] byte[] id, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_exists([MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_element_box([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [Out] float[] values);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_attribute(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_style(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_translate_scale_2d([MarshalAs(UnmanagedType.LPUTF8Str)] string id, float x, float y, float sx, float sy);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_inner_rml([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string markup);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_append_element(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string parent,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string tag,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_remove_element([MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_command_transforms([Out] float[] transforms, int floatCapacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_resize(int w, int h);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_mouse_move(int x, int y);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_mouse_button(int b, int down);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_mouse_wheel(float dy);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_update();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_render();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_set_text([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float urui_get_scroll_top([MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_pixel_style(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string property,
            float pixels);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_command_count();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_commands([Out] CommandNative[] commands, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_geometry_exists(ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_texture_exists(ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_geometry_info(ulong id, out int v, out int i);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_copy_geometry(ulong id, [Out] VertexNative[] v, int vc, [Out] int[] i, int ic);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_texture_info(ulong id, out int w, out int h, out int bytes);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_copy_texture(ulong id, [Out] byte[] data, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_pop_event([Out] byte[] data, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr urui_last_error();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_render_plan_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_render_plan_enable(int enabled);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_render_plan_stamp(out UruiFrameStampNative stamp);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_ref_acquire([MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_valid(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_release(ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_count();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_set_translate_scale_2d(ulong token, float x, float y, float sx, float sy);

        internal static string LastError()
        {
            var p = urui_last_error();
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
        }
    }
}
