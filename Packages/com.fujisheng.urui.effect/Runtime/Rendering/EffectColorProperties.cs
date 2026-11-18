using UnityEngine;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    /// <summary>模式专用的数值覆盖；auto 保留颜色或预设中的分量，允许表达颜色语法不能表示的负偏移。</summary>
    internal sealed class EffectColorProperties
    {
        readonly StylePropertyDefinition hue, saturation, value, contrast, brightness, alpha;
        internal EffectColorProperties(string prefix)
        {
            hue = new(prefix + "-hue-shift", StyleValueType.Number);
            saturation = new(prefix + "-saturation-shift", StyleValueType.Number);
            value = new(prefix + "-value-shift", StyleValueType.Number);
            contrast = new(prefix + "-contrast-shift", StyleValueType.Number);
            brightness = new(prefix + "-brightness-shift", StyleValueType.Number);
            alpha = new(prefix + "-alpha", StyleValueType.Number);
        }
        internal Color Apply(EffectStyleView style, Color color, ColorFilter mode)
        {
            if (mode == ColorFilter.HsvModifier)
            {
                if (style.Contains(hue)) { color.r = Mathf.Clamp(style.GetNumber(hue.Name), -.5f, .5f); }
                if (style.Contains(saturation)) { color.g = Mathf.Clamp(style.GetNumber(saturation.Name), -1, 1); }
                if (style.Contains(value)) { color.b = Mathf.Clamp(style.GetNumber(value.Name), -1, 1); }
            }
            else if (mode == ColorFilter.Contrast)
            {
                if (style.Contains(contrast)) { color.r = Mathf.Clamp(style.GetNumber(contrast.Name), -1, 1); }
                if (style.Contains(brightness)) { color.g = Mathf.Clamp(style.GetNumber(brightness.Name), -1, 1); }
            }
            if (style.Contains(alpha)) { color.a = style.GetNumber(alpha.Name); }
            return color;
        }
    }
}
