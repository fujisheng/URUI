using System;
using UnityEngine;
using URUI.Rendering.Effects;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    internal static class EffectGradation
    {
        internal static void Read(EffectStyleView style, EffectParameters p)
        {
            p.GradationMode = style.GetKeyword("--urui-fx-gradation-mode") switch
            {
                "none" => GradationMode.None,
                "horizontal" => GradationMode.Horizontal,
                "horizontal-gradient" => GradationMode.HorizontalGradient,
                "vertical" => GradationMode.Vertical,
                "vertical-gradient" => GradationMode.VerticalGradient,
                "radial-fast" => GradationMode.RadialFast,
                "radial-detail" => GradationMode.RadialDetail,
                "diagonal-to-right-bottom" => GradationMode.DiagonalToRightBottom,
                "diagonal-to-left-bottom" => GradationMode.DiagonalToLeftBottom,
                "angle" => GradationMode.Angle,
                "angle-gradient" => GradationMode.AngleGradient,
                "diagonal" => GradationMode.Diagonal,
                _ => throw new InvalidOperationException("无效的 URUI 渐变模式。")
            };
            p.GradationColorFilter = (GradationColorFilter)EffectVisualProperties.ColorMode(style, "gradation-color-filter");
            p.GradationIntensity = Mathf.Clamp01(style.GetNumber("--urui-fx-gradation-intensity"));
            p.GradationColor1 = style.GetColor("--urui-fx-gradation-color1");
            p.GradationColor2 = style.GetColor("--urui-fx-gradation-color2");
            p.GradationColor3 = style.GetColor("--urui-fx-gradation-color3");
            p.GradationColor4 = style.GetColor("--urui-fx-gradation-color4");
            p.GradationOffset = Mathf.Clamp(style.GetNumber("--urui-fx-gradation-offset"), -1, 1);
            p.GradationScale = Mathf.Clamp(style.GetNumber("--urui-fx-gradation-scale"), .01f, 10);
            p.GradationRotation = Mathf.Clamp(style.GetNumber("--urui-fx-gradation-rotation"), 0, 360);
        }
    }
}
