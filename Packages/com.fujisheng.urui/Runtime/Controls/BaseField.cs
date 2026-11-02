using System;
using System.Collections.Generic;
using URUI.Elements;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>统一的值控件接口；所有调用限所属文档主线程。</summary>
    public interface INotifyValueChanged<T>
    {
        /// <summary>已提交的值，相同值不重复通知。</summary>
        T Value { get; set; }
        /// <summary>更新值与界面但不发送 ChangeEvent。</summary>
        void SetValueWithoutNotify(T value);
    }

    /// <summary>复用原生表单值，统一校验、静默更新和有类型变化通知。</summary>
    public abstract class BaseField<T> : Element, INotifyValueChanged<T>
    {
        T value;
        int writeDepth;
        /// <summary>文档工厂构造入口。</summary>
        protected BaseField(ElementContext context) : base(context) { }
        /// <summary>将原生字符串解析为有类型的值。</summary>
        protected abstract T ParseValue(string text);
        /// <summary>将有类型的值转换为原生格式。</summary>
        protected abstract string FormatValue(T value);
        /// <summary>对值进行范围或内容校验。</summary>
        protected virtual T NormalizeValue(T input) => input;
        /// <summary>是否在每次原生编辑时提交，延后输入控件可覆写。</summary>
        protected virtual bool CommitOnInput => true;
        /// <summary>读取原生初始值并启用焦点。</summary>
        protected override void OnInitialize()
        {
            value = NormalizeValue(ParseValue(NativeMethods.ReadControl(Token)));
            Focusable = true;
        }
        /// <summary>已提交值；输入法预览期间维持原来的已提交值。</summary>
        public T Value
        {
            get { RequireValid(); return value; }
            set => SetValue(value, true);
        }
        /// <summary>更新原生界面和提交值，不产生变化通知。</summary>
        public void SetValueWithoutNotify(T input) => SetValue(input, false);
        /// <summary>注册可释放的值变化订阅。</summary>
        public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<T>> callback) => RegisterCallback(callback);
        /// <summary>解绑指定值变化回调。</summary>
        public void UnregisterValueChangedCallback(Action<ChangeEvent<T>> callback) => UnregisterCallback(callback);

        void SetValue(T input, bool notify)
        {
            RequireValid();
            T normalized = NormalizeValue(input);
            T previous = value;
            writeDepth++;
            try
            {
                if (NativeMethods.urui_control_value_write(Token, FormatValue(normalized)) != 1)
                {
                    throw new InvalidOperationException("原生控件拒绝值写入。");
                }

                value = NormalizeValue(ParseValue(NativeMethods.ReadControl(Token)));
            }
            finally
            {
                writeDepth--;
                if (writeDepth == 0)
                {
                    Document.FinishNativeCall();
                }
            }
            if (notify && IsValid && !EqualityComparer<T>.Default.Equals(previous, value))
            {
                SendEvent(new ChangeEvent<T> { PreviousValue = previous, NewValue = value });
            }
        }

        /// <summary>更新选项等内部结构，将中间变化合并为最终一次值事件。</summary>
        protected void ChangeStructure(Action action)
        {
            T previous = value;
            writeDepth++;
            try { action(); value = ParseValue(NativeMethods.ReadControl(Token)); }
            finally
            {
                writeDepth--; if (writeDepth == 0)
                {
                    Document.FinishNativeCall();
                }
            }
            if (IsValid && !EqualityComparer<T>.Default.Equals(previous, value))
            {
                SendEvent(new ChangeEvent<T> { PreviousValue = previous, NewValue = value });
            }
        }

        /// <summary>提交原生编辑内容；不能解析或非有限的编辑保留已提交值。</summary>
        protected bool CommitInput()
        {
            T next;
            try { next = NormalizeValue(ParseValue(NativeMethods.ReadControl(Token))); }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            catch (ArgumentOutOfRangeException) { return false; }
            T previous = value;
            value = next;
            if (!EqualityComparer<T>.Default.Equals(previous, next))
            {
                SendEvent(new ChangeEvent<T> { PreviousValue = previous, NewValue = next });
            }

            return true;
        }

        /// <summary>消费原生值变化并转换成统一的托管值事件。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (!ReferenceEquals(evt.Target, this) || writeDepth > 0)
            {
                return;
            }

            if (evt is NativeValueChangedEvent)
            {
                if (CommitOnInput)
                {
                    CommitInput();
                }
            }
            else if (evt is BlurEvent)
            {
                SetValueWithoutNotify(value);
            }
        }
    }
}
