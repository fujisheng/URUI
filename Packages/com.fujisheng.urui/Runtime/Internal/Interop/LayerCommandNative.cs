using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerCommandNative
    {
        internal int command_index;
        internal int kind;
        internal ulong source;
        internal ulong target;
        internal ulong mask;
        internal int blend;
        internal int flags;
        internal int clip_x;
        internal int clip_y;
        internal int clip_w;
        internal int clip_h;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct FilterNative
    {
        internal ulong handle;
        internal ulong element;
        internal float value;
        internal float sigma;
        internal float x;
        internal float y;
        internal float r;
        internal float g;
        internal float b;
        internal float a;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerRegionNative
    {
        internal ulong layer;
        internal int x;
        internal int y;
        internal int width;
        internal int height;
    }
    internal static partial class NativeMethods
    {
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_filter_api_version();
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_filter_info(ulong handle, out FilterNative value, [Out] byte[] name, int capacity);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_layer_regions([Out] LayerRegionNative[] regions, int capacity);
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_layer_commands([Out] LayerCommandNative[] commands, int capacity);
    }
}
