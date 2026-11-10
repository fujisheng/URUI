using System;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>整数滑块与作者填充、滑钮节点的呈现组合。</summary>
    public sealed class IntegerSliderVisual : INotifyValueChanged<int>, IDisposable
    {
        readonly RangeVisual<int> visual;
        /// <summary>负责输入、范围约束和键盘操作的整数滑块。</summary>
        public IntegerSlider Control { get; }
        /// <summary>使用同一文档内的作者填充和滑钮节点。</summary>
        public IntegerSliderVisual(IntegerSlider control, Element fill, Element thumb)
        {
            Control=control ?? throw new ArgumentNullException(nameof(control));
            if (control.Parent is AuthoredSliderSkin owner) owner.RequireManualCreationAllowed();
            visual=new RangeVisual<int>(control,fill,thumb);
        }
        /// <summary>更新值与外观，保留正常值变化通知。</summary>
        public int Value { get=>Control.Value; set=>Control.Value=value; }
        /// <summary>同步值和外观，不发出用户变化通知。</summary>
        public void SetValueWithoutNotify(int value) => Control.SetValueWithoutNotify(value);
        /// <summary>解除呈现订阅，作者节点仍由文档持有。</summary>
        public void Dispose() => visual.Dispose();
    }
}
