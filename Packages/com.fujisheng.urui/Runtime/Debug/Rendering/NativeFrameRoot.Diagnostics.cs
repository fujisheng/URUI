#if URUI_DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal
{
    internal sealed partial class NativeFrameRoot
    {
        internal static Action<string> faultForProbe;
        internal bool holdCompletedFencesForProbe;

        internal int RetiredForProbe => retired.Count;

        static void Probe(string at)
        {
            faultForProbe?.Invoke(at);
        }

        internal ParameterTableRenderer.Stats Last
        {
            get;
            private set;
        }
        internal int CommandCount
        {
            get;
            private set;
        }
    }
}
#endif
