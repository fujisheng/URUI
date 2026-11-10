using System;
using System.Collections.Generic;
using URUI.Controls;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        Dictionary<ulong, Element> elements => content.Elements;
        readonly Dictionary<Type, ElementFactory> factories = new Dictionary<Type, ElementFactory>();
        readonly Dictionary<string, ElementFactory> tags = new Dictionary<string, ElementFactory>(StringComparer.Ordinal);
        bool factoriesInitialized;
        sealed class FragmentChange { internal bool Committed; }
        FragmentChange fragmentChange;
        bool changingFragment => fragmentChange != null;
        internal uint LayoutVersion { get => content?.LayoutVersion ?? 0; private set => content.LayoutVersion = value; }

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
            RequireEffectDomAccess();
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
        public Element DocumentElement
        {
            get
            {
                CheckThread();
                if (!IsLoaded)
                {
                    return null;
                }

                return WrapElement(NativeMethods.urui_document_root(NativeHandle)) ?? throw new InvalidOperationException("URUI 文档根身份不可用。");
            }
        }

        /// <summary>当前文档的 body；未加载时为 null。</summary>
        public Element Body { get { CheckThread(); return IsLoaded ? WrapElement(NativeMethods.urui_document_body(NativeHandle)) : null; } }

        /// <summary>创建当前文档拥有的有类型节点；脱离层级时仍有效，卸载时失效。</summary>
        public T Create<T>() where T : Element
        {
            RequireEffectDomAccess();
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

            ulong token = NativeMethods.urui_element_create(NativeHandle, factory.Tag);
            if (token == 0)
            {
                throw new InvalidOperationException("URUI 节点创建失败。");
            }

            try
            {
                if (factory.InputType != null && NativeMethods.urui_element_write(NativeHandle, token, 2, "type", factory.InputType) != 1)
                {
                    throw new InvalidOperationException("无法设置输入控件类型。");
                }

                if (NativeMethods.urui_element_write(NativeHandle, token, 2, "data-urui-type", factory.Type.FullName) != 1)
                {
                    throw new InvalidOperationException("无法设置控件身份。");
                }

                return (T)WrapElement(token);
            }
            catch
            {
                NativeMethods.urui_element_destroy(NativeHandle, token);
                SweepInvalidElements();
                throw;
            }
        }

        /// <summary>按 HTML 标签创建脱离层级的节点；文档负责其整个生命周期。</summary>
        public Element CreateElement(string tag = "div")
        {
            RequireEffectDomAccess();
            CheckThread();
            RequireName(tag, nameof(tag));
            if (!IsLoaded)
            {
                throw new InvalidOperationException("请先加载文档。");
            }

            return WrapElement(NativeMethods.urui_element_create(NativeHandle, tag)) ?? throw new InvalidOperationException("URUI 无法创建节点。");
        }

        /// <summary>在根及后代查询首个有类型节点。</summary>
        public T Q<T>(string name = null, string className = null) where T : Element => DocumentElement?.Q<T>(name, className);
        /// <summary>构造有类型查询；文档需已加载。</summary>
        public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element
            => (DocumentElement ?? throw new InvalidOperationException("请先加载文档。")).Query<T>(name, className);
        /// <summary>在根及后代查询首项；未加载时为 null。</summary>
        public Element Q(string name = null, string className = null)
        {
            CheckQuery(name, className);
            return DocumentElement?.Q(name, className);
        }
        /// <summary>当前根及后代的深度优先快照；保留原有非泛型查询契约。</summary>
        public IReadOnlyList<Element> Query(string name = null, string className = null)
        {
            CheckQuery(name, className);
            return DocumentElement?.Query(name, className) ?? Array.Empty<Element>();
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

            var handle = NativeMethods.urui_surface_element_owner(token);
            var state = activeContent?.Handle == handle ? activeContent : candidateContent?.Handle == handle ? candidateContent : null;
            if (state == null || state.Retired || NativeMethods.urui_element_ref_valid(handle, token) != 1) return null;
            var elements = state.Elements;
            if (elements.TryGetValue(token, out var existing))
            {
                return existing;
            }

            EnsureFactories();
            string marker = NativeMethods.ReadElement(handle, token, 3, "data-urui-type");
            ElementFactory factory = null;
            if (marker != null)
            {
                foreach (var entry in factories.Values)
                {
                    if (entry.Type.FullName == marker) { factory = entry; break; }
                }
                if (factory == null)
                {
                    throw new InvalidOperationException("HTML 引用了未注册控件类型：" + marker);
                }
            }
            if (factory == null)
            {
                string tag = NativeMethods.ReadElement(handle, token, 1);
                string input = tag == "input" ? NativeMethods.ReadElement(handle, token, 3, "type") ?? "text" : null;
                if (input != null) input = input.ToLowerInvariant();
                tags.TryGetValue(input == null ? tag : tag + ":" + input, out factory);
            }
            var context = new ElementContext(this, state.Generation, token, handle);
            var value = factory == null ? new Element(context) : factory.Construct(context);
            if (value == null || value.Token != token || !ReferenceEquals(value.Document, this))
            {
                throw new InvalidOperationException("控件工厂必须返回当前上下文对应的元素。");
            }

            elements.Add(token, value);
            state.ElementSnapshot = null;
            try { value.Initialize(); }
            catch { elements.Remove(token); state.ElementSnapshot = null; value.Invalidate(); throw; }
            return value;
        }

        internal void InitializeElements()
        {
            LayoutVersion = 0;
            InitializeSubtree(DocumentElement);
        }

        internal void InitializeSubtree(Element root)
        {
            var pending = new Stack<Element>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var child in current.Hierarchy.Children())
                {
                    pending.Push(child);
                }
            }
        }

        internal void ReplaceInnerHtml(Element target, string html, string sourceUrl)
        {
            if (changingFragment) { EnqueueDeferred(() => target.SetInnerHtml(html, sourceUrl), true); return; }
            var previousChangingContent = changingContent;
            var change = fragmentChange = new FragmentChange();
            changingContent = true;
            surface.BeginTransaction();
            var began = false;
            try
            {
                Element.RequireSuccess(NativeMethods.urui_element_begin_inner_html(NativeHandle, target.Token, html, sourceUrl), "准备 HTML 内容");
                began = true;
                InitializeSubtree(target);
                Element.RequireSuccess(NativeMethods.urui_element_end_inner_html(NativeHandle, target.Token, 1), "提交 HTML 内容");
                began = false;
                change.Committed = true;
            }
            catch
            {
                if (began) Element.RequireSuccess(NativeMethods.urui_element_end_inner_html(NativeHandle, target.Token, 0), "回滚 HTML 内容");
                throw;
            }
            finally
            {
                SweepInvalidElements();
                fragmentChange = null; changingContent = previousChangingContent;
                surface.EndTransaction();
                FinishNativeCall();
            }
        }

        internal void RefreshElements(bool layout)
        {
            using var profile = URUI.Internal.Hosting.RenderHost.ElementRefreshPhase.Auto();
            if (!IsLoaded)
            {
                return;
            }

            if (layout)
            {
                LayoutVersion++;
            }

            var selected = content;
            foreach (var value in selected.GetElements())
            {
                if (!IsLoaded || !ReferenceEquals(content, selected))
                {
                    break;
                }

                value.Refresh(layout);
            }
            SweepInvalidElements();
        }

        internal void NotifyLayoutUpdated()
        {
            if (IsLoaded && layoutUpdated != null)
            {
                var callbacks = layoutCallbacks ??= layoutUpdated.GetInvocationList();
                BeginControlUpdate();
                try
                {
                    foreach (Action<Document> callback in callbacks)
                    {
                        if (!IsLoaded || HasPendingInvalidation) { break; }
                        callback(this);
                    }
                }
                finally { EndControlUpdate(); }
            }
        }

        internal void SweepInvalidElements()
        {
            var selected = content;
            if (selected == null) { return; }
            foreach (var value in selected.GetElements())
            {
                if (!value.IsValid)
                {
                    selected.Elements.Remove(value.Token);
                    selected.EffectLayouts.Remove(value.Token);
                    selected.ElementSnapshot = null;
                }
            }
        }
    }
}
