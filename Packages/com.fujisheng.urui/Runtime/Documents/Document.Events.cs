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
        internal enum DeferInvalidation
        {
            None,
            InvalidatesDocument
        }

        static readonly NativeMethods.ElementEventCallback eventCallback = ReceiveNativeEvent;
        internal static NativeMethods.ElementEventCallback NativeEventCallback => eventCallback;
        int invalidatingRequests;
        internal bool HasPendingInvalidation => invalidatingRequests > 0;
        readonly Stack<List<(Element element, Element.EventSubscription[] callbacks)>> idleEventPaths = new();
        int eventDepth;
        int nativeDepth;
        int imageCallbackDepth;

        internal void BeginImageCallback()
        {
            nativeDepth++;
            imageCallbackDepth++;
            surface.BeginDispatch();
        }

        internal void EndImageCallback()
        {
            imageCallbackDepth--;
            nativeDepth--;
            surface.EndDispatch();
        }

        internal void StartEvents()
        {
            if (NativeMethods.urui_animation_api_version() != NativeMethods.ExpectedAnimationApiVersion)
            {
                throw new NotSupportedException($"需要 URUI 命名动画协议 v{NativeMethods.ExpectedAnimationApiVersion}。");
            }
            var expectedEventSize = IntPtr.Size == 8 ? 88 : 84;
            if (NativeMethods.urui_element_api_version() != NativeMethods.ExpectedElementApiVersion ||
                Marshal.SizeOf<ElementEventNative>() != expectedEventSize)
            {
                throw new InvalidOperationException($"URUI 原生插件需要元素协议版本 {NativeMethods.ExpectedElementApiVersion} 和 {expectedEventSize} 字节事件包。");
            }

            if (NativeMethods.urui_document_listen(SurfaceHandle, NativeHandle) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("URUI 同步事件初始化失败。");
            }
        }

        [AOT.MonoPInvokeCallback(typeof(NativeMethods.ElementEventCallback))]
        static int ReceiveNativeEvent(ref ElementEventNative native)
        {
            var owner = URUI.Hosting.Surface.FindDocument(native.surface, native.document);
            if (ReferenceEquals(owner, null))
            {
                return 2;
            }
            if (owner.changingFragment)
            {
                return 0;
            }

            owner.surface.BeginDispatch();
            owner.nativeDepth++;
            try
            {
                if (!owner || !owner.isActiveAndEnabled || !owner.IsLoaded || owner.NativeHandle != native.document || owner.HasPendingInvalidation)
                {
                    return 2;
                }

                var target = owner.WrapElement(native.target);
                if (target == null || !target.IsAttached)
                {
                    return 2;
                }

                if (!target.EnabledInHierarchy && native.type != 5 && native.type != 12 && native.type != 15 && native.type != 16 && native.type != 17 && native.type != 18 && native.type != 19)
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
                    key.KeyCode = URUI.Hosting.SurfaceInput.FromNativeKey(native.key);
                    key.Modifiers = (InputModifiers)native.modifiers;
                }
                if (value is TextInputEvent text)
                {
                    text.Text = Marshal.PtrToStringUTF8(native.text) ?? "";
                }
                if (value is AnimationEndEvent || value is AnimationCreatedEvent)
                {
                    if (!NativeMethods.TryReadAnimationCurrentEvent(native.surface, native.document, out var animationName, out var identity, out var iterations))
                    {
                        throw new InvalidOperationException("原生命名动画事件无效。");
                    }
                    var property = Marshal.PtrToStringUTF8(native.text) ?? "";
                    if (value is AnimationEndEvent animation)
                    {
                        animation.PropertyName = property;
                        animation.AnimationName = animationName;
                        animation.Identity = identity;
                    }
                    if (value is AnimationCreatedEvent created)
                    {
                        created.PropertyName = property;
                        created.AnimationName = animationName;
                        created.Identity = identity;
                        created.Iterations = iterations;
                    }
                }
                if (value is TransitionEndEvent transition)
                {
                    transition.PropertyName = Marshal.PtrToStringUTF8(native.text) ?? "";
                }

                owner.DispatchEvent(target, value);
                // Managed propagation has already completed. Let URUI execute its default
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
                owner.surface.EndDispatch();
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

            var path = idleEventPaths.Count > 0 ? idleEventPaths.Pop() : new List<(Element element, Element.EventSubscription[] callbacks)>();
            uint version = generation;
            bool wasAttached = target.IsAttached;
            try
            {
                for (var current = target; current != null; current = current.Parent)
                {
                    path.Add((current, current.CaptureSubscriptions()));
                    if (!value.Bubbles)
                    {
                        break;
                    }
                }
                value.Target = target;
                value.Dispatching = true;
                eventDepth++;
                surface.BeginDispatch();
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
                    surface.EndDispatch();
                    value.CurrentTarget = null;
                    value.Dispatching = false;
                }
            }
            finally
            {
                path.Clear();
                idleEventPaths.Push(path);
            }
            bool CanDeliver() => IsCurrent(version) && target.IsValid && (!wasAttached || target.IsAttached)
                && !HasPendingInvalidation && !value.IsImmediatePropagationStopped;
        }

        internal bool Defer(Action action, DeferInvalidation invalidation, bool catchExceptions = false)
        {
            if (effectCallbackDepth != 0 || surface && surface.IsEffectCallback)
            {
                throw new InvalidOperationException("Effect Prepare/Record 中禁止修改 DOM 或文档。请使用不可变 EffectFrame。");
            }
            if (eventDepth == 0 && nativeDepth == 0 && (ReferenceEquals(surface, null) || !surface.IsDispatching))
            {
                return false;
            }
            EnqueueDeferred(action, invalidation, catchExceptions);
            return true;
        }

        bool DeferLifecycle(Action action)
        {
            if (!changingContent)
            {
                return Defer(action, DeferInvalidation.InvalidatesDocument);
            }
            EnqueueDeferred(action, DeferInvalidation.InvalidatesDocument);
            return true;
        }

        internal readonly struct DeferredAction
        {
            readonly Document owner;
            readonly Action action;
            readonly FragmentChange fragment;
            readonly DeferInvalidation invalidation;
            readonly bool catchExceptions;

            internal DeferredAction(Document owner, Action action, DeferInvalidation invalidation, bool catchExceptions)
            {
                this.owner = owner;
                this.action = action;
                fragment = owner?.fragmentChange;
                this.invalidation = invalidation;
                this.catchExceptions = catchExceptions;
            }

            internal void Invoke()
            {
                if (!ReferenceEquals(owner, null) && invalidation == DeferInvalidation.InvalidatesDocument)
                {
                    owner.invalidatingRequests--;
                }
                if (fragment != null && !fragment.Committed)
                {
                    return;
                }
                if (!catchExceptions)
                {
                    action();
                    return;
                }
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }
        }

        internal void EnqueueDeferred(Action action, DeferInvalidation invalidation, bool catchExceptions = false)
        {
            if (ReferenceEquals(surface, null))
            {
                throw new InvalidOperationException("文档回调缺少所属 Surface。");
            }
            if (invalidation == DeferInvalidation.InvalidatesDocument)
            {
                invalidatingRequests++;
            }
            surface.Enqueue(new DeferredAction(this, action, invalidation, catchExceptions));
        }

        /// <summary>
        /// 在当前同步事件和原生回调返回后执行；空闲时立即执行，不等待下一帧。
        /// </summary>
        /// <remarks>
        /// 适合需要同步创建/克隆节点的应用操作。调度本身不停止当前事件传播；延后操作的异常与事件回调一样记录。
        /// </remarks>
        public void RunAfterEvent(Action action)
        {
            CheckThread();
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (Defer(action, DeferInvalidation.None, catchExceptions: true))
            {
                return;
            }
            action();
        }

        internal void BeginControlUpdate()
        {
            CheckThread();
            eventDepth++;
            surface.BeginDispatch();
        }

        internal void EndControlUpdate()
        {
            eventDepth--;
            surface.EndDispatch();
            FinishNativeCall();
        }

        internal void FinishNativeCall()
        {
            MarkGeometryChanged();
            if (eventDepth != 0 || nativeDepth != 0 || changingContent)
            {
                return;
            }
            if (!ReferenceEquals(surface, null))
            {
                surface.FinishNativeCall();
            }
        }
    }
}
