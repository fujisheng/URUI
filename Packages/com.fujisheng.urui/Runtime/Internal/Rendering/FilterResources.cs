using System;
using UnityEngine;
namespace URUI.Internal.Rendering
{
    internal sealed class FilterResources : IDisposable
    {
        internal Material Material
        {
            get; private set;
        }
        internal FilterResources(string key)
        {
            var shader = UnityEngine.Resources.Load<Shader>("URUI/" + key);
            if (!shader || !shader.isSupported)
                throw new NotSupportedException("滤镜 Shader 缺失或不受支持：" + key);
            Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        public void Dispose()
        {
            if (!Material)
                return;
            // The retirement queue has already waited for GPU use. Edit Mode cannot use delayed Destroy.
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(Material);
            else UnityEngine.Object.Destroy(Material);
#else
            UnityEngine.Object.Destroy(Material);
#endif
            Material = null;
        }
    }
}
