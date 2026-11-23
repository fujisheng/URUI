using System;
using System.Collections.Generic;
using URUI.Elements;
using URUI.Events;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        InputScope inputScope;

        /// <summary>
        /// 控制文档是否接收用户输入，并约束指针释放留在最初按下的文档。
        /// 文档的 CSS、可见性和对后方文档的阻断由各自的接口管理。
        /// </summary>
        public void SetInputEnabled(bool enabled)
        {
            CheckThread();
            inputScope ??= new InputScope(this);
            inputScope.SetEnabled(enabled);
        }

        sealed class InputScope
        {
            readonly Document document;
            readonly List<IDisposable> pointerSubscriptions = new(3);
            readonly List<IDisposable> suspendedSubscriptions = new(6);
            Element root;
            bool enabled = true;

            internal InputScope(Document document)
            {
                this.document = document;
                document.Loaded += Bind;
                document.Unloaded += Unbind;
                if (document.IsLoaded)
                {
                    Bind(document);
                }
            }

            void Bind(Document owner)
            {
                root = owner.DocumentElement;
                pointerSubscriptions.Add(root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown));
                pointerSubscriptions.Add(root.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown));
                pointerSubscriptions.Add(root.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown));
                ApplySuspension();
            }

            void Unbind(Document owner)
            {
                owner.Surface.Input.ForgetPointerPresses(owner);
                Clear(suspendedSubscriptions);
                Clear(pointerSubscriptions);
                root = null;
            }

            internal void SetEnabled(bool value)
            {
                if (enabled == value)
                {
                    return;
                }

                enabled = value;
                ApplySuspension();
                if (!enabled && root != null && root.IsValid)
                {
                    var input = document.Surface.Input;
                    input.ForgetPointerPresses(document);
                    input.CancelPointers(root);
                    document.FocusedElement?.Blur();
                }
            }

            void ApplySuspension()
            {
                if (enabled)
                {
                    Clear(suspendedSubscriptions);
                }
                else if (root != null && root.IsValid)
                {
                    suspendedSubscriptions.Add(root.RegisterCallback<ClickEvent>(StopInput, TrickleDown.TrickleDown));
                    suspendedSubscriptions.Add(root.RegisterCallback<PointerMoveEvent>(StopInput, TrickleDown.TrickleDown));
                    suspendedSubscriptions.Add(root.RegisterCallback<WheelEvent>(StopInput, TrickleDown.TrickleDown));
                    suspendedSubscriptions.Add(root.RegisterCallback<KeyDownEvent>(StopInput, TrickleDown.TrickleDown));
                    suspendedSubscriptions.Add(root.RegisterCallback<KeyUpEvent>(StopInput, TrickleDown.TrickleDown));
                    suspendedSubscriptions.Add(root.RegisterCallback<TextInputEvent>(StopInput, TrickleDown.TrickleDown));
                }
            }

            void OnPointerDown(PointerDownEvent evt)
            {
                if (!enabled)
                {
                    StopInput(evt);
                    return;
                }

                document.Surface.Input.SetPointerPress(evt.PointerId, evt.Button, document);
            }

            void OnPointerUp(PointerUpEvent evt)
            {
                if (!enabled || !document.Surface.Input.OwnsPointerPress(evt.PointerId, evt.Button, document))
                {
                    StopInput(evt);
                }
            }

            void OnPointerCancel(PointerCancelEvent evt)
            {
                document.Surface.Input.ForgetPointerPress(evt.PointerId, owner: document);
            }

            static void StopInput(EventBase evt)
            {
                evt.PreventDefault();
                evt.StopImmediatePropagation();
            }

            static void Clear(List<IDisposable> subscriptions)
            {
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }
                subscriptions.Clear();
            }
        }
    }
}
