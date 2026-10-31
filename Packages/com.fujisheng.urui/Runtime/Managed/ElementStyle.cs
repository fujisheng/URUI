using System;
using System.Globalization;
using UnityEngine;

namespace URUI
{
    /// <summary>写入 RmlUi 行内样式。使用显式 Set 方法，不缓存或伪造已计算样式的 getter。</summary>
    public sealed class ElementStyle
    {
        readonly Element element;
        internal ElementStyle(Element element) { this.element = element; }

        /// <summary>写入 RCSS 属性，例如 Set("position", "absolute")。</summary>
        public void Set(string property, string value) { element.SetStyle(property, value); }

        /// <summary>写入带 px 单位的属性，拒绝 NaN 和 Infinity。</summary>
        public void SetPixels(string property, float value) { element.SetPixels(property, value); }

        /// <summary>设置宽度（px）。</summary>
        public void SetWidth(float value) { SetPixels("width", value); }

        /// <summary>设置高度（px）。</summary>
        public void SetHeight(float value) { SetPixels("height", value); }

        /// <summary>设置距左边界的距离（px），定位规则仍由 RCSS 决定。</summary>
        public void SetLeft(float value) { SetPixels("left", value); }

        /// <summary>设置距上边界的距离（px）。</summary>
        public void SetTop(float value) { SetPixels("top", value); }

        /// <summary>设置 0 到 1 的透明度。</summary>
        public void SetOpacity(float value)
        {
            RequireFinite(value);
            if (value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
            Set("opacity", value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>切换 display: block/none。恢复原本的 flex/inline 等布局请直接调用 Set。</summary>
        public void SetDisplay(bool visible) { Set("display", visible ? "block" : "none"); }

        /// <summary>以 RGBA 字节写入背景颜色。</summary>
        public void SetBackgroundColor(Color value)
        {
            element.RequireValid();
            RequireFinite(value.r); RequireFinite(value.g); RequireFinite(value.b); RequireFinite(value.a);
            Set("background-color", "#" + ColorUtility.ToHtmlStringRGBA(value));
        }

        internal static void RequireFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "值必须是有限数字。");
        }
    }
}
