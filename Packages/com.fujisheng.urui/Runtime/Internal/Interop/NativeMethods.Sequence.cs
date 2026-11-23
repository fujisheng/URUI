using System;
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        // 100% blittable struct matching URUIAnimationSample (zero per-call string marshaling).
        [StructLayout(LayoutKind.Sequential)]
        internal struct SequenceSampleNative
        {
            public IntPtr Name;
            public IntPtr Easing;
            public float Progress;
        }

        // 100% blittable struct matching URUISequenceBatchItem.
        [StructLayout(LayoutKind.Sequential)]
        internal struct SequenceBatchItemNative
        {
            public ulong Token;
            public ulong Owner;
            public uint SampleOffset;
            public uint SampleCount;
            public int Mode;
            public int Reserved;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_sequence_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_sequence_layout(ulong surface, ulong document);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_keyframes_count(ulong surface, ulong document, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_keyframes_properties(ulong surface, ulong document, ulong token,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[] output, int capacity, out int required);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int urui_sequence_sample(ulong surface, ulong document, ulong token, ulong owner,
            SequenceSampleNative* samples, int count, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe int urui_sequence_apply_batch(ulong surface, ulong document,
            SequenceBatchItemNative* items, int itemCount,
            SequenceSampleNative* samples, int sampleCount);
    }
}
