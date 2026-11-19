using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace URUI.Effect.Editor.Tests
{
    [Serializable]
    public struct ImportRange
    {
        public float m_Min;
        public float m_Max;
        public ImportRange(float min, float max)
        {
            m_Min = min;
            m_Max = max;
        }
    }
    // 测试使用与锁定71字段相同的序列化结构，验证导入器可在无 Coffee 项目编译/运行。
    public class ImportSourceFixture : ScriptableObject
    {
        public ToneFilter m_ToneFilter = ToneFilter.None;
        public float m_ToneIntensity = 1;
        public ColorFilter m_ColorFilter = ColorFilter.None;
        public float m_ColorIntensity = 1;
        public Color m_Color = Color.white;
        public bool m_ColorGlow = false;
        public SamplingFilter m_SamplingFilter = SamplingFilter.None;
        public float m_SamplingIntensity = 0.5f;
        public float m_SamplingWidth = 1;
        public TransitionFilter m_TransitionFilter = TransitionFilter.None;
        public float m_TransitionRate = 0.5f;
        public bool m_TransitionReverse = default;
        public Texture m_TransitionTex = default;
        public Vector2 m_TransitionTexScale = new Vector2(1, 1);
        public Vector2 m_TransitionTexOffset = new Vector2(0, 0);
        public Vector2 m_TransitionTexSpeed = new Vector2(0, 0);
        public float m_TransitionRotation = 0;
        public bool m_TransitionKeepAspectRatio = true;
        public float m_TransitionWidth = 0.2f;
        public float m_TransitionSoftness = 0.2f;
        public ImportRange m_TransitionRange = new ImportRange(0, 1);
        public ColorFilter m_TransitionColorFilter = ColorFilter.MultiplyAdditive;
        public Color m_TransitionColor = new Color(0f, 0.5f, 1.0f, 1.0f);
        public bool m_TransitionColorGlow = false;
        public bool m_TransitionPatternReverse = false;
        public float m_TransitionAutoPlaySpeed = 0f;
        public TargetMode m_TargetMode = TargetMode.None;
        public Color m_TargetColor = Color.white;
        public float m_TargetRange = 0.1f;
        public float m_TargetSoftness = 0.5f;
        public BlendType m_BlendType = BlendType.AlphaBlend;
        public BlendMode m_SrcBlendMode = BlendMode.One;
        public BlendMode m_DstBlendMode = BlendMode.OneMinusSrcAlpha;
        public ShadowMode m_ShadowMode = ShadowMode.None;
        public Vector2 m_ShadowDistance = new Vector2(1f, -1f);
        public int m_ShadowIteration = 1;
        public float m_ShadowFade = 0.9f;
        public float m_ShadowMirrorScale = 0.5f;
        public float m_ShadowBlurIntensity = 1;
        public ColorFilter m_ShadowColorFilter = ColorFilter.Replace;
        public Color m_ShadowColor = Color.white;
        public bool m_ShadowColorGlow = false;
        public GradationMode m_GradationMode = GradationMode.None;
        public float m_GradationIntensity = 1;
        public GradationColorFilter m_GradationColorFilter = GradationColorFilter.Multiply;
        public Color m_GradationColor1 = Color.white;
        public Color m_GradationColor2 = Color.white;
        public Color m_GradationColor3 = Color.white;
        public Color m_GradationColor4 = Color.white;
        public Gradient m_GradationGradient = new Gradient();
        public float m_GradationOffset = 0;
        public float m_GradationScale = 1;
        public float m_GradationRotation = 0;
        public EdgeMode m_EdgeMode = EdgeMode.None;
        public float m_EdgeWidth = 0.5f;
        public ColorFilter m_EdgeColorFilter = ColorFilter.Replace;
        public Color m_EdgeColor = Color.white;
        public bool m_EdgeColorGlow = false;
        public float m_EdgeShinyRate = 0.5f;
        public float m_EdgeShinyWidth = 0.5f;
        public float m_EdgeShinyAutoPlaySpeed = 1f;
        public PatternArea m_PatternArea = PatternArea.Inner;
        public DetailFilter m_DetailFilter = DetailFilter.None;
        public float m_DetailIntensity = 1;
        public Color m_DetailColor = Color.white;
        public ImportRange m_DetailThreshold = new ImportRange(0, 1);
        public Texture m_DetailTex = default;
        public Vector2 m_DetailTexScale = new Vector2(1, 1);
        public Vector2 m_DetailTexOffset = new Vector2(0, 0);
        public Vector2 m_DetailTexSpeed = new Vector2(0, 0);
        public Flip m_Flip = 0;
    }
    public sealed class UnknownImportSource : ImportSourceFixture
    {
        public float m_FutureMode;
    }
    public sealed class ComponentImportSource : ImportSourceFixture
    {
        public float m_SamplingScale = 1;
        public bool m_AllowToModifyMeshShape = true;
        public RectTransform m_CustomRoot;
    }
}
