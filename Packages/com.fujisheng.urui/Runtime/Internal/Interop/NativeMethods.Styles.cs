using System;
using System.Runtime.InteropServices;
namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct StyleValue
        {
            internal int NameOffset, Kind;
            internal float X, Y, Z, W;
            internal int TextOffset, SourceOffset;
        }
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int urui_style_api_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_element_style_snapshot(ulong documentHandle, ulong token, ulong expectedRevision, [Out] StyleValue[] values, int capacity,
            [Out] byte[] names, int namesCapacity, out ulong revision, out int valueCount, out int nameBytes);
        [StructLayout(LayoutKind.Sequential)]
        internal struct FrozenStyleHandle {internal ulong Slot,Version;}
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_style_frozen_api_version();
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_style_snapshot_freeze(ulong owner,ulong token,IntPtr previous,out FrozenStyleHandle output);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_style_snapshot_release(ulong owner,in FrozenStyleHandle snapshot);
        [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int urui_style_snapshot_copy(ulong owner,in FrozenStyleHandle snapshot,ulong expected,
            [Out] StyleValue[] values,int capacity,[Out] byte[] names,int namesCapacity,out ulong revision,out int valueCount,out int nameBytes);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int urui_style_diagnostics([Out] ulong[] values, int capacity);
    }
}
