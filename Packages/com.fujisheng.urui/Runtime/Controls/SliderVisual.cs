using System;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>将滑块状态投射到作者填充和滑钮节点；支持静默写入及水平/垂直布局。</summary>
    public sealed class SliderVisual : INotifyValueChanged<float>, IDisposable
    {
        readonly RangeVisual<float> visual;
        /// <summary>负责输入、范围约束和键盘操作的标准滑块。</summary>
        public Slider Control { get; }
        /// <summary>使用同一文档内的作者填充和滑钮节点。</summary>
        public SliderVisual(Slider control, Element fill, Element thumb)
        {
            Control=control ?? throw new ArgumentNullException(nameof(control));
            if (control.Parent is AuthoredSliderSkin owner) owner.RequireManualCreationAllowed();
            visual=new RangeVisual<float>(control,fill,thumb);
        }
        /// <summary>更新原生值和外观，保留正常值变化通知。</summary>
        public float Value { get=>Control.Value; set=>Control.Value=value; }
        /// <summary>同步值和外观，不发出用户变化通知。</summary>
        public void SetValueWithoutNotify(float value) => Control.SetValueWithoutNotify(value);
        /// <summary>解除呈现订阅，作者节点仍由文档持有。</summary>
        public void Dispose() => visual.Dispose();
    }
}
