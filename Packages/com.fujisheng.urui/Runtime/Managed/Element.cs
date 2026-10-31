using System;
using System.Collections.Generic;
using URUI.Internal;
using UnityEngine;

namespace URUI
{
    /// <summary>
    /// 由 Document.Q 返回的元素。重载、删除或替换后失效；请重新查询。
    /// RML id 必须保持唯一；更新属性时按查询 ID 寻址，不支持就地修改 id。
    /// </summary>
    public sealed class Element : IDisposable
    {
        readonly Document document;
        readonly uint generation;
        readonly ElementReference reference;
        readonly List<ClickSubscription> subscriptions = new List<ClickSubscription>();
        bool disposed;

        /// <summary>
        /// 查询时使用的 RML id。
        /// </summary>
        public string Id
        {
            get;
        }

        /// <summary>
        /// 样式写入入口，使用 RmlUi RCSS 语义。
        /// </summary>
        public ElementStyle Style
        {
            get;
        }

        /// <summary>
        /// 文档和原对象仍有效。Dispose 后返回 false。
        /// </summary>
        /// <remarks>在 Unity 主线程检查；同 ID 的新节点不会让旧包装重新有效。</remarks>
        public bool IsValid
        {
            get
            {
                document.CheckThread();
                if (disposed)
                {
                    return false;
                }

                if (document.IsCurrent(generation) && reference.IsValid)
                {
                    return true;
                }

                Invalidate();
                document.Forget(this);
                return false;
            }
        }

        internal Element(Document document, string id, uint generation, ElementReference reference)
        {
            this.document = document;
            this.generation = generation;
            this.reference = reference;
            Id = id;
            Style = new ElementStyle(this);
        }

        /// <summary>
        /// 写入普通文本，不将值解释为 RML。原子节点的子元素可能被替换。
        /// </summary>
        /// <param name="value">非 null 且不包含 NUL 的普通文本。</param>
        public void SetText(string value)
        {
            RequireValid();
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("文本不能包含 NUL。", nameof(value));
            }

            document.Host.SetText(Id, value);
        }

