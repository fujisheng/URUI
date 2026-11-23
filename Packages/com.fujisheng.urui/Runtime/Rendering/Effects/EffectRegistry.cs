using System;
using System.Collections.Generic;
using URUI.Documents;
using URUI.Internal.Interop;

namespace URUI.Rendering.Effects
{
    /// <summary>
    /// 显式文档级注册；加载期间不可更改类型或默默覆盖定义。支持通过返回句柄注销。
    /// </summary>
    public sealed class EffectRegistry
    {
        readonly Document document;
        bool checkedProtocol;
        ulong lastPrunedInvalidationSerial;

        void CheckProtocol()
        {
            if (checkedProtocol)
            {
                return;
            }
            try
            {
                if (NativeMethods.urui_effect_api_version() != NativeMethods.ExpectedEffectApiVersion)
                {
                    throw new NotSupportedException($"需要 URUI 效果协议 v{NativeMethods.ExpectedEffectApiVersion}。");
                }
            }
            catch (EntryPointNotFoundException error)
            {
                throw new NotSupportedException($"已安装的 native 插件缺少 URUI 效果协议 v{NativeMethods.ExpectedEffectApiVersion}，请重建当前目标插件。", error);
            }
            checkedProtocol = true;
        }

        readonly Dictionary<string, EffectDefinition> definitions = new(StringComparer.Ordinal);
        readonly Dictionary<string, Func<ILayerFilterProgram>> filters = new(StringComparer.Ordinal);
        readonly HashSet<string> cacheableFilters = new(StringComparer.Ordinal);
        readonly Dictionary<ulong, ClockBinding> clocks = new();
        readonly List<ulong> invalidTokens = new();
        readonly Dictionary<ulong, ParameterBinding> parameters = new();

        /// <summary>
        /// 布局更新后、原生绘制前更新播放时间；禁止在此回调重新同步布局。
        /// </summary>
        public event Action Updating;

        /// <summary>
        /// 原生绘制已冻结变换、任何效果 Prepare 之前，收集控制器参数。所有文档先收集再 Prepare。
        /// </summary>
        public event Action ParametersPreparing;

        internal void UpdateParameters() => Updating?.Invoke();
        internal void FreezeParameters() => ParametersPreparing?.Invoke();
        internal bool HasParameterBinding(ulong token) => parameters.ContainsKey(token);

        internal EffectLayout ApplyParameters(URUI.Elements.Element element, EffectLayout layout)
            => parameters.TryGetValue(element.Token, out var value) ? value.Resolve(layout) : layout;

        internal IEffectParameterBinding ProgramBinding(ulong token) => parameters.TryGetValue(token, out var value) ? value.Program : null;
        public EffectLayout GetLayout(URUI.Elements.Element element) => document.RawEffectLayout(element);

        public IDisposable BindParameters(URUI.Elements.Element element, Func<EffectLayout, EffectLayout> resolve)
        {
            document.CheckThread();
            document.RequireEffectDomAccess();
            if (element == null || !element.IsValid || !ReferenceEquals(element.Document, document) || resolve == null)
            {
                throw new ArgumentException("参数绑定需要当前文档的有效节点和解析器。");
            }
            var binding = new ParameterBinding(this, element, resolve);
            if (!parameters.TryAdd(element.Token, binding))
            {
                throw new InvalidOperationException("节点已经绑定效果参数。");
            }
            return binding;
        }

        /// <summary>
        /// 绑定固定参数与惰性公开布局；绑定租约释放后恢复作者样式。
        /// </summary>
        public IDisposable BindProgramParameters(URUI.Elements.Element element, IEffectParameterBinding resolve)
        {
            if (resolve == null)
            {
                throw new ArgumentNullException(nameof(resolve));
            }
            var lease = BindParameters(element, resolve.ResolveLayout);
            parameters[element.Token].Program = resolve;
            return lease;
        }

        /// <summary>
        /// 声明运行时参数需要捕获及显式命中策略；不修改作者 CSS，默认两个标志均为 false。
        /// </summary>
        public void SetRuntimeState(URUI.Elements.Element element, bool captureRequired, bool inputDisabled)
        {
            document.CheckThread();
            document.RequireEffectDomAccess();
            if (element == null || !element.IsValid || !ReferenceEquals(element.Document, document))
            {
                throw new ArgumentException("状态需要当前文档的有效节点。");
            }
            if (NativeMethods.urui_element_effect_state(document.SurfaceHandle, element.NativeHandle, element.Token, captureRequired ? 1 : 0, inputDisabled ? 1 : 0) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("效果运行时状态更新失败。");
            }
        }

        sealed class ParameterBinding : IDisposable
        {
            EffectRegistry owner;
            internal readonly URUI.Elements.Element Element;
            internal readonly Func<EffectLayout, EffectLayout> Resolve;
            internal IEffectParameterBinding Program;

            internal ParameterBinding(EffectRegistry owner, URUI.Elements.Element element, Func<EffectLayout, EffectLayout> resolve)
            {
                this.owner = owner;
                Element = element;
                Resolve = resolve;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }
                owner.document.CheckThread();
                if (Element.IsValid)
                {
                    owner.SetRuntimeState(Element, false, false);
                }
                if (owner.parameters.TryGetValue(Element.Token, out var current) && ReferenceEquals(current, this))
                {
                    owner.parameters.Remove(Element.Token);
                }
                owner = null;
            }
        }

        sealed class RegistrationHandle : IDisposable
        {
            Action unregister;
            internal RegistrationHandle(Action unregister)
            {
                this.unregister = unregister;
            }
            public void Dispose()
            {
                var action = unregister;
                unregister = null;
                action?.Invoke();
            }
        }

