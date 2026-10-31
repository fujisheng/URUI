#if URUI_DEBUG
using System;
using System.Runtime.InteropServices;

namespace URUI.Internal
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct UruiApiProfileNative
    {
        public ulong style_ns;
        public ulong attribute_ns;
        public ulong style_calls;
        public ulong attribute_calls;
    }

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

    // Diagnostic ABI is available only when managed and native URUI_DEBUG agree.
    internal static partial class NativeMethods
    {
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_profile_take(out UruiProfileNative profile);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void urui_api_profile_enable(int enabled);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_api_profile_take(out UruiApiProfileNative profile);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_typed_probe_mode(int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_typed_probe_take(out UruiTypedProbeNative profile);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_style_probe_mode(int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_style_probe_take(out UruiStyleProbeNative profile);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_lookup_profile_enable(int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_lookup_profile_take(out UruiLookupProfileNative profile);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_lookup_inline_enable(int enabled);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_lookup_queue_selftest();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_texture_lifecycle_selftest();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_touch_selftest();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_dom_selftest();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_transform_selftest();
    }
}
#endif
