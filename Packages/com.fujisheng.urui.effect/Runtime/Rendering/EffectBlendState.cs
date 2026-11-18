using System;
using UnityEngine.Rendering;
using URUI.Rendering.Effects;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    internal static class EffectBlendState
    {
        internal static (BlendMode,BlendMode) Factors(EffectParameters p)=>p.BlendType switch {
            BlendType.AlphaBlend=>(BlendMode.One,BlendMode.OneMinusSrcAlpha),BlendType.Multiply=>(BlendMode.DstColor,BlendMode.OneMinusSrcAlpha),
            BlendType.Additive=>(BlendMode.One,BlendMode.One),BlendType.SoftAdditive=>(BlendMode.OneMinusDstColor,BlendMode.One),
            BlendType.MultiplyAdditive=>(BlendMode.DstColor,BlendMode.One),BlendType.Custom=>(p.SrcBlendMode,p.DstBlendMode),_=>throw new InvalidOperationException("无效的 URUI Blend 模式。")};
        internal static readonly StylePropertyDefinition[] Parameters={
            new("--urui-fx-blend-type",StyleValueType.Keyword),new("--urui-fx-src-blend-mode",StyleValueType.Keyword),new("--urui-fx-dst-blend-mode",StyleValueType.Keyword)
        };
        static BlendMode Factor(string value) => value switch {
            "zero"=>BlendMode.Zero,"one"=>BlendMode.One,"dst-color"=>BlendMode.DstColor,"src-color"=>BlendMode.SrcColor,
            "one-minus-dst-color"=>BlendMode.OneMinusDstColor,"src-alpha"=>BlendMode.SrcAlpha,"one-minus-src-color"=>BlendMode.OneMinusSrcColor,
            "dst-alpha"=>BlendMode.DstAlpha,"one-minus-dst-alpha"=>BlendMode.OneMinusDstAlpha,"src-alpha-saturate"=>BlendMode.SrcAlphaSaturate,
            "one-minus-src-alpha"=>BlendMode.OneMinusSrcAlpha,_=>throw new InvalidOperationException("无效的 URUI Blend 因子："+value) };
        internal static EffectComposite Resolve(EffectLayout layout)
        {
            var style=layout.Style;
            var pair=style.GetKeyword("--urui-fx-blend-type") switch {
                "alpha-blend"=>(BlendMode.One,BlendMode.OneMinusSrcAlpha),"multiply"=>(BlendMode.DstColor,BlendMode.OneMinusSrcAlpha),
                "additive"=>(BlendMode.One,BlendMode.One),"soft-additive"=>(BlendMode.OneMinusDstColor,BlendMode.One),
                "multiply-additive"=>(BlendMode.DstColor,BlendMode.One),
                "custom"=>(Factor(style.GetKeyword("--urui-fx-src-blend-mode")),Factor(style.GetKeyword("--urui-fx-dst-blend-mode"))),
                _=>throw new InvalidOperationException("无效的 URUI Blend 模式。") };
            return new EffectComposite(pair.Item1,pair.Item2);
        }
        internal static void Read(EffectStyleView style,EffectParameters p)
        {
            p.BlendType=style.GetKeyword("--urui-fx-blend-type") switch {
                "custom"=>BlendType.Custom,"alpha-blend"=>BlendType.AlphaBlend,"multiply"=>BlendType.Multiply,
                "additive"=>BlendType.Additive,"soft-additive"=>BlendType.SoftAdditive,"multiply-additive"=>BlendType.MultiplyAdditive,
                _=>throw new InvalidOperationException("无效的 URUI Blend 模式。")};
            p.SrcBlendMode=Factor(style.GetKeyword("--urui-fx-src-blend-mode"));p.DstBlendMode=Factor(style.GetKeyword("--urui-fx-dst-blend-mode"));
        }
    }
}
