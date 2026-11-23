using System;
using System.Globalization;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    /// <summary>
    /// CSS 长度单位；Unset 表示没有行内覆盖。
    /// </summary>
    public enum LengthUnit
    {
        Unset,
        Pixel,
        Percent,
        Em,
        Rem,
        Auto
    }
    /// <summary>
    /// 保留单位的样式长度，所有数值必须有限。
    /// </summary>
    public readonly struct Length
    {
        /// <summary>
        /// 长度数值；Auto/Unset 不使用此值。
        /// </summary>
        public float Value
        {
            get;
        }
        /// <summary>
        /// 长度单位。
        /// </summary>
        public LengthUnit Unit
        {
            get;
        }
        Length(float value, LengthUnit unit)
        {
            ElementStyle.RequireFinite(value);
            Value = value;
            Unit = unit;
        }
        /// <summary>
        /// 移除行内覆盖。
        /// </summary>
        public static Length Unset => default;
        /// <summary>
        /// 由布局决定的长度。
        /// </summary>
        public static Length Auto => new Length(0, LengthUnit.Auto);
        /// <summary>
        /// 像素长度。
        /// </summary>
        public static Length Pixels(float value) => new Length(value, LengthUnit.Pixel);
        /// <summary>
        /// 百分比长度，100 表示完整父级尺寸。
        /// </summary>
        public static Length Percent(float value) => new Length(value, LengthUnit.Percent);
        /// <summary>
        /// 相对当前字号的长度。
        /// </summary>
        public static Length Em(float value) => new Length(value, LengthUnit.Em);
        /// <summary>
        /// 相对根字号的长度。
        /// </summary>
        public static Length Rem(float value) => new Length(value, LengthUnit.Rem);
        /// <summary>
        /// 数值隐式转换为像素。
        /// </summary>
        public static implicit operator Length(float value) => Pixels(value);
        /// <summary>
        /// 返回保留原单位的 CSS 表达式。
        /// </summary>
        public override string ToString()
        {
            if (Unit == LengthUnit.Unset)
            {
                return null;
            }

            if (Unit == LengthUnit.Auto)
            {
                return "auto";
            }

            string suffix = Unit == LengthUnit.Percent ? "%" : Unit == LengthUnit.Em ? "em" : Unit == LengthUnit.Rem ? "rem" : "px";
            return Value.ToString("R", CultureInfo.InvariantCulture) + suffix;
        }
        internal static Length Parse(string value)
        {
            if (value == null)
            {
                return Unset;
            }

            if (value == "auto")
            {
                return Auto;
            }

            if (value.EndsWith("rem", StringComparison.Ordinal))
            {
                return Rem(float.Parse(value.AsSpan(0, value.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            if (value.EndsWith("px", StringComparison.Ordinal))
            {
                return Pixels(float.Parse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            if (value.EndsWith("%", StringComparison.Ordinal))
            {
                return Percent(float.Parse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            if (value.EndsWith("em", StringComparison.Ordinal))
            {
                return Em(float.Parse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            return Pixels(float.Parse(value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// 对应 CSS 的 DisplayStyle 枚举；Unset 移除行内覆盖。
    /// </summary>
    public enum DisplayStyle
    {
        Unset,
        Block,
        Inline,
        InlineBlock,
        Flex,
        InlineFlex,
        None
    }

    /// <summary>
    /// 对应 CSS 的 Position 枚举；Unset 移除行内覆盖。
    /// </summary>
    public enum Position
    {
        Unset,
        Static,
        Relative,
        Absolute
    }

    /// <summary>
    /// 对应 CSS 的 FlexDirection 枚举；Unset 移除行内覆盖。
    /// </summary>
    public enum FlexDirection
    {
        Unset,
        Row,
        RowReverse,
        Column,
        ColumnReverse
    }

    /// <summary>
    /// 对应 CSS 的 FlexWrap 枚举；Unset 移除行内覆盖。
    /// </summary>
    public enum FlexWrap
    {
        Unset,
        Nowrap,
        Wrap,
        WrapReverse
    }

    /// <summary>
    /// 对应 CSS 的 Align 枚举；Unset 移除行内覆盖。
    /// </summary>
    public enum Align
    {
        Unset,
        Auto,
        FlexStart,
        FlexEnd,
        Center,
        Stretch,
        Baseline
    }

    /// <summary>
    /// 对应 CSS 的 Justify 枚举；Unset 移除行内覆盖。
    /// </summary>
    public enum Justify
    {
        Unset,
        FlexStart,
        FlexEnd,
        Center,
        SpaceBetween,
        SpaceAround,
        SpaceEvenly
    }

    /// <summary>
    /// 对应 CSS 的 Overflow 枚举；Unset 移除行内覆盖。
    /// </summary>
    public enum Overflow
    {
        Unset,
        Visible,
        Hidden,
        Auto,
        Scroll
    }

    public sealed partial class ElementStyle
    {
        /// <summary>
        /// 读取真实行内覆盖；未设置时为 null，不包含样式表规则。
        /// </summary>
        public string Get(string property)
        {
            URUI.Documents.Document.RequireName(property, nameof(property));
            return element.ReadStyle(property, true);
        }
        void SetLength(string name, Length value)
        {
            switch (value.Unit)
            {
                case LengthUnit.Unset:
                    Remove(name);
                    break;
                case LengthUnit.Pixel:
                    SetPixels(name, value.Value);
                    break;
                case LengthUnit.Percent:
                    SetPercent(name, value.Value);
                    break;
                case LengthUnit.Auto:
                    Set(name, "auto");
                    break;
                default:
                    Set(name, value.ToString());
                    break;
            }
        }
        T GetEnum<T>(string name) where T : struct
        {
            string value = Get(name);
            if (value == null)
            {
                return default;
            }
            if (EnumStyleText<T>.ByName.TryGetValue(value, out var parsed))
            {
                return parsed;
            }
            return (T)Enum.Parse(typeof(T), value.Replace("-", ""), true);
        }
        static class EnumStyleText<T> where T : struct
        {
            internal static readonly System.Collections.Generic.Dictionary<T, string> Values;
            internal static readonly System.Collections.Generic.Dictionary<string, T> ByName;

            static EnumStyleText()
            {
                var values = new System.Collections.Generic.Dictionary<T, string>();
                var byName = new System.Collections.Generic.Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
                foreach (T value in Enum.GetValues(typeof(T)))
                {
                    if (Convert.ToInt32(value) == 0)
                    {
                        values.Add(value, null);
                        continue;
                    }
                    var text = value.ToString();
                    var result = new System.Text.StringBuilder();
                    for (var i = 0; i < text.Length; i++)
                    {
                        if (i > 0 && char.IsUpper(text[i]))
                        {
                            result.Append('-');
                        }
                        result.Append(char.ToLowerInvariant(text[i]));
                    }
                    var css = result.ToString();
                    values.Add(value, css);
                    byName[css] = value;
                }
                Values = values;
                ByName = byName;
            }
        }
        void SetEnum<T>(string name, T value) where T : struct
        {
            // 枚举定义与 CSS 规范文本固定；避免每次动态位置写入都反射、装箱和格式化。
            if (!EnumStyleText<T>.Values.TryGetValue(value, out var text))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (text == null)
            {
                Remove(name);
                return;
            }
            Set(name, text);
        }
        /// <summary>
        /// width 的行内长度覆盖。
        /// </summary>
        public Length Width
        {
            get => Length.Parse(Get("width"));
            set => SetLength("width", value);
        }
        /// <summary>
        /// height 的行内长度覆盖。
        /// </summary>
        public Length Height
        {
            get => Length.Parse(Get("height"));
            set => SetLength("height", value);
        }
        /// <summary>
        /// min-width 的行内长度覆盖。
        /// </summary>
        public Length MinWidth
        {
            get => Length.Parse(Get("min-width"));
            set => SetLength("min-width", value);
        }
        /// <summary>
        /// min-height 的行内长度覆盖。
        /// </summary>
        public Length MinHeight
        {
            get => Length.Parse(Get("min-height"));
            set => SetLength("min-height", value);
        }
        /// <summary>
        /// max-width 的行内长度覆盖。
        /// </summary>
        public Length MaxWidth
        {
            get => Length.Parse(Get("max-width"));
            set => SetLength("max-width", value);
        }
        /// <summary>
        /// max-height 的行内长度覆盖。
        /// </summary>
        public Length MaxHeight
        {
            get => Length.Parse(Get("max-height"));
            set => SetLength("max-height", value);
        }
        /// <summary>
        /// left 的行内长度覆盖。
        /// </summary>
        public Length Left
        {
            get => Length.Parse(Get("left"));
            set => SetLength("left", value);
        }
        /// <summary>
        /// top 的行内长度覆盖。
        /// </summary>
        public Length Top
        {
            get => Length.Parse(Get("top"));
            set => SetLength("top", value);
        }
        /// <summary>
        /// right 的行内长度覆盖。
        /// </summary>
        public Length Right
        {
            get => Length.Parse(Get("right"));
            set => SetLength("right", value);
        }
        /// <summary>
        /// bottom 的行内长度覆盖。
        /// </summary>
        public Length Bottom
        {
            get => Length.Parse(Get("bottom"));
            set => SetLength("bottom", value);
        }
        /// <summary>
        /// margin-left 的行内长度覆盖。
        /// </summary>
        public Length MarginLeft
        {
            get => Length.Parse(Get("margin-left"));
            set => SetLength("margin-left", value);
        }
        /// <summary>
        /// margin-right 的行内长度覆盖。
        /// </summary>
        public Length MarginRight
        {
            get => Length.Parse(Get("margin-right"));
            set => SetLength("margin-right", value);
        }
        /// <summary>
        /// margin-top 的行内长度覆盖。
        /// </summary>
        public Length MarginTop
        {
            get => Length.Parse(Get("margin-top"));
            set => SetLength("margin-top", value);
        }
        /// <summary>
        /// margin-bottom 的行内长度覆盖。
        /// </summary>
        public Length MarginBottom
        {
            get => Length.Parse(Get("margin-bottom"));
            set => SetLength("margin-bottom", value);
        }
        /// <summary>
        /// padding-left 的行内长度覆盖。
        /// </summary>
        public Length PaddingLeft
        {
            get => Length.Parse(Get("padding-left"));
            set => SetLength("padding-left", value);
        }
        /// <summary>
        /// padding-right 的行内长度覆盖。
        /// </summary>
        public Length PaddingRight
        {
            get => Length.Parse(Get("padding-right"));
            set => SetLength("padding-right", value);
        }
        /// <summary>
        /// padding-top 的行内长度覆盖。
        /// </summary>
        public Length PaddingTop
        {
            get => Length.Parse(Get("padding-top"));
            set => SetLength("padding-top", value);
        }
        /// <summary>
        /// padding-bottom 的行内长度覆盖。
        /// </summary>
        public Length PaddingBottom
        {
            get => Length.Parse(Get("padding-bottom"));
            set => SetLength("padding-bottom", value);
        }
        /// <summary>
        /// font-size 的行内长度覆盖。
        /// </summary>
        public Length FontSize
        {
            get => Length.Parse(Get("font-size"));
            set => SetLength("font-size", value);
        }
        /// <summary>
        /// flex-basis 的行内长度覆盖。
        /// </summary>
        public Length FlexBasis
        {
            get => Length.Parse(Get("flex-basis"));
            set => SetLength("flex-basis", value);
        }
        /// <summary>
        /// border-left-width 的行内长度覆盖。
        /// </summary>
        public Length BorderLeftWidth
        {
            get => Length.Parse(Get("border-left-width"));
            set => SetLength("border-left-width", value);
        }
        /// <summary>
        /// border-right-width 的行内长度覆盖。
        /// </summary>
        public Length BorderRightWidth
        {
            get => Length.Parse(Get("border-right-width"));
            set => SetLength("border-right-width", value);
        }
        /// <summary>
        /// border-top-width 的行内长度覆盖。
        /// </summary>
        public Length BorderTopWidth
        {
            get => Length.Parse(Get("border-top-width"));
            set => SetLength("border-top-width", value);
        }
        /// <summary>
        /// border-bottom-width 的行内长度覆盖。
        /// </summary>
        public Length BorderBottomWidth
        {
            get => Length.Parse(Get("border-bottom-width"));
            set => SetLength("border-bottom-width", value);
        }
        /// <summary>
        /// display 的行内枚举覆盖。
        /// </summary>
        public DisplayStyle Display
        {
            get => GetEnum<DisplayStyle>("display");
            set => SetEnum("display", value);
        }
        /// <summary>
        /// position 的行内枚举覆盖。
        /// </summary>
        public Position Position
        {
            get => GetEnum<Position>("position");
            set => SetEnum("position", value);
        }
        /// <summary>
        /// flex-direction 的行内枚举覆盖。
        /// </summary>
        public FlexDirection FlexDirection
        {
            get => GetEnum<FlexDirection>("flex-direction");
            set => SetEnum("flex-direction", value);
        }
        /// <summary>
        /// flex-wrap 的行内枚举覆盖。
        /// </summary>
        public FlexWrap FlexWrap
        {
            get => GetEnum<FlexWrap>("flex-wrap");
            set => SetEnum("flex-wrap", value);
        }
        /// <summary>
        /// align-items 的行内枚举覆盖。
        /// </summary>
        public Align AlignItems
        {
            get => GetEnum<Align>("align-items");
            set => SetEnum("align-items", value);
        }
        /// <summary>
        /// align-self 的行内枚举覆盖。
        /// </summary>
        public Align AlignSelf
        {
            get => GetEnum<Align>("align-self");
            set => SetEnum("align-self", value);
        }
        /// <summary>
        /// justify-content 的行内枚举覆盖。
        /// </summary>
        public Justify JustifyContent
        {
            get => GetEnum<Justify>("justify-content");
            set => SetEnum("justify-content", value);
        }
        /// <summary>
        /// overflow 的行内枚举覆盖。
        /// </summary>
        public Overflow Overflow
        {
            get => GetEnum<Overflow>("overflow");
            set => SetEnum("overflow", value);
        }
        /// <summary>
        /// 水平方向的行内溢出处理。
        /// </summary>
        public Overflow OverflowX
        {
            get => GetEnum<Overflow>("overflow-x");
            set => SetEnum("overflow-x", value);
        }
        /// <summary>
        /// 垂直方向的行内溢出处理。
        /// </summary>
        public Overflow OverflowY
        {
            get => GetEnum<Overflow>("overflow-y");
            set => SetEnum("overflow-y", value);
        }
        /// <summary>
        /// opacity 的行内数值；null 移除覆盖。
        /// </summary>
        public float? Opacity
        {
            get => Get("opacity") is string text ? float.Parse(text, CultureInfo.InvariantCulture) : (float?)null;
            set
            {
                if (!value.HasValue)
                {
                    Remove("opacity");
                    return;
                }
                RequireFinite(value.Value);
                SetOpacity(value.Value);
            }
        }
        /// <summary>
        /// flex-grow 的行内数值；null 移除覆盖。
        /// </summary>
        public float? FlexGrow
        {
            get => Get("flex-grow") is string text ? float.Parse(text, CultureInfo.InvariantCulture) : (float?)null;
            set
            {
                if (!value.HasValue)
                {
                    Remove("flex-grow");
                    return;
                }
                SetNumber("flex-grow", value.Value);
            }
        }
        /// <summary>
        /// flex-shrink 的行内数值；null 移除覆盖。
        /// </summary>
        public float? FlexShrink
        {
            get => Get("flex-shrink") is string text ? float.Parse(text, CultureInfo.InvariantCulture) : (float?)null;
            set
            {
                if (!value.HasValue)
                {
                    Remove("flex-shrink");
                    return;
                }
                SetNumber("flex-shrink", value.Value);
            }
        }
        /// <summary>
        /// 背景颜色行内覆盖；null 移除覆盖。
        /// </summary>
        public Color? BackgroundColor
        {
            get
            {
                string text = Get("background-color");
                if (text == null)
                {
                    return null;
                }

                if (ColorUtility.TryParseHtmlString(text, out var value) || TryParseCssRgb(text, out value))
                {
                    return value;
                }

                throw new NotSupportedException("请通过 Get 读取此 CSS 颜色表达式：" + text);
            }
            set
            {
                if (value.HasValue)
                {
                    SetBackgroundColor(value.Value);
                }
                else
                {
                    Remove("background-color");
                }
            }
        }

        static bool TryParseCssRgb(string text, out Color color)
        {
            color = default;
            var span = text.AsSpan().Trim();
            bool hasAlpha;
            if (span.StartsWith("rgba(".AsSpan(), StringComparison.OrdinalIgnoreCase) && span.EndsWith(")".AsSpan()))
            {
                hasAlpha = true;
                span = span.Slice(5, span.Length - 6);
            }
            else if (span.StartsWith("rgb(".AsSpan(), StringComparison.OrdinalIgnoreCase) && span.EndsWith(")".AsSpan()))
            {
                hasAlpha = false;
                span = span.Slice(4, span.Length - 5);
            }
            else
            {
                return false;
            }
            Span<float> parts = stackalloc float[4];
            int count = 0;
            while (!span.IsEmpty)
            {
                if (count >= 4)
                {
                    return false;
                }
                int comma = span.IndexOf(',');
                var token = (comma < 0 ? span : span.Slice(0, comma)).Trim();
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out parts[count]))
                {
                    return false;
                }
                count++;
                span = comma < 0 ? ReadOnlySpan<char>.Empty : span.Slice(comma + 1);
            }
            if ((hasAlpha && count != 4) || (!hasAlpha && count != 3))
            {
                return false;
            }
            float a = hasAlpha ? (parts[3] > 1f ? parts[3] / 255f : parts[3]) : 1f;
            color = new Color(parts[0] / 255f, parts[1] / 255f, parts[2] / 255f, Mathf.Clamp01(a));
            return true;
        }
    }
}
