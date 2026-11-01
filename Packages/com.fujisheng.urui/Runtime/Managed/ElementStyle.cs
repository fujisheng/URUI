using System;
using System.Globalization;
using UnityEngine;

namespace URUI
{
    /// <summary>
    /// 写入 RmlUi 行内样式。使用显式 Set 方法，不缓存或伪造已计算样式的 getter。
    /// </summary>
    public sealed class ElementStyle
    {
        readonly Element element;

        internal ElementStyle(Element element)
        {
            this.element = element;
        }

        /// <summary>
        /// 写入 RCSS 属性，例如 Set("position", "absolute")。
        /// </summary>
        /// <param name="property">非空 RCSS 属性名。</param>
        /// <param name="value">非 null 且不包含 NUL 的属性值，由 RmlUi 解析。</param>
        public void Set(string property, string value)
        {
            element.SetStyle(property, value);
        }

        /// <summary>
        /// 写入带 px 单位的属性，拒绝 NaN 和 Infinity。
        /// </summary>
        /// <param name="property">接受 px 数值的 RCSS 属性名。</param>
        /// <param name="value">有限像素数值。</param>
        public void SetPixels(string property, float value)
        {
            element.SetPixels(property, value);
        }

        /// <summary>
        /// 设置宽度（px）。
        /// </summary>
        /// <param name="value">有限像素数值，布局语义由 RCSS 决定。</param>
        public void SetWidth(float value)
        {
            SetPixels("width", value);
        }

        /// <summary>
        /// 设置高度（px）。
        /// </summary>
        /// <param name="value">有限像素数值，布局语义由 RCSS 决定。</param>
        public void SetHeight(float value)
        {
            SetPixels("height", value);
        }

        /// <summary>
        /// 设置距左边界的距离（px），定位规则仍由 RCSS 决定。
        /// </summary>
        /// <param name="value">有限像素数值。</param>
        public void SetLeft(float value)
        {
            SetPixels("left", value);
        }

        /// <summary>
        /// 设置距上边界的距离（px）。
        /// </summary>
        /// <param name="value">有限像素数值，定位规则由 RCSS 决定。</param>
        public void SetTop(float value)
        {
            SetPixels("top", value);
        }

        /// <summary>
        /// 设置 0 到 1 的透明度。
        /// </summary>
        /// <param name="value">包含边界的 0 到 1 有限数值。</param>
        public void SetOpacity(float value)
        {
            RequireFinite(value);
            if (value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            // RCSS 使用小数点，不能随设备区域设置写成逗号。
            Set("opacity", value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 切换 display: block/none。恢复原本的 flex/inline 等布局请直接调用 Set。
        /// </summary>
        /// <param name="visible">true 使用 block，false 使用 none。</param>
        public void SetDisplay(bool visible)
        {
            Set("display", visible ? "block" : "none");
        }

        /// <summary>
        /// 以 RGBA 字节写入背景颜色。
        /// </summary>
        /// <param name="value">分量均为有限数值的 Unity 颜色，写入时转换为 RGBA 字节。</param>
        public void SetBackgroundColor(Color value)
        {
            element.RequireValid();
            RequireFinite(value.r);
            RequireFinite(value.g);
            RequireFinite(value.b);
            RequireFinite(value.a);
            Set("background-color", "#" + ColorUtility.ToHtmlStringRGBA(value));
        }

        /// <summary>移除行内 RCSS 覆盖，让类名或样式表重新决定该属性。</summary>
        public void Remove(string property)
        {
            element.RemoveStyle(property);
        }

        internal static void RequireFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "值必须是有限数字。");
            }
        }
    }
}
