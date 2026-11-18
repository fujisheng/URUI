using System;
namespace URUI.Effect.Rendering
{
    internal static class EffectSampling
    {
        internal static SamplingFilter ReadMode(string value) => value switch
        {
            "none" => SamplingFilter.None, "blur-fast" => SamplingFilter.BlurFast, "blur-medium" => SamplingFilter.BlurMedium,
            "blur-detail" => SamplingFilter.BlurDetail, "pixelation" => SamplingFilter.Pixelation, "rgb-shift" => SamplingFilter.RgbShift,
            "edge-luminance" => SamplingFilter.EdgeLuminance, "edge-alpha" => SamplingFilter.EdgeAlpha,
            _ => throw new InvalidOperationException("URUI 采样模式无效：" + value)
        };
        internal static int Quality(SamplingFilter mode) => mode >= SamplingFilter.BlurFast && mode <= SamplingFilter.BlurDetail ? (int)mode : 0;
    }
}
