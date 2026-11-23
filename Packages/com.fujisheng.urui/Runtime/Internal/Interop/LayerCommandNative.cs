using System.Runtime.InteropServices;
using System.Text;

namespace URUI.Internal.Interop
{
    internal static class LayerCommandKind
    {
        internal const int Push = 1;
        internal const int Pop = 2;
        internal const int SaveMask = 3;
        internal const int Composite = 4;
        internal const int ReleaseMask = 5;
        internal const int ApplyMask = 6;
    }

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

    internal static partial class NativeMethods
    {
        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_filter_api_version();

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_filter_info(ulong surface, ulong handle, out FilterNative value, [Out] byte[] name, int capacity, out int required);

        [DllImport(NativeLibrary.Name, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_get_layer_commands(ulong surface, [Out] LayerCommandNative[] commands, int capacity);

        internal static bool TryReadFilterInfo(ulong surface, ulong handle, out FilterNative value, out string name)
        {
            name = null;
            var bytes = snapshotScratch ??= new byte[256];
            int status = urui_filter_info(surface, handle, out value, bytes, bytes.Length, out int required);
            if (status == NativeStatus.BufferTooSmall && required > 0 && required <= 64 * 1024)
            {
                bytes = GrowScratch(ref snapshotScratch, required, 64 * 1024);
                status = urui_filter_info(surface, handle, out value, bytes, bytes.Length, out required);
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
