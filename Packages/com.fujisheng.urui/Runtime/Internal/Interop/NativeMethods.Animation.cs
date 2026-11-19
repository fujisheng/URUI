using System.Runtime.InteropServices;
namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_ui_clock_api_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_ui_time(double now);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_animation_api_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_animation_next_identity();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_animation_current_event(ulong documentHandle, [Out] byte[] name, int capacity, out ulong identity, out int iterations);
    }
}
