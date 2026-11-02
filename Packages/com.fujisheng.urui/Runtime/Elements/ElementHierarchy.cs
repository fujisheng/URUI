using System;
using System.Collections.Generic;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    /// <summary>真实 DOM 层级访问器；组合控件内部节点也包含在内。</summary>
    public readonly struct ElementHierarchy
    {
        readonly Element owner;
        internal ElementHierarchy(Element owner) { this.owner = owner; }
        /// <summary>直接 DOM 子节点数量。</summary>
        public int ChildCount
        {
            get
            {
                int count = NativeMethods.urui_element_child_count(owner.CheckedToken());
                return count >= 0 ? count : throw new InvalidOperationException("无法读取层级。");
            }
        }
        /// <summary>直接 DOM 子节点的有序快照。</summary>
        public IReadOnlyList<Element> Children()
        {
            int count = ChildCount;
            var tokens = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                tokens[i] = NativeMethods.urui_element_child_at(owner.Token, i);
            }

            var result = new Element[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = owner.Document.WrapElement(tokens[i]);
            }

            return result;
        }
        /// <summary>挂载到真实 DOM 子节点末尾。</summary>
        public void Add(Element child) => owner.MoveChild(child, -1);
        /// <summary>插入真实 DOM 子节点索引。</summary>
        public void Insert(int index, Element child)
        {
            if (index < 0 || index > ChildCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            owner.MoveChild(child, index);
        }
        /// <summary>脱离全部真实 DOM 子节点；节点仍由文档拥有。</summary>
        public void Clear()
        {
            if (NativeMethods.urui_element_clear(owner.CheckedToken()) != 1)
            {
                throw new InvalidOperationException("无法清空层级。");
            }

            owner.Document.RefreshElements(false);
        }
    }
}
