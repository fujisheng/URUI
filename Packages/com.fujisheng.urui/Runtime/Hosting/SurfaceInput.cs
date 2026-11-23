using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Elements;
using URUI.Controls;
using URUI.Events;
using URUI.Internal.Interop;
using UnityInput = UnityEngine.Input;
using URUI.Documents;

namespace URUI.Hosting
{
    /// <summary>
    /// 主线程同步输入适配器。默认轮询旧 Input Manager，可由外部输入系统替代。
    /// </summary>
    public sealed class SurfaceInput
    {
        readonly Surface surface;
        readonly List<(int Pointer, int Button, Document Owner)> pointerPresses = new();
        Document document => surface.FocusedDocument ?? surface.TopDocument;
        readonly Dictionary<KeyCode, float> repeating = new Dictionary<KeyCode, float>();
        readonly HashSet<KeyCode> activeHeldKeys = new HashSet<KeyCode>();
        readonly List<KeyCode> heldKeyScratch = new List<KeyCode>();
        static readonly KeyCode[] keys = PhysicalKeys();
        string composition = "";
        bool hadFocus = true;
        Element clipboardFocus;
        ulong clipboardVersion;
        bool hasLastMouseState;
        Vector2 lastMousePosition;
        int lastMouseButtons;
        InputModifiers lastMouseModifiers;
        ulong lastPollLayoutVersion;

        // Shared chrome must also observe presses on empty Surface space or another document.
        internal event Action<Vector2> PointerPressed;

        internal SurfaceInput(Surface surface)
        {
            this.surface = surface;
        }

        /// <summary>
        /// 是否使用 Unity 旧输入后端。关闭后仍可显式发送输入。
        /// </summary>
        public bool UseUnityInput
        {
            get;
            set;
        } = true;

        /// <summary>
        /// 使用 Unity 屏幕坐标判断场景输入是否应让给此 Surface。
        /// 命中作者节点或存在可见模态文档时由 UI 接管。文档根与 body 的空白区域允许场景输入。
        /// </summary>
        public bool BlocksPointer(Vector2 screenPosition)
        {
            surface.CheckThread();
            var host = surface.Host;
            if (!surface.isActiveAndEnabled || !host || !host.Ready ||
                Screen.width <= 0 || Screen.height <= 0 ||
                !new Rect(0, 0, Screen.width, Screen.height).Contains(screenPosition))
            {
                return false;
            }

            foreach (var owner in surface.DocumentsSnapshot())
            {
                if (owner && owner.IsLoaded && owner.Presented && owner.IsModal)
                {
                    return true;
                }
            }

            var surfaceHandle = surface.SurfaceHandle;
            var point = Position(screenPosition);
            var token = NativeMethods.urui_surface_pick(surfaceHandle, point.x, point.y, out var handle);
            var ownerDocument = Surface.FindDocument(surfaceHandle, handle);
            return token != 0 && ownerDocument && ReferenceEquals(ownerDocument.Surface, surface) &&
                token != NativeMethods.urui_document_root(surfaceHandle, handle) &&
                token != NativeMethods.urui_document_body(surfaceHandle, handle);
        }

        void RequireReady(bool allowCancellation = false)
        {
            surface.CheckThread();
            if (surface.IsEffectCallback || surface.IsBusy && !allowCancellation)
            {
                throw new InvalidOperationException("不能在 Surface 同步回调或加载事务中重新分发输入。");
            }
            if (!surface.HasLoadedDocuments)
            {
                throw new InvalidOperationException("请先加载文档。");
            }
        }

        void Complete(int status)
        {
            surface.FinishNativeCall();
            if (status < 0)
            {
                throw new InvalidOperationException("URUI 输入被拒绝：" + status);
            }
        }

        /// <summary>
        /// 发送文档坐标指针输入；触摸编号必须非负，且在接触期间保持稳定。
        /// </summary>
        public void SendPointer(PointerPhase phase, Vector2 position, int pointerId = 0, bool isTouch = false,
            int button = 0, int pressedButtons = 0, Vector2 delta = default, InputModifiers modifiers = InputModifiers.None)
        {
            RequireReady();
            if (pointerId < 0 || (int)phase < 0 || (int)phase > 4 || button < 0 || button > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(pointerId));
            }

            if (phase == PointerPhase.Down)
            {
                ForgetPointerPress(pointerId, button);
            }
            int status;
            try
            {
                status = NativeMethods.urui_pointer_event(surface.SurfaceHandle, pointerId, isTouch ? 1 : 0, (int)phase,
                    position.x, position.y, button, pressedButtons, delta.x, delta.y, (int)modifiers);
            }
            finally
            {
                if (phase == PointerPhase.Up)
                {
                    ForgetPointerPress(pointerId, button);
                }
                else if (phase == PointerPhase.Cancel)
                {
                    ForgetPointerPress(pointerId);
                }
            }
            Complete(status);
            if (phase == PointerPhase.Down)
            {
                PointerPressed?.Invoke(position);
            }
        }

