using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Elements;
using URUI.Rendering;
using URUI.Rendering.Effects;
namespace URUI.Documents
{
    public sealed partial class Document
    {
        EffectRegistry effects;
        int effectCallbackDepth;
        Dictionary<string, ILayerFilterProgram> filterPrograms => content.FilterPrograms;
        Dictionary<(ulong token, EffectKind kind, ulong input), EffectEntry> effectInstances => content.EffectInstances;
        readonly HashSet<(ulong token, EffectKind kind, ulong input)> preparedEffects = new();
        readonly List<(ulong token, EffectKind kind, ulong input)> removedEffects = new();
        readonly float[] effectLayoutScratch = new float[40];
        Action<Action> effectSubmissionSink;
        internal Action<Action> EffectSubmissionSink => effectSubmissionSink ??= QueueEffectSubmission;
        void QueueEffectSubmission(Action callback) => host.QueueEffectSubmission(this, callback);
        EffectLayout FreezeEffectLayout(Element element, URUI.Styles.ComputedStyleSnapshot style)
        {
            if (URUI.Internal.Interop.NativeMethods.urui_effect_layout(SurfaceHandle, NativeHandle, element.Token, effectLayoutScratch, effectLayoutScratch.Length, out var root) != URUI.Internal.Interop.NativeStatus.Ok)
            {
                throw new InvalidOperationException("效果坐标快照已失效。");
            }
            var data = effectLayoutScratch;
            var own = Matrix4x4.identity;
            var rootMatrix = Matrix4x4.identity;
            for (var index = 0; index < 16; index++)
            {
                own[index] = data[4 + index];
                rootMatrix[index] = data[24 + index];
            }
            var rectangle = new Rect(data[0], data[1], data[2], data[3]);
            var rootRectangle = new Rect(data[20], data[21], data[22], data[23]);
            if (content.EffectLayouts.TryGetValue(element.Token, out var previous) && ReferenceEquals(previous.Style, style) &&
                previous.ContentRect.Equals(rectangle) && previous.ElementToDocument.Equals(own) && previous.RootToken == root &&
                previous.RootContentRect.Equals(rootRectangle) && previous.RootToDocument.Equals(rootMatrix))
            {
                return previous;
            }
            var result = new EffectLayout(style, rectangle, own, root, rootRectangle, rootMatrix);
            content.EffectLayouts[element.Token] = result;
            return result;
        }
        readonly struct PreparedLayout
        {
            internal readonly Rect Content;
            internal readonly Rect RootContent;
            internal readonly Matrix4x4 Transform;
            internal readonly Matrix4x4 RootTransform;
            internal readonly ulong Root;
            internal PreparedLayout(float[] values, ulong root)
            {
                Content = new Rect(values[0], values[1], values[2], values[3]);
                RootContent = new Rect(values[20], values[21], values[22], values[23]);
                var own = Matrix4x4.identity;
                var parent = Matrix4x4.identity;
                for (var index = 0; index < 16; index++)
                {
                    own[index] = values[4 + index];
                    parent[index] = values[24 + index];
                }
                Transform = own;
                RootTransform = parent;
                Root = root;
            }
            internal bool Matches(EffectLayout value) => value != null && value.ContentRect.Equals(Content) && value.RootContentRect.Equals(RootContent) && value.ElementToDocument.Equals(Transform) && value.RootToDocument.Equals(RootTransform) && value.RootToken == Root;
            internal EffectLayout Materialize(URUI.Styles.ComputedStyleSnapshot style) => new(style, Content, Transform, Root, RootContent, RootTransform);
        }
        PreparedLayout PrepareLayout(Element element)
        {
            if (URUI.Internal.Interop.NativeMethods.urui_effect_layout(SurfaceHandle, NativeHandle, element.Token, effectLayoutScratch, effectLayoutScratch.Length, out var root) != URUI.Internal.Interop.NativeStatus.Ok)
            {
                throw new InvalidOperationException("程序准备坐标已失效。");
            }
            return new PreparedLayout(effectLayoutScratch, root);
        }
        sealed class PreparedInputs : IPreparedEffectInputs, IDisposable
        {
            readonly Document owner;
            readonly Element element;
            URUI.Internal.Rendering.NativePreparedStyle style;
            internal PreparedLayout Layout;
            internal EffectInput Input;
            uint? sourceVersion;
            double time;
            ulong callback;
            ulong clock;
            bool active;
            bool hasBoundFrame;
            IEffectParameterBinding programBinding;
            EffectProgramCoordinates coordinates;
            Vector4[] boundValues;
            int boundValueCount;
            EffectLayout boundLayout;
            EffectGeometry materializedSource;
            EffectLayout materializedLayout;
            internal PreparedInputs(Document owner, Element element)
            {
                this.owner = owner;
                this.element = element;
            }
            internal void Capture(EffectInput input, uint? version, double now, ulong clock)
            {
                style.Replace(element);
                this.clock = clock;
                Layout = owner.PrepareLayout(element);
                Input = input;
                sourceVersion = version;
                time = now;
                hasBoundFrame = false;
                boundLayout = null;
                boundValueCount = 0;
                programBinding = owner.effects?.ProgramBinding(element.Token);
                coordinates = new(Layout.Content, Layout.RootContent, Layout.Transform, Layout.RootTransform);
                if (programBinding != null)
                {
                    coordinates = owner.ParameterCoordinates(programBinding, coordinates);
                }
                if (programBinding == null && owner.HasEffectParameterBinding(element.Token))
                {
                    boundLayout = owner.ParameterLayout(element, style.Materialize(element));
                    hasBoundFrame = true;
                }
            }
            public EffectProgramCoordinates Coordinates => hasBoundFrame ? new(boundLayout.ContentRect, boundLayout.RootContentRect, boundLayout.ElementToDocument, boundLayout.RootToDocument) : coordinates;
            public EffectProgramSource Source
            {
                get
                {
                    var captured = sourceVersion.HasValue;
                    var capture = Input.captureBounds;
                    var ink = Input.sourceInkBounds;
                    var uv = captured ? new Rect((ink.x - capture.x) / capture.width, (capture.yMax - ink.yMax) / capture.height, ink.width / capture.width, ink.height / capture.height) :
                        Input.preparedSource.Base != null ? Input.preparedSource.UvBounds : Input.geometry?.UvBounds ?? new Rect(0, 0, 1, 1);
                    return new EffectProgramSource(Input.source, Input.preparedSource.Base ?? Input.geometry, captured, Input.borrowed, capture, uv);
                }
            }
            public Vector2 Size => Layout.Content.size;
            public Vector2 ContentOffset => Layout.Content.position;
            public double Time => time;
            public bool IsActive(ulong version) => active && callback == version;
            internal ulong Open()
            {
                callback = checked(callback + 1);
                active = true;
                return callback;
            }
            internal void Close()
            {
                active = false;
            }
            public EffectFrame Materialize()
            {
                if (hasBoundFrame)
                {
                    return new EffectFrame(element.Token, boundLayout, Input.source, Input.borrowed, time, sourceVersion, SourceGeometry(), Input.captureBounds, Input.sourceInkBounds);
                }
                var snapshot = style.Materialize(element);
                if (materializedLayout == null || !ReferenceEquals(materializedLayout.Style, snapshot) || !Layout.Matches(materializedLayout))
                {
                    materializedLayout = Layout.Materialize(snapshot);
                }
                var layout = programBinding == null ? materializedLayout : owner.ApplyParameterLayout(element, materializedLayout);
                return new EffectFrame(element.Token, layout, Input.source, Input.borrowed, time, sourceVersion, SourceGeometry(), Input.captureBounds, Input.sourceInkBounds);
            }
            EffectGeometry SourceGeometry() => Input.preparedSource.Base == null ? Input.geometry : materializedSource = Input.preparedSource.Materialize(materializedSource);
            internal URUI.Styles.ComputedStyleSnapshot BoundStyle => hasBoundFrame ? boundLayout.Style : null;
            internal void FreezeProgramValues(ReadOnlySpan<Vector4> values)
            {
                if (boundValues == null || boundValues.Length < values.Length)
                {
                    boundValues = new Vector4[values.Length];
                }
                values.CopyTo(boundValues);
                boundValueCount = values.Length;
            }
            internal ImageProgramInput ShaderInput(RenderTimeInput timeInput)
            {
                if (timeInput != RenderTimeInput.None && timeInput != RenderTimeInput.UIClock)
                {
                    throw new NotSupportedException("固定参数图片程序需要声明 None 或 UIClock 时间源。");
                }
                return new(Input.preparedSource.Base?.Bounds ?? Input.geometry?.Bounds ?? Layout.Content, Size, time, timeInput == RenderTimeInput.UIClock ? clock : 0, (Input.borrowed ? 1u : 0u) | (!Input.borrowed ? 2u : 0u) | (sourceVersion.HasValue ? 4u : 0u), hasBoundFrame ? boundLayout.Style : null, boundValues == null ? default : boundValues.AsMemory(0, boundValueCount));
            }
            public void Dispose()
            {
                active = false;
                style.Dispose();
            }
        }
        public EffectRegistry Effects
        {
            get
            {
                CheckThread();
                return effects ??= new EffectRegistry(this);
            }
        }
        internal void CollectEffectClocks(Action<URUI.Animation.IUIClock> collect)
        {
            collect(surface.UIClock);
            effects?.CollectClocks(collect);
        }
        double EffectTime(Element element, out ulong identity) => host.FrozenEffectTime(effects.ClockFor(element), out identity);
        internal void UpdateEffectParameters()
        {
            effects?.UpdateParameters();
        }
        internal void FreezeEffectParameters()
        {
            effects?.FreezeParameters();
        }
        internal EffectLayout RawEffectLayout(Element element)
        {
            CheckThread();
            RequireEffectDomAccess();
            if (element == null || !element.IsValid || !element.IsAttached || !ReferenceEquals(element.Document, this))
            {
                throw new ArgumentException("参数需要当前文档的有效节点。");
            }
            element.AcceptRenderedLayout();
            return FreezeEffectLayout(element, Styles.GetComputed(element));
        }
        EffectLayout ParameterLayout(Element element, URUI.Styles.ComputedStyleSnapshot style)
        {
            return ApplyParameterLayout(element, FreezeEffectLayout(element, style));
        }
        EffectLayout ApplyParameterLayout(Element element, EffectLayout raw)
        {
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                return effects?.ApplyParameters(element, raw) ?? raw;
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        internal void RequireEffectDomAccess()
        {
            if (effectCallbackDepth != 0 || surface && surface.IsEffectCallback)
            {
                throw new InvalidOperationException("Effect Prepare/Record 中请使用不可变 EffectFrame，不能访问 DOM。");
            }
        }
        internal bool HasEffects => effects != null && effects.Count != 0;
        sealed class EffectEntry : IDisposable
        {
            internal EffectContext context;
            internal IElementEffect instance;
            internal EffectFrame frame;
            internal string name;
            internal Rect bounds;
            internal PreparedInputs prepared;
            public void Dispose()
            {
                prepared?.Dispose();
                try
                {
                    instance?.Dispose();
                }
                finally
                {
                    context?.Dispose();
                    instance = null;
                    context = null;
                    frame = default;
                }
            }
        }
        internal Rect EffectInkBounds(ulong token, EffectKind kind = EffectKind.Image, ulong input = 0) => effectInstances[(token, kind, input)].bounds;
        internal bool IsParameterImage(string name) => effects != null && effects.TryGet(name, out var definition) && definition.RenderParameters != null;
        internal EffectKind EffectKindForName(string name)
        {
            if (effects == null || !effects.TryGet(name, out var definition))
            {
                throw new InvalidOperationException("效果未注册：" + name);
            }
            return definition.Kind;
        }
        internal void BeginEffectPreparation()
        {
            preparedEffects.Clear();
        }
        internal void EndEffectPreparation()
        {
            removedEffects.Clear();
            foreach (var pair in effectInstances)
            {
                if (!preparedEffects.Contains(pair.Key))
                {
                    removedEffects.Add(pair.Key);
                }
            }
            foreach (var key in removedEffects)
            {
                RetireEffect(effectInstances[key]);
                effectInstances.Remove(key);
            }
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                foreach (var entry in effectInstances.Values)
                {
                    if (entry.instance is IParameterImageResources resources)
                    {
                        resources.CommitResources();
                    }
                }
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        internal bool TryGetEffectFrame(Element element, out EffectFrame frame)
        {
            CheckThread();
            frame = default;
            if (element == null || !ReferenceEquals(element.Document, this) || !element.IsValid)
            {
                return false;
            }
            if (effectInstances.TryGetValue((element.Token, EffectKind.Element, 0), out var entry))
            {
                frame = entry.prepared?.Materialize() ?? entry.frame;
                return frame.Layout != null;
            }
            foreach (var pair in effectInstances)
            {
                if (pair.Key.token == element.Token)
                {
                    frame = pair.Value.prepared?.Materialize() ?? pair.Value.frame;
                    return frame.Layout != null;
                }
            }
            return false;
        }
        internal bool ResolveProgramCoverage(ulong token, string name, Span<Matrix4x4> transforms, out int count, out Vector4 expansion, out EffectComposite composite)
        {
            count = 0;
            expansion = default;
            composite = default;
            if (effects == null || !effects.TryGet(name, out var definition) || definition.RenderParameters == null)
            {
                return false;
            }
            var element = WrapElement(token) ?? throw new InvalidOperationException("效果所属节点已失效。");
            element.AcceptRenderedLayout();
            var key = (token, definition.Kind, 0UL);
            preparedEffects.Add(key);
            if (!content.ImageLayouts.TryGetValue(name, out var layout))
            {
                content.ImageLayouts.Add(name, layout = new URUI.Internal.Rendering.NativeProgramLayout(this, name, definition.RenderParameters));
            }
            Span<Vector4> values = stackalloc Vector4[layout.RowCount];
            layout.ReadValues(element, values);
            if (effectInstances.TryGetValue(key, out var entry) && entry.name != name)
            {
                RetireEffect(entry);
                effectInstances.Remove(key);
                entry = null;
            }
            try
            {
                effectCallbackDepth++;
                surface.BeginEffect();
                try
                {
                    if (entry == null)
                    {
                        entry = new EffectEntry
                        {
                            name = name,
                            context = new EffectContext(definition, element, token, RetireEffect)
                        };
                        try
                        {
                            entry.instance = definition.Factory.Create(entry.context) ?? throw new InvalidOperationException("程序工厂返回 null。");
                        }
                        catch
                        {
                            RetireEffect(entry);
                            entry = null;
                            throw;
                        }
                        effectInstances.Add(key, entry);
                    }
                }
                finally
                {
                    effectCallbackDepth--;
                    surface.EndEffect();
                }
                if (entry.instance is not IParameterEffectLayout coverage)
                {
                    throw new InvalidOperationException("固定参数子树程序需要实现 IParameterEffectLayout。");
                }
                entry.prepared ??= new PreparedInputs(this, element);
                var now = EffectTime(element, out var clock);
                entry.prepared.Capture(new EffectInput(token, name, null, false, null), null, now, clock);
                if (HasTypedEffectParameterBinding(element.Token))
                {
                    ApplyProgramBinding(element.Token, name: layout.Name, values);
                    entry.prepared.FreezeProgramValues(values);
                }
                else if (entry.prepared.BoundStyle != null)
                {
                    layout.ReadBoundValues(entry.prepared.BoundStyle, values);
                }
                effectCallbackDepth++;
                surface.BeginEffect();
                var version = entry.prepared.Open();
                try
                {
                    var preparation = new EffectProgramPreparation(entry.prepared, version, values);
                    coverage.PrepareCoverage(preparation, transforms, out count, out expansion, out composite);
                    if (count < 0 || count > transforms.Length)
                    {
                        throw new InvalidOperationException("覆盖变换数量无效。");
                    }
                    var declared = definition.Expansion;
                    if (definition.HasCoverageCallbacks)
                    {
                        var frozen = entry.prepared.Materialize().Layout;
                        declared = definition.ResolveExpansion(frozen);
                        var additional = definition.ResolveBoundsTransforms(frozen);
                        if (additional.Length > transforms.Length - count)
                        {
                            throw new InvalidOperationException("声明的覆盖变换超出容量。");
                        }
                        for (var i = 0; i < additional.Length; i++)
                        {
                            transforms[count++] = additional[i];
                        }
                        if (definition.HasCompositeCallback)
                        {
                            composite = definition.ResolveComposite(frozen);
                        }
                    }
                    expansion += declared;
                    for (var i = 0; i < 4; i++)
                    {
                        if (!float.IsFinite(expansion[i]) || expansion[i] < 0)
                        {
                            throw new InvalidOperationException("程序扩边无效。");
                        }
                    }
                }
                finally
                {
                    entry.prepared.Close();
                    effectCallbackDepth--;
                    surface.EndEffect();
                }
                return true;
            }
            catch
            {
                if (effectInstances.Remove(key, out var failed))
                {
                    RetireEffect(failed);
                }
                throw;
            }
        }
        internal Vector4 ResolveEffectOutput(ulong token, string name, out EffectComposite composite, out Matrix4x4[] transforms)
        {
            var element = WrapElement(token);
            if (element == null || !element.IsAttached || effects == null || !effects.TryGet(name, out var definition))
            {
                throw new InvalidOperationException("效果扩边所属节点或程序已失效：" + name);
            }
            element.AcceptRenderedLayout();
            var layout = ParameterLayout(element, Styles.GetComputed(element));
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                composite = definition.ResolveComposite(layout);
                transforms = definition.ResolveBoundsTransforms(layout);
                return definition.ResolveExpansion(layout);
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        internal void PrepareEffects(IReadOnlyList<EffectInput> uses, uint? sourceVersion = null)
        {
            if (uses.Count == 0 && effectInstances.Count == 0)
            {
                return;
            }
            for (var useIndex = 0; useIndex < uses.Count; useIndex++)
            {
                var use = uses[useIndex];
                var element = WrapElement(use.token);
                if (element == null || !element.IsAttached)
                {
                    continue;
                }
                if (effects == null || !effects.TryGet(use.name, out var definition))
                {
                    throw new InvalidOperationException($"{Source?.name ?? name}: img #{element.Name}, --urui-image-effect '{use.name}' 未注册。");
                }
                var key = (use.token, definition.Kind, use.input);
                preparedEffects.Add(key);
                if (effectInstances.TryGetValue(key, out var entry) && entry.name != use.name)
                {
                    RetireEffect(entry);
                    effectInstances.Remove(key);
                    entry = null;
                }
                element.AcceptRenderedLayout();
                if (definition.RenderParameters != null && !content.ImageLayouts.ContainsKey(use.name))
                {
                    content.ImageLayouts.Add(use.name, new URUI.Internal.Rendering.NativeProgramLayout(this, use.name, definition.RenderParameters));
                }
                if (definition.RenderParameters != null)
                {
                    PrepareProgramEffect(use, sourceVersion, element, definition, key, entry);
                    continue;
                }
                var snapshot = Styles.GetComputed(element);
                for (var parameterIndex = 0; parameterIndex < definition.Parameters.Count; parameterIndex++)
                {
                    var parameter = definition.Parameters[parameterIndex];
                    if (!snapshot.Contains(parameter))
                    {
                        throw new InvalidOperationException($"{Source?.name ?? name}: img #{element.Name}, {use.name} 参数缺失或类型错误：{parameter.Name}");
                    }
                }
                var frame = new EffectFrame(use.token, ParameterLayout(element, snapshot), use.source, use.borrowed, EffectTime(element, out _), sourceVersion, use.geometry, use.captureBounds, use.sourceInkBounds);
                effectCallbackDepth++;
                surface.BeginEffect();
                try
                {
                    if (entry == null)
                    {
                        entry = new EffectEntry
                        {
                            name = use.name,
                            context = new EffectContext(definition, element, use.token, RetireEffect)
                        };
                        try
                        {
                            entry.instance = definition.Factory.Create(entry.context) ?? throw new InvalidOperationException("效果工厂返回 null：" + use.name);
                        }
                        catch
                        {
                            RetireEffect(entry);
                            throw;
                        }
                        effectInstances.Add(key, entry);
                    }
                    entry.instance.Prepare(frame);
                    entry.frame = frame;
                    var content = new Rect(frame.ContentOffset, frame.Size);
                    var e = definition.ResolveExpansion(frame.Layout);
                    entry.bounds = Rect.MinMaxRect(content.xMin - e.x, content.yMin - e.y, content.xMax + e.z, content.yMax + e.w);
                    if (entry.instance is IEffectBounds ink)
                    {
                        var b = ink.InkBounds;
                        if (!float.IsFinite(b.x) || !float.IsFinite(b.y) || !float.IsFinite(b.width) || !float.IsFinite(b.height) || b.width < 0 || b.height < 0)
                        {
                            throw new InvalidOperationException("效果绘制范围无效：" + definition.Name);
                        }
                        entry.bounds = Rect.MinMaxRect(Mathf.Min(entry.bounds.xMin, b.xMin), Mathf.Min(entry.bounds.yMin, b.yMin), Mathf.Max(entry.bounds.xMax, b.xMax), Mathf.Max(entry.bounds.yMax, b.yMax));
                    }
                }
                catch
                {
                    if (effectInstances.Remove(key, out var failed))
                    {
                        RetireEffect(failed);
                    }
                    throw;
                }
                finally
                {
                    effectCallbackDepth--;
                    surface.EndEffect();
                }
            }
        }
        void PrepareProgramEffect(EffectInput use, uint? sourceVersion, Element element, EffectDefinition definition, (ulong token, EffectKind kind, ulong input) key, EffectEntry entry)
        {
            var layout = content.ImageLayouts[use.name];
            Span<Vector4> values = stackalloc Vector4[layout.RowCount];
            layout.ReadValues(element, values);
            try
            {
                if (entry == null)
                {
                    entry = new EffectEntry
                    {
                        name = use.name,
                        context = new EffectContext(definition, element, use.token, RetireEffect)
                    };
                    effectCallbackDepth++;
                    surface.BeginEffect();
                    try
                    {
                        entry.instance = definition.Factory.Create(entry.context) ?? throw new InvalidOperationException("程序工厂返回 null。");
                    }
                    finally
                    {
                        effectCallbackDepth--;
                        surface.EndEffect();
                    }
                    effectInstances.Add(key, entry);
                }
                if (entry.instance is not IParameterImageEffect program)
                {
                    throw new InvalidOperationException("固定布局的工厂必须实现 IParameterImageEffect。");
                }
                entry.prepared ??= new PreparedInputs(this, element);
                var now = EffectTime(element, out var clock);
                entry.prepared.Capture(use, sourceVersion, now, clock);
                if (HasTypedEffectParameterBinding(element.Token))
                {
                    ApplyProgramBinding(element.Token, name: layout.Name, values);
                    entry.prepared.FreezeProgramValues(values);
                }
                else if (entry.prepared.BoundStyle != null)
                {
                    layout.ReadBoundValues(entry.prepared.BoundStyle, values);
                }
                effectCallbackDepth++;
                surface.BeginEffect();
                var version = entry.prepared.Open();
                try
                {
                    var preparation = new EffectProgramPreparation(entry.prepared, version, values);
                    program.PrepareProgram(preparation);
                    var content = entry.prepared.Layout.Content;
                    var expansion = definition.HasLayoutCallbacks ? definition.ResolveExpansion(entry.prepared.Materialize().Layout) : definition.Expansion;
                    entry.bounds = Rect.MinMaxRect(content.xMin - expansion.x, content.yMin - expansion.y, content.xMax + expansion.z, content.yMax + expansion.w);
                    if (entry.instance is IEffectBounds ink)
                    {
                        var b = ink.InkBounds;
                        if (!float.IsFinite(b.x) || !float.IsFinite(b.y) || !float.IsFinite(b.width) || !float.IsFinite(b.height) || b.width < 0 || b.height < 0)
                        {
                            throw new InvalidOperationException("程序绘制范围无效。");
                        }
                        entry.bounds = Rect.MinMaxRect(Mathf.Min(entry.bounds.xMin, b.xMin), Mathf.Min(entry.bounds.yMin, b.yMin), Mathf.Max(entry.bounds.xMax, b.xMax), Mathf.Max(entry.bounds.yMax, b.yMax));
                    }
                }
                finally
                {
                    entry.prepared.Close();
                    effectCallbackDepth--;
                    surface.EndEffect();
                }
            }
            catch
            {
                if (effectInstances.Remove(key, out var failed))
                {
                    RetireEffect(failed);
                }
                else if (entry != null)
                {
                    RetireEffect(entry);
                }
                throw;
            }
        }
        EffectProgramCoordinates ParameterCoordinates(IEffectParameterBinding binding, EffectProgramCoordinates coordinates)
        {
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                return binding.ResolveCoordinates(coordinates);
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        void ApplyProgramBinding(ulong token, string name, Span<Vector4> values)
        {
            var binding = effects?.ProgramBinding(token);
            if (binding == null)
            {
                throw new InvalidOperationException("固定参数绑定已失效。");
            }
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                binding.ResolveProgramParameters(name, values);
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        internal bool HasTypedEffectParameterBinding(ulong token) => effects?.ProgramBinding(token) != null;
        internal void BeginEffectProgramRead()
        {
            effectCallbackDepth++;
            surface.BeginEffect();
        }
        internal void EndEffectProgramRead()
        {
            effectCallbackDepth--;
            surface.EndEffect();
        }
        internal bool HasEffectParameterBinding(ulong token) => effects?.HasParameterBinding(token) == true;
        internal bool TryGetImageProgram(ulong token, ulong input, out Material material, out URUI.Internal.Rendering.NativeProgramLayout parameters, out ImageProgramInput frame, out IParameterImageEffect renderProgram, out RenderGeometry geometry, EffectKind kind = EffectKind.Image)
        {
            material = null;
            parameters = null;
            frame = default;
            renderProgram = null;
            geometry = RenderGeometry.Unknown;
            if (!effectInstances.TryGetValue((token, kind, input), out var entry) || entry.instance is not IParameterImageEffect program)
            {
                return false;
            }
            renderProgram = program;
            frame = entry.prepared?.ShaderInput(program.TimeInput) ?? throw new InvalidOperationException("程序尚未准备输入。");
            material = program.Program;
            if (!material || !material.HasProperty("_UruiParameters"))
            {
                throw new InvalidOperationException("固定参数图片材质缺少参数表契约。");
            }
            if (!content.ImageLayouts.TryGetValue(entry.name, out parameters))
            {
                throw new InvalidOperationException("固定参数图片缺少布局。");
            }
            if (effects.TryGet(entry.name, out var definition))
            {
                geometry = definition.RenderProgram.Geometry;
            }
            return true;
        }

        internal bool RecordEffect(ulong token, ElementPaintContext paint, Mesh geometry, Action<Action> submitted, EffectKind kind = EffectKind.Image, ulong input = 0)
        {
            if (!effectInstances.TryGetValue((token, kind, input), out var entry))
            {
                throw new InvalidOperationException("效果没有已准备的实例。");
            }
            var context = new EffectRenderContext(paint, geometry, entry.frame, submitted);
            effectCallbackDepth++;
            surface.BeginEffect();
            entry.context.Recording = true;
            try
            {
                entry.instance.Record(context);
                return !context.UsesOriginal;
            }
            finally
            {
                entry.context.Recording = false;
                effectCallbackDepth--;
                surface.EndEffect();
                context.Close();
            }
        }
        internal void EffectSubmitted(Action callback)
        {
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                callback();
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        void RetireEffect(IDisposable resource)
        {
            // CPU snapshot banks are no longer consumed after the callback; GPU
            // retirement retains only the instance's Unity resources.
            if (resource is EffectEntry entry)
            {
                entry.prepared?.Dispose();
            }
            host.RetirePaintResource(resource);
        }
        internal bool IsFilterCacheable(string name) => URUI.Internal.Rendering.BuiltinFilterPrograms.IsBuiltin(name) || effects != null && effects.IsFilterCacheable(name);
        internal void PrepareFilter(FilterInvocation invocation)
        {
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                if (!filterPrograms.TryGetValue(invocation.Name, out var program))
                {
                    if (URUI.Internal.Rendering.BuiltinFilterPrograms.IsBuiltin(invocation.Name))
                    {
                        program = URUI.Internal.Rendering.BuiltinFilterPrograms.Create(invocation.Name);
                    }
                    else
                    {
                        if (effects == null || !effects.TryGetFilter(invocation.Name, out var factory))
                        {
                            throw new InvalidOperationException($"{Source?.name ?? name}: filter '{invocation.Name}' 未注册。");
                        }
                        program = factory() ?? throw new InvalidOperationException("滤镜工厂返回 null：" + invocation.Name);
                    }
                    filterPrograms.Add(invocation.Name, program);
                }
                program.Prepare(invocation);
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        internal void RecordFilter(FilterRenderContext context, FilterInvocation invocation)
        {
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                filterPrograms[invocation.Name].Record(context, invocation);
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
                context.Close();
            }
        }
        void ReleaseEffects(ContentState state)
        {
            foreach (var layout in state.ImageLayouts.Values)
            {
                layout.Dispose();
            }
            state.ImageLayouts.Clear();
            foreach (var entry in state.EffectInstances.Values)
            {
                entry.prepared?.Dispose();
            }
            var resources = new List<IDisposable>(state.EffectInstances.Count + state.FilterPrograms.Count);
            resources.AddRange(state.EffectInstances.Values);
            resources.AddRange(state.FilterPrograms.Values);
            if (resources.Count == 0)
            {
                return;
            }
            if (host)
            {
                host.RetirePaintResources(resources);
            }
            else
            {
                foreach (var resource in resources)
                {
                    resource.Dispose();
                }
            }
            state.EffectInstances.Clear();
            state.FilterPrograms.Clear();
        }
    }
}
