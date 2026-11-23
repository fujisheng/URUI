using System;
using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_ui_clock_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_surface_set_ui_time(ulong surface, double now);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_animation_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong urui_animation_next_identity();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_animation_current_event(ulong surface, ulong documentHandle, [Out] byte[] name, int capacity, out int required, out ulong identity, out int iterations);

        internal static bool TryReadAnimationCurrentEvent(ulong surface, ulong documentHandle, out string name, out ulong identity, out int iterations)
        {
            name = null;
            identity = 0;
            iterations = 0;
            var bytes = snapshotScratch ??= new byte[256];
            int status = urui_animation_current_event(surface, documentHandle, bytes, bytes.Length, out int required, out identity, out iterations);
            if (status == NativeStatus.Absent)
            {
                return false;
            }
            if (status == NativeStatus.BufferTooSmall && required > 0 && required <= 64 * 1024)
            {
                bytes = GrowScratch(ref snapshotScratch, required, 64 * 1024);
                status = urui_animation_current_event(surface, documentHandle, bytes, bytes.Length, out required, out identity, out iterations);
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
