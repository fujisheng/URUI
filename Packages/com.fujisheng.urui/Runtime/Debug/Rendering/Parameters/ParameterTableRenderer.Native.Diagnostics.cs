#if URUI_DEBUG
using System;
using UnityEngine;

namespace URUI.Internal.Rendering.Parameters
{
    internal sealed partial class ParameterTableRenderer
    {
        internal Action beforeNativePlanForProbe;
        internal Action<Mesh> afterNativeUploadForProbe;
    }
}
#endif
