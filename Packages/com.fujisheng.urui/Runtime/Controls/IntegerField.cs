using System;
using System.Globalization;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>有类型的 IntegerField 表单控件。</summary>
    public class IntegerField : NumericField<int>
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal IntegerField(ElementContext context) : base(context) { }
        /// <summary>读取原生控件值。</summary>
        protected override int ParseValue(string value) => string.IsNullOrWhiteSpace(value) ? 0 : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        /// <summary>使用与区域设置无关的原生格式。</summary>
        protected override string FormatValue(int value) => value.ToString(CultureInfo.InvariantCulture);
        /// <summary>整数可精确转换为 double。</summary>
        protected override double ToNumber(int value) => value;
        /// <summary>步进在整数边界饱和，不产生溢出。</summary>
        protected override int FromNumber(double value) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(value, MidpointRounding.AwayFromZero)));
    }
}
