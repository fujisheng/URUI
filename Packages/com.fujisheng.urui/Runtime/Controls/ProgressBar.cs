using System;
using System.Globalization;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>
    /// 可配置范围的进度显示，默认 0 到 100；标题更新保留内部结构。
    /// </summary>
    public class ProgressBar : Element, INotifyValueChanged<float>
    {
        Element fill;
        Element content;
        Element label;
        float currentValue;
        float low;
        float high = 100;
        Action project;
        bool projectionQueued;
        /// <summary>
        /// 文档工厂构造入口。
        /// </summary>
        protected internal ProgressBar(ElementContext context) : base(context) { }
        /// <summary>
        /// 额外业务子节点的容器。
        /// </summary>
        public override Element ContentContainer => content ?? this;
        /// <summary>
        /// 进度值，有限值夹取到当前上下限。
        /// </summary>
        public float Value
        {
            get
            {
                RequireValid();
                return currentValue;
            }
            set
            {
                float previous = currentValue;
                SetValueWithoutNotify(value);
                if (previous != currentValue)
                {
                    SendEvent(new ChangeEvent<float>
                    {
                        PreviousValue = previous,
                        NewValue = currentValue
                    });
                }
            }
        }
        /// <summary>
        /// 覆盖进度条标题文字。
        /// </summary>
        public override string Text
        {
            get => GetAttribute("data-urui-label") ?? label.Text;
            set => SetAttribute("data-urui-label", value);
        }
        /// <summary>
        /// 范围下限，必须不大于上限。
        /// </summary>
        public float LowValue
        {
            get
            {
                RequireValid();
                return low;
            }
            set
            {
                RequireValid();
                ElementStyle.RequireFinite(value);
                if (value > high)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                float previous = currentValue;
                SetAttribute("min", value.ToString("R", CultureInfo.InvariantCulture));
                Notify(previous);
            }
        }
        /// <summary>
        /// 范围上限，必须不小于下限。
        /// </summary>
        public float HighValue
        {
            get
            {
                RequireValid();
                return high;
            }
            set
            {
                RequireValid();
                ElementStyle.RequireFinite(value);
                if (value < low)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                float previous = currentValue;
                SetAttribute("max", value.ToString("R", CultureInfo.InvariantCulture));
                Notify(previous);
            }
        }
        /// <summary>
        /// 订阅进度值变化。
        /// </summary>
        public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<float>> callback) => RegisterCallback(callback);
        /// <summary>
        /// 解绑进度值变化。
        /// </summary>
        public void UnregisterValueChangedCallback(Action<ChangeEvent<float>> callback) => UnregisterCallback(callback);
        /// <summary>
        /// 静默更新进度和填充宽度。
        /// </summary>
        public void SetValueWithoutNotify(float value)
        {
            RequireValid();
            ElementStyle.RequireFinite(value);
            SetAttribute("value", Mathf.Clamp(value, low, high).ToString("R", CultureInfo.InvariantCulture));
        }
        /// <summary>
        /// 复用作者填充、标题和内容区；空的运行时控件生成同名结构。
        /// </summary>
        protected override void OnInitialize()
        {
            ReadState();
            var children = Hierarchy.Children();
            fill = ControlMarkup.Find(children, "urui-progress-fill");
            label = ControlMarkup.Find(children, "urui-progress-title");
            content = ControlMarkup.Find(children, "urui-progress-content");
            if (fill != null || label != null || content != null)
            {
                ControlMarkup.RequireOnly(children, fill, label, content);
            }
            else
            {
                ControlMarkup.RequireOnly(children);
                fill = Document.Create<Element>();
                fill.AddToClassList("urui-progress-fill");
                fill.Style.Height = Length.Percent(100);
                fill.Style.Position = Position.Absolute;
                label = Document.Create<Label>();
                label.AddToClassList("urui-progress-title");
                label.Style.Position = Position.Relative;
                content = Document.Create<Element>();
                content.AddToClassList("urui-progress-content");
                Hierarchy.Add(fill);
                Hierarchy.Add(label);
                Hierarchy.Add(content);
            }
            project = Project;
            RequestProjection();
        }
        /// <summary>
        /// 属性写入同步范围、值和标题，不发送用户变化通知。
        /// </summary>
        protected override void OnAttributeChanged(string name)
        {
            base.OnAttributeChanged(name);
            if (project == null)
            {
                return;
            }
            if (name == "min" || name == "max" || name == "value")
            {
                ReadState();
            }
            if (name == "min" || name == "max" || name == "value" || name == "data-urui-label")
            {
                RequestProjection();
            }
        }
        void ReadState()
        {
            float nextLow = ReadInitial("min", 0);
            float nextHigh = ReadInitial("max", 100);
            if (nextHigh < nextLow)
            {
                throw new ArgumentException("进度条上限不能小于下限。");
            }
            float next = Mathf.Clamp(ReadInitial("value", 0), nextLow, nextHigh);
            low = nextLow;
            high = nextHigh;
            currentValue = next;
        }
        void Notify(float previous)
        {
            if (IsValid && previous != currentValue)
            {
                SendEvent(new ChangeEvent<float>
                {
                    PreviousValue = previous,
                    NewValue = currentValue
                });
            }
        }
        void RequestProjection()
        {
            if (projectionQueued)
            {
                return;
            }
            projectionQueued = true;
            Document.RunAfterEvent(project);
        }
        void Project()
        {
            projectionQueued = false;
            if (!IsValid || !fill.IsValid || !label.IsValid)
            {
                return;
            }
            fill.Style.Width = Length.Percent(high > low ? (float)(((double)currentValue - low) / ((double)high - low) * 100) : 0);
            var caption = GetAttribute("data-urui-label");
            if (caption != null)
            {
                label.Text = caption;
            }
            SetAttribute("aria-valuemin", low.ToString("R", CultureInfo.InvariantCulture));
            SetAttribute("aria-valuemax", high.ToString("R", CultureInfo.InvariantCulture));
            SetAttribute("aria-valuenow", currentValue.ToString("R", CultureInfo.InvariantCulture));
        }
        float ReadInitial(string name, float fallback)
        {
            string raw = GetAttribute(name);
            if (raw == null)
            {
                return fallback;
            }

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number))
            {
                throw new ArgumentException("进度条 " + name + " 必须是有限数字。");
            }

            return number;
        }
    }
}
