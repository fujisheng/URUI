using System.Globalization;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>支持范围、指针拖动和键盘操作的 int 滑块。</summary>
    public class IntegerSlider : RangeField<int>
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal IntegerSlider(ElementContext context) : base(context) { }
        /// <summary>读取原生数值。</summary>
        protected override int ParseValue(string value) => UnityEngine.Mathf.RoundToInt(MapNativeValue(float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0));
        /// <summary>写入区域无关的数值。</summary>
        protected override string FormatValue(int value) => MapNativeValue(value).ToString("R", CultureInfo.InvariantCulture);
        /// <summary>转成滑动计算数值。</summary>
        protected override float ToNumber(int value) => value;
        /// <summary>转回控件值。</summary>
        protected override int FromNumber(float value) => UnityEngine.Mathf.RoundToInt(value);
    }
}
