using System;
using System.Collections.Generic;
using URUI.Styles;
namespace URUI.Effect.Rendering
{
    // Explicit field mapping keeps per-frame rendering free of reflection and serialization.
    internal static class EffectParameterStyle
    {
        static class Keywords<T> where T : Enum
        {
            internal static readonly Dictionary<T, string> Values = Create();
            static Dictionary<T, string> Create()
            {
                var values = new Dictionary<T, string>();
                foreach (T value in Enum.GetValues(typeof(T)))
                {
                    var name = value.ToString();
                    var text = new System.Text.StringBuilder();
                    for (var i = 0; i < name.Length; i++)
                    {
                        if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                        {
                            text.Append('-');
                        }
                        text.Append(char.ToLowerInvariant(name[i]));
                    }
                    values[value] = text.ToString();
                }
                return values;
            }
        }
        static string Keyword<T>(T value) where T : Enum => Keywords<T>.Values.TryGetValue(value, out var text) ? text : throw new ArgumentOutOfRangeException(nameof(value));
        internal static Dictionary<string, StyleOverride> Write(EffectParameters p, Dictionary<string, StyleOverride> values = null)
        {
            values ??= new Dictionary<string, StyleOverride>(StringComparer.Ordinal);
            values.Clear();
            values["--urui-fx-tone-filter"] = StyleOverride.FromKeyword(Keyword(p.ToneFilter));
            values["--urui-fx-tone-intensity"] = StyleOverride.Number((float)p.ToneIntensity);
            values["--urui-fx-color-filter"] = StyleOverride.FromKeyword(Keyword(p.ColorFilter));
            values["--urui-fx-color-intensity"] = StyleOverride.Number((float)p.ColorIntensity);
            values["--urui-fx-color"] = StyleOverride.Color(p.Color);
            values["--urui-fx-color-glow"] = StyleOverride.FromKeyword(p.ColorGlow ? "true" : "false");
            values["--urui-fx-sampling-filter"] = StyleOverride.FromKeyword(Keyword(p.SamplingFilter));
            values["--urui-fx-sampling-intensity"] = StyleOverride.Number((float)p.SamplingIntensity);
            values["--urui-fx-sampling-width"] = StyleOverride.Number((float)p.SamplingWidth);
            values["--urui-fx-transition-filter"] = StyleOverride.FromKeyword(Keyword(p.TransitionFilter));
            values["--urui-fx-transition-progress"] = StyleOverride.Number((float)p.TransitionProgress);
            values["--urui-fx-transition-reverse"] = StyleOverride.FromKeyword(p.TransitionReverse ? "true" : "false");
            values["--urui-fx-transition-texture-scale-x"] = StyleOverride.Number(p.TransitionTextureScale.x);
            values["--urui-fx-transition-texture-scale-y"] = StyleOverride.Number(p.TransitionTextureScale.y);
            values["--urui-fx-transition-texture-offset-x"] = StyleOverride.Number(p.TransitionTextureOffset.x);
            values["--urui-fx-transition-texture-offset-y"] = StyleOverride.Number(p.TransitionTextureOffset.y);
            values["--urui-fx-transition-texture-speed-x"] = StyleOverride.Number(p.TransitionTextureSpeed.x);
            values["--urui-fx-transition-texture-speed-y"] = StyleOverride.Number(p.TransitionTextureSpeed.y);
            values["--urui-fx-transition-rotation"] = StyleOverride.Number((float)p.TransitionRotation);
            values["--urui-fx-transition-keep-aspect-ratio"] = StyleOverride.FromKeyword(p.TransitionKeepAspectRatio ? "true" : "false");
            values["--urui-fx-transition-width"] = StyleOverride.Number((float)p.TransitionWidth);
            values["--urui-fx-transition-softness"] = StyleOverride.Number((float)p.TransitionSoftness);
            values["--urui-fx-transition-range-min"] = StyleOverride.Number(p.TransitionRange.Min);
            values["--urui-fx-transition-range-max"] = StyleOverride.Number(p.TransitionRange.Max);
            values["--urui-fx-transition-color-filter"] = StyleOverride.FromKeyword(Keyword(p.TransitionColorFilter));
            values["--urui-fx-transition-color"] = StyleOverride.Color(p.TransitionColor);
            values["--urui-fx-transition-color-glow"] = StyleOverride.FromKeyword(p.TransitionColorGlow ? "true" : "false");
            values["--urui-fx-transition-pattern-reverse"] = StyleOverride.FromKeyword(p.TransitionPatternReverse ? "true" : "false");
            values["--urui-fx-transition-auto-play-speed"] = StyleOverride.Number((float)p.TransitionAutoPlaySpeed);
            values["--urui-fx-target-mode"] = StyleOverride.FromKeyword(Keyword(p.TargetMode));
            values["--urui-fx-target-color"] = StyleOverride.Color(p.TargetColor);
            values["--urui-fx-target-range"] = StyleOverride.Number((float)p.TargetRange);
            values["--urui-fx-target-softness"] = StyleOverride.Number((float)p.TargetSoftness);
            values["--urui-fx-blend-type"] = StyleOverride.FromKeyword(Keyword(p.BlendType));
            values["--urui-fx-src-blend-mode"] = StyleOverride.FromKeyword(Keyword(p.SrcBlendMode));
            values["--urui-fx-dst-blend-mode"] = StyleOverride.FromKeyword(Keyword(p.DstBlendMode));
            values["--urui-fx-shadow-mode"] = StyleOverride.FromKeyword(Keyword(p.ShadowMode));
            values["--urui-fx-shadow-distance-x"] = StyleOverride.Number(p.ShadowDistance.x);
            values["--urui-fx-shadow-distance-y"] = StyleOverride.Number(p.ShadowDistance.y);
            values["--urui-fx-shadow-iteration"] = StyleOverride.Number((float)p.ShadowIteration);
            values["--urui-fx-shadow-fade"] = StyleOverride.Number((float)p.ShadowFade);
            values["--urui-fx-shadow-mirror-scale"] = StyleOverride.Number((float)p.ShadowMirrorScale);
            values["--urui-fx-shadow-blur-intensity"] = StyleOverride.Number((float)p.ShadowBlurIntensity);
            values["--urui-fx-shadow-color-filter"] = StyleOverride.FromKeyword(Keyword(p.ShadowColorFilter));
            values["--urui-fx-shadow-color"] = StyleOverride.Color(p.ShadowColor);
            values["--urui-fx-shadow-color-glow"] = StyleOverride.FromKeyword(p.ShadowColorGlow ? "true" : "false");
            values["--urui-fx-gradation-mode"] = StyleOverride.FromKeyword(Keyword(p.GradationMode));
            values["--urui-fx-gradation-intensity"] = StyleOverride.Number((float)p.GradationIntensity);
            values["--urui-fx-gradation-color-filter"] = StyleOverride.FromKeyword(Keyword(p.GradationColorFilter));
            values["--urui-fx-gradation-color1"] = StyleOverride.Color(p.GradationColor1);
            values["--urui-fx-gradation-color2"] = StyleOverride.Color(p.GradationColor2);
            values["--urui-fx-gradation-color3"] = StyleOverride.Color(p.GradationColor3);
            values["--urui-fx-gradation-color4"] = StyleOverride.Color(p.GradationColor4);
            values["--urui-fx-gradation-offset"] = StyleOverride.Number((float)p.GradationOffset);
            values["--urui-fx-gradation-scale"] = StyleOverride.Number((float)p.GradationScale);
            values["--urui-fx-gradation-rotation"] = StyleOverride.Number((float)p.GradationRotation);
            values["--urui-fx-edge-mode"] = StyleOverride.FromKeyword(Keyword(p.EdgeMode));
            values["--urui-fx-edge-width"] = StyleOverride.Number((float)p.EdgeWidth);
            values["--urui-fx-edge-color-filter"] = StyleOverride.FromKeyword(Keyword(p.EdgeColorFilter));
            values["--urui-fx-edge-color"] = StyleOverride.Color(p.EdgeColor);
            values["--urui-fx-edge-color-glow"] = StyleOverride.FromKeyword(p.EdgeColorGlow ? "true" : "false");
            values["--urui-fx-edge-shiny-progress"] = StyleOverride.Number((float)p.EdgeShinyProgress);
            values["--urui-fx-edge-shiny-width"] = StyleOverride.Number((float)p.EdgeShinyWidth);
            values["--urui-fx-edge-shiny-auto-play-speed"] = StyleOverride.Number((float)p.EdgeShinyAutoPlaySpeed);
            values["--urui-fx-pattern-area"] = StyleOverride.FromKeyword(Keyword(p.PatternArea));
            values["--urui-fx-detail-filter"] = StyleOverride.FromKeyword(Keyword(p.DetailFilter));
            values["--urui-fx-detail-intensity"] = StyleOverride.Number((float)p.DetailIntensity);
            values["--urui-fx-detail-color"] = StyleOverride.Color(p.DetailColor);
            values["--urui-fx-detail-threshold-min"] = StyleOverride.Number(p.DetailThreshold.Min);
            values["--urui-fx-detail-threshold-max"] = StyleOverride.Number(p.DetailThreshold.Max);
            values["--urui-fx-detail-texture-scale-x"] = StyleOverride.Number(p.DetailTextureScale.x);
            values["--urui-fx-detail-texture-scale-y"] = StyleOverride.Number(p.DetailTextureScale.y);
            values["--urui-fx-detail-texture-offset-x"] = StyleOverride.Number(p.DetailTextureOffset.x);
            values["--urui-fx-detail-texture-offset-y"] = StyleOverride.Number(p.DetailTextureOffset.y);
            values["--urui-fx-detail-texture-speed-x"] = StyleOverride.Number(p.DetailTextureSpeed.x);
            values["--urui-fx-detail-texture-speed-y"] = StyleOverride.Number(p.DetailTextureSpeed.y);
            values["--urui-fx-flip"] = StyleOverride.Number((float)p.Flip);
            values["--urui-fx-sampling-scale"] = StyleOverride.Number((float)p.SamplingScale);
            values["--urui-fx-allow-to-modify-mesh-shape"] = StyleOverride.FromKeyword(p.AllowToModifyMeshShape ? "true" : "false");
            return values;
        }
    }
}
