using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Styles;
namespace URUI.Effect.Rendering
{
    // 私有同步视图只借用模块字典。公开出口物化独立快照，不能向调用者暴露这些可复用数据。
    internal readonly struct EffectStyleView
    {
        readonly ComputedStyleSnapshot author;
        readonly IReadOnlyDictionary<string, StyleOverride> basis;
        readonly IReadOnlyDictionary<string, StyleOverride> animated;
        internal EffectStyleView(ComputedStyleSnapshot author, IReadOnlyDictionary<string, StyleOverride> basis, IReadOnlyDictionary<string, StyleOverride> animated = null)
        {
            this.author = author;
            this.basis = basis;
            this.animated = animated;
        }
        public static implicit operator EffectStyleView(ComputedStyleSnapshot value) => new(value, null);
        internal bool TryOverride(string name, out StyleOverride value)
        {
            var origin = author.GetOrigin(name);
            if (origin <= StyleValueOrigin.Author && animated != null && animated.TryGetValue(name, out value))
            {
                return true;
            }
            if (origin <= StyleValueOrigin.UserAgent && basis != null && basis.TryGetValue(name, out value))
            {
                return true;
            }
            value = default;
            return false;
        }
        internal float GetNumber(string name)
        {
            if (!TryOverride(name, out var value))
            {
                return author.GetNumber(name);
            }
            if (value.TryGetNumber(out var result))
            {
                return result;
            }
            throw new InvalidOperationException("数值覆盖类型不匹配：" + name);
        }
        internal Color GetColor(string name)
        {
            if (!TryOverride(name, out var value))
            {
                return author.GetColor(name);
            }
            if (value.TryGetColor(out var result))
            {
                return result;
            }
            throw new InvalidOperationException("颜色覆盖类型不匹配：" + name);
        }
        internal string GetKeyword(string name)
        {
            if (!TryOverride(name, out var value))
            {
                return author.GetKeyword(name);
            }
            if (value.TryGetKeyword(out var result))
            {
                return result;
            }
            throw new InvalidOperationException("关键字覆盖类型不匹配：" + name);
        }
        internal bool Contains(StylePropertyDefinition property) => author.Contains(property);
        internal ComputedStyleSnapshot Materialize()
        {
            var result = basis == null ? author : author.WithOverrides(basis, StyleValueOrigin.UserAgent);
            return animated == null ? result : result.WithOverrides(animated, StyleValueOrigin.Author);
        }
    }
}
