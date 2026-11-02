using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>支持 Unicode、选区、剪贴板与输入法组合的文本控件。</summary>
    public class TextField : TextInputField<string>
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal TextField(ElementContext context) : base(context) { }
        /// <summary>输入值不能为 null 或包含 NUL。</summary>
        protected override string NormalizeValue(string value)
        {
            RequireText(value, nameof(value));
            return Multiline ? value : value.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
        }
        /// <summary>读取原生文本。</summary>
        protected override string ParseValue(string value) => value;
        /// <summary>保留原始文本内容。</summary>
        protected override string FormatValue(string value) => value;
        /// <summary>文本入口与 Value 一致，不替换控件内部结构。</summary>
        public override string Text { get => Value; set => Value = value; }
        /// <summary>多行输入；RML input 节点只能单行，需使用 textarea 或 C# 创建支持多行切换。</summary>
        public bool Multiline
        {
            get => GetAttribute("data-urui-multiline") == "true" || (TagName == "textarea" && GetAttribute("data-urui-multiline") == null);
            set
            {
                if (value && TagName != "textarea")
                {
                    throw new NotSupportedException("多行输入请使用 textarea 标签。");
                }

                SetAttribute("data-urui-multiline", value ? "true" : "false");
                SetValueWithoutNotify(Value);
            }
        }
        /// <summary>多行 Enter 保留换行，单行 Enter 提交编辑。</summary>
        protected override bool SubmitOnEnter => !Multiline;
        /// <summary>单行模式拦截换行，其他值变化由表单基类处理。</summary>
        protected override void OnEvent(EventBase evt)
        {
            base.OnEvent(evt);
            if (evt is NativeValueChangedEvent && ReferenceEquals(evt.Target, this) && IsValid && !Document.HasPendingInvalidation)
            {
                string raw = NativeMethods.ReadControl(Token);
                string normalized = NormalizeValue(raw);
                if (normalized != raw)
                {
                    ReplaceEditingText(normalized);
                }
            }
        }
    }
}
