using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    public partial class Element
    {
        readonly List<EventSubscription> subscriptions = new List<EventSubscription>();

        /// <summary>注册指定类型与阶段的同步回调；同一委托和阶段仅注册一次，Dispose 只解绑。</summary>
        public IDisposable RegisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where TEvent : EventBase
        {
            RequireValid();
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            foreach (var value in subscriptions)
            {
                if (value.EventType == typeof(TEvent) && value.Callback.Equals(callback) && value.Capture == trickleDown)
                {
                    return value;
                }
            }

            var added = new EventSubscription(this, typeof(TEvent), callback, evt => callback((TEvent)evt), trickleDown);
            subscriptions.Add(added);
            return added;
        }

        /// <summary>解绑指定委托和阶段；节点失效后的重复清理安全。</summary>
        public void UnregisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where TEvent : EventBase
        {
            document.CheckThread();
            foreach (var value in subscriptions.ToArray())
            {
                if (value.EventType == typeof(TEvent) && value.Callback.Equals(callback) && value.Capture == trickleDown)
                {
                    value.Dispose();
                }
            }
        }

        /// <summary>同步发送托管自定义事件，目标固定为本元素。</summary>
        public void SendEvent(EventBase value)
        {
            RequireValid();
            document.DispatchEvent(this, value);
            document.FinishNativeCall();
        }

        /// <summary>捕获已有指针，使越界后的移动与释放仍发送到本元素。</summary>
        public void CapturePointer(int pointerId)
        {
            RequireValid();
            RequireSuccess(NativeMethods.urui_pointer_capture(pointerId, token), "捕获指针");
            document.FinishNativeCall();
        }

        /// <summary>仅释放本元素持有的指针捕获。</summary>
        public void ReleasePointer(int pointerId)
        {
            document.CheckThread();
            if (IsValid && NativeMethods.urui_pointer_captured(pointerId) == token)
            {
                RequireSuccess(NativeMethods.urui_pointer_capture(pointerId, 0), "释放指针");
                document.FinishNativeCall();
            }
        }

        /// <summary>检查是否拥有指定指针。</summary>
        public bool HasPointerCapture(int pointerId)
        {
            RequireValid();
            return NativeMethods.urui_pointer_captured(pointerId) == token;
        }

        /// <summary>控件事件钩子；在本元素普通回调之后执行。</summary>
        protected virtual void OnEvent(EventBase value) { }

        internal EventSubscription[] CaptureSubscriptions() => subscriptions.ToArray();

        internal void Deliver(EventBase value, TrickleDown phase, EventSubscription[] snapshot)
        {
            foreach (var subscription in snapshot)
            {
                if (!IsValid || value.IsImmediatePropagationStopped || document.HasPendingInvalidation)
                {
                    break;
                }

                if (subscription.EventType != value.GetType() || subscription.Capture != phase || subscription.Owner == null)
                {
                    continue;
                }

                try
                {
                    subscription.Invoke(value);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }

            if (IsValid && !document.HasPendingInvalidation && !value.IsDefaultPrevented && phase == TrickleDown.NoTrickleDown)
            {
                OnEvent(value);
            }
        }

        internal sealed class EventSubscription : IDisposable
        {
            internal Element Owner;
            internal readonly Type EventType;
            internal Delegate Callback;
            internal Action<EventBase> Invoke;
            internal readonly TrickleDown Capture;

            internal EventSubscription(Element owner, Type type, Delegate callback, Action<EventBase> invoke, TrickleDown capture)
            {
                Owner = owner;
                EventType = type;
                Callback = callback;
                Invoke = invoke;
                Capture = capture;
            }

            public void Dispose()
            {
                if (Owner == null)
                {
                    return;
                }

                Owner.document.CheckThread();
                var owner = Owner;
                owner.subscriptions.Remove(this);
                Clear();
            }

            internal void Clear()
            {
                Owner = null;
                Callback = null;
                Invoke = null;
            }
        }
    }
}
