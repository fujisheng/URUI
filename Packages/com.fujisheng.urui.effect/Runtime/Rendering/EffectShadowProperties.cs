using System;
using UnityEngine;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    internal static class EffectShadowProperties
    {
        static readonly EffectColorProperties color = new("--urui-fx-shadow-color");
        internal static void Read(EffectStyleView style, EffectParameters p)
        {
            p.ShadowMode = ReadMode(style.GetKeyword("--urui-fx-shadow-mode"));
            p.ShadowDistance = new Vector2(Mathf.Clamp(style.GetNumber("--urui-fx-shadow-distance-x"), -600, 600), Mathf.Clamp(style.GetNumber("--urui-fx-shadow-distance-y"), -600, 600));
            p.ShadowIteration = Mathf.Clamp(Mathf.RoundToInt(style.GetNumber("--urui-fx-shadow-iteration")), 1, 5);
            p.ShadowFade = Mathf.Clamp01(style.GetNumber("--urui-fx-shadow-fade"));
            p.ShadowMirrorScale = Mathf.Clamp(style.GetNumber("--urui-fx-shadow-mirror-scale"), 0, 2);
            p.ShadowBlurIntensity = Mathf.Clamp01(style.GetNumber("--urui-fx-shadow-blur-intensity"));
            p.ShadowColorFilter = EffectVisualProperties.ColorMode(style, "shadow-color-filter");
            p.ShadowColor = color.Apply(style, style.GetColor("--urui-fx-shadow-color"), p.ShadowColorFilter);
            p.ShadowColorGlow = style.GetKeyword("--urui-fx-shadow-color-glow") == "true";
            p.Flip = (Flip)Mathf.Clamp(Mathf.RoundToInt(style.GetNumber("--urui-fx-flip")), 0, 15);
        }
        internal static ShadowMode ReadMode(string value) => value switch
        {
            "none" => ShadowMode.None,
            "shadow" => ShadowMode.Shadow,
            "shadow3" => ShadowMode.Shadow3,
            "outline" => ShadowMode.Outline,
            "outline8" => ShadowMode.Outline8,
            "mirror" => ShadowMode.Mirror,
            _ => throw new InvalidOperationException("无效的 URUI 阴影模式。")
        };
    }
}
