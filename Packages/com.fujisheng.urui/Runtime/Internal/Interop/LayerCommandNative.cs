using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerCommandNative
    {
        internal int command_index,kind;
        internal ulong source,target,mask;
        internal int blend,flags,clip_x,clip_y,clip_w,clip_h;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct FilterNative {internal ulong handle,element;internal float value,sigma,x,y,r,g,b,a;}
    [StructLayout(LayoutKind.Sequential)]
    internal struct LayerRegionNative {internal ulong layer;internal int x,y,width,height;}
    internal static partial class NativeMethods
    {
        [DllImport(NativeLibrary.Name,CallingConvention=CallingConvention.Cdecl)]
        internal static extern int urui_filter_api_version();
        [DllImport(NativeLibrary.Name,CallingConvention=CallingConvention.Cdecl)]
        internal static extern int urui_filter_info(ulong handle,out FilterNative value,[Out] byte[] name,int capacity);
        [DllImport(NativeLibrary.Name,CallingConvention=CallingConvention.Cdecl)]
        internal static extern int urui_get_layer_regions([Out] LayerRegionNative[] regions,int capacity);
        [DllImport(NativeLibrary.Name,CallingConvention=CallingConvention.Cdecl)]
        internal static extern int urui_get_layer_commands([Out] LayerCommandNative[] commands,int capacity);
    }
}
