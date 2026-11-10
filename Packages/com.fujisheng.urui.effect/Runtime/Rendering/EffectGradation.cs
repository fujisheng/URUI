using System;
using UnityEngine;
using URUI.Rendering.Effects;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    internal static class EffectGradation
    {
        internal static readonly StylePropertyDefinition[] Parameters = {
            new("--urui-fx-gradation-mode",StyleValueType.Keyword),new("--urui-fx-gradation-color-filter",StyleValueType.Keyword),
            new("--urui-fx-gradation-intensity",StyleValueType.Number),new("--urui-fx-gradation-offset",StyleValueType.Number),
            new("--urui-fx-gradation-scale",StyleValueType.Number),new("--urui-fx-gradation-rotation",StyleValueType.Number),
            new("--urui-fx-gradation-gradient",StyleValueType.Image),
            new("--urui-fx-gradation-color1",StyleValueType.Color),new("--urui-fx-gradation-color2",StyleValueType.Color),
            new("--urui-fx-gradation-color3",StyleValueType.Color),new("--urui-fx-gradation-color4",StyleValueType.Color)
        };
        internal static void Read(ComputedStyleSnapshot style,EffectParameters p)
        {
            p.GradationMode=style.GetKeyword("--urui-fx-gradation-mode") switch {
                "none"=>GradationMode.None,"horizontal"=>GradationMode.Horizontal,"horizontal-gradient"=>GradationMode.HorizontalGradient,
                "vertical"=>GradationMode.Vertical,"vertical-gradient"=>GradationMode.VerticalGradient,"radial-fast"=>GradationMode.RadialFast,
                "radial-detail"=>GradationMode.RadialDetail,"diagonal-to-right-bottom"=>GradationMode.DiagonalToRightBottom,
                "diagonal-to-left-bottom"=>GradationMode.DiagonalToLeftBottom,"angle"=>GradationMode.Angle,"angle-gradient"=>GradationMode.AngleGradient,
                "diagonal"=>GradationMode.Diagonal,_=>throw new InvalidOperationException("无效的 URUI 渐变模式。") };
            p.GradationColorFilter=(GradationColorFilter)EffectVisualProperties.ColorMode(style,"gradation-color-filter");
            p.GradationIntensity=Mathf.Clamp01(style.GetNumber("--urui-fx-gradation-intensity"));
            p.GradationColor1=style.GetColor("--urui-fx-gradation-color1");p.GradationColor2=style.GetColor("--urui-fx-gradation-color2");
            p.GradationColor3=style.GetColor("--urui-fx-gradation-color3");p.GradationColor4=style.GetColor("--urui-fx-gradation-color4");
            p.GradationOffset=Mathf.Clamp(style.GetNumber("--urui-fx-gradation-offset"),-1,1);
            p.GradationScale=Mathf.Clamp(style.GetNumber("--urui-fx-gradation-scale"),.01f,10);
            p.GradationRotation=Mathf.Clamp(style.GetNumber("--urui-fx-gradation-rotation"),0,360);
        }
        internal static void Bind(MaterialPropertyBlock block,EffectFrame frame,EffectParameters p,Texture gradient)
        {
            block.SetInt("_GradationMode",(int)p.GradationMode);block.SetInt("_GradationColorFilter",(int)p.GradationColorFilter);
            block.SetFloat("_GradationIntensity",p.GradationIntensity);block.SetVector("_GradationColor1",p.GradationColor1);
            block.SetVector("_GradationColor2",p.GradationColor2);block.SetVector("_GradationColor3",p.GradationColor3);block.SetVector("_GradationColor4",p.GradationColor4);
            block.SetTexture("_GradationTex",gradient ? gradient : Texture2D.whiteTexture);
            var inverse=1/p.GradationScale;
            var gradientMode=p.GradationMode==GradationMode.HorizontalGradient || p.GradationMode==GradationMode.VerticalGradient || p.GradationMode==GradationMode.AngleGradient;
            var radial=p.GradationMode==GradationMode.RadialFast || p.GradationMode==GradationMode.RadialDetail;
            var offset=gradientMode ? -.5f*(inverse+1)-p.GradationOffset : radial ? p.GradationOffset : p.GradationOffset*(inverse+1)/2-inverse*.5f+.5f;
            block.SetVector("_GradationTex_ST",new Vector4(inverse,1,offset,0));
            var rotation=p.GradationMode switch {
                GradationMode.DiagonalToLeftBottom=>135,GradationMode.DiagonalToRightBottom=>45,GradationMode.Vertical=>90,
                GradationMode.VerticalGradient=>-90,GradationMode.Angle or GradationMode.AngleGradient=>p.GradationRotation,_=>0 };
            var size=frame.Layout.RootContentRect.size;var scale=new Vector3(1/Mathf.Max(.001f,size.x),-1/Mathf.Max(.001f,size.y),1);
            if(p.TransitionKeepAspectRatio) {scale.x=Mathf.Min(scale.x,-scale.y);scale.y=-scale.x;}
            if((p.Flip&Flip.Effect)!=0) {if((p.Flip&Flip.Horizontal)!=0)scale.x=-scale.x;if((p.Flip&Flip.Vertical)!=0)scale.y=-scale.y;}
            var radians=rotation*Mathf.Deg2Rad;var multiplier=Mathf.Max(Mathf.Abs(Mathf.Cos(radians)-Mathf.Sin(radians)),Mathf.Abs(Mathf.Cos(radians)+Mathf.Sin(radians)));
            var center=frame.Layout.RootContentRect.center;
            block.SetMatrix("_EffectGradMatrix",Matrix4x4.TRS(new Vector3(.5f,.5f),Quaternion.Euler(0,0,rotation),scale/multiplier)*
                Matrix4x4.Translate(new Vector3(-center.x,-center.y))*frame.Layout.RootToDocument.inverse);
        }
    }
}
