#if URUI_DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Internal
{
    internal sealed partial class ParameterTableRenderer
    {
        internal Action beforeNativePlanForProbe;
        internal Action<Mesh> afterNativeUploadForProbe;
    }
}
#endif