        internal EffectRegistry(Document document)
        {
            this.document = document;
            document.Unloaded += _ =>
            {
                clocks.Clear();
                parameters.Clear();
                lastPrunedInvalidationSerial = 0;
            };
            document.LayoutUpdated += _ =>
            {
                if (clocks.Count == 0 && parameters.Count == 0)
                {
                    return;
                }
                var serial = document.InvalidationSerial;
                if (serial == lastPrunedInvalidationSerial)
                {
                    return;
                }
                lastPrunedInvalidationSerial = serial;
                if (clocks.Count > 0)
                {
                    invalidTokens.Clear();
                    foreach (var pair in clocks)
                    {
                        if (!pair.Value.Element.IsValid)
                        {
                            invalidTokens.Add(pair.Key);
                        }
                    }
                    for (var i = 0; i < invalidTokens.Count; i++)
                    {
                        clocks.Remove(invalidTokens[i]);
                    }
                }
                if (parameters.Count > 0)
                {
                    invalidTokens.Clear();
                    foreach (var pair in parameters)
                    {
                        if (!pair.Value.Element.IsValid)
                        {
                            invalidTokens.Add(pair.Key);
                        }
                    }
                    for (var i = 0; i < invalidTokens.Count; i++)
                    {
                        parameters.Remove(invalidTokens[i]);
                    }
                }
            };
        }

        sealed class ClockBinding : IDisposable
        {
            internal readonly URUI.Elements.Element Element;
            internal readonly URUI.Animation.IUIClock Clock;
            EffectRegistry owner;

            internal ClockBinding(EffectRegistry owner, URUI.Elements.Element element, URUI.Animation.IUIClock clock)
            {
                this.owner = owner;
                Element = element;
                Clock = clock;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }
                owner.document.CheckThread();
                if (owner.clocks.TryGetValue(Element.Token, out var current) && ReferenceEquals(current, this))
                {
                    owner.clocks.Remove(Element.Token);
                }
                owner = null;
            }
        }

        /// <summary>
        /// 覆盖一个效果实例的时间源；调用方持有租约，解除后恢复Document时钟，不更换CSS的文档时基。
        /// </summary>
        public IDisposable BindClock(URUI.Elements.Element element, URUI.Animation.IUIClock clock)
        {
            document.CheckThread();
            document.RequireEffectDomAccess();
            if (element == null || !element.IsValid || !ReferenceEquals(element.Document, document) || clock == null)
            {
                throw new ArgumentException("效果时钟需要当前文档的有效元素和非空时钟。");
            }
            var binding = new ClockBinding(this, element, clock);
            if (!clocks.TryAdd(element.Token, binding))
            {
                throw new InvalidOperationException("元素已经绑定效果时钟。");
            }
            return binding;
        }

        internal void CollectClocks(Action<URUI.Animation.IUIClock> collect)
        {
            foreach (var binding in clocks.Values)
            {
                if (binding.Element.IsValid && binding.Element.IsAttached)
                {
                    collect(binding.Clock);
                }
            }
        }

        internal URUI.Animation.IUIClock ClockFor(URUI.Elements.Element element) => clocks.TryGetValue(element.Token, out var binding) ? binding.Clock : document.Surface.UIClock;
        public int Count => definitions.Count + filters.Count;

        public IDisposable RegisterFilter(string name, Func<ILayerFilterProgram> factory, bool cacheable = false)
        {
            document.CheckThread();
            CheckProtocol();
            if (document.IsLoaded)
            {
                throw new InvalidOperationException("滤镜程序必须在 Document.Load 前注册。");
            }
            if (string.IsNullOrWhiteSpace(name) || factory == null)
            {
                throw new ArgumentException("滤镜名和工厂必须有效。");
            }
            if (!filters.TryAdd(name, factory))
            {
                throw new InvalidOperationException("重复滤镜程序：" + name);
            }
            if (cacheable)
            {
                cacheableFilters.Add(name);
            }
            return new RegistrationHandle(() =>
            {
                if (filters.TryGetValue(name, out var current) && ReferenceEquals(current, factory))
                {
                    filters.Remove(name);
                    cacheableFilters.Remove(name);
                }
            });
        }

        internal bool IsFilterCacheable(string name) => cacheableFilters.Contains(name);

        internal void ConfigureDocument(ulong surfaceHandle, ulong handle)
        {
            foreach (var definition in definitions.Values)
            {
                if (definition.ImageCondition is { } condition &&
                    NativeMethods.urui_document_effect_image_condition(surfaceHandle, handle, definition.Name, condition.Property, condition.DirectValue) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("效果的原图条件注册失败：" + definition.Name);
                }
            }
        }

        internal bool TryGetFilter(string name, out Func<ILayerFilterProgram> factory) => filters.TryGetValue(name, out factory);

        public IDisposable Register(EffectDefinition definition)
        {
            document.CheckThread();
            CheckProtocol();
            if (document.IsLoaded)
            {
                throw new InvalidOperationException("效果定义必须在 Document.Load 前注册。");
            }
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (!definitions.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException("重复效果定义：" + definition.Name);
            }
            return new RegistrationHandle(() =>
            {
                if (definitions.TryGetValue(definition.Name, out var current) && ReferenceEquals(current, definition))
                {
                    definitions.Remove(definition.Name);
                }
            });
        }

        public bool TryGetFrame(URUI.Elements.Element element, out EffectFrame frame) => document.TryGetEffectFrame(element, out frame);
        public bool TryGet(string name, out EffectDefinition definition) => definitions.TryGetValue(name, out definition);
    }
}
