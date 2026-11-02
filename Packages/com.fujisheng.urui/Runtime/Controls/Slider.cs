using System.Globalization;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>支持范围、指针拖动和键盘操作的 float 滑块。</summary>
    public class Slider : RangeField<float>
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal Slider(ElementContext context) : base(context) { }
        /// <summary>读取原生数值。</summary>
        protected override float ParseValue(string value) => MapNativeValue(float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0);
        /// <summary>写入区域无关的数值。</summary>
        protected override string FormatValue(float value) => MapNativeValue(value).ToString("R", CultureInfo.InvariantCulture);
        /// <summary>转成滑动计算数值。</summary>
        protected override float ToNumber(float value) => value;
        /// <summary>转回控件值。</summary>
        protected override float FromNumber(float value) => value;
    }
}
