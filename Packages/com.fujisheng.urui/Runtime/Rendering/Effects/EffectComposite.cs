using System;
using UnityEngine.Rendering;

namespace URUI.Rendering.Effects
{
    /// <summary>效果的最终目标混合；default 沿用标准 CSS 合成，显式状态使用 Unity 固定 Blend 因子。</summary>
    public readonly struct EffectComposite
    {
        public bool UsesUnityBlend { get; }
        public BlendMode Source { get; }
        public BlendMode Destination { get; }
        public EffectComposite(BlendMode source, BlendMode destination)
        {
            if ((int)source<0 || (int)source>10 || (int)destination<0 || (int)destination>10)
                throw new ArgumentOutOfRangeException(nameof(source), "Blend 因子必须是 Unity 定义的合法值。");
            UsesUnityBlend=true;Source=source;Destination=destination;
        }
    }
    /// <summary>一个计算属性满足给定值时才允许原图直接路径；其余值使用节点最终合成。</summary>
    public sealed class EffectImageCondition
    {
        public string Property { get; }
        public string DirectValue { get; }
        public EffectImageCondition(string property,string directValue)
        {
            if(string.IsNullOrWhiteSpace(property)||!property.StartsWith("--",StringComparison.Ordinal)||property.IndexOfAny(new[]{' ','\0',';',':'})>=0||string.IsNullOrWhiteSpace(directValue))
                throw new ArgumentException("直接图片条件需要自定义属性名和非空值。");
            Property=property;DirectValue=directValue;
        }
    }
}
