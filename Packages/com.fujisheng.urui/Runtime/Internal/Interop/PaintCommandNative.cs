using System.Runtime.InteropServices;
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
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_effect_state(ulong document, ulong element, int captureRequired, int inputDisabled);
        [StructLayout(LayoutKind.Sequential)]
        internal struct ColorFilterNative
        {
            internal int Kind;
            internal float Value;
        }
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_direct_color_filters(int enabled);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_effect_expansion(ulong filter, float left, float top, float right, float bottom);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_effect_transforms(ulong filter, [In] float[] matrices, int count);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_effect_layout(ulong document, ulong element, [Out] float[] layout, int capacity, out ulong root);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int urui_document_effect_image_condition(ulong document, string effect, string property, string directValue);
        [StructLayout(LayoutKind.Sequential)]
        internal struct EffectInputRegionNative
        {
            internal ulong Filter;
            internal ulong Layer;
            internal int X;
            internal int Y;
            internal int Width;
            internal int Height;
        }
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_effect_input_regions([Out] EffectInputRegionNative[] regions, int capacity);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_paint_color_filters(ulong shader, [Out] ColorFilterNative[] filters, int capacity);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_effect_api_version();
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_effect_ink_bounds(int command, float x, float y, float width, float height);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_visible_in_hierarchy(ulong documentHandle, ulong token);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_paint_commands([Out] PaintCommandNative[] commands, int capacity);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_paint_shader_name(ulong shader, [Out] byte[] name, int capacity);
    }
}
