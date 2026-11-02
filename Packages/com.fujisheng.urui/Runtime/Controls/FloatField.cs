using System;
using System.Globalization;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>有类型的 FloatField 表单控件。</summary>
    public class FloatField : NumericField<float>
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal FloatField(ElementContext context) : base(context) { }
        /// <summary>读取原生控件值。</summary>
        protected override float ParseValue(string value) => string.IsNullOrWhiteSpace(value) ? 0 : float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        /// <summary>使用与区域设置无关的原生格式。</summary>
        protected override string FormatValue(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        /// <summary>拒绝非有限数值。</summary>
        protected override float NormalizeValue(float value) { ElementStyle.RequireFinite(value); return value; }
        /// <summary>转为步进计算值。</summary>
        protected override double ToNumber(float value) => value;
        /// <summary>步进在有限浮点范围内饱和。</summary>
        protected override float FromNumber(double value) => (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, value));
    }
}
