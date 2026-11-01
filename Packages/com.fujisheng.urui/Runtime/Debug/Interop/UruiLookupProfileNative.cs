#if URUI_DEBUG
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct UruiLookupProfileNative
    {
        public ulong lookup_ns;
        public ulong style_lookup_ns;
        public ulong queries;
        public ulong style_queries;
        public ulong nodes_visited;
        public ulong max_frontier;
        public ulong inline_calls;
        public ulong overflow_calls;
    }
}
#endif
