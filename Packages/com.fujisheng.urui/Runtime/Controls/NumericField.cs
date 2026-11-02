using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>数值编辑共用的键盘步进；中间文本和提交规则由文本输入基类管理。</summary>
    public abstract class NumericField<T> : TextInputField<T>
    {
        /// <summary>文档工厂构造入口。</summary>
        protected NumericField(ElementContext context) : base(context) { }
        /// <summary>转换为精确覆盖 int 与 float 范围的步进值。</summary>
        protected abstract double ToNumber(T value);
        /// <summary>夹取到具体数值类型的可表示范围。</summary>
        protected abstract T FromNumber(double value);
        /// <summary>上下方向键步长，必须为正有限数值，默认为 1。</summary>
        public T Step
        {
            get { RequireValid(); return ParseValue(GetAttribute("step") ?? "1"); }
            set
            {
                double number = ToNumber(value);
                if (!double.IsFinite(number) || number <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                SetAttribute("step", FormatValue(value));
            }
        }
        /// <summary>只处理自身的上下方向键，保留只读文本的选择与复制。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (evt is KeyDownEvent key && ReferenceEquals(evt.Target, this) && EnabledInHierarchy && !IsReadOnly
                && (key.KeyCode == KeyCode.UpArrow || key.KeyCode == KeyCode.DownArrow))
            {
                double current;
                try { current = ToNumber(ParseValue(NativeMethods.ReadControl(Token))); }
                catch (FormatException) { current = ToNumber(Value); }
                catch (OverflowException) { current = ToNumber(Value); }
                if (!double.IsFinite(current))
                {
                    current = ToNumber(Value);
                }

                var next = FromNumber(current + ToNumber(Step) * (key.KeyCode == KeyCode.UpArrow ? 1 : -1));
                if (IsDelayed)
                {
                    ReplaceEditingText(FormatValue(next));
                }
                else
                {
                    Value = next;
                }

                key.PreventDefault();
                return;
            }
            base.OnEvent(evt);
        }
    }
}
