using System;
using System.Globalization;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>滑块的物理布局方向。</summary>
    public enum SliderDirection { Horizontal, Vertical }
    /// <summary>带范围夹取、独立指针捕获和键盘操作的滑动值控件。</summary>
    public abstract class RangeField<T> : BaseField<T>
    {
        int dragging = -1;
        bool inverted;
        /// <summary>文档工厂构造入口。</summary>
        protected RangeField(ElementContext context) : base(context) { }
        /// <summary>将有类型数值转换成浮点数。</summary>
        protected abstract float ToNumber(T value);
        /// <summary>将浮点数转换成控件数值。</summary>
        protected abstract T FromNumber(float value);
        /// <summary>水平或垂直布局，坐标和键盘调整与方向一致。</summary>
        public SliderDirection Direction
        {
            get => GetAttribute("orientation") == "vertical" ? SliderDirection.Vertical : SliderDirection.Horizontal;
            set
            {
                if (value != SliderDirection.Horizontal && value != SliderDirection.Vertical)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                SetAttribute("orientation", value == SliderDirection.Vertical ? "vertical" : "horizontal");
            }
        }
        /// <summary>反转物理方向与数值方向，切换时不改变已提交值。</summary>
        public bool Inverted
        {
            get { RequireValid(); return inverted; }
            set
            {
                RequireValid();
                if (inverted == value)
                {
                    return;
                }

                T previous = Value;
                inverted = value;
                if (value)
                {
                    SetAttribute("data-urui-exact-range", "");
                }
                else
                {
                    RemoveAttribute("data-urui-exact-range");
                }

                SetValueWithoutNotify(previous);
            }
        }
        /// <summary>原生滑块使用物理方向数值，反向模式在边界做对称映射。</summary>
        protected float MapNativeValue(float value) => inverted ? LowValue + (HighValue - value) : value;
        /// <summary>范围下限，必须不大于上限。</summary>
        public float LowValue
        {
            get => ReadNumber("min", 0);
            set
            {
                ElementStyle.RequireFinite(value);
                if (value > HighValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                SetAttribute("min", value.ToString("R", CultureInfo.InvariantCulture));
                Value = Value;
            }
        }
        /// <summary>范围上限，必须不小于下限。</summary>
        public float HighValue
        {
            get => ReadNumber("max", 100);
            set
            {
                ElementStyle.RequireFinite(value);
                if (value < LowValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                SetAttribute("max", value.ToString("R", CultureInfo.InvariantCulture));
                Value = Value;
            }
        }
        /// <summary>键盘每次调整的步长，必须为正有限数值。</summary>
        public float Step
        {
            get => ReadNumber("step", 1);
            set
            {
                ElementStyle.RequireFinite(value);
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                SetAttribute("step", value.ToString("R", CultureInfo.InvariantCulture));
                Value = Value;
            }
        }
        float ReadNumber(string name, float fallback) => float.TryParse(GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        /// <summary>拒绝非有限值，并将数值夹取到当前范围。</summary>
        protected override T NormalizeValue(T input)
        {
            float number = ToNumber(input);
            ElementStyle.RequireFinite(number);
            double low = LowValue, high = HighValue, step = Step;
            if (!double.IsFinite(low) || !double.IsFinite(high) || high < low || !double.IsFinite(step) || step <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(input), "滑块需要有效的范围和正步长。");
            }

            double clamped = Math.Max(low, Math.Min(high, number));
            double snapped = low + Math.Round((clamped - low) / step, MidpointRounding.AwayFromZero) * step;
            return FromNumber((float)Math.Max(low, Math.Min(high, snapped)));
        }
        void FromPointer(PointerEventBase evt)
        {
            if (!HasLayout)
            {
                return;
            }

            var local = WorldToLocal(evt.Position);
            bool vertical = Direction == SliderDirection.Vertical;
            float length = vertical ? ContentRect.height : ContentRect.width;
            if (length <= 0)
            {
                return;
            }

            float ratio = Mathf.Clamp01(vertical ? (local.y - ContentRect.y) / length : (local.x - ContentRect.x) / length);
            if (inverted)
            {
                ratio = 1 - ratio;
            }

            float number = Mathf.Lerp(LowValue, HighValue, ratio);
            number = LowValue + Mathf.Round((number - LowValue) / Step) * Step;
            Value = FromNumber(number);
        }
        /// <summary>每个滑块分别持有指针，不复用鼠标拖拽状态。</summary>
        protected override void OnEvent(EventBase evt)
        {
            base.OnEvent(evt);
            if (evt is PointerCancelEvent || evt is PointerCaptureOutEvent || evt is BlurEvent)
            {
                dragging = -1;
                return;
            }
            if (!EnabledInHierarchy)
            {
                return;
            }

            if (evt is PointerDownEvent down && down.Button == 0 && dragging < 0)
            {
                dragging = down.PointerId;
                // 多指拖动独立于键盘焦点，不能用第二根手指的焦点切换取消第一根。
                if (!down.IsTouch)
                {
                    Focus();
                }

                CapturePointer(dragging);
                FromPointer(down);
                down.PreventDefault();
            }
            else if (evt is PointerMoveEvent move && move.PointerId == dragging)
            {
                FromPointer(move); move.PreventDefault();
            }
            else if (evt is PointerUpEvent up && up.PointerId == dragging)
            {
                FromPointer(up); ReleasePointer(dragging); dragging = -1; up.PreventDefault();
            }
            else if (evt is PointerCancelEvent || evt is PointerCaptureOutEvent)
            {
                dragging = -1;
            }
            else if (evt is KeyDownEvent key && ReferenceEquals(key.Target, this))
            {
                float number = ToNumber(Value);
                int delta = key.KeyCode == KeyCode.RightArrow ? 1 : key.KeyCode == KeyCode.LeftArrow ? -1
                    : key.KeyCode == KeyCode.DownArrow ? (Direction == SliderDirection.Vertical ? 1 : -1)
                    : key.KeyCode == KeyCode.UpArrow ? (Direction == SliderDirection.Vertical ? -1 : 1) : 0;
                if (delta != 0)
                {
                    number += Step * delta * (inverted ? -1 : 1);
                }
                else if (key.KeyCode == KeyCode.Home)
                {
                    number = LowValue;
                }
                else if (key.KeyCode == KeyCode.End)
                {
                    number = HighValue;
                }
                else
                {
                    return;
                }

                Value = FromNumber(number); key.PreventDefault();
            }
        }
    }
}
