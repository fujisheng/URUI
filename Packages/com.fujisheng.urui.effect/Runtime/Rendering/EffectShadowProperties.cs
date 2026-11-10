using System;
using UnityEngine;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    internal static class EffectShadowProperties
    {
        static readonly EffectColorProperties color=new("--urui-fx-shadow-color");
        internal static readonly StylePropertyDefinition[] Parameters = {
            new("--urui-fx-shadow-mode",StyleValueType.Keyword),new("--urui-fx-shadow-color-filter",StyleValueType.Keyword),new("--urui-fx-shadow-color-glow",StyleValueType.Keyword),
            new("--urui-fx-shadow-distance-x",StyleValueType.Number),new("--urui-fx-shadow-distance-y",StyleValueType.Number),new("--urui-fx-shadow-iteration",StyleValueType.Number),
            new("--urui-fx-shadow-fade",StyleValueType.Number),new("--urui-fx-shadow-mirror-scale",StyleValueType.Number),new("--urui-fx-shadow-blur-intensity",StyleValueType.Number),
            new("--urui-fx-shadow-color",StyleValueType.Color),new("--urui-fx-flip",StyleValueType.Number)
        };
        internal static void Read(ComputedStyleSnapshot style,EffectParameters p)
        {
            p.ShadowMode=ReadMode(style.GetKeyword("--urui-fx-shadow-mode"));
            p.ShadowDistance=new Vector2(Mathf.Clamp(style.GetNumber("--urui-fx-shadow-distance-x"),-600,600),Mathf.Clamp(style.GetNumber("--urui-fx-shadow-distance-y"),-600,600));
            p.ShadowIteration=Mathf.Clamp(Mathf.RoundToInt(style.GetNumber("--urui-fx-shadow-iteration")),1,5);
            p.ShadowFade=Mathf.Clamp01(style.GetNumber("--urui-fx-shadow-fade"));
            p.ShadowMirrorScale=Mathf.Clamp(style.GetNumber("--urui-fx-shadow-mirror-scale"),0,2);
            p.ShadowBlurIntensity=Mathf.Clamp01(style.GetNumber("--urui-fx-shadow-blur-intensity"));
            p.ShadowColorFilter=EffectVisualProperties.ColorMode(style,"shadow-color-filter");
            p.ShadowColor=color.Apply(style,style.GetColor("--urui-fx-shadow-color"),p.ShadowColorFilter);
            p.ShadowColorGlow=style.GetKeyword("--urui-fx-shadow-color-glow")=="true";
            p.Flip=(Flip)Mathf.Clamp(Mathf.RoundToInt(style.GetNumber("--urui-fx-flip")),0,15);
        }
        internal static ShadowMode ReadMode(string value) => value switch {
                "none"=>ShadowMode.None,"shadow"=>ShadowMode.Shadow,"shadow3"=>ShadowMode.Shadow3,
                "outline"=>ShadowMode.Outline,"outline8"=>ShadowMode.Outline8,"mirror"=>ShadowMode.Mirror,
                _=>throw new InvalidOperationException("无效的 URUI 阴影模式。") };
        internal static void Bind(MaterialPropertyBlock properties,EffectParameters p)
        {
            properties.SetInt("_ShadowColorFilter",(int)p.ShadowColorFilter);properties.SetInt("_ShadowColorGlow",p.ShadowColorGlow ? 1 : 0);
            properties.SetVector("_ShadowColor",p.ShadowColor);properties.SetFloat("_ShadowBlurIntensity",p.ShadowBlurIntensity);
        }
    }
}
