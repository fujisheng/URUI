using System;
using System.Collections.Generic;
using URUI.Events;

namespace URUI.Elements
{
    public sealed partial class Element
    {
        readonly List<ClickSubscription> subscriptions = new List<ClickSubscription>();

        /// <summary>注册节点事件，当前仅支持 ClickEvent；相同委托重复注册返回同一订阅。</summary>
        /// <remarks>返回值 Dispose 只解绑。脱离层级会保留订阅，销毁或卸载会使订阅失效。</remarks>
        public IDisposable RegisterCallback<TEvent>(Action<TEvent> callback) where TEvent : struct
        {
            RequireValid();
            RequireEvent<TEvent>();
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var typed = (Action<ClickEvent>)(object)callback;
            foreach (var subscription in subscriptions)
            {
                if (subscription.Callback == typed)
                {
                    return subscription;
                }
            }

            var added = new ClickSubscription(this, typed);
            subscriptions.Add(added);
            return added;
        }

        /// <summary>解绑指定委托；节点销毁后的清理和重复解绑安全，未支持的事件类型会抛异常。</summary>
        public void UnregisterCallback<TEvent>(Action<TEvent> callback) where TEvent : struct
        {
            document.CheckThread();
            RequireEvent<TEvent>();
            var typed = (Action<ClickEvent>)(object)callback;
            for (var i = subscriptions.Count - 1; i >= 0; i--)
            {
                if (subscriptions[i].Callback == typed)
                {
                    subscriptions[i].Dispose();
                }
            }
        }

        static void RequireEvent<TEvent>() where TEvent : struct
        {
            if (typeof(TEvent) != typeof(ClickEvent))
            {
                throw new NotSupportedException("URUI 当前仅提供 ClickEvent 通知。");
            }
        }

        internal ClickSubscription[] CaptureClickSubscriptions() => subscriptions.ToArray();

        internal sealed class ClickSubscription : IDisposable
        {
            Element owner;

            internal Action<ClickEvent> Callback
            {
                get;
                private set;
            }

            internal ClickSubscription(Element owner, Action<ClickEvent> callback)
            {
                this.owner = owner;
                Callback = callback;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }

                owner.document.CheckThread();
                owner.subscriptions.Remove(this);
                Clear();
            }

            internal void Clear()
            {
                Callback = null;
                owner = null;
            }
        }
    }
}
