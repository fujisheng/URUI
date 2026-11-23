using System;
using System.Collections.Generic;

namespace URUI.Elements
{
    /// <summary>
    /// 可复用的有类型查询；每次执行遍历当前节点，结果是有序快照。
    /// </summary>
    public sealed class ElementQuery<T> where T : Element
    {
        readonly Element root;
        readonly string name;
        readonly string className;
        readonly List<Func<T, bool>> predicates = new List<Func<T, bool>>();
        bool directChildren;

        internal ElementQuery(Element root, string name, string className)
        {
            this.root = root;
            this.name = name;
            this.className = className;
        }

        /// <summary>
        /// 追加谓词，执行时按加入顺序筛选。
        /// </summary>
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

        /// <summary>
        /// 将范围限制为直接内容子节点。
        /// </summary>
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

        bool Matches(Element element, out T typed)
        {
            if (element is not T matched || (directChildren && !ReferenceEquals(element.Parent, root)))
            {
                typed = null;
                return false;
            }
            for (var i = 0; i < predicates.Count; i++)
            {
                if (!predicates[i](matched))
                {
                    typed = null;
                    return false;
                }
            }
            typed = matched;
            return true;
        }

        /// <summary>
        /// 创建当前匹配节点的列表，不修改节点身份。
        /// </summary>
        public List<T> ToList()
        {
            var result = new List<T>();
            var candidates = root.Query(name, className);
            for (var i = 0; i < candidates.Count; i++)
            {
                if (Matches(candidates[i], out var typed))
                {
                    result.Add(typed);
                }
            }
            return result;
        }

        /// <summary>
        /// 返回首项，没有匹配时为 null（短路求值，不分配结果列表）。
        /// </summary>
        public T First() => AtIndex(0);

        /// <summary>
        /// 返回末项，没有匹配时为 null。
        /// </summary>
        public T Last()
        {
            T last = null;
            var candidates = root.Query(name, className);
            for (var i = 0; i < candidates.Count; i++)
            {
                if (Matches(candidates[i], out var typed))
                {
                    last = typed;
                }
            }
            return last;
        }

        /// <summary>
        /// 返回指定匹配索引，不存在时为 null（短路求值，不分配结果列表）。
        /// </summary>
        public T AtIndex(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (index == 0 && !directChildren && predicates.Count == 0 && typeof(T) == typeof(Element))
            {
                return (T)root.Q(name, className);
            }

            var seen = 0;
            var candidates = root.Query(name, className);
            for (var i = 0; i < candidates.Count; i++)
            {
                if (Matches(candidates[i], out var typed))
                {
                    if (seen == index)
                    {
                        return typed;
                    }
                    seen++;
                }
            }
            return null;
        }

        /// <summary>
        /// 对有序快照逐项执行操作。
        /// </summary>
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
