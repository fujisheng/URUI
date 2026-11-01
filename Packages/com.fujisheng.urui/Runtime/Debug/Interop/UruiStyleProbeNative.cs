#if URUI_DEBUG
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct UruiStyleProbeNative
    {
        public ulong lookup_ns;
        public ulong parse_ns;
        public ulong apply_ns;
        public ulong compare_ns;
        public ulong calls;
        public ulong parsed_calls;
        public ulong rejected_calls;
        public ulong properties;
        public ulong comparable;
        public ulong equal_properties;
        public ulong equal_calls;
        public ulong unsupported_calls;
    }
}
#endif
