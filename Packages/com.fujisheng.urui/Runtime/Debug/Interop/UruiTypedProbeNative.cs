#if URUI_DEBUG
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct UruiTypedProbeNative
    {
        public ulong total_ns;
        public ulong lookup_ns;
        public ulong construct_ns;
        public ulong apply_ns;
        public ulong calls;
        public ulong misses;
        public ulong rejects;
        public ulong clock_pairs;
    }
}
#endif
