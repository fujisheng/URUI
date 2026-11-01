#if URUI_DEBUG
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct UruiProfileNative
    {
        public ulong text_ns;
        public ulong update_ns;
        public ulong render_ns;
        public ulong geometry_compile_ns;
        public ulong command_copy_ns;
        public ulong geometry_copy_ns;
        public ulong text_settext_ns;
        public ulong text_setinnerrml_ns;
        public ulong text_lookup_ns;
        public uint text_calls;
        public uint geometry_compiles;
        public uint geometry_vertices;
        public uint geometry_indices;
        public uint text_fast_calls;
        public uint text_fallback_calls;
        public uint text_lookups;
    }
}
#endif
