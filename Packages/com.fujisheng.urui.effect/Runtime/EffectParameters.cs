using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Effect
{
    /// <summary>效果区间；源字段的 min/max 顺序和语义保持不变。</summary>
    [Serializable]
    public struct EffectRange
    {
        public float Min;
        public float Max;
        public EffectRange(float min, float max) { Min = min; Max = max; }
    }

    /// <summary>纯参数数据；与 UIEffect 5.8.4 的模式和默认值对应，不持有节点或渲染实例。</summary>
    [Serializable]
    public sealed class EffectParameters
    {
        public ToneFilter ToneFilter = ToneFilter.None;
        [Range(0, 1)] public float ToneIntensity = 1;

        public ColorFilter ColorFilter = ColorFilter.None;
        [Range(0, 1)] public float ColorIntensity = 1;
        public Color Color = Color.white;
        public bool ColorGlow = false;

        public SamplingFilter SamplingFilter = SamplingFilter.None;
        [Range(0, 1)] public float SamplingIntensity = 0.5f;
        [Range(0.5f, 10f)] public float SamplingWidth = 1;

        public TransitionFilter TransitionFilter = TransitionFilter.None;
        [Range(0, 1)] public float TransitionProgress = 0.5f;
        public bool TransitionReverse;
        public Texture TransitionTexture;
        public Vector2 TransitionTextureScale = new Vector2(1, 1);
        public Vector2 TransitionTextureOffset = new Vector2(0, 0);
        public Vector2 TransitionTextureSpeed = new Vector2(0, 0);
        [Range(0, 360)] public float TransitionRotation = 0;
        public bool TransitionKeepAspectRatio = true;
        [Range(0, 1)] public float TransitionWidth = 0.2f;
        [Range(0, 1)] public float TransitionSoftness = 0.2f;
        public EffectRange TransitionRange = new EffectRange(0, 1);
        public ColorFilter TransitionColorFilter = ColorFilter.MultiplyAdditive;
        public Color TransitionColor = new Color(0f, 0.5f, 1.0f, 1.0f);
        public bool TransitionColorGlow = false;
        public bool TransitionPatternReverse = false;
        [Range(-5, 5)] public float TransitionAutoPlaySpeed = 0f;

        public TargetMode TargetMode = TargetMode.None;
        public Color TargetColor = Color.white;
        [Range(0, 1)] public float TargetRange = 0.1f;
        [Range(0, 1)] public float TargetSoftness = 0.5f;

        public BlendType BlendType = BlendType.AlphaBlend;
        public BlendMode SrcBlendMode = BlendMode.One;
        public BlendMode DstBlendMode = BlendMode.OneMinusSrcAlpha;

        public ShadowMode ShadowMode = ShadowMode.None;
        public Vector2 ShadowDistance = new Vector2(1f, -1f);
        [Range(1, 5)] public int ShadowIteration = 1;
        [Range(0, 1)] public float ShadowFade = 0.9f;
        [Range(0, 2)] public float ShadowMirrorScale = 0.5f;
        [Range(0, 1)] public float ShadowBlurIntensity = 1;
        public ColorFilter ShadowColorFilter = ColorFilter.Replace;
        public Color ShadowColor = Color.white;
        public bool ShadowColorGlow = false;

        public GradationMode GradationMode = GradationMode.None;
        [Range(0, 1)] public float GradationIntensity = 1;
        public GradationColorFilter GradationColorFilter = GradationColorFilter.Multiply;
        public Color GradationColor1 = Color.white;
        public Color GradationColor2 = Color.white;
        public Color GradationColor3 = Color.white;
        public Color GradationColor4 = Color.white;
        public Gradient GradationGradient = new Gradient();
        [Range(-1, 1)] public float GradationOffset = 0;
        [Range(0.01f, 10)] public float GradationScale = 1;
        [Range(0, 360)] public float GradationRotation = 0;

        public EdgeMode EdgeMode = EdgeMode.None;
        [Range(0, 1)] public float EdgeWidth = 0.5f;
        public ColorFilter EdgeColorFilter = ColorFilter.Replace;
        public Color EdgeColor = Color.white;
        public bool EdgeColorGlow = false;
        [Range(0, 1)] public float EdgeShinyProgress = 0.5f;
        [Range(0, 1)] public float EdgeShinyWidth = 0.5f;
        [Range(-5, 5)] public float EdgeShinyAutoPlaySpeed = 1f;
        public PatternArea PatternArea = PatternArea.Inner;

        public DetailFilter DetailFilter = DetailFilter.None;
        [Range(0, 1)] public float DetailIntensity = 1;
        public Color DetailColor = Color.white;
        public EffectRange DetailThreshold = new EffectRange(0, 1);
        public Texture DetailTexture;
        public Vector2 DetailTextureScale = new Vector2(1, 1);
        public Vector2 DetailTextureOffset = new Vector2(0, 0);
        public Vector2 DetailTextureSpeed = new Vector2(0, 0);

        public Flip Flip = 0;

        public float SamplingScale = 1;
        public bool AllowToModifyMeshShape = true;

        /// <summary>纹理作为共享资源引用，Gradient 数据独立复制，供文档和节点分别修改。</summary>
        public EffectParameters Clone()
        {
            var clone = (EffectParameters)MemberwiseClone();
            clone.GradationGradient = new Gradient();
            if (GradationGradient != null)
            {
                clone.GradationGradient.SetKeys(GradationGradient.colorKeys, GradationGradient.alphaKeys);
                clone.GradationGradient.mode = GradationGradient.mode;
                clone.GradationGradient.colorSpace = GradationGradient.colorSpace;
            }
            return clone;
        }
        // 内部计算快照只共享已由模块独立持有、不可变的资源；公共出口继续使用深复制 Clone。
        internal void CopyValuesFrom(EffectParameters source)
        {
            ToneFilter=source.ToneFilter;
            ToneIntensity=source.ToneIntensity;
            ColorFilter=source.ColorFilter;
            ColorIntensity=source.ColorIntensity;
            Color=source.Color;
            ColorGlow=source.ColorGlow;
            SamplingFilter=source.SamplingFilter;
            SamplingIntensity=source.SamplingIntensity;
            SamplingWidth=source.SamplingWidth;
            TransitionFilter=source.TransitionFilter;
            TransitionProgress=source.TransitionProgress;
            TransitionReverse=source.TransitionReverse;
            TransitionTexture=source.TransitionTexture;
            TransitionTextureScale=source.TransitionTextureScale;
            TransitionTextureOffset=source.TransitionTextureOffset;
            TransitionTextureSpeed=source.TransitionTextureSpeed;
            TransitionRotation=source.TransitionRotation;
            TransitionKeepAspectRatio=source.TransitionKeepAspectRatio;
            TransitionWidth=source.TransitionWidth;
            TransitionSoftness=source.TransitionSoftness;
            TransitionRange=source.TransitionRange;
            TransitionColorFilter=source.TransitionColorFilter;
            TransitionColor=source.TransitionColor;
            TransitionColorGlow=source.TransitionColorGlow;
            TransitionPatternReverse=source.TransitionPatternReverse;
            TransitionAutoPlaySpeed=source.TransitionAutoPlaySpeed;
            TargetMode=source.TargetMode;
            TargetColor=source.TargetColor;
            TargetRange=source.TargetRange;
            TargetSoftness=source.TargetSoftness;
            BlendType=source.BlendType;
            SrcBlendMode=source.SrcBlendMode;
            DstBlendMode=source.DstBlendMode;
            ShadowMode=source.ShadowMode;
            ShadowDistance=source.ShadowDistance;
            ShadowIteration=source.ShadowIteration;
            ShadowFade=source.ShadowFade;
            ShadowMirrorScale=source.ShadowMirrorScale;
            ShadowBlurIntensity=source.ShadowBlurIntensity;
            ShadowColorFilter=source.ShadowColorFilter;
            ShadowColor=source.ShadowColor;
            ShadowColorGlow=source.ShadowColorGlow;
            GradationMode=source.GradationMode;
            GradationIntensity=source.GradationIntensity;
            GradationColorFilter=source.GradationColorFilter;
            GradationColor1=source.GradationColor1;
            GradationColor2=source.GradationColor2;
            GradationColor3=source.GradationColor3;
            GradationColor4=source.GradationColor4;
            GradationGradient=source.GradationGradient;
            GradationOffset=source.GradationOffset;
            GradationScale=source.GradationScale;
            GradationRotation=source.GradationRotation;
            EdgeMode=source.EdgeMode;
            EdgeWidth=source.EdgeWidth;
            EdgeColorFilter=source.EdgeColorFilter;
            EdgeColor=source.EdgeColor;
            EdgeColorGlow=source.EdgeColorGlow;
            EdgeShinyProgress=source.EdgeShinyProgress;
            EdgeShinyWidth=source.EdgeShinyWidth;
            EdgeShinyAutoPlaySpeed=source.EdgeShinyAutoPlaySpeed;
            PatternArea=source.PatternArea;
            DetailFilter=source.DetailFilter;
            DetailIntensity=source.DetailIntensity;
            DetailColor=source.DetailColor;
            DetailThreshold=source.DetailThreshold;
            DetailTexture=source.DetailTexture;
            DetailTextureScale=source.DetailTextureScale;
            DetailTextureOffset=source.DetailTextureOffset;
            DetailTextureSpeed=source.DetailTextureSpeed;
            Flip=source.Flip;
            SamplingScale=source.SamplingScale;
            AllowToModifyMeshShape=source.AllowToModifyMeshShape;
        }
        internal EffectParameters CopyForRendering()=>(EffectParameters)MemberwiseClone();
    }
}
