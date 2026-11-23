using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_set_replacement(ulong surface, ulong owner, ulong previous);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_register_resource(ulong surface, ulong owner,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source, IntPtr bytes, int length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_register_external_texture(ulong surface, ulong owner,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source, int width, int height, ulong resource);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_register_sprite(ulong surface, ulong owner,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string imageSource,
            float x, float y, float width, float height, float displayScale);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_preload_image(ulong surface, ulong owner,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source, out int width, out int height);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_release_image(ulong surface, ulong owner,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_create(ulong surface, out ulong result);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_load_html(ulong surface, ulong owner,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string html,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_load_compiled(ulong surface, ulong owner, IntPtr bytes, int length);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_begin_compiled_template(ulong surface, ulong owner, ulong root, IntPtr bytes, int length);

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
        internal static extern int urui_element_compiled_info(ulong surface, ulong owner, ulong root, int queryKind,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key, out CompiledNodeInfo info);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_compiled_tokens(ulong surface, ulong owner, ulong root, ulong identity, ulong revision, [Out] ulong[] output, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_close(ulong surface, ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_set_presentation(ulong surface, ulong owner, int layer, int order, int visible, int modal);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_document_root(ulong surface, ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_document_body(ulong surface, ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_document_layout_version(ulong surface, ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int urui_document_drain_invalidated_elements(ulong surface, ulong owner,
            ulong* output, int capacity, out int total, out int overflow);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_set_inner_html(ulong surface, ulong owner, ulong element,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string html,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_begin_inner_html(ulong surface, ulong owner, ulong element,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string html,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_end_inner_html(ulong surface, ulong owner, ulong element, int commit);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_surface_element_owner(ulong surface, ulong element);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_surface_focused_document(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_surface_pick(ulong surface, float x, float y, out ulong owner);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_filter_document(ulong surface, ulong filter);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_resource_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_resolve_resource_url(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string baseUrl,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
            [Out] byte[] output, int capacity, out int required);

        internal static string ResolveResourceUrl(string baseUrl, string source)
        {
            var bytes = snapshotScratch ??= new byte[256];
            int status = urui_resolve_resource_url(baseUrl, source, bytes, bytes.Length, out int required);
            if (status == NativeStatus.BufferTooSmall && required > 0 && required <= 16 * 1024 * 1024)
            {
                bytes = GrowScratch(ref snapshotScratch, required);
                status = urui_resolve_resource_url(baseUrl, source, bytes, bytes.Length, out required);
            }
            if (status != NativeStatus.Ok || required <= 0 || required > bytes.Length || bytes[required - 1] != 0)
            {
                throw new InvalidOperationException("URUI 无法解析资源地址：" + LastError());
            }
            return Encoding.UTF8.GetString(bytes, 0, required - 1);
        }
    }
}
