using System;
using System.Globalization;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>可配置范围的进度显示，默认 0 到 100；标题更新保留内部结构。</summary>
    public class ProgressBar : Element, INotifyValueChanged<float>
    {
        Element fill, content;
        Label label;
        float currentValue, low, high = 100;
        /// <summary>文档工厂构造入口。</summary>
        protected internal ProgressBar(ElementContext context) : base(context) { }
        /// <summary>额外业务子节点的容器。</summary>
        public override Element ContentContainer => content ?? this;
        /// <summary>进度值，有限值夹取到当前上下限。</summary>
        public float Value
        {
            get { RequireValid(); return currentValue; }
            set
            {
                float previous = currentValue;
                SetValueWithoutNotify(value);
                if (previous != currentValue)
                {
                    SendEvent(new ChangeEvent<float> { PreviousValue = previous, NewValue = currentValue });
                }
            }
        }
        /// <summary>覆盖进度条标题文字。</summary>
        public override string Text { get => label.Text; set => label.Text = value; }
        /// <summary>范围下限，必须不大于上限。</summary>
        public float LowValue
        {
            get { RequireValid(); return low; }
            set { RequireValid(); ElementStyle.RequireFinite(value); if (value > high) { throw new ArgumentOutOfRangeException(nameof(value)); } low = value; Value = currentValue; }
        }
        /// <summary>范围上限，必须不小于下限。</summary>
        public float HighValue
        {
            get { RequireValid(); return high; }
            set { RequireValid(); ElementStyle.RequireFinite(value); if (value < low) { throw new ArgumentOutOfRangeException(nameof(value)); } high = value; Value = currentValue; }
        }
        /// <summary>订阅进度值变化。</summary>
        public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<float>> callback) => RegisterCallback(callback);
        /// <summary>解绑进度值变化。</summary>
        public void UnregisterValueChangedCallback(Action<ChangeEvent<float>> callback) => UnregisterCallback(callback);
        /// <summary>静默更新进度和填充宽度。</summary>
        public void SetValueWithoutNotify(float value)
        {
            RequireValid(); ElementStyle.RequireFinite(value);
            currentValue = Mathf.Clamp(value, low, high);
            fill.Style.Width = Length.Percent(high > low ? (float)(((double)currentValue - low) / ((double)high - low) * 100) : 0);
        }
        /// <summary>创建填充、标题和内容节点。</summary>
        protected override void OnInitialize()
        {
            low = ReadInitial("min", 0);
            high = ReadInitial("max", 100);
            if (high < low)
            {
                throw new ArgumentException("进度条上限不能小于下限。");
            }

            var children = Hierarchy.Children();
            Style.Display = DisplayStyle.Block;
            Style.Position = Position.Relative;
            fill = Document.Create<Element>();
            fill.AddToClassList("urui-progress-fill");
            fill.Style.Height = Length.Percent(100);
            fill.Style.Position = Position.Absolute;
            label = Document.Create<Label>();
            label.AddToClassList("urui-progress-title");
            // 标题与填充都参与定位，保持后创建的标题绘制在填充之上。
            label.Style.Position = Position.Relative;
            content = Document.Create<Element>();
            Hierarchy.Add(fill); Hierarchy.Add(label); Hierarchy.Add(content);
            foreach (var child in children)
            {
                content.Add(child);
            }

            SetValueWithoutNotify(ReadInitial("value", 0));
            label.Text = GetAttribute("text") ?? "";
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
