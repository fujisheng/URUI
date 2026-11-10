using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Effect.Rendering
{
    /// <summary>共享且不可变的绘制程序；Blend 状态属于程序，节点参数属于属性块。</summary>
    internal sealed class UiEffectProgram : IDisposable
    {
        internal Material Material { get; }
        internal UiEffectProgram(BlendMode source, BlendMode destination, int samplingQuality)
        {
            var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/UiEffect");
            if (!shader || !shader.isSupported) { throw new NotSupportedException("当前平台缺少 URUI.Effect/UiEffect 程序。"); }
            Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Material.SetInt("_SrcBlend", (int)source); Material.SetInt("_DstBlend", (int)destination);
            if (samplingQuality < 0 || samplingQuality > 3) { throw new ArgumentOutOfRangeException(nameof(samplingQuality)); }
            if (samplingQuality != 0) { Material.EnableKeyword(samplingQuality == 1 ? "URUI_BLUR_FAST" : samplingQuality == 2 ? "URUI_BLUR_MEDIUM" : "URUI_BLUR_DETAIL"); }
        }
        public void Dispose() { if (Material) { EffectObjectLifetime.Destroy(Material); } }
    }
}
