using System;
using UnityEngine;
using URUI.Styles;
namespace URUI.Effect.Rendering
{
    internal static class EffectParameterReader
    {
        static readonly EffectColorProperties colorProperties=new("--urui-fx-color");
        internal static void Read(ComputedStyleSnapshot style,EffectParameters p)
        {
            p.ToneFilter = (ToneFilter)Mode(style.GetKeyword("--urui-fx-tone-filter"), "tone");
            p.ColorFilter = (ColorFilter)Mode(style.GetKeyword("--urui-fx-color-filter"), "color");
            p.TargetMode = (TargetMode)Mode(style.GetKeyword("--urui-fx-target-mode"), "target");
            p.ToneIntensity = Mathf.Clamp01(style.GetNumber("--urui-fx-tone-intensity"));
            p.ColorIntensity = Mathf.Clamp01(style.GetNumber("--urui-fx-color-intensity"));
            p.Color = colorProperties.Apply(style, style.GetColor("--urui-fx-color"), p.ColorFilter);
            p.ColorGlow = style.GetKeyword("--urui-fx-color-glow") == "true";
            p.TargetColor = style.GetColor("--urui-fx-target-color");
            p.TargetRange = Mathf.Clamp01(style.GetNumber("--urui-fx-target-range"));
            p.TargetSoftness = Mathf.Clamp01(style.GetNumber("--urui-fx-target-softness"));
            p.SamplingFilter = EffectSampling.ReadMode(style.GetKeyword("--urui-fx-sampling-filter"));
            p.SamplingIntensity = Mathf.Clamp01(style.GetNumber("--urui-fx-sampling-intensity"));
            p.SamplingWidth = Mathf.Clamp(style.GetNumber("--urui-fx-sampling-width"), .5f, 10);
            p.SamplingScale = Mathf.Clamp(style.GetNumber("--urui-fx-sampling-scale"), .01f, 100);
            p.AllowToModifyMeshShape = style.GetKeyword("--urui-fx-allow-to-modify-mesh-shape") == "true";
            EffectVisualProperties.Read(style, p);
            EffectGradation.Read(style, p);
            EffectShadowProperties.Read(style, p);
            EffectBlendState.Read(style,p);
        }
        static int Mode(string value, string family)
        {
            return (family, value) switch
            {
                (_, "none") => 0,
                ("tone", "grayscale") => 1, ("tone", "sepia") => 2, ("tone", "negative") => 3,
                ("tone", "retro") => 4, ("tone", "posterize") => 5,
                ("color", "multiply") => 1, ("color", "additive") => 2, ("color", "subtractive") => 3,
                ("color", "replace") => 4, ("color", "multiply-luminance") => 5, ("color", "multiply-additive") => 6,
                ("color", "hsv-modifier") => 7, ("color", "contrast") => 8,
                ("target", "hue") => 1, ("target", "luminance") => 2,
                _ => throw new InvalidOperationException("URUI 效果模式无效：" + family + "=" + value)
            };
        }
    }
}
