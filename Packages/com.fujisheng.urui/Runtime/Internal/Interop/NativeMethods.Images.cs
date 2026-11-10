using System;
using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    internal static partial class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ImageResolveCallback(ulong documentHandle, IntPtr source, out ulong resource, out ulong sourceIdentity, out int width, out int height);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ImageReleaseCallback(ulong resource);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_image_resolver_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int urui_set_image_resolver(ImageResolveCallback resolve, ImageReleaseCallback release);
    }
}
