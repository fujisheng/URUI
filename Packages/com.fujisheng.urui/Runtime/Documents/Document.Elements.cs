using System;
using System.Collections.Generic;
using URUI.Controls;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        readonly Dictionary<ulong, Element> elements = new Dictionary<ulong, Element>();
        readonly Dictionary<Type, ElementFactory> factories = new Dictionary<Type, ElementFactory>();
        readonly Dictionary<string, ElementFactory> tags = new Dictionary<string, ElementFactory>(StringComparer.Ordinal);
        bool factoriesInitialized;
        internal uint LayoutVersion { get; private set; }

        internal sealed class ElementFactory
        {
            internal Type Type;
            internal string Tag, InputType;
            internal Func<ElementContext, Element> Construct;
        }

        void EnsureFactories()
        {
            if (factoriesInitialized)
            {
                return;
            }

            factoriesInitialized = true;
            RegisterFactory("div", context => new Element(context));
            ControlFactories.Install(this);
        }

        internal void RegisterFactory<T>(string tag, Func<ElementContext, T> factory, string inputType = null) where T : Element
        {
            var entry = new ElementFactory { Type = typeof(T), Tag = tag, InputType = inputType, Construct = context => factory(context) };
            factories[typeof(T)] = entry;
            string key = inputType == null ? tag : tag + ":" + inputType;
            if (!tags.ContainsKey(key))
            {
                tags.Add(key, entry);
            }
        }

        internal void RegisterTag(string tag, Type type) => tags[tag] = factories[type];

        /// <summary>注册自定义标签和类型工厂；现有标签与已经确定的节点类型不被替换。</summary>
        public void RegisterElementType<T>(string tag, Func<ElementContext, T> factory) where T : Element
        {
            CheckThread();
            RequireName(tag, nameof(tag));
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            EnsureFactories();
            if (tags.ContainsKey(tag) || factories.ContainsKey(typeof(T)))
            {
                throw new InvalidOperationException("标签或控件类型已注册。");
            }

            RegisterFactory(tag, factory);
        }

        /// <summary>当前文档根；未加载时为 null，所有访问限主线程。</summary>
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

        /// <summary>创建当前文档拥有的有类型节点；脱离层级时仍有效，卸载时失效。</summary>
        public T Create<T>() where T : Element
        {
            CheckThread();
            if (!IsLoaded)
            {
                throw new InvalidOperationException("请先加载文档。");
            }

            EnsureFactories();
            if (!factories.TryGetValue(typeof(T), out var factory))
            {
                throw new NotSupportedException("请先注册控件类型：" + typeof(T).FullName);
            }

            ulong token = NativeMethods.urui_element_create(factory.Tag);
            if (token == 0)
            {
                throw new InvalidOperationException("URUI 节点创建失败。");
            }

            try
            {
                if (factory.InputType != null && NativeMethods.urui_element_write(token, 2, "type", factory.InputType) != 1)
                {
                    throw new InvalidOperationException("无法设置输入控件类型。");
                }

                if (NativeMethods.urui_element_write(token, 2, "data-urui-type", factory.Type.FullName) != 1)
                {
                    throw new InvalidOperationException("无法设置控件身份。");
                }

                return (T)WrapElement(token);
            }
            catch
            {
                NativeMethods.urui_element_destroy(token);
                SweepInvalidElements();
                throw;
            }
        }

        /// <summary>按 RML 标签创建脱离层级的节点；文档负责其整个生命周期。</summary>
        public Element CreateElement(string tag = "div")
        {
            CheckThread();
            RequireName(tag, nameof(tag));
            if (!IsLoaded)
            {
                throw new InvalidOperationException("请先加载文档。");
            }

            return WrapElement(NativeMethods.urui_element_create(tag)) ?? throw new InvalidOperationException("URUI 无法创建节点。");
        }

        /// <summary>在根及后代查询首个有类型节点。</summary>
        public T Q<T>(string name = null, string className = null) where T : Element => Root?.Q<T>(name, className);
        /// <summary>构造有类型查询；文档需已加载。</summary>
        public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element
            => (Root ?? throw new InvalidOperationException("请先加载文档。")).Query<T>(name, className);
        /// <summary>在根及后代查询首项；未加载时为 null。</summary>
        public Element Q(string name = null, string className = null)
        {
            CheckQuery(name, className);
            return Root?.Q(name, className);
        }
        /// <summary>当前根及后代的深度优先快照；保留原有非泛型查询契约。</summary>
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
                throw new InvalidOperationException("URUI 元素身份获取失败或超出预算。");
            }

            if (token == 0)
            {
                return null;
            }

            if (!IsLoaded || NativeMethods.urui_element_ref_valid(token) != 1)
            {
                return null;
            }

            if (elements.TryGetValue(token, out var existing))
            {
                return existing;
            }

            EnsureFactories();
            string marker = NativeMethods.ReadElement(token, 3, "data-urui-type");
            ElementFactory factory = null;
            if (marker != null)
            {
                foreach (var entry in factories.Values)
                {
                    if (entry.Type.FullName == marker) { factory = entry; break; }
                }
                if (factory == null)
                {
                    throw new InvalidOperationException("RML 引用了未注册控件类型：" + marker);
                }
            }
            if (factory == null)
            {
                string tag = NativeMethods.ReadElement(token, 1);
                string input = tag == "input" ? NativeMethods.ReadElement(token, 3, "type") ?? "text" : null;
                tags.TryGetValue(input == null ? tag : tag + ":" + input, out factory);
            }
            var context = new ElementContext(this, generation, token);
            var value = factory == null ? new Element(context) : factory.Construct(context);
            if (value == null || value.Token != token || !ReferenceEquals(value.Document, this))
            {
                throw new InvalidOperationException("控件工厂必须返回当前上下文对应的元素。");
            }

            elements.Add(token, value);
            try { value.Initialize(); }
            catch { elements.Remove(token); value.Invalidate(); throw; }
            return value;
        }

        internal void InitializeElements()
        {
            LayoutVersion = 0;
            var pending = new Stack<Element>();
            pending.Push(Root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var child in current.Hierarchy.Children())
                {
                    pending.Push(child);
                }
            }
        }

        internal void RefreshElements(bool layout)
        {
            if (!IsLoaded)
            {
                return;
            }

            if (layout)
            {
                LayoutVersion++;
            }

            foreach (var value in new List<Element>(elements.Values))
            {
                if (!IsLoaded)
                {
                    break;
                }

                if (value.IsValid)
                {
                    value.Refresh(layout);
                }
            }
            SweepInvalidElements();
        }

        internal void SweepInvalidElements()
        {
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
    }
}
