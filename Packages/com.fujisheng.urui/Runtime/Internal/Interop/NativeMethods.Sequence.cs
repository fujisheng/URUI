using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SequenceSample
        {
            [MarshalAs(UnmanagedType.LPUTF8Str)] public string Name;
            [MarshalAs(UnmanagedType.LPUTF8Str)] public string Easing;
            public float Progress;
        }
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_sequence_api_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_sequence_layout(ulong document);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_keyframes_count(ulong document, ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_keyframes_properties(ulong document, ulong token, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[] output, int capacity, out int required);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_sequence_sample(ulong document, ulong token, ulong owner, [In] SequenceSample[] samples, int count, int mode);
    }
}
