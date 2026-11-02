using System;
using System.Collections.Generic;

namespace URUI.Elements
{
    /// <summary>可复用的有类型查询；每次执行遍历当前节点，结果是有序快照。</summary>
    public sealed class ElementQuery<T> where T : Element
    {
        readonly Element root;
        readonly string name, className;
        readonly List<Func<T, bool>> predicates = new List<Func<T, bool>>();
        bool directChildren;
        internal ElementQuery(Element root, string name, string className)
        {
            this.root = root;
            this.name = name;
            this.className = className;
        }
        /// <summary>追加谓词，执行时按加入顺序筛选。</summary>
        public ElementQuery<T> Where(Func<T, bool> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            var next = Copy();
            next.predicates.Add(predicate);
            return next;
        }
        /// <summary>将范围限制为直接内容子节点。</summary>
        public ElementQuery<T> Children()
        {
            var next = Copy();
            next.directChildren = true;
            return next;
        }
        ElementQuery<T> Copy()
        {
            var next = new ElementQuery<T>(root, name, className) { directChildren = directChildren };
            next.predicates.AddRange(predicates);
            return next;
        }
        /// <summary>创建当前匹配节点的列表，不修改节点身份。</summary>
        public List<T> ToList()
        {
            var result = new List<T>();
            foreach (var element in root.Query(name, className))
            {
                if (!(element is T typed) || (directChildren && !ReferenceEquals(element.Parent, root)))
                {
                    continue;
                }

                bool matches = true;
                foreach (var predicate in predicates)
                {
                    if (!predicate(typed)) { matches = false; break; }
                }
                if (matches)
                {
                    result.Add(typed);
                }
            }
            return result;
        }
        /// <summary>返回首项，没有匹配时为 null。</summary>
        public T First() => AtIndex(0);
        /// <summary>返回末项，没有匹配时为 null。</summary>
        public T Last()
        {
            var values = ToList();
            return values.Count == 0 ? null : values[values.Count - 1];
        }
        /// <summary>返回指定匹配索引，不存在时为 null。</summary>
        public T AtIndex(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var values = ToList();
            return index < values.Count ? values[index] : null;
        }
        /// <summary>对有序快照逐项执行操作。</summary>
        public void ForEach(Action<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            foreach (var value in ToList())
            {
                if (value.IsValid)
                {
                    action(value);
                }
            }
        }
    }
}
