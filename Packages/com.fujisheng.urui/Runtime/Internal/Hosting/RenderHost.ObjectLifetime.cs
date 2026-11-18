using System;
using UnityEngine;
using URUI.Internal.Resources;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        sealed class RetiredObject : IDisposable
        {
            UnityEngine.Object value;
            internal RetiredObject(UnityEngine.Object value) { this.value = value; }
            public void Dispose() { if (value) UnityEngine.Object.DestroyImmediate(value); value = null; }
        }
        static void DestroyRenderObject(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else ImagePinRetirement.Retire(null, null, resource: new RetiredObject(value));
        }
    }
}
