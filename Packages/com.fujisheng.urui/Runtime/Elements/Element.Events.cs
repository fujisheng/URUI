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
        EventSubscription[] subscriptionSnapshot;
        Action deferredPointerCancellation;
        // 每个节点复用取消操作，延迟与立即执行共用原有的线程、回调和世代校验。
        internal Action DeferredPointerCancellation => deferredPointerCancellation ??= CancelOwnedPointers;
        void CancelOwnedPointers() => document.Surface.Input.CancelPointers(this);
        ulong nativeInterestVersion;
        bool hasEventHook=true;
        static readonly Dictionary<Type,bool> eventHookTypes=new();
        internal void RefreshEventInterest()
        {
            if(!IsValid)return;
            var type=GetType();
            if(!eventHookTypes.TryGetValue(type,out var hook))
            {
                var method=type.GetMethod(nameof(OnEvent),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(EventBase)},null);
                // Missing reflection metadata is conservative. A user override is
                // an event consumer even when it registers no public delegate.
                hook=method==null||method.DeclaringType!=typeof(Element);eventHookTypes.Add(type,hook);
            }
            hasEventHook=hook;
            uint mask=hook?0x000ffffeu:0;
            foreach(var subscription in subscriptions)
            {var id=EventTypes.NativeId(subscription.EventType);if(id!=0)mask|=1u<<id;}
            nativeInterestVersion=checked(nativeInterestVersion+1);
            RequireSuccess(NativeMethods.urui_element_event_interest(NativeHandle,token,mask,nativeInterestVersion),"更新事件兴趣");
        }

        // 布局和挂载通知由 Document.DispatchEvent 固定为目标独占；祖先不消费它们。
        // 未初始化的控件和缺失反射元数据继续保守投递，公开事件也不进行对象复用。
        bool HasLocalNotificationConsumer(Type eventType)
        {
            if(hasEventHook)return true;
            foreach(var subscription in subscriptions)
                if(subscription.EventType==eventType&&subscription.Owner!=null)return true;
            return false;
        }


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
            subscriptions.Add(added);subscriptionSnapshot=null;RefreshEventInterest();
            return added;
        }

        /// <summary>解绑指定委托和阶段；节点失效后的重复清理安全。</summary>
        public void UnregisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where TEvent : EventBase
        {
            document.CheckThread();
            for(var index=subscriptions.Count-1;index>=0;index--)
            {
                var value=subscriptions[index];
                if(value.EventType==typeof(TEvent)&&value.Callback.Equals(callback)&&value.Capture==trickleDown)value.Dispose();
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
            RequireSuccess(NativeMethods.urui_pointer_capture(NativeHandle, pointerId, token), "捕获指针");
            document.FinishNativeCall();
        }

        /// <summary>仅释放本元素持有的指针捕获。</summary>
        public void ReleasePointer(int pointerId)
        {
            document.CheckThread();
            if (IsValid && NativeMethods.urui_pointer_captured(NativeHandle, pointerId) == token)
            {
                RequireSuccess(NativeMethods.urui_pointer_capture(NativeHandle, pointerId, 0), "释放指针");
                document.FinishNativeCall();
            }
        }

        /// <summary>检查是否拥有指定指针。</summary>
        public bool HasPointerCapture(int pointerId)
        {
            RequireValid();
            return NativeMethods.urui_pointer_captured(NativeHandle, pointerId) == token;
        }

        /// <summary>控件事件钩子；在本元素普通回调之后执行。</summary>
        protected virtual void OnEvent(EventBase value) { }

        internal EventSubscription[] CaptureSubscriptions() => subscriptionSnapshot??=subscriptions.ToArray();

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
                owner.subscriptions.Remove(this);owner.subscriptionSnapshot=null;owner.RefreshEventInterest();
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
