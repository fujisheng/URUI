#if URUI_DEBUG
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct UruiApiProfileNative
    {
        public ulong style_ns;
        public ulong attribute_ns;
        public ulong style_calls;
        public ulong attribute_calls;
    }
}
#endif
