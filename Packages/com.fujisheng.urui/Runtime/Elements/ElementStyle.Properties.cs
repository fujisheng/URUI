using System;
using System.Globalization;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    /// <summary>RCSS 长度单位；Unset 表示没有行内覆盖。</summary>
    public enum LengthUnit { Unset, Pixel, Percent, Em, Rem, Auto }
    /// <summary>保留单位的样式长度，所有数值必须有限。</summary>
    public readonly struct Length
    {
        /// <summary>长度数值；Auto/Unset 不使用此值。</summary>
        public float Value { get; }
        /// <summary>长度单位。</summary>
        public LengthUnit Unit { get; }
        Length(float value, LengthUnit unit)
        {
            ElementStyle.RequireFinite(value);
            Value = value; Unit = unit;
        }
        /// <summary>移除行内覆盖。</summary>
        public static Length Unset => default;
        /// <summary>由布局决定的长度。</summary>
        public static Length Auto => new Length(0, LengthUnit.Auto);
        /// <summary>像素长度。</summary>
        public static Length Pixels(float value) => new Length(value, LengthUnit.Pixel);
        /// <summary>百分比长度，100 表示完整父级尺寸。</summary>
        public static Length Percent(float value) => new Length(value, LengthUnit.Percent);
        /// <summary>相对当前字号的长度。</summary>
        public static Length Em(float value) => new Length(value, LengthUnit.Em);
        /// <summary>相对根字号的长度。</summary>
        public static Length Rem(float value) => new Length(value, LengthUnit.Rem);
        /// <summary>数值隐式转换为像素。</summary>
        public static implicit operator Length(float value) => Pixels(value);
        /// <summary>返回保留原单位的 RCSS 表达式。</summary>
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

            foreach (string suffix in new[] { "rem", "px", "%", "em" })
            {
                if (!value.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }

                float amount = float.Parse(value.Substring(0, value.Length - suffix.Length), CultureInfo.InvariantCulture);
                return suffix == "rem" ? Rem(amount) : suffix == "em" ? Em(amount) : suffix == "%" ? Percent(amount) : Pixels(amount);
            }
            return Pixels(float.Parse(value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>对应 RCSS 的 DisplayStyle 枚举；Unset 移除行内覆盖。</summary>
    public enum DisplayStyle { Unset, Block, Inline, InlineBlock, Flex, InlineFlex, None }

    /// <summary>对应 RCSS 的 Position 枚举；Unset 移除行内覆盖。</summary>
    public enum Position { Unset, Static, Relative, Absolute }

    /// <summary>对应 RCSS 的 FlexDirection 枚举；Unset 移除行内覆盖。</summary>
    public enum FlexDirection { Unset, Row, RowReverse, Column, ColumnReverse }

    /// <summary>对应 RCSS 的 FlexWrap 枚举；Unset 移除行内覆盖。</summary>
    public enum FlexWrap { Unset, Nowrap, Wrap, WrapReverse }

    /// <summary>对应 RCSS 的 Align 枚举；Unset 移除行内覆盖。</summary>
    public enum Align { Unset, Auto, FlexStart, FlexEnd, Center, Stretch, Baseline }

    /// <summary>对应 RCSS 的 Justify 枚举；Unset 移除行内覆盖。</summary>
    public enum Justify { Unset, FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }

    /// <summary>对应 RCSS 的 Overflow 枚举；Unset 移除行内覆盖。</summary>
    public enum Overflow { Unset, Visible, Hidden, Auto, Scroll }

    public sealed partial class ElementStyle
    {
        /// <summary>读取真实行内覆盖；未设置时为 null，不包含样式表规则。</summary>
        public string Get(string property)
        {
            URUI.Documents.Document.RequireName(property, nameof(property));
            return NativeMethods.ReadStyle(element.CheckedToken(), property, true);
        }
        void SetLength(string name, Length value)
        {
            if (value.Unit == LengthUnit.Unset)
            {
                Remove(name);
            }
            else
            {
                Set(name, value.ToString());
            }
        }
        T GetEnum<T>(string name) where T : struct
        {
            string value = Get(name);
            return value == null ? default : (T)Enum.Parse(typeof(T), value.Replace("-", ""), true);
        }
        void SetEnum<T>(string name, T value) where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (Convert.ToInt32(value) == 0) { Remove(name); return; }
            string text = value.ToString();
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && char.IsUpper(text[i]))
                {
                    result.Append('-');
                }

                result.Append(char.ToLowerInvariant(text[i]));
            }
            Set(name, result.ToString());
        }
        /// <summary>width 的行内长度覆盖。</summary>
        public Length Width
        {
            get => Length.Parse(Get("width"));
            set => SetLength("width", value);
        }
        /// <summary>height 的行内长度覆盖。</summary>
        public Length Height
        {
            get => Length.Parse(Get("height"));
            set => SetLength("height", value);
        }
        /// <summary>min-width 的行内长度覆盖。</summary>
        public Length MinWidth
        {
            get => Length.Parse(Get("min-width"));
            set => SetLength("min-width", value);
        }
        /// <summary>min-height 的行内长度覆盖。</summary>
        public Length MinHeight
        {
            get => Length.Parse(Get("min-height"));
            set => SetLength("min-height", value);
        }
        /// <summary>max-width 的行内长度覆盖。</summary>
        public Length MaxWidth
        {
            get => Length.Parse(Get("max-width"));
            set => SetLength("max-width", value);
        }
        /// <summary>max-height 的行内长度覆盖。</summary>
        public Length MaxHeight
        {
            get => Length.Parse(Get("max-height"));
            set => SetLength("max-height", value);
        }
        /// <summary>left 的行内长度覆盖。</summary>
        public Length Left
        {
            get => Length.Parse(Get("left"));
            set => SetLength("left", value);
        }
        /// <summary>top 的行内长度覆盖。</summary>
        public Length Top
        {
            get => Length.Parse(Get("top"));
            set => SetLength("top", value);
        }
        /// <summary>right 的行内长度覆盖。</summary>
        public Length Right
        {
            get => Length.Parse(Get("right"));
            set => SetLength("right", value);
        }
        /// <summary>bottom 的行内长度覆盖。</summary>
        public Length Bottom
        {
            get => Length.Parse(Get("bottom"));
            set => SetLength("bottom", value);
        }
        /// <summary>margin-left 的行内长度覆盖。</summary>
        public Length MarginLeft
        {
            get => Length.Parse(Get("margin-left"));
            set => SetLength("margin-left", value);
        }
        /// <summary>margin-right 的行内长度覆盖。</summary>
        public Length MarginRight
        {
            get => Length.Parse(Get("margin-right"));
            set => SetLength("margin-right", value);
        }
        /// <summary>margin-top 的行内长度覆盖。</summary>
        public Length MarginTop
        {
            get => Length.Parse(Get("margin-top"));
            set => SetLength("margin-top", value);
        }
        /// <summary>margin-bottom 的行内长度覆盖。</summary>
        public Length MarginBottom
        {
            get => Length.Parse(Get("margin-bottom"));
            set => SetLength("margin-bottom", value);
        }
        /// <summary>padding-left 的行内长度覆盖。</summary>
        public Length PaddingLeft
        {
            get => Length.Parse(Get("padding-left"));
            set => SetLength("padding-left", value);
        }
        /// <summary>padding-right 的行内长度覆盖。</summary>
        public Length PaddingRight
        {
            get => Length.Parse(Get("padding-right"));
            set => SetLength("padding-right", value);
        }
        /// <summary>padding-top 的行内长度覆盖。</summary>
        public Length PaddingTop
        {
            get => Length.Parse(Get("padding-top"));
            set => SetLength("padding-top", value);
        }
        /// <summary>padding-bottom 的行内长度覆盖。</summary>
        public Length PaddingBottom
        {
            get => Length.Parse(Get("padding-bottom"));
            set => SetLength("padding-bottom", value);
        }
        /// <summary>font-size 的行内长度覆盖。</summary>
        public Length FontSize
        {
            get => Length.Parse(Get("font-size"));
            set => SetLength("font-size", value);
        }
        /// <summary>flex-basis 的行内长度覆盖。</summary>
        public Length FlexBasis
        {
            get => Length.Parse(Get("flex-basis"));
            set => SetLength("flex-basis", value);
        }
        /// <summary>border-left-width 的行内长度覆盖。</summary>
        public Length BorderLeftWidth
        {
            get => Length.Parse(Get("border-left-width"));
            set => SetLength("border-left-width", value);
        }
        /// <summary>border-right-width 的行内长度覆盖。</summary>
        public Length BorderRightWidth
        {
            get => Length.Parse(Get("border-right-width"));
            set => SetLength("border-right-width", value);
        }
        /// <summary>border-top-width 的行内长度覆盖。</summary>
        public Length BorderTopWidth
        {
            get => Length.Parse(Get("border-top-width"));
            set => SetLength("border-top-width", value);
        }
        /// <summary>border-bottom-width 的行内长度覆盖。</summary>
        public Length BorderBottomWidth
        {
            get => Length.Parse(Get("border-bottom-width"));
            set => SetLength("border-bottom-width", value);
        }
        /// <summary>display 的行内枚举覆盖。</summary>
        public DisplayStyle Display
        {
            get => GetEnum<DisplayStyle>("display");
            set => SetEnum("display", value);
        }
        /// <summary>position 的行内枚举覆盖。</summary>
        public Position Position
        {
            get => GetEnum<Position>("position");
            set => SetEnum("position", value);
        }
        /// <summary>flex-direction 的行内枚举覆盖。</summary>
        public FlexDirection FlexDirection
        {
            get => GetEnum<FlexDirection>("flex-direction");
            set => SetEnum("flex-direction", value);
        }
        /// <summary>flex-wrap 的行内枚举覆盖。</summary>
        public FlexWrap FlexWrap
        {
            get => GetEnum<FlexWrap>("flex-wrap");
            set => SetEnum("flex-wrap", value);
        }
        /// <summary>align-items 的行内枚举覆盖。</summary>
        public Align AlignItems
        {
            get => GetEnum<Align>("align-items");
            set => SetEnum("align-items", value);
        }
        /// <summary>align-self 的行内枚举覆盖。</summary>
        public Align AlignSelf
        {
            get => GetEnum<Align>("align-self");
            set => SetEnum("align-self", value);
        }
        /// <summary>justify-content 的行内枚举覆盖。</summary>
        public Justify JustifyContent
        {
            get => GetEnum<Justify>("justify-content");
            set => SetEnum("justify-content", value);
        }
        /// <summary>overflow 的行内枚举覆盖。</summary>
        public Overflow Overflow
        {
            get => GetEnum<Overflow>("overflow");
            set => SetEnum("overflow", value);
        }
        /// <summary>opacity 的行内数值；null 移除覆盖。</summary>
        public float? Opacity
        {
            get => Get("opacity") is string text ? float.Parse(text, CultureInfo.InvariantCulture) : (float?)null;
            set
            {
                if (!value.HasValue) { Remove("opacity"); return; }
                RequireFinite(value.Value);
                SetOpacity(value.Value);
            }
        }
        /// <summary>flex-grow 的行内数值；null 移除覆盖。</summary>
        public float? FlexGrow
        {
            get => Get("flex-grow") is string text ? float.Parse(text, CultureInfo.InvariantCulture) : (float?)null;
            set
            {
                if (!value.HasValue) { Remove("flex-grow"); return; }
                RequireFinite(value.Value);
                Set("flex-grow", value.Value.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        /// <summary>flex-shrink 的行内数值；null 移除覆盖。</summary>
        public float? FlexShrink
        {
            get => Get("flex-shrink") is string text ? float.Parse(text, CultureInfo.InvariantCulture) : (float?)null;
            set
            {
                if (!value.HasValue) { Remove("flex-shrink"); return; }
                RequireFinite(value.Value);
                Set("flex-shrink", value.Value.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        /// <summary>背景颜色行内覆盖；null 移除覆盖。</summary>
        public Color? BackgroundColor
        {
            get
            {
                string text = Get("background-color");
                if (text == null)
                {
                    return null;
                }

                if (ColorUtility.TryParseHtmlString(text, out var value))
                {
                    return value;
                }

                throw new NotSupportedException("请通过 Get 读取此 RCSS 颜色表达式：" + text);
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
    }
}
