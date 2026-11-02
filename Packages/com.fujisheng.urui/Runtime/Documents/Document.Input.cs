using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Elements;
using URUI.Controls;
using URUI.Events;
using URUI.Internal.Interop;
using UnityInput = UnityEngine.Input;

namespace URUI.Events
{
    /// <summary>归一化指针输入阶段。</summary>
    public enum PointerPhase { Down, Move, Up, Cancel, Wheel }
}

namespace URUI.Documents
{
    public sealed partial class Document
    {
        DocumentInput input;
        /// <summary>输入入口；可关闭内置轮询并接入调用者自己的输入来源。</summary>
        public DocumentInput Input => input ?? (input = new DocumentInput(this));
        /// <summary>当前焦点元素，没有焦点时为 null。</summary>
        public Element FocusedElement
        {
            get
            {
                CheckThread();
                return IsLoaded ? WrapElement(NativeMethods.urui_focused_element()) : null;
            }
        }
        /// <summary>命中文档坐标处的实际节点，不要求节点设置 Name。</summary>
        public Element Pick(Vector2 position)
        {
            CheckThread();
            ElementStyle.RequireFinite(position.x); ElementStyle.RequireFinite(position.y);
            return IsLoaded ? WrapElement(NativeMethods.urui_element_pick(position.x, position.y)) : null;
        }
    }

    /// <summary>主线程同步输入适配器。默认轮询旧 Input Manager，可由外部输入系统替代。</summary>
    public sealed class DocumentInput
    {
        readonly Document document;
        readonly Dictionary<KeyCode, float> repeating = new Dictionary<KeyCode, float>();
        static readonly KeyCode[] keys = PhysicalKeys();
        string composition = "";
        bool hadFocus = true;
        internal DocumentInput(Document document) { this.document = document; }
        /// <summary>是否使用 Unity 旧输入后端。关闭后仍可显式发送输入。</summary>
        public bool UseUnityInput { get; set; } = true;

        void RequireReady()
        {
            document.CheckThread();
            if (!document.IsLoaded)
            {
                throw new InvalidOperationException("请先加载文档。");
            }
        }
        void Complete(int status)
        {
            document.FinishNativeCall();
            if (status < 0)
            {
                throw new InvalidOperationException("URUI 输入被拒绝：" + status);
            }
        }
        /// <summary>发送文档坐标指针输入；触摸编号必须非负，且在接触期间保持稳定。</summary>
        public void SendPointer(PointerPhase phase, Vector2 position, int pointerId = 0, bool isTouch = false,
            int button = 0, int pressedButtons = 0, Vector2 delta = default, InputModifiers modifiers = InputModifiers.None)
        {
            RequireReady();
            if (pointerId < 0 || (int)phase < 0 || (int)phase > 4 || button < 0 || button > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(pointerId));
            }

            Complete(NativeMethods.urui_pointer_event(pointerId, isTouch ? 1 : 0, (int)phase, position.x, position.y,
                            button, pressedButtons, delta.x, delta.y, (int)modifiers));
        }
        /// <summary>发送键盘事件；文本字符使用 SendText，修饰键随本次事件提供。</summary>
        public void SendKey(KeyCode key, bool pressed, InputModifiers modifiers = InputModifiers.None)
        {
            RequireReady();
            if (key == KeyCode.Tab && pressed)
            {
                var root = document.Root;
                var evt = new KeyDownEvent { KeyCode = key, Modifiers = modifiers };
                document.DispatchEvent(document.FocusedElement ?? root, evt);
                document.FinishNativeCall();
                if (root.IsValid && !evt.IsDefaultPrevented)
                {
                    MoveFocus((modifiers & InputModifiers.Shift) != 0);
                }

                document.FinishNativeCall();
                return;
            }
            Complete(NativeMethods.urui_key_event((int)key, pressed ? 1 : 0, (int)modifiers));
        }

        void MoveFocus(bool reverse)
        {
            var candidates = new List<(Element element, int order, int position)>();
            int position = 0;
            foreach (var element in document.Query())
            {
                bool eligible = element.Focusable && element.EnabledInHierarchy && element.HasLayout;
                for (var parent = element; eligible && parent != null; parent = parent.Parent)
                {
                    eligible = parent.Visible && parent.ResolvedStyle.Get("display") != "none";
                }

                if (eligible)
                {
                    candidates.Add((element, element.TabIndex == 0 ? int.MaxValue : element.TabIndex, position));
                }

                position++;
            }
            candidates.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.position.CompareTo(b.position));
            if (candidates.Count == 0)
            {
                return;
            }

