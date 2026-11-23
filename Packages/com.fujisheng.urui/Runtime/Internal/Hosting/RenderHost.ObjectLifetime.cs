using System;
using UnityEngine;
using URUI.Internal.Resources;
using URUI.Resources;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        sealed class RetiredObject : IDisposable
        {
            UnityEngine.Object value;
            internal RetiredObject(UnityEngine.Object value)
            {
                this.value = value;
            }
            public void Dispose()
            {
                if (value)
                {
                    GpuRetirement.DestroyOwnedObject(value);
                }
                value = null;
            }
        }

        static void DestroyRenderObject(UnityEngine.Object value)
        {
            if (!value)
            {
                return;
            }
            if (Application.isPlaying)
            {
                // Batch Play Mode GPU object disposals into one end-of-frame GPU fence.
                ImagePinRetirement.RetireDeferred(new RetiredObject(value));
            }
            else
            {
                ImagePinRetirement.Retire(null, null, resource: new RetiredObject(value));
            }
        }
    }
}
