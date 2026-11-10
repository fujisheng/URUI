using System;
using System.Globalization;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>数值编辑共用的键盘步进；中间文本和提交规则由文本输入基类管理。</summary>
    public abstract class NumericField<T> : TextInputField<T>
    {
        double authoredStepBase;
        /// <summary>文档工厂构造入口。</summary>
        protected NumericField(ElementContext context) : base(context) { }
        /// <summary>转换为精确覆盖 int 与 float 范围的步进值。</summary>
        protected abstract double ToNumber(T value);
        /// <summary>夹取到具体数值类型的可表示范围。</summary>
        protected abstract T FromNumber(double value);
        /// <summary>保存作者 value 作为无 min 时的步进基点，实时编辑不会移动基点。</summary>
        protected override void OnInitialize()
        {
            ReadAuthoredStepBase();
            base.OnInitialize();
        }
        /// <summary>作者 value 的显式修改更新步进基点。</summary>
        protected override void OnAttributeChanged(string name)
        {
            base.OnAttributeChanged(name);
            if (name == "value") ReadAuthoredStepBase();
        }
        void ReadAuthoredStepBase() => authoredStepBase = TryNumber(GetAttribute("value"), out var value) ? value : 0;

        // 与 HTML 数值属性一致：拒绝前后空白、前导加号、单位、非有限数及不完整指数。
        static bool TryNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int i = text[0] == '-' ? 1 : 0;
            int start = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            bool digits = i > start;
            if (i < text.Length && text[i] == '.')
            {
                start = ++i;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
                if (i == start) return false;
                digits = true;
            }
            if (!digits) return false;
            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
                start = i;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
                if (i == start) return false;
            }
            return i == text.Length && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }
        double ReadStep() => TryNumber(GetAttribute("step"), out var step) && step > 0 ? step : 1;
        /// <summary>上下方向键步长，必须为正有限数值；无效作者值和 any 的键盘增量默认为 1。</summary>
        public T Step
        {
            get { RequireValid(); return FromNumber(ReadStep()); }
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
                try { current = ToNumber(ParseValue(NativeMethods.ReadControl(NativeHandle, Token))); }
                catch (FormatException) { current = ToNumber(Value); }
                catch (OverflowException) { current = ToNumber(Value); }
                if (!double.IsFinite(current))
                {
                    current = ToNumber(Value);
                }

                key.PreventDefault();
                bool any = string.Equals(GetAttribute("step"), "any", StringComparison.OrdinalIgnoreCase);
                bool hasMin = TryNumber(GetAttribute("min"), out var min);
                bool hasMax = TryNumber(GetAttribute("max"), out var max);
                if (hasMin && hasMax && min > max) return;
                double step = any ? 1 : ReadStep(), stepBase = any ? current : hasMin ? min : authoredStepBase;
                double position = (current - stepBase) / step;
                if (!double.IsFinite(position)) return;
                double nearest = Math.Round(position);
                if (Math.Abs(position - nearest) < 1e-7) position = nearest;
                double index = key.KeyCode == KeyCode.UpArrow ? Math.Floor(position) + 1 : Math.Ceiling(position) - 1;
                if (hasMin) index = Math.Max(index, any ? (min - stepBase) / step : Math.Ceiling((min - stepBase) / step));
                if (hasMax) index = Math.Min(index, any ? (max - stepBase) / step : Math.Floor((max - stepBase) / step));
                double number = stepBase + index * step;
                if (!double.IsFinite(number) || (hasMin && number < min) || (hasMax && number > max)) return;
                var next = FromNumber(number);
                if (IsDelayed)
                {
                    ReplaceEditingText(FormatValue(next));
                }
                else
                {
                    Value = next;
                }

                return;
            }
            base.OnEvent(evt);
        }
    }
}
