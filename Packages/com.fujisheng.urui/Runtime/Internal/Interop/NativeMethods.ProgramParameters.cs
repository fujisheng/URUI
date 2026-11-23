using System;
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProgramParameterKeyword
    {
        internal IntPtr Text;
        internal float Value;
        internal uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProgramParameterField
    {
        internal IntPtr Property;
        internal uint Offset;
        internal uint Kind;
        internal int OriginOffset;
        internal uint KeywordCount;
        internal IntPtr Keywords;
    }

    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_program_parameter_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_program_layout_register(ulong surface, ulong owner, ulong root,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr fields, uint fieldCount, uint rowCount, out ulong program);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_program_layout_values(ulong surface, ulong owner, ulong element, ulong program, IntPtr values, uint rowCount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_program_layout_release(ulong surface, ulong owner, ulong program);
    }
}
