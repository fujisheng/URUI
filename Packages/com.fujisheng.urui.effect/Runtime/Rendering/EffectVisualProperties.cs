using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Rendering.Effects;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    // UIEffect 5.8.4 参数范围；所有 CSS 值均来自原生类型化快照。
    internal static class EffectVisualProperties
    {
        static readonly EffectColorProperties transitionColor = new("--urui-fx-transition-color");
        static readonly EffectColorProperties edgeColor = new("--urui-fx-edge-color");
        static readonly string[] colorModes = { "none", "multiply", "additive", "subtractive", "replace", "multiply-luminance", "multiply-additive", "hsv-modifier", "contrast" };
        static readonly string[] transitionModes = { "none", "fade", "cutoff", "dissolve", "shiny", "mask", "melt", "burn", "pattern" };
        static readonly string[] detailModes = { "none", "masking", "multiply", "additive", "replace", "multiply-additive", "subtractive" };
        static readonly string[] edgeModes = { "none", "plain", "shiny" };
        static readonly string[] patternAreas = { "all", "inner", "edge" };
        internal static readonly StylePropertyDefinition[] Parameters = CreateParameters();
        static StylePropertyDefinition[] CreateParameters()
        {
            var result = new List<StylePropertyDefinition>();
            foreach (var name in new[] { "transition-filter", "transition-reverse", "transition-keep-aspect-ratio",
                "transition-color-filter", "transition-color-glow", "transition-pattern-reverse", "detail-filter",
                "edge-mode", "edge-color-filter", "edge-color-glow", "pattern-area" })
            {
                result.Add(new("--urui-fx-" + name, StyleValueType.Keyword));
            }
            foreach (var name in new[] { "transition-progress", "transition-width", "transition-softness", "transition-rotation",
                "transition-range-min", "transition-range-max", "transition-auto-play-speed", "detail-intensity",
                "detail-threshold-min", "detail-threshold-max", "edge-width", "edge-shiny-progress", "edge-shiny-width", "edge-shiny-auto-play-speed" })
            {
                result.Add(new("--urui-fx-" + name, StyleValueType.Number));
            }
            foreach (var group in new[] { "transition", "detail" })
            {
                result.Add(new("--urui-fx-" + group + "-texture", StyleValueType.Image));
                foreach (var field in new[] { "scale", "offset", "speed" })
                {
                    foreach (var axis in new[] { "x", "y" })
                    {
                        result.Add(new("--urui-fx-" + group + "-texture-" + field + "-" + axis, StyleValueType.Number));
                    }
                }
            }
            foreach (var name in new[] { "transition-color", "detail-color", "edge-color" })
            {
                result.Add(new("--urui-fx-" + name, StyleValueType.Color));
            }
            return result.ToArray();
        }
        static readonly Dictionary<string, string> names = new(StringComparer.Ordinal);
        static readonly Dictionary<string, (string X, string Y)> vectors = new(StringComparer.Ordinal);
        static string Name(string name)
        {
            if (!names.TryGetValue(name, out var key))
            {
                names.Add(name, key = "--urui-fx-" + name);
            }
            return key;
        }
        static float Number(EffectStyleView style, string name) => style.GetNumber(Name(name));
        static bool Flag(EffectStyleView style, string name) => style.GetKeyword(Name(name)) == "true";
        static Vector2 Vector(EffectStyleView style, string name)
        {
            if (!vectors.TryGetValue(name, out var keys))
            {
                vectors.Add(name, keys = (Name(name + "-x"), Name(name + "-y")));
            }
            return new(style.GetNumber(keys.X), style.GetNumber(keys.Y));
        }
        static int Mode(EffectStyleView style, string name, string[] values)
        {
            var value = style.GetKeyword(Name(name));
            var index = Array.IndexOf(values, value);
            if (index < 0)
            {
                throw new InvalidOperationException("URUI 效果模式无效：" + name + "=" + value);
            }
            return index;
        }
        internal static ColorFilter ColorMode(EffectStyleView style, string name) => (ColorFilter)Mode(style, name, colorModes);
        internal static void Read(EffectStyleView style, EffectParameters p)
        {
            p.TransitionFilter = (TransitionFilter)Mode(style, "transition-filter", transitionModes);
            p.TransitionProgress = Mathf.Clamp01(Number(style, "transition-progress"));
            p.TransitionReverse = Flag(style, "transition-reverse");
            p.TransitionTextureScale = Vector(style, "transition-texture-scale");
            p.TransitionTextureOffset = Vector(style, "transition-texture-offset");
            p.TransitionTextureSpeed = Vector(style, "transition-texture-speed");
            p.TransitionRotation = Mathf.Clamp(Number(style, "transition-rotation"), 0, 360);
            p.TransitionKeepAspectRatio = Flag(style, "transition-keep-aspect-ratio");
            p.TransitionWidth = Mathf.Clamp01(Number(style, "transition-width"));
            p.TransitionSoftness = Mathf.Clamp01(Number(style, "transition-softness"));
            p.TransitionRange = new(Mathf.Clamp01(Number(style, "transition-range-min")), Mathf.Clamp01(Number(style, "transition-range-max")));
            p.TransitionColorFilter = ColorMode(style, "transition-color-filter");
            p.TransitionColor = transitionColor.Apply(style, style.GetColor("--urui-fx-transition-color"), p.TransitionColorFilter);
            p.TransitionColorGlow = Flag(style, "transition-color-glow");
            p.TransitionPatternReverse = Flag(style, "transition-pattern-reverse");
            p.TransitionAutoPlaySpeed = Mathf.Clamp(Number(style, "transition-auto-play-speed"), -5, 5);
            p.DetailFilter = (DetailFilter)Mode(style, "detail-filter", detailModes);
            p.DetailIntensity = Mathf.Clamp01(Number(style, "detail-intensity"));
            p.DetailThreshold = new(Mathf.Clamp01(Number(style, "detail-threshold-min")), Mathf.Clamp01(Number(style, "detail-threshold-max")));
            p.DetailColor = style.GetColor("--urui-fx-detail-color");
            p.DetailTextureScale = Vector(style, "detail-texture-scale");
            p.DetailTextureOffset = Vector(style, "detail-texture-offset");
            p.DetailTextureSpeed = Vector(style, "detail-texture-speed");
            p.EdgeMode = (EdgeMode)Mode(style, "edge-mode", edgeModes);
            p.EdgeWidth = Mathf.Clamp01(Number(style, "edge-width"));
            p.EdgeColorFilter = ColorMode(style, "edge-color-filter");
            p.EdgeColor = edgeColor.Apply(style, style.GetColor("--urui-fx-edge-color"), p.EdgeColorFilter);
            p.EdgeColorGlow = Flag(style, "edge-color-glow");
            p.EdgeShinyProgress = Mathf.Clamp01(Number(style, "edge-shiny-progress"));
            p.EdgeShinyWidth = Mathf.Clamp01(Number(style, "edge-shiny-width"));
            p.EdgeShinyAutoPlaySpeed = Mathf.Clamp(Number(style, "edge-shiny-auto-play-speed"), -5, 5);
            p.PatternArea = (PatternArea)Mode(style, "pattern-area", patternAreas);
        }
    }
}
