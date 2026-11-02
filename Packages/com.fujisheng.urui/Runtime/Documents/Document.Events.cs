using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        static readonly NativeMethods.ElementEventCallback eventCallback = ReceiveNativeEvent;
        static Document eventDocument;
        readonly Queue<(Action action, bool invalidates)> deferred = new Queue<(Action, bool)>();
        int invalidatingRequests;
        internal bool HasPendingInvalidation => invalidatingRequests > 0;
        int eventDepth;
        int nativeDepth;
        bool flushingDeferred;

        internal void StartEvents()
        {
            if (NativeMethods.urui_element_api_version() != 1)
            {
                throw new InvalidOperationException("URUI 原生插件需要元素协议版本 1。");
            }

            eventDocument = this;
            if (NativeMethods.urui_set_event_callback(eventCallback) != 1 || NativeMethods.urui_document_listen() != 1)
            {
                throw new InvalidOperationException("URUI 同步事件初始化失败。");
            }
        }

        internal void StopEvents()
        {
            if (ReferenceEquals(eventDocument, this))
            {
                NativeMethods.urui_set_event_callback(null);
                eventDocument = null;
            }
        }

        [AOT.MonoPInvokeCallback(typeof(NativeMethods.ElementEventCallback))]
        static int ReceiveNativeEvent(ref ElementEventNative native)
        {
            var owner = eventDocument;
            if (ReferenceEquals(owner, null))
            {
                return 2;
            }

            owner.nativeDepth++;
            try
            {
                if (!owner || !owner.isActiveAndEnabled || !owner.IsLoaded || owner.HasPendingInvalidation)
                {
                    return 2;
                }

                var target = owner.WrapElement(native.target);
                if (target == null || !target.IsAttached)
                {
                    return 2;
                }

                if (!target.EnabledInHierarchy && native.type != 5 && native.type != 12 && native.type != 15 && native.type != 16)
                {
                    return 2;
                }

                var value = EventTypes.Create(native.type);
                if (value is PointerEventBase pointer)
                {
                    pointer.PointerId = native.pointer;
                    pointer.Position = new Vector2(native.x, native.y);
                    pointer.Button = native.button;
                    pointer.PressedButtons = native.buttons;
                    pointer.Modifiers = (InputModifiers)native.modifiers;
                    pointer.IsTouch = native.kind == 1;
                }
                if (value is WheelEvent wheel)
                {
                    wheel.Delta = new Vector2(native.dx, native.dy);
                }

                if (value is KeyboardEventBase key)
                {
                    key.KeyCode = DocumentInput.FromNativeKey(native.key);
                    key.Modifiers = (InputModifiers)native.modifiers;
                }
                if (value is TextInputEvent text)
                {
                    text.Text = Marshal.PtrToStringUTF8(native.text) ?? "";
                }

                owner.DispatchEvent(target, value);
                // Managed propagation has already completed. Let RmlUi execute its default
                // action unless explicitly cancelled, preserving StopPropagation semantics.
                return value.IsDefaultPrevented || owner.HasPendingInvalidation || !owner.IsLoaded ? 2 : 0;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                return 2;
            }
            finally
            {
                owner.nativeDepth--;
            }
        }

        internal void DispatchEvent(Element target, EventBase value)
        {
            CheckThread();
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Dispatching)
            {
                throw new InvalidOperationException("同一事件不能重入分发。");
            }

            var path = new List<(Element element, Element.EventSubscription[] callbacks)>();
            for (var current = target; current != null; current = current.Parent)
            {
                path.Add((current, current.CaptureSubscriptions()));
                if (!value.Bubbles || value is GeometryChangedEvent || value is AttachToDocumentEvent || value is DetachFromDocumentEvent)
                {
                    break;
                }
            }
            value.Target = target;
            value.Dispatching = true;
            uint version = generation;
            bool wasAttached = target.IsAttached;
            eventDepth++;
            try
            {
                for (int i = path.Count - 1; i >= 0; i--)
                {
                    if (!CanDeliver())
                    {
                        return;
                    }

                    var entry = path[i];
                    if (!entry.element.IsValid || (wasAttached && !entry.element.IsAttached))
                    {
                        continue;
                    }

                    value.CurrentTarget = entry.element;
                    value.PropagationPhase = i == 0 ? PropagationPhase.AtTarget : PropagationPhase.TrickleDown;
                    entry.element.Deliver(value, TrickleDown.TrickleDown, entry.callbacks);
                    if (value.IsPropagationStopped && i != 0)
                    {
                        return;
                    }
                }
                for (int i = 0; i < path.Count; i++)
                {
                    if (!CanDeliver() || (i != 0 && value.IsPropagationStopped))
                    {
                        return;
                    }

                    var entry = path[i];
                    if (!entry.element.IsValid || (wasAttached && !entry.element.IsAttached))
                    {
                        continue;
                    }

                    value.CurrentTarget = entry.element;
                    value.PropagationPhase = i == 0 ? PropagationPhase.AtTarget : PropagationPhase.BubbleUp;
                    entry.element.Deliver(value, TrickleDown.NoTrickleDown, entry.callbacks);
                }
            }
            finally
            {
                eventDepth--;
                value.Dispatching = false;
            }
            bool CanDeliver() => IsCurrent(version) && target.IsValid && (!wasAttached || target.IsAttached)
                && !HasPendingInvalidation && !value.IsImmediatePropagationStopped;
        }

        internal bool Defer(Action action, bool invalidates = true)
        {
            if (eventDepth == 0 && nativeDepth == 0)
            {
                return false;
            }

            deferred.Enqueue((action, invalidates));
            if (invalidates)
            {
                invalidatingRequests++;
            }

            return true;
        }

        internal void BeginControlUpdate() { CheckThread(); eventDepth++; }
        internal void EndControlUpdate() { eventDepth--; FinishNativeCall(); }

        internal void FinishNativeCall()
        {
            if (eventDepth != 0 || nativeDepth != 0 || flushingDeferred)
            {
                return;
            }

            flushingDeferred = true;
            try
            {
                if (host && host.Ready)
                {
                    NativeMethods.urui_process_deferred_operations();
                }

                while (deferred.Count > 0)
                {
                    var next = deferred.Dequeue();
                    if (next.invalidates)
                    {
                        invalidatingRequests--;
                    }

                    next.action();
                }
            }
            finally { flushingDeferred = false; }
        }
    }
}
