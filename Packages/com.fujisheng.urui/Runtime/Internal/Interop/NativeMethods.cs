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
        internal static extern int urui_command_protocol_version();

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_set_default_font_face(int family, byte[] bytes, int length, int faceIndex, int style, int weight);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_count(ulong documentHandle);

        const string Dll = NativeLibrary.Name;

        // 字符串固定按 UTF-8 传入，数组容量由调用者提供；原生端不接管托管资源所有权。
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_create(int w, int h);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_destroy();



        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_add_stylesheet(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string css, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl, int beforeAuthor);


        // Versioned metadata-only registration; resource IDs are logical, never GPU pointers.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_texture_protocol_version();


        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_texture_kind(ulong id);




        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_hit_test_id(ulong documentHandle, float x, float y, [Out] byte[] id, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_exists(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_element_box(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, [Out] float[] values);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_attribute(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_style(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_translate_scale_2d(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, float x, float y, float sx, float sy);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_inner_html(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string markup);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_append_element(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string parent,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string tag,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_remove_element(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_command_transforms([Out] float[] transforms, int floatCapacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_resize(int w, int h);




        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_update();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_render();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_set_text(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float urui_get_scroll_top(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_pixel_style(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
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
        internal static extern int urui_pop_event(ulong documentHandle, [Out] byte[] data, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr urui_last_error();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_render_plan_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_render_plan_stamp(out UruiFrameStampNative stamp);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_ref_acquire(ulong documentHandle, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_valid(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_release(ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_count(ulong documentHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_set_translate_scale_2d(ulong documentHandle, ulong token, float x, float y, float sx, float sy);

        internal static string LastError()
        {
            var p = urui_last_error();
            return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
        }
    }
}
