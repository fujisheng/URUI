using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_set_replacement(ulong owner, ulong previous);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_register_resource(ulong owner, [MarshalAs(UnmanagedType.LPUTF8Str)] string source, System.IntPtr bytes, int length);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_register_external_texture(ulong owner, [MarshalAs(UnmanagedType.LPUTF8Str)] string source, int width, int height, ulong resource);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_register_sprite(ulong owner, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string imageSource, float x, float y, float width, float height, float displayScale);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_preload_image(ulong owner, [MarshalAs(UnmanagedType.LPUTF8Str)] string source, out int width, out int height);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_release_image(ulong owner, [MarshalAs(UnmanagedType.LPUTF8Str)] string source);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_create(out ulong result);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_load_html(ulong owner,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string html, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_load_compiled(ulong owner, System.IntPtr bytes, int length);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_begin_compiled_template(ulong owner, ulong root, System.IntPtr bytes, int length);
        [StructLayout(LayoutKind.Sequential)]
        internal struct CompiledNodeInfo
        {
            internal ulong Identity;
            internal ulong Revision;
            internal uint LocalId;
            internal uint End;
            internal int Flags;
            internal int RootMatch;
        }
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_compiled_info(ulong owner, ulong root, int queryKind,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key, out CompiledNodeInfo info);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_compiled_tokens(ulong owner, ulong root, ulong identity, ulong revision, [Out] ulong[] output, int capacity);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_close(ulong owner);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_set_presentation(ulong owner, int layer, int order, int visible, int modal);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_document_root(ulong owner);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_document_body(ulong owner);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_set_inner_html(ulong owner, ulong element,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string html, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_begin_inner_html(ulong owner, ulong element,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string html, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_end_inner_html(ulong owner, ulong element, int commit);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_surface_element_owner(ulong element);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_surface_focused_document();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_surface_pick(float x, float y, out ulong owner);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_filter_document(ulong filter);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_resource_api_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_resolve_resource_url([MarshalAs(UnmanagedType.LPUTF8Str)] string baseUrl,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source, [Out] byte[] output, int capacity);
    }
}
