using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    /// <summary>
    /// 统一原生错误码契约（C ABI v13），与 Native~/urui.h 中的 URUIStatus 一一对应。
    /// </summary>
    internal static class NativeStatus
    {
        internal const int Ok = 1;
        internal const int Absent = 0;
        internal const int InvalidArg = -1;
        internal const int NotReady = -2;
        internal const int WrongThread = -3;
        internal const int Reentrancy = -4;
        internal const int BufferTooSmall = -5;
        internal const int Budget = -6;
        internal const int Validation = -7;
        internal const int Internal = -8;
        internal const int StaleRevision = -9;
        internal const int Structured = -10;
    }

    /// <summary>
    /// 同步 C ABI v13 边界。结构字段顺序和宽度必须与 Native~/urui.h 完全一致。
    /// 所有 Surface 级接口显式传入 surface 句柄；原生返回的指针仅在调用窗口内借用。
    /// </summary>
    internal static partial class NativeMethods
    {
        const string Dll = NativeLibrary.Name;

        internal const int ExpectedBridgeAbiVersion = 13;
        internal const int ExpectedRenderPlanVersion = 9;
        internal const int ExpectedCommandProtocolVersion = 3;
        internal const int ExpectedTextureProtocolVersion = 1;
        internal const int ExpectedImageResolverVersion = 3;
        internal const int ExpectedUIClockApiVersion = 2;
        internal const int ExpectedAnimationApiVersion = 2;
        internal const int ExpectedSequenceApiVersion = 2;
        internal const int ExpectedEffectApiVersion = 8;
        internal const int ExpectedFilterApiVersion = 4;
        internal const int ExpectedElementApiVersion = 4;
        internal const int ExpectedDocumentResourceApiVersion = 4;
        internal const int ExpectedStyleApiVersion = 5;
        internal const int ExpectedStyleFrozenApiVersion = 2;
        internal const int ExpectedProgramParameterApiVersion = 2;

        // Stateless protocol version queries.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_bridge_abi_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_command_protocol_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_texture_protocol_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_render_plan_version();

        // Surface lifecycle & error inspection.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_create(int width, int height, out ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_destroy(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_surface_last_error(ulong surface, [Out] byte[] output, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_last_error_copy([Out] byte[] output, int capacity, out int required);

        // Surface bootstrap & frame lifecycle.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_set_default_font_face(ulong surface, int family, byte[] bytes, int length, int faceIndex, int style, int weight);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_resize(ulong surface, int width, int height);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_update(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_synchronize_geometry(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_render(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_surface_layout_version(ulong surface);

        // Document stylesheets & recorded command/resource inspection.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_add_stylesheet(ulong surface, ulong documentHandle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string css,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl,
            int beforeAuthor);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_command_count(ulong surface);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_commands(ulong surface, [Out] CommandNative[] commands, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_geometry_exists(ulong surface, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_geometry_info(ulong surface, ulong id, out int vertexCount, out int indexCount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_copy_geometry(ulong surface, ulong id, [Out] VertexNative[] vertices, int vertexCapacity, [Out] int[] indices, int indexCapacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_texture_exists(ulong surface, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_texture_kind(ulong surface, ulong id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_texture_info(ulong surface, ulong id, out int width, out int height, out int bytes);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_copy_texture(ulong surface, ulong id, [Out] byte[] data, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int urui_copy_texture(ulong surface, ulong id, byte* data, int capacity);

        // Element reference handles.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_element_ref_acquire(ulong surface, ulong documentHandle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_valid(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_release(ulong surface, ulong documentHandle, ulong token);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_ref_set_translate_scale_2d(ulong surface, ulong documentHandle, ulong token, float x, float y, float sx, float sy);

        [ThreadStatic]
        static byte[] errorScratch;

        internal static string LastError(ulong surface = 0)
        {
            var bytes = errorScratch ??= new byte[512];
            int status = surface != 0
                ? urui_surface_last_error(surface, bytes, bytes.Length, out int required)
                : urui_last_error_copy(bytes, bytes.Length, out required);
            if (status == NativeStatus.BufferTooSmall && required > 1 && required <= 64 * 1024)
            {
                bytes = GrowScratch(ref errorScratch, required, 64 * 1024);
                status = surface != 0
                    ? urui_surface_last_error(surface, bytes, bytes.Length, out required)
                    : urui_last_error_copy(bytes, bytes.Length, out required);
            }
            if (status != NativeStatus.Ok || required <= 1 || required > bytes.Length)
            {
                return "";
            }
            return Encoding.UTF8.GetString(bytes, 0, required - 1);
        }

        internal static byte[] GrowScratch(ref byte[] scratch, int required, int maxCached = 256 * 1024)
        {
            if (required > maxCached)
            {
                return new byte[required];
            }
            int size = scratch == null ? 256 : scratch.Length;
            while (size < required)
            {
                size = size >= maxCached / 2 ? maxCached : size * 2;
            }
            return scratch = new byte[size];
        }
    }
}
