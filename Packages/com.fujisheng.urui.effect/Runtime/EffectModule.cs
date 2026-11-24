using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Rendering.Effects;
using URUI.Effect.Rendering;
namespace URUI.Effect
{
    /// <summary>
    /// 显式安装到一个文档的效果模块。项目资产和媒体输入由调用者绑定，不查找场景对象。
    /// </summary>
    public sealed class EffectModule : IDisposable
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Document, EffectModule> installed = new();
        /// <summary>
        /// 读取已安装模块，调试器不创建第二个模块。
        /// </summary>
        public static bool TryGet(Document document, out EffectModule module) => installed.TryGetValue(document, out module) && !module.disposed;
        /// <summary>
        /// 实际注册的效果样式参数。
        /// </summary>
        public static IReadOnlyList<URUI.Styles.StylePropertyDefinition> PropertyDefinitions
        {
            get;
        } = Array.AsReadOnly(UIEffectFactory.Parameters);
        public Document Document
        {
            get;
        }
        readonly Dictionary<Element, SurfaceEffectInput> surfaces = new();
        readonly Dictionary<Element, EffectController> controllers = new();
        readonly List<EffectController> controllerScratch = new();
        readonly HashSet<URUI.Effect.Elements.ParticlesElement> particles = new();
        bool disposed;
        readonly IDisposable particlesTypeRegistration;
        readonly IDisposable uiEffectRegistration;
        readonly IDisposable waveEffectRegistration;
        readonly IDisposable surfaceEffectRegistration;
        readonly IDisposable sequenceRegistration;
        sealed class SurfaceFactory : IEffectFactory
        {
            readonly EffectModule module;
            internal SurfaceFactory(EffectModule module)
            {
                this.module = module;
            }
            public IElementEffect Create(EffectContext context)
            {
                if (!module.surfaces.TryGetValue(context.Element, out var input))
                {
                    throw new InvalidOperationException("作者 surface-output 节点没有显式处理输入。");
                }
                return new SurfaceEffect(input, context);
            }
        }
        sealed class Binding : IDisposable
        {
            EffectModule owner;
            readonly Element element;
            internal Binding(EffectModule owner, Element element)
            {
                this.owner = owner;
                this.element = element;
            }
            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }
                owner.surfaces.Remove(element);
                owner = null;
            }
        }
        public EffectModule(Document document) : this(document, null) { }
        public EffectModule(Document document, IEffectResourceResolver resources)
        {
            Document = document ? document : throw new ArgumentNullException(nameof(document));
            if (TryGet(document, out _))
            {
                throw new InvalidOperationException("This document already owns an EffectModule.");
            }
            try
            {
                particlesTypeRegistration = document.RegisterElementType("urui-particles", context =>
                {
                    var element = new URUI.Effect.Elements.ParticlesElement(context, resources, value => particles.Remove(value), () => disposed);
                    particles.Add(element);
                    return element;
                });
                uiEffectRegistration = document.Effects.Register(new EffectDefinition("ui", EffectKind.Element, new UIEffectFactory(resources, RenderingParameters),
                    "ui:v1", "UIEffect 5.8.4; premultiplied RGBA; active render color space; HDR", UIEffectFactory.Parameters,
                    false, default, null, new EffectImageCondition("--urui-fx-blend-type", "alpha-blend"), null, null, EffectProgramDescriptors.UI, UIEffectParameters.Layout));
                waveEffectRegistration = document.Effects.Register(WaveEffect.Define());
                surfaceEffectRegistration = document.Effects.Register(new EffectDefinition("surface-output", EffectKind.Image, new SurfaceFactory(this), "surface:v1", "straight-alpha input/output; HDR; document color space", renderProgram: EffectProgramDescriptors.Surface));
                document.Effects.Updating += UpdateControllers;
                document.Effects.ParametersPreparing += FreezeControllers;
                document.Unloaded += Unloaded;
                sequenceRegistration = URUI.Animation.SequenceTrackRegistry.Register(document, new SequenceTrackFactory(resources));
                installed.Add(document, this);
            }
            catch
            {
                DisposeRegistrations();
                Dispose();
                throw;
            }
        }
        public EffectController GetController(Element element)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EffectModule));
            }
            if (element == null || !element.IsValid || !ReferenceEquals(element.Document, Document))
            {
                throw new ArgumentException("控制器需要当前文档的有效作者节点。", nameof(element));
            }
            if (!controllers.TryGetValue(element, out var value))
            {
                controllers.Add(element, value = new EffectController(this, element));
            }
            return value;
        }
        EffectParameters RenderingParameters(Element element) => controllers.TryGetValue(element, out var value) ? value.RenderingParameters : null;
        void SnapshotControllers()
        {
            controllerScratch.Clear();
            controllerScratch.AddRange(controllers.Values);
        }
        void UpdateControllers()
        {
            if (controllers.Count == 0)
            {
                return;
            }
            SnapshotControllers();
            for (var i = 0; i < controllerScratch.Count; i++)
            {
                controllerScratch[i].Tick();
            }
            controllerScratch.Clear();
        }
        void FreezeControllers()
        {
            if (controllers.Count == 0)
            {
                return;
            }
            SnapshotControllers();
            for (var i = 0; i < controllerScratch.Count; i++)
            {
                controllerScratch[i].Freeze();
            }
            controllerScratch.Clear();
        }
        void Unloaded(Document owner)
        {
            surfaces.Clear();
            if (controllers.Count == 0)
            {
                return;
            }
            SnapshotControllers();
            for (var i = 0; i < controllerScratch.Count; i++)
            {
                controllerScratch[i].Dispose();
            }
            controllerScratch.Clear();
        }
        internal void Release(EffectController controller)
        {
            controllers.Remove(controller.Element);
        }
        public IDisposable BindSurface(Element element, SurfaceEffectInput input)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EffectModule));
            }
            if (element == null || !ReferenceEquals(element.Document, Document) || !element.IsValid || input == null)
            {
                throw new ArgumentException("表面必须绑定当前文档的有效作者节点。");
            }
            if (!surfaces.TryAdd(element, input))
            {
                throw new InvalidOperationException("节点已有表面处理输入。");
            }
            return new Binding(this, element);
        }
        bool registrationsDisposed;

        void DisposeRegistrations()
        {
            if (registrationsDisposed)
            {
                return;
            }
            registrationsDisposed = true;
            surfaceEffectRegistration?.Dispose();
            waveEffectRegistration?.Dispose();
            uiEffectRegistration?.Dispose();
            particlesTypeRegistration?.Dispose();
        }

        void DisposeRegistrationsOnUnload(Document owner)
        {
            if (owner)
            {
                owner.Unloaded -= DisposeRegistrationsOnUnload;
            }
            DisposeRegistrations();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            sequenceRegistration?.Dispose();
            installed.Remove(Document);
            var keepUntilUnload = false;
            if (Document)
            {
                Document.Effects.Updating -= UpdateControllers;
                Document.Effects.ParametersPreparing -= FreezeControllers;
                Document.Unloaded -= Unloaded;
                Unloaded(Document);
                keepUntilUnload = Document.IsLoaded;
            }
            foreach (var element in particles)
            {
                element.Release();
            }
            particles.Clear();
            surfaces.Clear();
            if (keepUntilUnload && !registrationsDisposed)
            {
                // Live document may still finish rendering frames after module disposal (e.g. callback-driven dispose).
                Document.Unloaded += DisposeRegistrationsOnUnload;
            }
            else
            {
                DisposeRegistrations();
            }
        }
    }
}
