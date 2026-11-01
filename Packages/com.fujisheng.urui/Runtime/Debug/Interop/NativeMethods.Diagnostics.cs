#if URUI_DEBUG
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
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
