using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintCommandNative
    {
        internal ulong shader;
        internal ulong element;
        internal int command_index;
        internal int reserved;
    }

    internal static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct ColorFilterNative
        {
            internal int Kind;
            internal float Value;
        }

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_effect_api_version();

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_set_direct_color_filters(ulong surface, int enabled);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_effect_state(ulong surface, ulong document, ulong element, int captureRequired, int inputDisabled);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_effect_layout(ulong surface, ulong document, ulong element, [Out] float[] layout, int capacity, out ulong root);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_document_effect_image_condition(ulong surface, ulong document,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string effect,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string property,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string directValue);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_paint_color_filters(ulong surface, ulong shader, [Out] ColorFilterNative[] filters, int capacity);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_visible_in_hierarchy(ulong surface, ulong documentHandle, ulong token);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_paint_commands(ulong surface, [Out] PaintCommandNative[] commands, int capacity);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_paint_shader_name(ulong surface, ulong shader, [Out] byte[] name, int capacity, out int required);

        internal static bool TryReadPaintShaderName(ulong surface, ulong shader, out string name)
        {
            name = null;
            var bytes = snapshotScratch ??= new byte[256];
            int status = urui_paint_shader_name(surface, shader, bytes, bytes.Length, out int required);
            if (status == NativeStatus.BufferTooSmall && required > 0 && required <= 64 * 1024)
            {
                bytes = GrowScratch(ref snapshotScratch, required, 64 * 1024);
                status = urui_paint_shader_name(surface, shader, bytes, bytes.Length, out required);
            }
            if (status != NativeStatus.Ok || required <= 0 || required > bytes.Length || bytes[required - 1] != 0)
            {
                return false;
            }
            name = Encoding.UTF8.GetString(bytes, 0, required - 1);
            return true;
        }
    }
}
