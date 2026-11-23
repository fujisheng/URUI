using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Controls;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        Dictionary<ulong, Element> elements => content.Elements;
        readonly Dictionary<Type, ElementFactory> factories = new Dictionary<Type, ElementFactory>();
        readonly Dictionary<string, ElementFactory> factoriesByFullName = new Dictionary<string, ElementFactory>(StringComparer.Ordinal);
        readonly Dictionary<string, ElementFactory> tags = new Dictionary<string, ElementFactory>(StringComparer.Ordinal);
        readonly HashSet<string> eagerTags = new HashSet<string>(StringComparer.Ordinal);
        readonly Stack<ulong> initTokenStack = new Stack<ulong>();
        bool factoriesInitialized;
        const int RefreshWindowSize = 64;
        const int DrainWindowSize = 128;
        const int UpdatingHookSet = -2;
        static readonly Unity.Profiling.ProfilerMarker readStatesMarker = new("URUI.Elements.ReadStates");
        static readonly Unity.Profiling.ProfilerMarker updateHooksMarker = new("URUI.Elements.UpdateHooks");
        ulong[] refreshTokens;
        NativeMethods.ElementRefreshState[] refreshStates;

        sealed class FragmentChange
        {
            internal bool Committed;
        }
        FragmentChange fragmentChange;
        bool changingFragment => fragmentChange != null;

        internal uint LayoutVersion
        {
            get => content?.LayoutVersion ?? 0;
            private set => content.LayoutVersion = value;
        }

        internal sealed class ElementFactory
        {
            internal Type Type;
            internal string Tag;
            internal string InputType;
            internal Func<ElementContext, Element> Construct;
        }

        sealed class ElementTypeRegistration : IDisposable
        {
            Document owner;
            readonly string key;
            readonly Type type;
            readonly ElementFactory entry;

            internal ElementTypeRegistration(Document owner, string key, Type type, ElementFactory entry)
            {
                this.owner = owner;
                this.key = key;
                this.type = type;
                this.entry = entry;
            }

            public void Dispose()
            {
                var doc = owner;
                if (ReferenceEquals(doc, null))
                {
                    return;
                }
                owner = null;
                if (doc.tags.TryGetValue(key, out var existingTag) && ReferenceEquals(existingTag, entry))
                {
                    doc.tags.Remove(key);
                    if (entry.InputType == null)
                    {
                        doc.eagerTags.Remove(entry.Tag);
                    }
                }
                if (doc.factories.TryGetValue(type, out var existingType) && ReferenceEquals(existingType, entry))
                {
                    doc.factories.Remove(type);
                }
                if (type.FullName != null && doc.factoriesByFullName.TryGetValue(type.FullName, out var existingName) && ReferenceEquals(existingName, entry))
                {
                    doc.factoriesByFullName.Remove(type.FullName);
                }
            }
        }

        void EnsureFactories()
        {
            if (factoriesInitialized)
            {
                return;
            }

            factoriesInitialized = true;
            RegisterFactory("div", context => new Element(context), eagerInitialize: false);
            ControlFactories.Install(this);
        }

        internal ElementFactory RegisterFactory<T>(string tag, Func<ElementContext, T> factory, string inputType = null, bool eagerInitialize = true) where T : Element
        {
            var entry = new ElementFactory
            {
                Type = typeof(T),
                Tag = tag,
                InputType = inputType,
                Construct = context => factory(context)
            };
            factories[typeof(T)] = entry;
            if (typeof(T).FullName != null)
            {
                factoriesByFullName[typeof(T).FullName] = entry;
            }
            string key = inputType == null ? tag : tag + ":" + inputType;
            if (!tags.ContainsKey(key))
            {
                tags.Add(key, entry);
            }
            if (eagerInitialize)
            {
                eagerTags.Add(tag);
            }
            return entry;
        }

        internal void RegisterTag(string tag, Type type) => tags[tag] = factories[type];

        /// <summary>
        /// 注册自定义标签和类型工厂；现有标签与已经确定的节点类型不被替换。返回的句柄可用于注销。
        /// </summary>
        public IDisposable RegisterElementType<T>(string tag, Func<ElementContext, T> factory) where T : Element
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

            var entry = RegisterFactory(tag, factory);
            return new ElementTypeRegistration(this, tag, typeof(T), entry);
        }

        /// <summary>
        /// 当前文档根；未加载时为 null，所有访问限主线程。
        /// </summary>
        public Element DocumentElement
        {
            get
            {
                CheckThread();
                if (!IsLoaded)
                {
                    return null;
                }

                return WrapElement(NativeMethods.urui_document_root(SurfaceHandle, NativeHandle))
                    ?? throw new InvalidOperationException("URUI 文档根身份不可用。");
            }
        }

        /// <summary>
        /// 当前文档的 body；未加载时为 null。
        /// </summary>
        public Element Body
        {
            get
            {
                CheckThread();
                return IsLoaded ? WrapElement(NativeMethods.urui_document_body(SurfaceHandle, NativeHandle)) : null;
            }
        }

        /// <summary>
        /// 创建当前文档拥有的有类型节点；脱离层级时仍有效，卸载时失效。
        /// </summary>
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

            var surfaceHandle = SurfaceHandle;
            ulong token = NativeMethods.urui_element_create(surfaceHandle, NativeHandle, factory.Tag);
            if (token == 0)
            {
                throw new InvalidOperationException("URUI 节点创建失败。");
            }

            try
            {
                if (factory.InputType != null && NativeMethods.urui_element_write(surfaceHandle, NativeHandle, token, ElementWriteField.Attribute, "type", factory.InputType) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("无法设置输入控件类型。");
                }

                if (NativeMethods.urui_element_write(surfaceHandle, NativeHandle, token, ElementWriteField.Attribute, "data-urui-type", factory.Type.FullName) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("无法设置控件身份。");
                }

                return (T)WrapNewElement(token);
            }
            catch
            {
                NativeMethods.urui_element_destroy(surfaceHandle, NativeHandle, token);
                SweepInvalidElements();
                throw;
            }
        }

        /// <summary>
        /// 按 HTML 标签创建脱离层级的节点；文档负责其整个生命周期。
        /// </summary>
        public Element CreateElement(string tag = "div")
        {
            RequireEffectDomAccess();
            CheckThread();
            RequireName(tag, nameof(tag));
            if (!IsLoaded)
            {
                throw new InvalidOperationException("请先加载文档。");
            }

            return WrapNewElement(NativeMethods.urui_element_create(SurfaceHandle, NativeHandle, tag))
                ?? throw new InvalidOperationException("URUI 无法创建节点。");
        }

        /// <summary>
        /// 在根及后代查询首个有类型节点。
        /// </summary>
        public T Q<T>(string name = null, string className = null) where T : Element => DocumentElement?.Q<T>(name, className);

        /// <summary>
        /// 构造有类型查询；文档需已加载。
        /// </summary>
        public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element
            => (DocumentElement ?? throw new InvalidOperationException("请先加载文档。")).Query<T>(name, className);

        /// <summary>
        /// 在根及后代查询首项；未加载时为 null。
        /// </summary>
        public Element Q(string name = null, string className = null)
        {
            CheckQuery(name, className);
            return DocumentElement?.Q(name, className);
        }

        /// <summary>
        /// 当前根及后代的深度优先快照；保留原有非泛型查询契约。
        /// </summary>
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

        internal Element WrapElement(ulong token) => WrapElementCore(token, newlyCreated: false);

        internal Element WrapNewElement(ulong token) => WrapElementCore(token, newlyCreated: true);

        Element WrapElementCore(ulong token, bool newlyCreated)
        {
            if (token == ulong.MaxValue)
            {
                throw new InvalidOperationException("URUI 元素身份获取失败或超出预算。");
            }

            if (token == 0)
            {
                return null;
            }

            var surfaceHandle = SurfaceHandle;
            if (surfaceHandle == 0)
            {
                return null;
            }
            var handle = NativeMethods.urui_surface_element_owner(surfaceHandle, token);
            var state = activeContent?.Handle == handle ? activeContent : candidateContent?.Handle == handle ? candidateContent : null;
            if (state == null || state.Retired || NativeMethods.urui_element_ref_valid(surfaceHandle, handle, token) != NativeStatus.Ok)
            {
                return null;
            }
            return WrapElementKnownOwner(surfaceHandle, state, token, null, newlyCreated);
        }

        Element WrapElementKnownOwner(ulong surfaceHandle, ContentState state, ulong token, string knownTag, bool newlyCreated)
        {
            var handle = state.Handle;
            var elements = state.Elements;
            if (elements.TryGetValue(token, out var existing))
            {
                return existing;
            }

            EnsureFactories();
            string marker = NativeMethods.ReadElement(surfaceHandle, handle, token, ElementReadField.Attribute, "data-urui-type");
            ElementFactory factory = null;
            if (marker != null)
            {
                if (!factoriesByFullName.TryGetValue(marker, out factory))
                {
                    throw new InvalidOperationException("HTML 引用了未注册控件类型：" + marker);
                }
            }
            if (factory == null)
            {
                string tag = knownTag ?? NativeMethods.ReadKnownTag(surfaceHandle, handle, token);
                string input = tag == "input" ? NativeMethods.ReadElement(surfaceHandle, handle, token, ElementReadField.Attribute, "type") ?? "text" : null;
                if (input != null)
                {
                    input = input.ToLowerInvariant();
                }
                tags.TryGetValue(input == null ? tag : tag + ":" + input, out factory);
            }
            var context = new ElementContext(this, state.Generation, token, handle);
            var value = factory == null ? new Element(context) : factory.Construct(context);
            if (value == null || value.Token != token || !ReferenceEquals(value.Document, this))
            {
                throw new InvalidOperationException("控件工厂必须返回当前上下文对应的元素。");
            }

            uint initialLayoutVersion;
            if (newlyCreated)
            {
                initialLayoutVersion = state.LayoutVersion;
                state.PendingSubtreeLayoutVersions?.Remove(token);
            }
            else if (state.PendingSubtreeLayoutVersions != null && state.PendingSubtreeLayoutVersions.Remove(token, out var pendingVersion))
            {
                initialLayoutVersion = pendingVersion;
            }
            else
            {
                initialLayoutVersion = 0;
            }

            elements.Add(token, value);
            state.AppendElementSnapshot(value);
            try
            {
                value.Initialize(initialLayoutVersion);
            }
            catch
            {
                elements.Remove(token);
                state.ElementSnapshot = null;
                value.Invalidate();
                throw;
            }
            return value;
        }

        internal void InvalidateUpdateHooks()
        {
            if (content != null && content.CachedUpdateHookCount >= 0)
            {
                content.CachedUpdateHookCount = -1;
            }
        }

        internal void InitializeElements()
        {
            LayoutVersion = 0;
            var state = content;
            if (state == null || state.Retired)
            {
                return;
            }
            var surfaceHandle = SurfaceHandle;
            if (surfaceHandle == 0)
            {
                return;
            }
            var rootToken = NativeMethods.urui_document_root(surfaceHandle, state.Handle);
            if (rootToken == 0)
            {
                throw new InvalidOperationException("URUI 文档根身份不可用。");
            }
            InitializeSubtreeTokens(surfaceHandle, state, rootToken);
        }

        static readonly Unity.Profiling.ProfilerMarker initializeSubtreeMarker = new("URUI.Elements.InitializeSubtree");

        internal void InitializeSubtree(Element root)
        {
            if (root == null)
            {
                return;
            }
            var state = content;
            if (state == null || state.Retired || state.Handle != root.NativeHandle)
            {
                return;
            }
            var surfaceHandle = SurfaceHandle;
            if (surfaceHandle == 0)
            {
                return;
            }
            InitializeSubtreeTokens(surfaceHandle, state, root.Token);
        }

        internal void RecordPendingSubtreeLayout(Element root)
        {
            if (root == null)
            {
                return;
            }
            var state = content;
            if (state == null || state.Retired || state.LayoutVersion == 0 || state.Handle != root.NativeHandle)
            {
                return;
            }
            var surfaceHandle = SurfaceHandle;
            if (surfaceHandle == 0)
            {
                return;
            }
            var handle = state.Handle;
            var elements = state.Elements;
            var version = state.LayoutVersion;
            var startDepth = initTokenStack.Count;
            int rootChildren = NativeMethods.urui_element_child_count(surfaceHandle, handle, root.Token);
            for (var index = rootChildren - 1; index >= 0; index--)
            {
                var child = NativeMethods.urui_element_child_at(surfaceHandle, handle, root.Token, index);
                if (child != 0)
                {
                    initTokenStack.Push(child);
                }
            }
            try
            {
                while (initTokenStack.Count > startDepth)
                {
                    var token = initTokenStack.Pop();
                    if (token == 0)
                    {
                        continue;
                    }
                    if (!elements.ContainsKey(token))
                    {
                        (state.PendingSubtreeLayoutVersions ??= new Dictionary<ulong, uint>())[token] = version;
                    }
                    int childCount = NativeMethods.urui_element_child_count(surfaceHandle, handle, token);
                    for (var index = childCount - 1; index >= 0; index--)
                    {
                        var child = NativeMethods.urui_element_child_at(surfaceHandle, handle, token, index);
                        if (child != 0)
                        {
                            initTokenStack.Push(child);
                        }
                    }
                }
            }
            finally
            {
                while (initTokenStack.Count > startDepth)
                {
                    initTokenStack.Pop();
                }
            }
        }

        void InitializeSubtreeTokens(ulong surfaceHandle, ContentState state, ulong rootToken)
        {
            using var sample = initializeSubtreeMarker.Auto();
            if (rootToken == 0)
            {
                return;
            }
            EnsureFactories();
            var handle = state.Handle;
            var elements = state.Elements;
            var pendingVersion = state.LayoutVersion;
            var startDepth = initTokenStack.Count;
            initTokenStack.Push(rootToken);
            var mutatedByInit = false;
            try
            {
                while (initTokenStack.Count > startDepth)
                {
                    if (state.Retired)
                    {
                        return;
                    }
                    var token = initTokenStack.Pop();
                    if (token == 0)
                    {
                        continue;
                    }
                    if (mutatedByInit && NativeMethods.urui_element_ref_valid(surfaceHandle, handle, token) != NativeStatus.Ok)
                    {
                        continue;
                    }
                    if (!elements.ContainsKey(token))
                    {
                        var tag = NativeMethods.ReadKnownTag(surfaceHandle, handle, token);
                        if (tag == null || tag == "#text" || tag == "#comment")
                        {
                            continue;
                        }
                        if (eagerTags.Contains(tag) || NativeMethods.HasElementAttribute(surfaceHandle, handle, token, "data-urui-type"))
                        {
                            WrapElementKnownOwner(surfaceHandle, state, token, tag, newlyCreated: true);
                            mutatedByInit = true;
                            if (state.Retired)
                            {
                                return;
                            }
                        }
                        else if (pendingVersion > 0)
                        {
                            (state.PendingSubtreeLayoutVersions ??= new Dictionary<ulong, uint>())[token] = pendingVersion;
                        }
                    }
                    int childCount = NativeMethods.urui_element_child_count(surfaceHandle, handle, token);
                    for (var index = childCount - 1; index >= 0; index--)
                    {
                        var child = NativeMethods.urui_element_child_at(surfaceHandle, handle, token, index);
                        if (child != 0)
                        {
                            initTokenStack.Push(child);
                        }
                    }
                }
            }
            finally
            {
                while (initTokenStack.Count > startDepth)
                {
                    initTokenStack.Pop();
                }
            }
        }

        internal void ReplaceInnerHtml(Element target, string html, string sourceUrl, URUI.Resources.CompiledTemplate template = null)
        {
            if (changingFragment)
            {
                EnqueueDeferred(() =>
                {
                    if (template == null)
                    {
                        target.SetInnerHtml(html, sourceUrl);
                    }
                    else
                    {
                        target.SetInnerHtml(template);
                    }
                }, DeferInvalidation.InvalidatesDocument);
                return;
            }
            if (template != null && (content?.CompiledAsset == null || content.CompiledFingerprint != template.ParentFingerprint))
            {
                throw new InvalidOperationException("编译片段不属于当前页面资产，请重新加载页面：" + sourceUrl);
            }
            var previousChangingContent = changingContent;
            var change = fragmentChange = new FragmentChange();
            changingContent = true;
            surface.BeginTransaction();
            var began = false;
            var surfaceHandle = SurfaceHandle;
            try
            {
                var status = template == null
                    ? NativeMethods.urui_element_begin_inner_html(surfaceHandle, NativeHandle, target.Token, html, sourceUrl)
                    : template.Begin(surfaceHandle, NativeHandle, target.Token);
                Element.RequireSuccess(surfaceHandle, status, "准备 HTML 内容");
                began = true;
                InitializeSubtree(target);
                Element.RequireSuccess(surfaceHandle, NativeMethods.urui_element_end_inner_html(surfaceHandle, NativeHandle, target.Token, 1), "提交 HTML 内容");
                began = false;
                change.Committed = true;
            }
            catch
            {
                if (began)
                {
                    Element.RequireSuccess(surfaceHandle, NativeMethods.urui_element_end_inner_html(surfaceHandle, NativeHandle, target.Token, 0), "回滚 HTML 内容");
                }
                throw;
            }
            finally
            {
                SweepInvalidElements();
                fragmentChange = null;
                changingContent = previousChangingContent;
                surface.EndTransaction();
                FinishNativeCall();
            }
        }

        internal unsafe void RefreshElements(bool layout)
        {
            using var profile = URUI.Internal.Hosting.RenderHost.ElementRefreshPhase.Auto();
            if (!IsLoaded)
            {
                return;
            }

            if (layout)
            {
                LayoutVersion++;
                content.PendingSubtreeLayoutVersions?.Clear();
            }

            var selected = content;
            var surfaceHandle = SurfaceHandle;
            var initialStamp = ReadRefreshStamp(surfaceHandle);
            selected.RefRevision = initialStamp.References;
            var reuseObserved = selected.ObservedRefreshStamp.Matches(initialStamp);
            var cachedHooks = selected.CachedUpdateHookCount;
            if (reuseObserved && cachedHooks >= 0 && (!layout || cachedHooks == 0))
            {
                return;
            }
            var ownsHookPublication = cachedHooks != UpdatingHookSet;
            // 只有完整成功、且没有变化的布局刷新才能发布新证明。
            selected.ObservedRefreshStamp = default;
            selected.CachedUpdateHookCount = UpdatingHookSet;
            var completed = false;
            var publishedHooks = false;
            try
            {
                var callbacksRan = false;
                var invalidTokens = false;
                var mayHaveChanged = false;
                var snapshot = selected.GetElements();
                var tail = snapshot.Offset + snapshot.Count;
                var cacheUpdateHooks = layout && ownsHookPublication;
                var updateHookCount = 0;
                var firstIndex = 0;
                if (reuseObserved && cachedHooks > 0)
                {
                    using var sample = updateHooksMarker.Auto();
                    var reusedAll = true;
                    for (var hook = 0; hook < cachedHooks; hook++)
                    {
                        var element = snapshot.Array[tail + hook];
                        var refreshed = element.TryObservedRefreshState(out var state);
                        if (refreshed)
                        {
                            mayHaveChanged = element.Refresh(true, state.State, state.Layout, initialStamp.References);
                            callbacksRan |= mayHaveChanged;
                        }
                        if (!refreshed || !IsLoaded || !ReferenceEquals(content, selected) ||
                            !ReferenceEquals(selected.ElementSnapshot, snapshot.Array) || selected.Elements.Count != snapshot.Count ||
                            !initialStamp.Matches(ReadRefreshStamp(surfaceHandle)))
                        {
                            // 回调追加节点可覆盖钩子尾部；立即停止读取，并沿原来的固定快照继续。
                            reusedAll = false;
                            cacheUpdateHooks = false;
                            reuseObserved = false;
                            while (firstIndex < snapshot.Count && !ReferenceEquals(snapshot.Array[snapshot.Offset + firstIndex], element))
                            {
                                firstIndex++;
                            }
                            if (firstIndex == snapshot.Count)
                            {
                                throw new InvalidOperationException("更新钩子不属于已发布的节点快照。");
                            }
                            if (refreshed)
                            {
                                firstIndex++;
                            }
                            break;
                        }
                    }
                    if (reusedAll)
                    {
                        selected.ObservedRefreshStamp = initialStamp;
                        if (ownsHookPublication && selected.CachedUpdateHookCount == UpdatingHookSet)
                        {
                            selected.CachedUpdateHookCount = cachedHooks;
                            publishedHooks = true;
                        }
                        completed = true;
                        return;
                    }
                }
                var windowStart = 0;
                var windowEnd = 0;
                for (var index = firstIndex; index < snapshot.Count; index++)
                {
                    if (mayHaveChanged)
                    {
                        if (!IsLoaded || !ReferenceEquals(content, selected))
                        {
                            break;
                        }
                        // 回调可改任意节点，也可重入并覆盖共享的批读缓冲。
                        windowEnd = index;
                        if (reuseObserved && !initialStamp.Matches(ReadRefreshStamp(surfaceHandle)))
                        {
                            reuseObserved = false;
                        }
                    }
                    var element = snapshot.Array[snapshot.Offset + index];
                    NativeMethods.ElementRefreshState state;
                    if (!reuseObserved || !element.TryObservedRefreshState(out state))
                    {
                        if (index >= windowEnd)
                        {
                            refreshTokens ??= new ulong[RefreshWindowSize];
                            refreshStates ??= new NativeMethods.ElementRefreshState[RefreshWindowSize];
                            windowStart = index;
                            windowEnd = Math.Min(snapshot.Count, index + RefreshWindowSize);
                            for (var item = index; item < windowEnd; item++)
                            {
                                refreshTokens[item - index] = snapshot.Array[snapshot.Offset + item].Token;
                                if (layout && snapshot.Array[snapshot.Offset + item].HasUpdateHook)
                                {
                                    windowEnd = item + 1;
                                    break;
                                }
                            }
                            fixed (ulong* tokens = refreshTokens)
                            {
                                fixed (NativeMethods.ElementRefreshState* states = refreshStates)
                                {
                                    using var readSample = readStatesMarker.Auto();
                                    if (NativeMethods.urui_element_refresh_states(surfaceHandle, selected.Handle, tokens, states, (uint)(windowEnd - index)) != NativeStatus.Ok)
                                    {
                                        throw new InvalidOperationException("元素刷新状态读取失败：" + NativeMethods.LastError(surfaceHandle));
                                    }
                                }
                            }
                        }
                        state = refreshStates[index - windowStart];
                    }
                    invalidTokens |= (state.State & 1) == 0;
                    if (cacheUpdateHooks && element.HasUpdateHook && (state.State & 2) != 0)
                    {
                        if (selected.CachedUpdateHookCount != UpdatingHookSet ||
                            !ReferenceEquals(selected.ElementSnapshot, snapshot.Array) || selected.Elements.Count != snapshot.Count ||
                            tail + updateHookCount >= snapshot.Array.Length)
                        {
                            cacheUpdateHooks = false;
                        }
                        else
                        {
                            snapshot.Array[tail + updateHookCount++] = element;
                        }
                    }
                    mayHaveChanged = element.Refresh(layout, state.State, state.Layout, selected.RefRevision);
                    callbacksRan |= mayHaveChanged;
                }
                var current = IsLoaded && ReferenceEquals(content, selected);
                var finalStamp = current ? ReadRefreshStamp(surfaceHandle) : default;
                if (current)
                {
                    selected.RefRevision = finalStamp.References;
                }
                var sweep = invalidTokens || callbacksRan && (!current || finalStamp.References != initialStamp.References);
                if (sweep)
                {
                    SweepInvalidElements();
                }
                if (!sweep && current && initialStamp.Matches(finalStamp) && (layout || reuseObserved))
                {
                    selected.ObservedRefreshStamp = finalStamp;
                    if (cacheUpdateHooks && selected.CachedUpdateHookCount == UpdatingHookSet &&
                        snapshot.Count == selected.Elements.Count && ReferenceEquals(selected.ElementSnapshot, snapshot.Array))
                    {
                        selected.CachedUpdateHookCount = updateHookCount;
                        publishedHooks = true;
                    }
                }
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    selected.ObservedRefreshStamp = default;
                }
                // 重入会共享暂存尾部；没有独占发布的外层或内层均不能留下那份证明。
                if (!publishedHooks)
                {
                    selected.CachedUpdateHookCount = -1;
                }
            }
        }

        static NativeMethods.ElementRefreshStamp ReadRefreshStamp(ulong surfaceHandle)
        {
            if (NativeMethods.urui_element_refresh_stamp(surfaceHandle, out var stamp) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("元素刷新代际读取失败：" + NativeMethods.LastError(surfaceHandle));
            }
            return stamp;
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
                        if (!IsLoaded || HasPendingInvalidation)
                        {
                            break;
                        }
                        try
                        {
                            callback(this);
                        }
                        catch (Exception error)
                        {
                            Debug.LogException(error);
                        }
                    }
                }
                finally
                {
                    EndControlUpdate();
                }
            }
        }

        static readonly Unity.Profiling.ProfilerMarker sweepMarker = new("URUI.Elements.Sweep");

        internal unsafe void SweepInvalidElements()
        {
            using var sample = sweepMarker.Auto();
            var selected = content;
            if (selected == null)
            {
                return;
            }
            CheckThread();
            var surfaceHandle = selected.SurfaceHandle;
            if (surfaceHandle != 0 && IsCurrent(selected.Generation))
            {
                ulong* drained = stackalloc ulong[DrainWindowSize];
                var overflowed = false;
                while (IsCurrent(selected.Generation))
                {
                    var count = NativeMethods.urui_document_drain_invalidated_elements(
                        surfaceHandle, selected.Handle, drained, DrainWindowSize, out var total, out var overflow);
                    if (count < 0)
                    {
                        overflowed = true;
                        break;
                    }
                    if (overflow != 0)
                    {
                        overflowed = true;
                    }
                    if (count == 0)
                    {
                        break;
                    }
                    for (var i = 0; i < count; i++)
                    {
                        if (!IsCurrent(selected.Generation))
                        {
                            break;
                        }
                        var token = drained[i];
                        selected.PendingSubtreeLayoutVersions?.Remove(token);
                        if (selected.Elements.TryGetValue(token, out var value))
                        {
                            selected.Elements.Remove(token);
                            selected.EffectLayouts.Remove(token);
                            selected.ElementSnapshot = null;
                            selected.InvalidationSerial++;
                            value.Invalidate();
                        }
                    }
                }
                if (!overflowed)
                {
                    return;
                }
            }
            SweepInvalidElementsFull(selected);
        }

        unsafe void SweepInvalidElementsFull(ContentState selected)
        {
            var snapshot = selected.GetElements();
            var windowStart = 0;
            var windowEnd = 0;
            var current = false;
            var individualChecks = false;
            var surfaceHandle = selected.SurfaceHandle;
            for (var index = 0; index < snapshot.Count; index++)
            {
                if (!individualChecks && index >= windowEnd)
                {
                    windowStart = index;
                    windowEnd = Math.Min(snapshot.Count, index + RefreshWindowSize);
                    current = surfaceHandle != 0 && IsCurrent(selected.Generation);
                    if (current)
                    {
                        refreshTokens ??= new ulong[RefreshWindowSize];
                        refreshStates ??= new NativeMethods.ElementRefreshState[RefreshWindowSize];
                        for (var item = index; item < windowEnd; item++)
                        {
                            refreshTokens[item - index] = snapshot.Array[snapshot.Offset + item].Token;
                        }
                        fixed (ulong* tokens = refreshTokens)
                        {
                            fixed (NativeMethods.ElementRefreshState* states = refreshStates)
                            {
                                if (NativeMethods.urui_element_refresh_states(surfaceHandle, selected.Handle, tokens, states, (uint)(windowEnd - index)) != NativeStatus.Ok)
                                {
                                    throw new InvalidOperationException("元素清扫状态读取失败：" + NativeMethods.LastError(surfaceHandle));
                                }
                            }
                        }
                    }
                }
                var value = snapshot.Array[snapshot.Offset + index];
                if (!(individualChecks ? value.IsValid : value.ValidateSnapshot(current && (refreshStates[index - windowStart].State & 1) != 0)))
                {
                    // The base element only clears managed fields and subscriptions.
                    // A subclass can reenter from OnInvalidated; after that, check the
                    // rest individually rather than repeatedly prefetching stale windows.
                    individualChecks |= value.GetType() != typeof(Element);
                    selected.Elements.Remove(value.Token);
                    selected.EffectLayouts.Remove(value.Token);
                    selected.ElementSnapshot = null;
                    selected.InvalidationSerial++;
                }
            }
        }
    }
}
