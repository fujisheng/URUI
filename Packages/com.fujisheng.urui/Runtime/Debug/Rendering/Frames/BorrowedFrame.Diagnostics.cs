#if URUI_DEBUG
using System;

namespace URUI.Internal.Rendering.Frames
{
    internal sealed unsafe partial class BorrowedFrame
    {
        internal static Action<string> uploadFaultForProbe;

        static void UploadProbe(string at)
        {
            uploadFaultForProbe?.Invoke(at);
        }
    }
}
#endif
