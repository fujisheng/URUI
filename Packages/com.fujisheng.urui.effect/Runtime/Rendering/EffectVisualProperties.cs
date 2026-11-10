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
                result.Add(new("--urui-fx-" + name, StyleValueType.Keyword));
            foreach (var name in new[] { "transition-progress", "transition-width", "transition-softness", "transition-rotation",
                "transition-range-min", "transition-range-max", "transition-auto-play-speed", "detail-intensity",
                "detail-threshold-min", "detail-threshold-max", "edge-width", "edge-shiny-progress", "edge-shiny-width", "edge-shiny-auto-play-speed" })
                result.Add(new("--urui-fx-" + name, StyleValueType.Number));
            foreach (var group in new[] { "transition", "detail" })
            {
                result.Add(new("--urui-fx-" + group + "-texture", StyleValueType.Image));
                foreach (var field in new[] { "scale", "offset", "speed" })
                    foreach (var axis in new[] { "x", "y" }) result.Add(new("--urui-fx-" + group + "-texture-" + field + "-" + axis, StyleValueType.Number));
            }
            foreach (var name in new[] { "transition-color", "detail-color", "edge-color" }) result.Add(new("--urui-fx-" + name, StyleValueType.Color));
            return result.ToArray();
        }
        static float Number(ComputedStyleSnapshot style, string name) => style.GetNumber("--urui-fx-" + name);
        static bool Flag(ComputedStyleSnapshot style, string name) => style.GetKeyword("--urui-fx-" + name) == "true";
        static Vector2 Vector(ComputedStyleSnapshot style, string name) => new(Number(style, name + "-x"), Number(style, name + "-y"));
        static int Mode(ComputedStyleSnapshot style, string name, string[] values)
        {
            var value = style.GetKeyword("--urui-fx-" + name);
            var index = Array.IndexOf(values, value);
            if (index < 0) throw new InvalidOperationException("URUI 效果模式无效：" + name + "=" + value);
            return index;
        }
        internal static ColorFilter ColorMode(ComputedStyleSnapshot style, string name) => (ColorFilter)Mode(style, name, colorModes);
        internal static void Read(ComputedStyleSnapshot style, EffectParameters p)
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
        internal static bool UsesTime(EffectParameters p) =>
            p.TransitionFilter != TransitionFilter.None && (p.TransitionAutoPlaySpeed != 0 || p.TransitionTextureSpeed != Vector2.zero) ||
            p.DetailFilter != DetailFilter.None && p.DetailTextureSpeed != Vector2.zero ||
            p.EdgeMode == EdgeMode.Shiny && p.EdgeShinyAutoPlaySpeed != 0;
        internal static void Bind(MaterialPropertyBlock block, EffectFrame frame, EffectParameters p)
        {
            block.SetInt("_TransitionFilter", (int)p.TransitionFilter);
            block.SetFloat("_TransitionRate", p.TransitionProgress); block.SetInt("_TransitionReverse", p.TransitionReverse ? 1 : 0);
            block.SetTexture("_TransitionTex", p.TransitionTexture ? p.TransitionTexture : Texture2D.whiteTexture);
            block.SetVector("_TransitionTex_ST", new Vector4(p.TransitionTextureScale.x, p.TransitionTextureScale.y, p.TransitionTextureOffset.x, p.TransitionTextureOffset.y));
            block.SetVector("_TransitionTex_Speed", p.TransitionTextureSpeed);
            block.SetFloat("_TransitionWidth", p.TransitionWidth); block.SetFloat("_TransitionSoftness", p.TransitionSoftness);
            block.SetVector("_TransitionRange", new Vector4(p.TransitionRange.Min, p.TransitionRange.Max));
            block.SetInt("_TransitionColorFilter", (int)p.TransitionColorFilter); block.SetVector("_TransitionColor", p.TransitionColor);
            block.SetInt("_TransitionColorGlow", p.TransitionColorGlow ? 1 : 0); block.SetInt("_TransitionPatternReverse", p.TransitionPatternReverse ? 1 : 0);
            block.SetFloat("_TransitionAutoPlaySpeed", p.TransitionAutoPlaySpeed);
            block.SetInt("_DetailFilter", (int)p.DetailFilter); block.SetFloat("_DetailIntensity", p.DetailIntensity);
            block.SetVector("_DetailThreshold", new Vector4(p.DetailThreshold.Min, p.DetailThreshold.Max)); block.SetVector("_DetailColor", p.DetailColor);
            block.SetTexture("_DetailTex", p.DetailTexture ? p.DetailTexture : Texture2D.whiteTexture);
            block.SetVector("_DetailTex_ST", new Vector4(p.DetailTextureScale.x, p.DetailTextureScale.y, p.DetailTextureOffset.x, p.DetailTextureOffset.y));
            block.SetVector("_DetailTex_Speed", p.DetailTextureSpeed);
            block.SetInt("_EdgeMode", (int)p.EdgeMode); block.SetFloat("_EdgeWidth", p.EdgeWidth);
            block.SetInt("_EdgeColorFilter", (int)p.EdgeColorFilter); block.SetVector("_EdgeColor", p.EdgeColor);
            block.SetInt("_EdgeColorGlow", p.EdgeColorGlow ? 1 : 0); block.SetFloat("_EdgeShinyRate", p.EdgeShinyProgress);
            block.SetFloat("_EdgeShinyWidth", p.EdgeShinyWidth); block.SetFloat("_EdgeShinyAutoPlaySpeed", p.EdgeShinyAutoPlaySpeed);
            block.SetInt("_PatternArea", (int)p.PatternArea);
            block.SetFloat("_EffectTime", UsesTime(p) ? (float)frame.Time : 0);
            var size = frame.Layout.RootContentRect.size;
            var scale = new Vector3(1 / Mathf.Max(.001f, size.x), -1 / Mathf.Max(.001f, size.y), 1);
            if (p.TransitionKeepAspectRatio) { scale.x = Mathf.Min(scale.x, -scale.y); scale.y = -scale.x; }
            if ((p.Flip & Flip.Effect) != 0)
            { if ((p.Flip & Flip.Horizontal) != 0) scale.x = -scale.x; if ((p.Flip & Flip.Vertical) != 0) scale.y = -scale.y; }
            var radians = p.TransitionRotation * Mathf.Deg2Rad;
            var multiplier = Mathf.Max(Mathf.Abs(Mathf.Cos(radians) - Mathf.Sin(radians)), Mathf.Abs(Mathf.Cos(radians) + Mathf.Sin(radians)));
            var center = frame.Layout.RootContentRect.center;
            var local = Matrix4x4.Translate(new Vector3(-center.x, -center.y, 0)) * frame.Layout.RootToDocument.inverse;
            block.SetMatrix("_EffectRootMatrix", Matrix4x4.TRS(new Vector3(.5f,.5f), Quaternion.Euler(0,0,p.TransitionRotation), scale / multiplier) * local);
        }
    }
}
