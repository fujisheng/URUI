using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    internal interface ITextInputControl { bool IsReadOnly { get; } }

    /// <summary>文本与数值编辑共用的选区、只读、延后提交和提交通知。</summary>
    public abstract class TextInputField<T> : BaseField<T>, ITextInputControl
    {
        Action<T> submitted;
        bool submitPressed;
        /// <summary>文档工厂构造入口。</summary>
        protected TextInputField(ElementContext context) : base(context) { }
        /// <summary>只读仍允许焦点、选取和复制；程序赋值不受限制。</summary>
        public bool IsReadOnly
        {
            get => HasAttribute("readonly");
            set
            {
                if (value)
                {
                    SetAttribute("readonly", "");
                }
                else
                {
                    RemoveAttribute("readonly");
                }

                if (value && Focused)
                {
                    Document.Input.CancelComposition();
                }
            }
        }
        /// <summary>启用后仅在 Enter、失焦或 CommitValue 时提交编辑。</summary>
        public bool IsDelayed
        {
            get { RequireValid(); return HasAttribute("data-urui-delayed"); }
            set
            {
                bool previous = IsDelayed;
                if (value)
                {
                    SetAttribute("data-urui-delayed", "");
                }
                else
                {
                    RemoveAttribute("data-urui-delayed");
                }

                if (previous && !value)
                {
                    CommitValue();
                }
            }
        }
        /// <summary>非延后模式随编辑提交值。</summary>
        protected override bool CommitOnInput => !IsDelayed;
        /// <summary>单行控件按 Enter 提交，多行文本控件可覆写。</summary>
        protected virtual bool SubmitOnEnter => true;
        /// <summary>有效编辑通过 Enter 提交时触发；失焦不重复触发。</summary>
        public event Action<T> Submitted
        {
            add { RequireValid(); submitted += value; }
            remove { Document.CheckThread(); submitted -= value; }
        }
        /// <summary>当前编辑文本，赋值必须能解析成有效值。</summary>
        public override string Text { get => NativeMethods.ReadControl(CheckedToken()); set { RequireText(value, nameof(value)); Value = ParseValue(value); } }
        /// <summary>空值提示文字。</summary>
        public string Placeholder { get => GetAttribute("placeholder") ?? ""; set => SetAttribute("placeholder", value); }
        /// <summary>最大 Unicode 字符数，0 表示不限制。</summary>
        public int MaxLength
        {
            get => int.TryParse(GetAttribute("maxlength"), out var value) ? Math.Max(0, value) : 0;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                if (value == 0)
                {
                    RemoveAttribute("maxlength");
                }
                else
                {
                    SetAttribute("maxlength", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        /// <summary>按 Unicode 字符索引选取区间，自动请求焦点。</summary>
        public void SelectRange(int start, int end)
        {
            RequireValid();
            if (start < 0 || end < start)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (!Focus())
            {
                throw new InvalidOperationException("输入控件无法获得焦点。");
            }

            RequireValid();
            if (NativeMethods.urui_text_selection(ref start, ref end, 1) != 1)
            {
                throw new InvalidOperationException("无法设置选区。");
            }
        }
        /// <summary>当前 Unicode 字符选区；读取前必须获得焦点。</summary>
        public Vector2Int Selection
        {
            get
            {
                RequireValid();
                if (!Focused)
                {
                    throw new InvalidOperationException("请先让输入控件获得焦点。");
                }

                int start = 0, end = 0;
                if (NativeMethods.urui_text_selection(ref start, ref end, 0) != 1)
                {
                    throw new InvalidOperationException("无法读取选区。");
                }

                return new Vector2Int(start, end);
            }
        }
        /// <summary>提交并规范化编辑文本；无效输入恢复上次有效值并返回 false。</summary>
        public bool CommitValue()
        {
            RequireValid();
            bool valid = CommitInput();
            if (IsValid && !Document.HasPendingInvalidation)
            {
                SetValueWithoutNotify(Value);
            }

            return valid;
        }
        /// <summary>替换尚在编辑的原生文字，延后模式不因此提交。</summary>
        protected void ReplaceEditingText(string text)
        {
            RequireValid();
            if (NativeMethods.urui_control_value_write(Token, text) != 1)
            {
                throw new InvalidOperationException("无法更新编辑文本。");
            }

            Document.FinishNativeCall();
        }
        /// <summary>只读阻止编辑命令，Enter 与失焦按同一提交规则处理。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (!ReferenceEquals(evt.Target, this)) { base.OnEvent(evt); return; }
            if (evt is BlurEvent)
            {
                submitPressed = false;
                if (Focused)
                {
                    Document.Input.CancelComposition();
                }

                CommitValue();
                return;
            }
            if (evt is KeyUpEvent up && (up.KeyCode == KeyCode.Return || up.KeyCode == KeyCode.KeypadEnter))
            {
                submitPressed = false;
            }

            if (IsReadOnly && (evt is TextInputEvent || evt is KeyDownEvent edit && IsEditingKey(edit)))
            {
                evt.PreventDefault();
                return;
            }
            if (evt is KeyDownEvent key && SubmitOnEnter && (key.KeyCode == KeyCode.Return || key.KeyCode == KeyCode.KeypadEnter))
            {
                key.PreventDefault();
                if (submitPressed)
                {
                    return;
                }

                submitPressed = true;
                if (CommitValue() && IsValid && !Document.HasPendingInvalidation && submitted != null)
                {
                    foreach (Action<T> callback in submitted.GetInvocationList())
                    {
                        if (!IsValid || Document.HasPendingInvalidation)
                        {
                            break;
                        }

                        try { callback(Value); } catch (Exception error) { Debug.LogException(error); }
                    }
                }

                return;
            }
            base.OnEvent(evt);
        }
        static bool IsEditingKey(KeyDownEvent key) => key.KeyCode == KeyCode.Backspace || key.KeyCode == KeyCode.Delete
            || key.KeyCode == KeyCode.Return || key.KeyCode == KeyCode.KeypadEnter
            || (key.Modifiers & (InputModifiers.Control | InputModifiers.Command)) != 0 && (key.KeyCode == KeyCode.X || key.KeyCode == KeyCode.V);
        /// <summary>释放提交回调。</summary>
        protected override void OnInvalidated() { submitted = null; base.OnInvalidated(); }
    }
}