        /// <summary>
        /// 设置属性。为保护引用身份，不允许修改 id；需要改名时请替换节点并重新查询。
        /// </summary>
        /// <param name="name">非空属性名，不能为 id。</param>
        /// <param name="value">非 null 且不包含 NUL 的属性值。</param>
        public void SetAttribute(string name, string value)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("请勿修改元素 id。", nameof(name));
            }

            if (value == null || value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("属性值不能为 null 或包含 NUL。", nameof(value));
            }

            RequireSuccess(document.Host.SetAttribute(Id, name, value), "设置属性");
        }

        /// <summary>
        /// 替换内部 RML，旧子元素与其订阅不会跟随相同 id 的新对象。
        /// </summary>
        /// <param name="markup">非 null 且不包含 NUL 的内部 RML；空字符串清空子节点。</param>
        /// <remarks>调用者负责保持文档内的 ID 唯一；结构替换后重新查询子元素。</remarks>
        public void SetInnerMarkup(string markup)
        {
            RequireValid();
            if (markup == null || markup.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("RML 不能为 null 或包含 NUL。", nameof(markup));
            }

            RequireSuccess(document.Host.SetInnerRml(Id, markup), "设置内部 RML");
        }

        /// <summary>
        /// 追加具有唯一 id 的元素并返回引用；此接口不负责创建 UITK 控件。
        /// </summary>
        /// <param name="tag">RmlUi 支持的非空标签名。</param>
        /// <param name="id">在当前文档中尚未使用的非空 ID。</param>
        /// <returns>新节点的引用包装；引用获取失败时为 null。</returns>
        public Element Append(string tag, string id)
        {
            RequireValid();
            Document.RequireName(tag, nameof(tag));
            Document.RequireName(id, nameof(id));
            if (document.Host.ElementExists(id))
            {
                throw new ArgumentException("元素 id 必须唯一。", nameof(id));
            }

            RequireSuccess(document.Host.AppendElement(Id, tag, id), "追加元素");
            return document.Q(id);
        }

        /// <summary>
        /// 从文档移除此元素，释放本引用和点击订阅。
        /// </summary>
        /// <remarks>子节点随结构删除而失效；再次使用时需要重新创建和查询。</remarks>
        public void Remove()
        {
            RequireValid();
            RequireSuccess(document.Host.RemoveElement(Id), "移除元素");
            Dispose();
        }

        /// <summary>
        /// 设置二维位移与缩放。弱引用直接寻址，不暴露原生句柄。
        /// </summary>
        /// <param name="translation">使用 RCSS 坐标的二维位移，各分量必须为有限数值。</param>
        /// <param name="scale">二维缩放，各分量必须为有限数值。</param>
        public void SetTransform(Vector2 translation, Vector2 scale)
        {
            RequireValid();
            ElementStyle.RequireFinite(translation.x);
            ElementStyle.RequireFinite(translation.y);
            ElementStyle.RequireFinite(scale.x);
            ElementStyle.RequireFinite(scale.y);
            RequireSuccess(reference.SetTranslateScale2D(translation.x, translation.y, scale.x, scale.y), "设置变换");
        }

        /// <summary>
        /// 注册点击回调。相同委托重复注册无副作用；返回的订阅可 Dispose。
        /// </summary>
        /// <param name="callback">非 null 的点击处理器。</param>
        /// <returns>由调用者持有并释放的订阅；相等的委托返回同一订阅。</returns>
        /// <remarks>节点或文档失效时订阅自动失效；订阅不提供冒泡或捕获协议。</remarks>
        public IDisposable RegisterClickCallback(Action<ClickEvent> callback)
        {
            RequireValid();
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            foreach (var subscription in subscriptions)
            {
                if (subscription.Callback == callback)
                {
                    return subscription;
                }
            }

            var added = new ClickSubscription(this, callback);
            subscriptions.Add(added);
            return added;
        }

        /// <summary>
        /// 解绑指定点击委托。重复解绑安全。
        /// </summary>
        /// <param name="callback">注册时使用的委托；未注册或为 null 时不做处理。</param>
        public void UnregisterClickCallback(Action<ClickEvent> callback)
        {
            document.CheckThread();
            // Dispose 会修改订阅列表，倒序遍历避免跳过后续项。
            for (var i = subscriptions.Count - 1; i >= 0; i--)
            {
                if (subscriptions[i].Callback == callback)
                {
                    subscriptions[i].Dispose();
                }
            }
        }

        /// <summary>
        /// 释放弱引用和订阅，不移除 RML 节点；重复调用安全。
        /// </summary>
        /// <remarks>必须在 Unity 主线程调用。以后可通过 Document.Q 重新取得包装。</remarks>
        public void Dispose()
        {
            document.CheckThread();
            Invalidate();
            document.Forget(this);
        }

        internal void Invalidate()
        {
            if (disposed)
            {
                return;
            }
            // 统一清空订阅但不逐项修改列表，文档失效与主动释放共用同一路径。
            disposed = true;
            foreach (var subscription in subscriptions)
            {
                subscription.Clear();
            }

            subscriptions.Clear();
            reference.Dispose();
        }

        internal void DispatchClick()
        {
            // 使用快照保证本次分发顺序稳定；回调期间新增的订阅留到下一次点击。
            var snapshot = subscriptions.ToArray();
            var notification = new ClickEvent(this);
            foreach (var subscription in snapshot)
            {
                if (!IsValid)
                {
                    // 回调可能卸载或重载文档，不能继续向旧元素发送通知。
                    break;
                }

                if (subscription.Callback == null)
                {
                    // 快照中的订阅可能已经被前一个回调释放。
                    continue;
                }

                try
                {
                    subscription.Callback(notification);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        internal void RequireValid()
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("URUI 元素已释放或失效，请使用 Document.Q 重新查询。");
            }
        }

        internal void SetStyle(string name, string value)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            if (value == null || value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("样式值不能为 null 或包含 NUL。", nameof(value));
            }

            RequireSuccess(document.Host.SetStyle(Id, name, value), "设置样式");
        }

        internal void SetPixels(string name, float pixels)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            ElementStyle.RequireFinite(pixels);
            RequireSuccess(document.Host.SetPixelStyle(Id, name, pixels), "设置像素样式");
        }

        static void RequireSuccess(bool success, string operation)
        {
            if (!success)
            {
                throw new InvalidOperationException("URUI 无法" + operation + "，请检查 RmlUi 支持的名称和值。");
            }
        }

        sealed class ClickSubscription : IDisposable
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
