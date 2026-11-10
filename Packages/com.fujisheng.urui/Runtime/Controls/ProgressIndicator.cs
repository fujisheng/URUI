using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>把进度行为接到作者节点；填充节点、九宫格和皮肤均由资源声明。</summary>
    public sealed class ProgressIndicator : INotifyValueChanged<float>
    {
        readonly Element host, fill;
        readonly float minimum, maximum;
        float value;
        public ProgressIndicator(Element host, Element fill, float minimum = 0, float maximum = 1)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.fill = fill ?? throw new ArgumentNullException(nameof(fill));
            if (!ReferenceEquals(host.Document, fill.Document)) throw new ArgumentException("填充节点必须属于同一文档。", nameof(fill));
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
            this.minimum = minimum; this.maximum = maximum;
            SetValueWithoutNotify(minimum);
        }
        public float Value
        {
            get => value;
            set
            {
                var previous = this.value;
                SetValueWithoutNotify(value);
                if (previous != this.value) host.SendEvent(new ChangeEvent<float> { PreviousValue = previous, NewValue = this.value });
            }
        }
        public void SetValueWithoutNotify(float value)
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            this.value = Mathf.Clamp(value, minimum, maximum);
            fill.Style.SetPercent("width", maximum > minimum ? (float)(((double)this.value - minimum) / ((double)maximum - minimum) * 100) : 0);
        }
    }
}
