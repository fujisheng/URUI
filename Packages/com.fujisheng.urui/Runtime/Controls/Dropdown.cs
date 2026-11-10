using System;
using System.Collections.Generic;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>原生 select 下拉控件，Value 对应 option 的 value 属性。</summary>
    public class Dropdown : BaseField<string>
    {
        internal event Action PresentationChanged;
        /// <summary>同步普通、静默和原生选择写入后的作者皮肤。</summary>
        protected override void OnValueSynchronized()
        {
            base.OnValueSynchronized();
            PresentationChanged?.Invoke();
        }
        /// <summary>启用状态改变时同步独立的作者触发按钮。</summary>
        protected override void OnAttributeChanged(string name)
        {
            base.OnAttributeChanged(name);
            if (name == "disabled") PresentationChanged?.Invoke();
        }
        /// <summary>失效节点不保留作者呈现订阅。</summary>
        protected override void OnInvalidated()
        {
            PresentationChanged = null;
            base.OnInvalidated();
        }
        /// <summary>文档工厂构造入口。</summary>
        protected internal Dropdown(ElementContext context) : base(context) { }
        /// <summary>读取原生选择值。</summary>
        protected override string ParseValue(string value) => value;
        /// <summary>保留选项值字符串。</summary>
        protected override string FormatValue(string value) { RequireText(value, nameof(value)); return value; }
        /// <summary>选项值的有序快照；设置时文本和值相同，现有 option 节点被销毁。</summary>
        public IReadOnlyList<string> Choices
        {
            get
            {
                int count = NativeMethods.urui_select_option_count(NativeHandle, CheckedToken());
                if (count < 0)
                {
                    throw new InvalidOperationException("无法读取原生选项。");
                }

                var result = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    result.Add(NativeMethods.ReadOption(NativeHandle, Token, i));
                }

                return result;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                var copy = new List<string>(value);
                foreach (var item in copy)
                {
                    RequireText(item, nameof(value));
                }

                ChangeStructure(() =>
                                {
                                    string previous = Value;
                                    if (NativeMethods.urui_select_options_clear(NativeHandle, Token) != 1)
                                    {
                                        throw new InvalidOperationException("无法清空选项。");
                                    }

                                    Document.SweepInvalidElements();
                                    foreach (string item in copy)
                                    {
                                        if (NativeMethods.urui_select_option_add(NativeHandle, Token, item) != 1)
                                        {
                                            throw new InvalidOperationException("无法添加选项。");
                                        }
                                    }

                                    SetValueWithoutNotify(copy.Contains(previous) ? previous : copy.Count > 0 ? copy[0] : "");
                                });
            }
        }
        /// <summary>当前选项索引，没有匹配时为 -1。</summary>
        public int SelectedIndex
        {
            get
            {
                RequireValid();
                return NativeMethods.urui_select_selection(NativeHandle, Token, -1);
            }
            set
            {
                var choices = Choices;
                if (value < 0 || value >= choices.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                ChangeStructure(() =>
                                {
                                    if (NativeMethods.urui_select_selection(NativeHandle, Token, value) < 0)
                                    {
                                        throw new InvalidOperationException("无法选择选项。");
                                    }
                                });
            }
        }
    }
}