        internal void SetPointerPress(int pointer, int button, Document owner)
        {
            ForgetPointerPress(pointer, button);
            pointerPresses.Add((pointer, button, owner));
        }

        internal bool OwnsPointerPress(int pointer, int button, Document owner)
        {
            foreach (var press in pointerPresses)
            {
                if (press.Pointer == pointer && press.Button == button && ReferenceEquals(press.Owner, owner))
                {
                    return true;
                }
            }
            return false;
        }

        internal void ForgetPointerPress(int pointer, int? button = null, Document owner = null)
        {
            for (var index = pointerPresses.Count - 1; index >= 0; index--)
            {
                var press = pointerPresses[index];
                if (press.Pointer == pointer && (!button.HasValue || button.Value == press.Button)
                    && (owner == null || ReferenceEquals(press.Owner, owner)))
                {
                    pointerPresses.RemoveAt(index);
                }
            }
        }

        internal void ForgetPointerPresses(Document owner)
        {
            for (var index = pointerPresses.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(pointerPresses[index].Owner, owner))
                {
                    pointerPresses.RemoveAt(index);
                }
            }
        }

        /// <summary>
        /// 发送键盘事件；文本字符使用 SendText，修饰键随本次事件提供。
        /// </summary>
        public void SendKey(KeyCode key, bool pressed, InputModifiers modifiers = InputModifiers.None)
        {
            RequireReady();
            if (key == KeyCode.Tab && pressed)
            {
                if (!document)
                {
                    return;
                }
                var root = document.DocumentElement;
                var evt = new KeyDownEvent
                {
                    KeyCode = key,
                    Modifiers = modifiers
                };
                document.DispatchEvent(document.FocusedElement ?? root, evt);
                surface.FinishNativeCall();
                if (root.IsValid && !evt.IsDefaultPrevented)
                {
                    MoveFocus((modifiers & InputModifiers.Shift) != 0);
                }

                surface.FinishNativeCall();
                return;
            }
            Complete(NativeMethods.urui_key_event(surface.SurfaceHandle, (int)key, pressed ? 1 : 0, (int)modifiers));
        }

        void MoveFocus(bool reverse)
        {
            var candidates = new List<(Element element, long order, int position)>();
            int position = 0;
            foreach (var element in document.Query())
            {
                bool eligible = element.Focusable && element.TabIndex >= 0 && element.EnabledInHierarchy && element.HasLayout && element.IsVisibleInHierarchy;
                if (eligible)
                {
                    candidates.Add((element, element.TabIndex == 0 ? long.MaxValue : element.TabIndex, position));
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

        /// <summary>
        /// 插入已经提交的文本，原生层按 Unicode 字符更新选区和光标。
        /// </summary>
        public void SendText(string text)
        {
            RequireReady();
            Element.RequireText(text, nameof(text));
            if (document?.FocusedElement is ITextInputControl field && field.IsReadOnly)
            {
                return;
            }

            Complete(NativeMethods.urui_text_input(surface.SurfaceHandle, text));
        }

        /// <summary>
        /// 更新输入法组合串，预览不产生控件值变化通知。
        /// </summary>
        public void SendComposition(string text)
        {
            RequireReady();
            Element.RequireText(text, nameof(text));
            if (document?.FocusedElement is ITextInputControl field && field.IsReadOnly)
            {
                return;
            }

            Complete(NativeMethods.urui_text_composition(surface.SurfaceHandle, text, 0));
        }

        /// <summary>
        /// 提交输入法组合串并产生一次值变化通知。
        /// </summary>
        public void CommitComposition(string text)
        {
            RequireReady();
            Element.RequireText(text, nameof(text));
            if (document?.FocusedElement is ITextInputControl field && field.IsReadOnly)
            {
                return;
            }

            Complete(NativeMethods.urui_text_composition(surface.SurfaceHandle, text, 1));
        }

        /// <summary>
        /// 取消组合串，恢复原先的已提交内容和选区。
        /// </summary>
        public void CancelComposition()
        {
            RequireReady(true);
            Complete(NativeMethods.urui_text_composition(surface.SurfaceHandle, "", 2));
        }

        /// <summary>
        /// 取消全部按下/捕获状态并释放焦点；应用失焦时自动调用。
        /// </summary>
        public void Reset()
        {
            RequireReady();
            if (composition.Length > 0 && document?.FocusedElement is ITextInputControl)
            {
                CancelComposition();
            }

            composition = "";
            repeating.Clear();
            activeHeldKeys.Clear();
            pointerPresses.Clear();
            hasLastMouseState = false;
            Complete(NativeMethods.urui_input_reset(surface.SurfaceHandle));
        }

        /// <summary>
        /// 取消指定子树持有的指针交互；隐藏或移除交互区域前可调用。
        /// </summary>
        public void CancelPointers(Element owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            surface.CheckThread();
            if (!ReferenceEquals(owner.Document.Surface, surface))
            {
                throw new ArgumentException("Pointer owner belongs to another document.", nameof(owner));
            }
            if (owner.Document.Defer(owner.DeferredPointerCancellation, Document.DeferInvalidation.None))
            {
                return;
            }

            if (owner.IsValid && owner.Document.IsLoaded)
            {
                Complete(NativeMethods.urui_cancel_pointers(surface.SurfaceHandle, owner.NativeHandle, owner.Token));
            }
        }

        internal void OnUnload()
        {
            clipboardFocus = null;
            clipboardVersion = 0;
            composition = "";
            repeating.Clear();
            activeHeldKeys.Clear();
            hasLastMouseState = false;
        }

        ulong UnityClipboardVersion()
        {
            var version = NativeMethods.urui_clipboard_version(surface.SurfaceHandle);
            if (version == 0)
            {
                throw new InvalidOperationException("剪贴板所属上下文已失效。");
            }
            return version;
        }

        void ImportUnityClipboard()
        {
            var result = NativeMethods.urui_clipboard_write(surface.SurfaceHandle, GUIUtility.systemCopyBuffer ?? "");
            if (result != NativeStatus.Ok)
            {
                throw new InvalidOperationException("系统剪贴板同步失败。");
            }
        }

        internal void SyncUnityClipboardFocus()
        {
            surface.CheckThread();
            var focus = document?.FocusedElement;
            if (ReferenceEquals(focus, clipboardFocus))
            {
                return;
            }
            if (clipboardVersion == 0)
            {
                clipboardVersion = UnityClipboardVersion();
            }
            FlushUnityClipboard();
            clipboardFocus = focus;
            if (focus is ITextInputControl)
            {
                ImportUnityClipboard();
            }
        }

        internal void FlushUnityClipboard()
        {
            surface.CheckThread();
            var version = UnityClipboardVersion();
            if (clipboardVersion == 0)
            {
                clipboardVersion = version;
                return;
            }
            if (version == clipboardVersion)
            {
                return;
            }
            GUIUtility.systemCopyBuffer = NativeMethods.ReadClipboard(surface.SurfaceHandle) ?? "";
            clipboardVersion = version;
        }

        internal void SendUnityKey(KeyCode key, bool pressed, InputModifiers modifiers)
        {
            SyncUnityClipboardFocus();
            if (pressed && key == KeyCode.V && (modifiers & (InputModifiers.Control | InputModifiers.Command)) != 0)
            {
                ImportUnityClipboard();
            }
            SendKey(key, pressed, modifiers);
            if (surface.HasLoadedDocuments)
            {
                FlushUnityClipboard();
            }
        }

        internal void PollUnityPointer(Vector2 position, int pointer, bool touch, int buttons, InputModifiers modifiers)
        {
            RequireReady();
            Complete(NativeMethods.urui_pointer_poll(surface.SurfaceHandle, pointer, touch ? 1 : 0, position.x, position.y, buttons, (int)modifiers));
        }

        internal void PollUnity()
        {
            if (!UseUnityInput)
            {
                return;
            }

            bool focused = Application.isFocused;
            if (hadFocus && !focused && surface.HasLoadedDocuments)
            {
                Reset();
            }

            hadFocus = focused;
            if (!focused || !surface.HasLoadedDocuments)
            {
                return;
            }

            var modifiers = CurrentModifiers();
            if (UnityInput.touchCount > 0)
            {
                hasLastMouseState = false;
                for (int i = 0; i < UnityInput.touchCount; i++)
                {
                    var touch = UnityInput.GetTouch(i);
                    var phase = touch.phase == TouchPhase.Began ? PointerPhase.Down
                        : touch.phase == TouchPhase.Ended ? PointerPhase.Up
                        : touch.phase == TouchPhase.Canceled ? PointerPhase.Cancel : PointerPhase.Move;
                    if (phase == PointerPhase.Move)
                    {
                        PollUnityPointer(Position(touch.position), checked(touch.fingerId + 1), true, 0, modifiers);
                    }
                    else
                    {
                        SendPointer(phase, Position(touch.position), checked(touch.fingerId + 1), true, modifiers: modifiers);
                    }
                    if (!surface.HasLoadedDocuments)
                    {
                        return;
                    }
                }
            }
            else if (Screen.width > 0 && Screen.height > 0)
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

                var layoutVersion = NativeMethods.urui_surface_layout_version(surface.SurfaceHandle);
                if (!hasLastMouseState || point != lastMousePosition || buttons != lastMouseButtons ||
                    modifiers != lastMouseModifiers || layoutVersion != lastPollLayoutVersion)
                {
                    PollUnityPointer(point, 0, false, buttons, modifiers);
                    if (!surface.HasLoadedDocuments)
                    {
                        return;
                    }
                    lastMousePosition = point;
                    lastMouseButtons = buttons;
                    lastMouseModifiers = modifiers;
                    lastPollLayoutVersion = layoutVersion;
                    hasLastMouseState = true;
                }

                for (int b = 0; b < 3; b++)
                {
                    if (UnityInput.GetMouseButtonDown(b))
                    {
                        SendPointer(PointerPhase.Down, point, button: b, pressedButtons: buttons, modifiers: modifiers);
                        if (!surface.HasLoadedDocuments)
                        {
                            return;
                        }
                    }

                    if (UnityInput.GetMouseButtonUp(b))
                    {
                        SendPointer(PointerPhase.Up, point, button: b, pressedButtons: buttons, modifiers: modifiers);
                        if (!surface.HasLoadedDocuments)
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
            if (!surface.HasLoadedDocuments)
            {
                return;
            }

            var activeDoc = document;
            var focusedElement = activeDoc?.FocusedElement;
            var isTextInput = focusedElement is ITextInputControl;
            if (isTextInput || clipboardFocus != null)
            {
                SyncUnityClipboardFocus();
            }

            var hasKeyActivity = UnityInput.anyKey || UnityInput.anyKeyDown;
            if (hasKeyActivity)
            {
                var now = Time.unscaledTime;
                foreach (var key in keys)
                {
                    if (UnityInput.GetKeyDown(key))
                    {
                        activeHeldKeys.Add(key);
                        SendUnityKey(key, true, modifiers);
                        if (!surface.HasLoadedDocuments)
                        {
                            return;
                        }
                        repeating[key] = now + .4f;
                    }
                    else if (UnityInput.GetKeyUp(key))
                    {
                        activeHeldKeys.Remove(key);
                        SendUnityKey(key, false, modifiers);
                        repeating.Remove(key);
                    }
                    else if (repeating.TryGetValue(key, out var next) && UnityInput.GetKey(key) && now >= next)
                    {
                        SendUnityKey(key, true, modifiers);
                        if (!surface.HasLoadedDocuments)
                        {
                            return;
                        }
                        repeating[key] = now + .05f;
                    }
                    if (!surface.HasLoadedDocuments)
                    {
                        return;
                    }
                }
            }
            else if (activeHeldKeys.Count > 0)
            {
                heldKeyScratch.Clear();
                foreach (var key in activeHeldKeys)
                {
                    heldKeyScratch.Add(key);
                }
                for (var i = 0; i < heldKeyScratch.Count; i++)
                {
                    var key = heldKeyScratch[i];
                    if (UnityInput.GetKeyUp(key) || !UnityInput.GetKey(key))
                    {
                        activeHeldKeys.Remove(key);
                        repeating.Remove(key);
                        SendUnityKey(key, false, modifiers);
                        if (!surface.HasLoadedDocuments)
                        {
                            return;
                        }
                    }
                }
            }

            if (isTextInput || clipboardFocus != null)
            {
                FlushUnityClipboard();
            }

            if (hasKeyActivity || isTextInput || composition.Length > 0)
            {
                string nextComposition = UnityInput.compositionString ?? "";
                string committed = UnityInput.inputString ?? "";
                if (committed.Length > 0)
                {
                    committed = committed.Replace("\b", "").Replace("\r", "").Replace("\n", "");
                }
                if (composition.Length > 0 && nextComposition.Length == 0)
                {
                    if (committed.Length > 0)
                    {
                        CommitComposition(committed);
                        committed = "";
                    }
                    else
                    {
                        CancelComposition();
                    }
                }
                else if (nextComposition.Length > 0 && nextComposition != composition)
                {
                    SendComposition(nextComposition);
                }

                if (!surface.HasLoadedDocuments)
                {
                    return;
                }

                composition = nextComposition;
                if (committed.Length > 0 && nextComposition.Length == 0)
                {
                    SendText(committed);
                }
            }

            var field = hasKeyActivity || isTextInput ? document?.FocusedElement : focusedElement;
            if (field != null && field.HasLayout && surface.Width > 0 && surface.Height > 0)
            {
                var bounds = field.WorldBound;
                float scaleX = (float)Screen.width / surface.Width;
                float scaleY = (float)Screen.height / surface.Height;
                UnityInput.compositionCursorPos = new Vector2(bounds.x * scaleX, Screen.height - bounds.yMax * scaleY);
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

        Vector2 Position(Vector2 point)
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return Vector2.zero;
            }
            return new Vector2(point.x * surface.Width / Screen.width, surface.Height - 1 - point.y * surface.Height / Screen.height);
        }

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
