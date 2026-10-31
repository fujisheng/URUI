#if URUI_DEBUG
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace URUI.Internal
{
    internal sealed partial class ParameterFrame
    {
        internal static Action afterCorePinForProbe;
        internal static int corePinsForProbe;

        internal int NativeCoreReadyCommands
        {
            get;
            private set;
        }
        internal int NativeCoreFallbackCommands
        {
            get;
            private set;
        }
        internal long NativeCoreBytes
        {
            get;
            private set;
        }
    }
}
#endif
