using System;
using System.Globalization;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>
    /// 滑块的物理布局方向。
    /// </summary>
    public enum SliderDirection
    {
        Horizontal,
        Vertical
    }
    /// <summary>
    /// 带范围夹取、独立指针捕获和键盘操作的滑动值控件。
    /// </summary>
    public abstract class RangeField<T> : BaseField<T>
    {
        internal event Action PresentationChanged;
        internal float PresentationPercent
        {
            get
            {
                var info = ReadRange();
                var low = info.Min;
                var high = info.Max;
                var percent = high > low ? (float)(((double)ToNumber(Value) - low) / ((double)high - low) * 100) : 0;
                return Inverted ? 100 - percent : percent;
            }
        }
        /// <summary>
        /// 原生范围和方向属性变更后立即刷新作者皮肤。
        /// </summary>
        protected override void OnAttributeChanged(string name)
        {
            base.OnAttributeChanged(name);
            if (name == "min" || name == "max" || name == "step" || name == "value" || name == "data-urui-orientation" || name == "data-urui-exact-range")
            {
                PresentationChanged?.Invoke();
            }
        }
        /// <summary>
        /// 同步普通写入、静默写入以及原生输入的皮肤状态。
        /// </summary>
        protected override void OnValueSynchronized()
        {
            base.OnValueSynchronized();
            PresentationChanged?.Invoke();
        }
        /// <summary>
        /// 过期节点不保留作者皮肤的引用。
        /// </summary>
        protected override void OnInvalidated()
        {
            PresentationChanged = null;
            dragging = -1;
            base.OnInvalidated();
        }
        int dragging = -1;
        bool inverted;
        /// <summary>
        /// 文档工厂构造入口。
        /// </summary>
        protected RangeField(ElementContext context) : base(context) { }
        /// <summary>
        /// 将有类型数值转换成浮点数。
        /// </summary>
        protected abstract float ToNumber(T value);
        /// <summary>
        /// 将浮点数转换成控件数值。
        /// </summary>
        protected abstract T FromNumber(float value);
        /// <summary>
        /// 水平或垂直布局，坐标和键盘调整与方向一致。
        /// </summary>
        public SliderDirection Direction
        {
            get => GetAttribute("data-urui-orientation") == "vertical" ? SliderDirection.Vertical : SliderDirection.Horizontal;
            set
            {
                if (value != SliderDirection.Horizontal && value != SliderDirection.Vertical)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                SetAttribute("data-urui-orientation", value == SliderDirection.Vertical ? "vertical" : "horizontal");
            }
        }
        /// <summary>
        /// 反转物理方向与数值方向，切换时不改变已提交值。
        /// </summary>
        public bool Inverted
        {
            get
            {
                RequireValid();
                return inverted;
            }
            set
            {
                RequireValid();
                if (inverted == value)
                {
                    return;
                }

                T previous = Value;
                ChangeStructure(() =>
                {
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
                });
            }
        }
        /// <summary>
        /// 原生滑块使用物理方向数值，反向模式在边界做对称映射。
        /// </summary>
        protected float MapNativeValue(float value) => inverted ? LowValue + (HighValue - value) : value;
        /// <summary>
        /// 范围下限，必须不大于上限。
        /// </summary>
        public float LowValue
        {
            get => ReadRange().Min;
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
        /// <summary>
        /// 范围上限，必须不小于下限。
        /// </summary>
        public float HighValue
        {
            get => ReadRange().Max;
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
        /// <summary>
        /// 键盘每次调整的步长，必须为正有限数值。
        /// </summary>
        public float Step
        {
            get => ReadRange().Step;
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
        NativeMethods.RangeInfo ReadRange()
        {
            RequireValid();
            if (NativeMethods.urui_control_range_info(SurfaceHandle, NativeHandle, CheckedToken(), out var info) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("无法读取滑块的有效范围。");
            }
            return info;
        }
        /// <summary>
        /// 普通输入由原生统一校验；反向模式先按语义方向计算步进，再映射物理值。
        /// </summary>
        protected override T NormalizeValue(T input)
        {
            var number = ToNumber(input);
            ElementStyle.RequireFinite(number);
            if (!inverted)
            {
                return input;
            }
            var info = ReadRange();
            double low = info.Min;
            double high = info.Max;
            double step = info.Step;
            double clamped = Math.Max(low, Math.Min(high, number));
            if ((info.Flags & 1) == 0 && high > low)
            {
                var last = Math.Floor((high - low) / step);
                var index = Math.Min(last, Math.Floor((clamped - low) / step + .5));
                clamped = low + index * step;
            }
            return FromNumber((float)clamped);
        }
        void FromPointer(PointerEventBase evt)
        {
            if (!HasLayout)
            {
                return;
            }

            var local = WorldToLocal(evt.Position);
            bool vertical = Direction == SliderDirection.Vertical;
            var contentRect = ContentRect;
            float length = vertical ? contentRect.height : contentRect.width;
            if (length <= 0)
            {
                return;
            }

            float ratio = Mathf.Clamp01(vertical ? (local.y - contentRect.y) / length : (local.x - contentRect.x) / length);
            if (inverted)
            {
                ratio = 1 - ratio;
            }

            var info = ReadRange();
            float number = Mathf.Lerp(info.Min, info.Max, ratio);
            Value = FromNumber(number);
        }
        /// <summary>
        /// 每个滑块分别持有指针，不复用鼠标拖拽状态。
        /// </summary>
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
                FromPointer(move);
                move.PreventDefault();
            }
            else if (evt is PointerUpEvent up && up.PointerId == dragging)
            {
                FromPointer(up);
                ReleasePointer(dragging);
                dragging = -1;
                up.PreventDefault();
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

                Value = FromNumber(number);
                key.PreventDefault();
            }
        }
    }
}
