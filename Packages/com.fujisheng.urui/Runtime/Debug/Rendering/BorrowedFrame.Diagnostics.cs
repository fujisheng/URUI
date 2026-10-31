#if URUI_DEBUG
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal
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
