// 模式与数值依据 UIEffect 5.8.4；来源及 MIT 许可见 ThirdPartyNotices.md。
using System;
using UnityEngine.Rendering;

namespace URUI.Effect
{
    public enum ToneFilter
    {
        None = 0,
        Grayscale = 1,
        Sepia = 2,
        Negative = 3,
        Retro = 4,
        Posterize = 5
    }

    public enum ColorFilter
    {
        None = 0,
        Multiply = 1,
        Additive = 2,
        Subtractive = 3,
        Replace = 4,
        MultiplyLuminance = 5,
        MultiplyAdditive = 6,
        HsvModifier = 7,
        Contrast = 8
    }

    public enum GradationColorFilter
    {
        None = (int)ColorFilter.None,
        Multiply = (int)ColorFilter.Multiply,
        Additive = (int)ColorFilter.Additive,
        Subtractive = (int)ColorFilter.Subtractive,
        Replace = (int)ColorFilter.Replace,
        MultiplyLuminance = (int)ColorFilter.MultiplyLuminance,
        MultiplyAdditive = (int)ColorFilter.MultiplyAdditive
    }

    public enum SamplingFilter
    {
        None = 0,
        BlurFast = 1,
        BlurMedium = 2,
        BlurDetail = 3,
        Pixelation = 4,
        RgbShift = 5,
        EdgeLuminance = 6,
        EdgeAlpha = 7
    }

    public enum TransitionFilter
    {
        None = 0,
        Fade = 1,
        Cutoff = 2,
        Dissolve = 3,
        Shiny = 4,
        Mask = 5,
        Melt = 6,
        Burn = 7,
        Pattern = 8
    }

    public enum BlendType
    {
        Custom,
        AlphaBlend,
        Multiply,
        Additive,
        SoftAdditive,
        MultiplyAdditive
    }

    public enum TargetMode
    {
        None = 0,
        Hue = 1,
        Luminance = 2
    }

    public enum ShadowMode
    {
        None = 0,
        Shadow,
        Shadow3,
        Outline,
        Outline8,
        Mirror
    }

    public enum EdgeMode
    {
        None = 0,
        Plain,
        Shiny
    }

    public enum PatternArea
    {
        All = 0,
        Inner = 1,
        Edge = 2
    }

    public enum GradationMode
    {
        None = 0,
        Horizontal = 1,
        HorizontalGradient = 2,
        Vertical = 3,
        VerticalGradient = 4,
        RadialFast = 5,
        RadialDetail = 6,
        Diagonal = 11,
        DiagonalToRightBottom = 7,
        DiagonalToLeftBottom = 8,
        Angle = 9,
        AngleGradient = 10
    }

    public enum DetailFilter
    {
        None = 0,
        Masking = 1,
        Multiply = 2,
        Additive = 3,
        Subtractive = 6,
        Replace = 4,
        MultiplyAdditive = 5
    }

    [Flags]
    public enum Flip
    {
        Horizontal = 1 << 0,
        Vertical = 1 << 1,
        Effect = 1 << 2,
        Shadow = 1 << 3
    }

}
