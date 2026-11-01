using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Documents
{
    // 在首个业务回调前固定点击路径与订阅快照，回调修改层级时不继续访问失效接收者。
    public sealed partial class Document
    {
        void DispatchClick(ulong token)
        {
            if (!isActiveAndEnabled || !IsLoaded)
            {
                return;
            }

            var target = WrapElement(token);
            if (target == null || !target.IsAttached)
            {
                return;
            }

            var version = generation;
            var path = new List<(Element receiver, Element.ClickSubscription[] subscriptions)>();
            // 路径和所有订阅在第一条回调前固定；回调中新增的祖先订阅留到下一次点击。
            for (var current = target; current != null; current = current.Parent)
            {
                path.Add((current, current.CaptureClickSubscriptions()));
            }

            foreach (var delivery in path)
            {
                foreach (var subscription in delivery.subscriptions)
                {
                    if (!IsCurrent(version) || !target.IsValid || !target.IsAttached)
                    {
                        return;
                    }

                    if (!delivery.receiver.IsValid || !delivery.receiver.IsAttached || subscription.Callback == null)
                    {
                        continue;
                    }

                    try
                    {
                        subscription.Callback(new ClickEvent(target, delivery.receiver));
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                    }
                }
            }
        }
    }
}
