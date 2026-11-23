using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Effect.Rendering
{
    /// <summary>
    /// 共享且不可变的绘制程序；Blend 状态属于程序，节点参数属于原生参数表。
    /// </summary>
    internal sealed class UIEffectProgram : IDisposable
    {
        static readonly int transitionTexId = Shader.PropertyToID("_TransitionTex");
        static readonly int detailTexId = Shader.PropertyToID("_DetailTex");
        static readonly int gradationTexId = Shader.PropertyToID("_GradationTex");
        static readonly int triangleFactsId = Shader.PropertyToID("_UruiTriangleFacts");
        static readonly int srcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int dstBlendId = Shader.PropertyToID("_DstBlend");
        static Shader cachedShader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cachedShader = null;

        internal Material Material
        {
            get;
        }
        internal UIEffectProgram(BlendMode source, BlendMode destination, int samplingQuality, Texture transition, Texture detail, Texture gradient, Texture triangles, bool complexPixel)
        {
            var shader = cachedShader ? cachedShader : (cachedShader = UnityEngine.Resources.Load<Shader>("URUI.Effect/UiEffect"));
            if (!shader || !shader.isSupported)
            {
                throw new NotSupportedException("当前平台缺少 URUI.Effect/UiEffect 程序。");
            }
            Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Material.SetTexture(transitionTexId, transition ? transition : Texture2D.whiteTexture);
            Material.SetTexture(detailTexId, detail ? detail : Texture2D.whiteTexture);
            Material.SetTexture(gradationTexId, gradient ? gradient : Texture2D.whiteTexture);
            Material.SetTexture(triangleFactsId, triangles);
            Material.SetInt(srcBlendId, (int)source);
            Material.SetInt(dstBlendId, (int)destination);
            if (samplingQuality < 0 || samplingQuality > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(samplingQuality));
            }
            if (complexPixel)
            {
                Material.EnableKeyword("URUI_COMPLEX_PIXEL");
            }
            if (samplingQuality != 0)
            {
                Material.EnableKeyword(samplingQuality == 1 ? "URUI_BLUR_FAST" : samplingQuality == 2 ? "URUI_BLUR_MEDIUM" : "URUI_BLUR_DETAIL");
            }
        }
        public void Dispose()
        {
            if (Material)
            {
                EffectObjectLifetime.Destroy(Material);
            }
        }
    }
}
