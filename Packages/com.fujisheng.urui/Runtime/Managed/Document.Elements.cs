using System;
using System.Collections.Generic;
using URUI.Internal;
using UnityEngine;

namespace URUI
{
    public sealed partial class Document
    {
        readonly Dictionary<ulong, Element> elements = new Dictionary<ulong, Element>();

        /// <summary>当前 RML 文档的根节点，未加载时为 null；根的 Parent 永远为 null。</summary>
        public Element Root
        {
            get
            {
                CheckThread();
                if (!IsLoaded)
                {
                    return null;
                }

                return WrapElement(NativeMethods.urui_element_root()) ?? throw new InvalidOperationException("URUI 文档根身份不可用。");
            }
        }

        /// <summary>创建当前文档拥有的脱离层级节点，默认 div；需要 Add 后才参与显示和输入。</summary>
        /// <remarks>没有绑定、控件工厂或 FUI 依赖。文档卸载时也会销毁尚未挂载的节点。</remarks>
        public Element CreateElement(string tag = "div")
        {
            CheckThread();
            RequireName(tag, nameof(tag));
            if (!IsLoaded)
            {
                throw new InvalidOperationException("请先加载文档，再创建元素。");
            }

            return WrapElement(NativeMethods.urui_element_create(tag)) ?? throw new InvalidOperationException("URUI 无法创建此标签或元素引用预算已满。");
        }

        /// <summary>在当前根及其 DOM 后代中查找名字/类名的首项；未加载时为 null。</summary>
        public Element Q(string name = null, string className = null)
        {
            CheckQuery(name, className);
            return Root?.Q(name, className);
        }

        /// <summary>当前根及其 DOM 后代的深度优先快照；未加载时为空列表。</summary>
        public IReadOnlyList<Element> Query(string name = null, string className = null)
        {
            CheckQuery(name, className);
            return Root?.Query(name, className) ?? Array.Empty<Element>();
        }

        void CheckQuery(string name, string className)
        {
            CheckThread();
            if (name != null)
            {
                Element.RequireText(name, nameof(name));
            }

            if (className != null)
            {
                Element.RequireClassName(className);
            }
        }

        internal Element WrapElement(ulong token)
        {
            if (token == ulong.MaxValue)
            {
                throw new InvalidOperationException("URUI 元素身份获取失败或已超出引用预算。");
            }

            if (token == 0)
            {
                return null;
            }

            if (!IsLoaded || NativeMethods.urui_element_ref_valid(token) != 1)
            {
                return null;
            }

            if (elements.TryGetValue(token, out var value))
            {
                return value;
            }

            value = new Element(this, generation, token);
            elements.Add(token, value);
            return value;
        }

        internal void SweepInvalidElements()
        {
            // 结构替换会同步销毁子树。清空失效节点回调，避免闭包等到下一次查询才释放。
            var removed = new List<ulong>();
            foreach (var pair in elements)
            {
                if (!pair.Value.IsValid)
                {
                    removed.Add(pair.Key);
                }
            }

            foreach (var token in removed)
            {
                elements.Remove(token);
            }
        }

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
