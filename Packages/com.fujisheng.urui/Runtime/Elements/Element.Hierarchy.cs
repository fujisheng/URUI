using System;
using System.Collections.Generic;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    public partial class Element
    {
        /// <summary>真实 DOM 父级；文档根和脱离层级的根节点返回 null。</summary>
        public Element Parent
        {
            get
            {
                RequireValid();
                return document.WrapElement(NativeMethods.urui_element_parent(token));
            }
        }

        /// <summary>直接 DOM 子节点数量，不包括 RmlUi 的内部非 DOM 装饰节点。</summary>
        public int ChildCount
        {
            get
            {
                RequireValid();
                if (!ReferenceEquals(ContentContainer, this)) return ContentContainer.ChildCount;
                int count = NativeMethods.urui_element_child_count(token);
                if (count < 0)
                {
                    throw new InvalidOperationException("URUI 无法读取子节点。");
                }

                return count;
            }
        }

        /// <summary>按 DOM 顺序读取直接子节点，索引必须在 [0, ChildCount) 内。</summary>
        public Element this[int index]
        {
            get
            {
                if (!ReferenceEquals(ContentContainer, this)) return ContentContainer[index];
                if (index < 0 || index >= ChildCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                var child = document.WrapElement(NativeMethods.urui_element_child_at(token, index));
                return child ?? throw new InvalidOperationException("URUI 子节点身份不可用。");
            }
        }

        /// <summary>直接子节点的有序快照；后续结构变化不会修改此列表。</summary>
        public IReadOnlyList<Element> Children()
        {
            var result = new Element[ChildCount];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = this[i];
            }

            return result;
        }

        /// <summary>在自身及 DOM 后代中按名字和单个类名查找首项；两个条件同时匹配，null 表示不过滤。</summary>
        public Element Q(string name = null, string className = null)
        {
            RequireValid();
            if (name != null)
            {
                RequireText(name, nameof(name));
            }

            if (className != null)
            {
                RequireClassName(className);
            }

            return document.WrapElement(NativeMethods.urui_element_find(token, name, className));
        }

        /// <summary>自身及 DOM 后代的深度优先有序快照；精确匹配名字/类名，不解析 CSS 或 UQuery 表达式。</summary>
        public IReadOnlyList<Element> Query(string name = null, string className = null)
        {
            RequireValid();
            if (name != null)
            {
                RequireText(name, nameof(name));
            }

            if (className != null)
            {
                RequireClassName(className);
            }

            int count = NativeMethods.urui_element_query(token, name, className, null, 0);
            if (count < 0)
            {
                throw new InvalidOperationException("URUI 查询失败或超出元素引用预算。");
            }

            if (count == 0)
            {
                return Array.Empty<Element>();
            }

            var values = new ulong[count];
            if (NativeMethods.urui_element_query(token, name, className, values, count) != count)
            {
                throw new InvalidOperationException("URUI 查询快照无效。");
            }

            var result = new Element[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = document.WrapElement(values[i]) ?? throw new InvalidOperationException("URUI 查询节点已失效。");
            }

            return result;
        }

        /// <summary>将同一文档的节点移动到末尾；可挂载新建或脱离层级的完整子树。</summary>
        public void Add(Element child) => ContentContainer.MoveChild(child, -1);

        /// <summary>移动到最终子节点索引；ChildCount 表示追加，普通校验失败不会移动节点。</summary>
        public void Insert(int index, Element child)
        {
            if (index < 0 || index > ChildCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            ContentContainer.MoveChild(child, index);
        }

        internal void MoveChild(Element child, int index)
        {
            RequireValid();
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            if (!ReferenceEquals(document, child.document))
            {
                throw new ArgumentException("节点必须属于同一文档。", nameof(child));
            }

            child.RequireValid();
            if (ReferenceEquals(child, document.Root))
            {
                throw new InvalidOperationException("不能移动文档根节点。");
            }

            for (var ancestor = this; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, child))
                {
                    throw new ArgumentException("不能形成循环层级。", nameof(child));
                }
            }

            RequireSuccess(NativeMethods.urui_element_insert(token, child.token, index), "移动子节点");
            document.RefreshElements(false);
        }

        /// <summary>移出指定的直接子节点，节点和其回调仍可复用。</summary>
        public void Remove(Element child)
        {
            RequireValid();
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            if (!ReferenceEquals(child.Parent, ContentContainer))
            {
                throw new ArgumentException("节点不是当前元素的直接子节点。", nameof(child));
            }

            child.RemoveFromHierarchy();
        }

        /// <summary>脱离父级但保持有效及文档所有权，之后可重新 Add；文档根不能脱离。</summary>
        public void RemoveFromHierarchy()
        {
            RequireValid();
            RequireSuccess(NativeMethods.urui_element_detach(token), "移出层级");
            document.RefreshElements(false);
        }

        /// <summary>将所有直接子节点移出层级；它们仍由文档拥有，需要删除时显式 Destroy。</summary>
        public void Clear()
        {
            RequireValid();
            RequireSuccess(NativeMethods.urui_element_clear(ContentContainer.token), "清空层级");
            document.RefreshElements(false);
        }

        /// <summary>销毁此节点及其子树，使相关包装与订阅失效；不能销毁文档根。</summary>
        public void Destroy()
        {
            RequireValid();
            RequireSuccess(NativeMethods.urui_element_destroy(token), "销毁节点");
            document.SweepInvalidElements();
        }
    }
}
