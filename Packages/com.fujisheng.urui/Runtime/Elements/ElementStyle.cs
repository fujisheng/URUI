using System;
using System.Globalization;
using UnityEngine;

namespace URUI.Elements
{
    /// <summary>
    /// 读写真实 URUI 行内样式。计算结果由 ResolvedStyle 单独读取。
    /// </summary>
    public sealed partial class ElementStyle
    {
        readonly Element element;

        internal ElementStyle(Element element)
        {
            this.element = element;
        }

        /// <summary>
        /// 写入 CSS 属性，例如 Set("position", "absolute")。
        /// </summary>
        /// <param name="property">非空 CSS 属性名。</param>
        /// <param name="value">非 null 且不包含 NUL 的属性值，由 URUI 解析。</param>
        public void Set(string property, string value)
        {
            element.SetStyle(property, value);
        }

        /// <summary>
        /// 写入带 px 单位的属性，拒绝 NaN 和 Infinity。
        /// </summary>
        /// <param name="property">接受 px 数值的 CSS 属性名。</param>
        /// <param name="value">有限像素数值。</param>
        public void SetPixels(string property, float value)
        {
            element.SetPixels(property, value);
        }

        /// <summary>更新无单位的运行时值；不构造 CSS 字符串。</summary>
        public void SetNumber(string property, float value) => element.SetNumeric(property, value, 0);

        /// <summary>更新百分比运行时值；布局与皮肤仍由资源声明。</summary>
        public void SetPercent(string property, float value) => element.SetNumeric(property, value, 2);

        /// <summary>更新带 dp 单位的动态值，保留文档密度缩放语义。</summary>
        public void SetDensityPixels(string property, float value) => element.SetNumeric(property, value, 3);

        /// <summary>写入已注册属性的关键字序号；原生解析器校验该序号，不构造样式文本。</summary>
        public void SetKeyword(string property, int value) => element.SetNumeric(property, value, 4);

        /// <summary>更新动态颜色值；属性必须接受颜色，不生成 CSS 文本。</summary>
        public void SetColor(string property, Color value) => element.SetColor(property, value);

        /// <summary>
        /// 设置宽度（px）。
        /// </summary>
        /// <param name="value">有限像素数值，布局语义由 CSS 决定。</param>
        public void SetWidth(float value)
        {
            SetPixels("width", value);
        }

        /// <summary>
        /// 设置高度（px）。
        /// </summary>
        /// <param name="value">有限像素数值，布局语义由 CSS 决定。</param>
        public void SetHeight(float value)
        {
            SetPixels("height", value);
        }

        /// <summary>
        /// 设置距左边界的距离（px），定位规则仍由 CSS 决定。
        /// </summary>
        /// <param name="value">有限像素数值。</param>
        public void SetLeft(float value)
        {
            SetPixels("left", value);
        }

        /// <summary>
        /// 设置距上边界的距离（px）。
        /// </summary>
        /// <param name="value">有限像素数值，定位规则由 CSS 决定。</param>
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
            SetNumber("opacity", value);
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
            SetColor("background-color", value);
        }

        /// <summary>移除行内 CSS 覆盖，让类名或样式表重新决定该属性。</summary>
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