            var focused = document.FocusedElement;
            int current = candidates.FindIndex(item => ReferenceEquals(item.element, focused));
            for (int step = 1; step <= candidates.Count; step++)
            {
                int index = reverse ? (current < 0 ? candidates.Count : current) - step : current + step;
                index = (index % candidates.Count + candidates.Count) % candidates.Count;
                var element = candidates[index].element;
                if (element.IsValid && element.Focus())
                {
                    return;
                }
            }
        }
        /// <summary>插入已经提交的文本，原生层按 Unicode 字符更新选区和光标。</summary>
        public void SendText(string text)
        {
            RequireReady();
            Element.RequireText(text, nameof(text));
            if (document.FocusedElement is ITextInputControl field && field.IsReadOnly)
            {
                return;
            }

            Complete(NativeMethods.urui_text_input(text));
        }
        /// <summary>更新输入法组合串，预览不产生控件值变化通知。</summary>
        public void SendComposition(string text)
        {
            RequireReady();
            Element.RequireText(text, nameof(text));
            if (document.FocusedElement is ITextInputControl field && field.IsReadOnly)
            {
                return;
            }

            Complete(NativeMethods.urui_text_composition(text, 0));
        }
        /// <summary>提交输入法组合串并产生一次值变化通知。</summary>
        public void CommitComposition(string text)
        {
            RequireReady();
            Element.RequireText(text, nameof(text));
            if (document.FocusedElement is ITextInputControl field && field.IsReadOnly)
            {
                return;
            }

            Complete(NativeMethods.urui_text_composition(text, 1));
        }
        /// <summary>取消组合串，恢复原先的已提交内容和选区。</summary>
        public void CancelComposition()
        {
            RequireReady();
            Complete(NativeMethods.urui_text_composition("", 2));
        }
        /// <summary>取消全部按下/捕获状态并释放焦点；应用失焦时自动调用。</summary>
        public void Reset()
        {
            RequireReady();
            if (composition.Length > 0 && document.FocusedElement is ITextInputControl)
            {
                CancelComposition();
            }

            composition = "";
            repeating.Clear();
            Complete(NativeMethods.urui_input_reset());
        }

        internal void CancelPointers(Element owner)
        {
            if (document.Defer(() => CancelPointers(owner), false))
            {
                return;
            }

            if (owner.IsValid && document.IsLoaded)
            {
                Complete(NativeMethods.urui_cancel_pointers(owner.Token));
            }
        }

        internal void OnUnload()
        {
            composition = "";
            repeating.Clear();
        }

        internal void PollUnity()
        {
            if (!UseUnityInput)
            {
                return;
            }

            bool focused = Application.isFocused;
            if (hadFocus && !focused)
            {
                Reset();
            }

            hadFocus = focused;
            if (!focused || !document.IsLoaded)
            {
                return;
            }

            var modifiers = CurrentModifiers();
            if (UnityInput.touchCount > 0)
            {
                for (int i = 0; i < UnityInput.touchCount; i++)
                {
                    var touch = UnityInput.GetTouch(i);
                    var phase = touch.phase == TouchPhase.Began ? PointerPhase.Down
                        : touch.phase == TouchPhase.Ended ? PointerPhase.Up
                        : touch.phase == TouchPhase.Canceled ? PointerPhase.Cancel : PointerPhase.Move;
                    SendPointer(phase, Position(touch.position), checked(touch.fingerId + 1), true, modifiers: modifiers);
                    if (!document.IsLoaded)
                    {
                        return;
                    }
                }
            }
            else
            {
                var point = Position(UnityInput.mousePosition);
                int buttons = 0;
                for (int b = 0; b < 3; b++)
                {
                    if (UnityInput.GetMouseButton(b))
                    {
                        buttons |= 1 << b;
                    }
                }

                SendPointer(PointerPhase.Move, point, pressedButtons: buttons, modifiers: modifiers);
                if (!document.IsLoaded)
                {
                    return;
                }
                for (int b = 0; b < 3; b++)
                {
                    if (UnityInput.GetMouseButtonDown(b))
                    {
                        SendPointer(PointerPhase.Down, point, button: b, pressedButtons: buttons, modifiers: modifiers);
                        if (!document.IsLoaded)
                        {
                            return;
                        }
                    }

                    if (UnityInput.GetMouseButtonUp(b))
                    {
                        SendPointer(PointerPhase.Up, point, button: b, pressedButtons: buttons, modifiers: modifiers);
                        if (!document.IsLoaded)
                        {
                            return;
                        }
                    }
                }
                if (UnityInput.mouseScrollDelta.sqrMagnitude > 0)
                {
                    SendPointer(PointerPhase.Wheel, point, delta: -UnityInput.mouseScrollDelta, modifiers: modifiers);
                }
            }
            if (!document.IsLoaded)
            {
                return;
            }

            NativeMethods.urui_clipboard_write(GUIUtility.systemCopyBuffer ?? "");
            foreach (var key in keys)
            {
                if (UnityInput.GetKeyDown(key))
                {
                    SendKey(key, true, modifiers);
                    if (!document.IsLoaded)
                    {
                        return;
                    }
                    repeating[key] = Time.unscaledTime + .4f;
                }
                else if (UnityInput.GetKeyUp(key))
                {
                    SendKey(key, false, modifiers);
                    repeating.Remove(key);
                }
                else if (repeating.TryGetValue(key, out var next) && UnityInput.GetKey(key) && Time.unscaledTime >= next)
                {
                    SendKey(key, true, modifiers);
                    if (!document.IsLoaded)
                    {
                        return;
                    }
                    repeating[key] = Time.unscaledTime + .05f;
                }
                if (!document.IsLoaded)
                {
                    return;
                }
            }
            string clipboard = NativeMethods.ReadClipboard();
            if (clipboard != GUIUtility.systemCopyBuffer)
            {
                GUIUtility.systemCopyBuffer = clipboard;
            }

            string nextComposition = UnityInput.compositionString ?? "";
            string committed = UnityInput.inputString ?? "";
            committed = committed.Replace("\b", "").Replace("\r", "").Replace("\n", "");
            if (composition.Length > 0 && nextComposition.Length == 0)
            {
                if (committed.Length > 0) { CommitComposition(committed); committed = ""; }
                else
                {
                    CancelComposition();
                }
            }
            else if (nextComposition.Length > 0 && nextComposition != composition)
            {
                SendComposition(nextComposition);
            }

            if (!document.IsLoaded)
            {
                return;
            }

            composition = nextComposition;
            if (committed.Length > 0 && nextComposition.Length == 0)
            {
                SendText(committed);
            }

            var field = document.FocusedElement;
            if (field != null && field.HasLayout)
            {
                var bounds = field.WorldBound;
                UnityInput.compositionCursorPos = new Vector2(bounds.x, Screen.height - bounds.yMax);
            }
        }
        static KeyCode[] PhysicalKeys()
        {
            var result = new List<KeyCode>();
            var seen = new HashSet<int>();
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if ((int)key > 0 && (int)key < 324 && seen.Add((int)key))
                {
                    result.Add(key);
                }
            }

            return result.ToArray();
        }
        static Vector2 Position(Vector2 point) => new Vector2(point.x, Screen.height - 1 - point.y);
        static InputModifiers CurrentModifiers()
        {
            InputModifiers value = 0;
            if (UnityInput.GetKey(KeyCode.LeftShift) || UnityInput.GetKey(KeyCode.RightShift))
            {
                value |= InputModifiers.Shift;
            }

            if (UnityInput.GetKey(KeyCode.LeftControl) || UnityInput.GetKey(KeyCode.RightControl))
            {
                value |= InputModifiers.Control;
            }

            if (UnityInput.GetKey(KeyCode.LeftAlt) || UnityInput.GetKey(KeyCode.RightAlt))
            {
                value |= InputModifiers.Alt;
            }

            if (UnityInput.GetKey(KeyCode.LeftCommand) || UnityInput.GetKey(KeyCode.RightCommand))
            {
                value |= InputModifiers.Command;
            }

            return value;
        }
        internal static KeyCode FromNativeKey(int key) => (KeyCode)key;
    }
}
