using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Documents;
using URUI.Elements;
using URUI.Rendering;
using URUI.Effect.Rendering;
namespace URUI.Effect
{
    /// <summary>
    /// 持久注册一个作者节点；粒子源独立准备、原子替换并经宿主 GPU fence 退役。
    /// </summary>
    public sealed class ParticlePainter : IDisposable
    {
        sealed class Emitter
        {
            internal ParticleSystem System;
            internal ParticleSystemRenderer Renderer;
            internal Mesh Mesh;
            internal Mesh Trails;
            internal Material[] Materials;
            internal ulong GeometryVersion;
            internal ParticleSystem.Particle[] Particles = Array.Empty<ParticleSystem.Particle>();
            internal int Frame = -1;
            internal int Version = -1;
            internal int Count;
            internal uint OriginalSeed;
            internal bool OriginalAutoSeed;
        }
        static GameObject sharedCameraObject;
        static Camera sharedCamera;
        static int sharedCameraUsers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            particleShader = null;
            if (sharedCameraObject)
            {
                EffectObjectLifetime.Destroy(sharedCameraObject);
            }
            sharedCameraObject = null;
            sharedCamera = null;
            sharedCameraUsers = 0;
        }

        static Camera AcquireBakingCamera()
        {
            if (!sharedCameraObject || !sharedCamera)
            {
                sharedCameraObject = new GameObject("URUI particle baking camera") { hideFlags = HideFlags.HideAndDontSave };
                sharedCamera = sharedCameraObject.AddComponent<Camera>();
                sharedCamera.enabled = false;
                sharedCamera.orthographic = true;
                sharedCamera.aspect = 1;
                sharedCamera.transform.position = new Vector3(0, 0, -1000);
            }
            sharedCameraUsers++;
            return sharedCamera;
        }

        static void ReleaseBakingCamera()
        {
            if (sharedCameraUsers > 0 && --sharedCameraUsers == 0)
            {
                if (sharedCameraObject)
                {
                    EffectObjectLifetime.Destroy(sharedCameraObject);
                }
                sharedCameraObject = null;
                sharedCamera = null;
            }
        }

        sealed class SourceState : IDisposable
        {
            internal GameObject Instance;
            internal Camera Camera;
            bool cameraAcquired;
            internal readonly List<Emitter> Emitters = new();
            sealed class Programs
            {
                internal readonly List<(ProgramState State, ExternalGeometryProgram Program)> Values = new();
                internal bool HasLast;
                internal int LastCrc;
                internal Shader LastShader;
                internal ExternalGeometryProgram LastProgram;
            }
            readonly Dictionary<Material, Programs> programs = new();
            internal ExternalGeometryProgram Program(Material material)
            {
                if (!material)
                {
                    throw new ArgumentException("粒子源没有有效材质。", nameof(material));
                }
                if (!programs.TryGetValue(material, out var entry))
                {
                    entry = new Programs();
                    programs.Add(material, entry);
                }
                var crc = material.ComputeCRC();
                var matShader = material.shader;
                if (entry.HasLast && entry.LastCrc == crc && entry.LastShader == matShader)
                {
                    return entry.LastProgram;
                }
                var state = ReadProgramState(material);
                for (var i = 0; i < entry.Values.Count; i++)
                {
                    var value = entry.Values[i];
                    if (value.State.Equals(state))
                    {
                        entry.HasLast = true;
                        entry.LastCrc = crc;
                        entry.LastShader = matShader;
                        entry.LastProgram = value.Program;
                        return value.Program;
                    }
                }
                if (entry.Values.Count >= 256)
                {
                    throw new InvalidOperationException("粒子材质的离散渲染状态超出预算。");
                }
                var program = CreateProgram(material);
                entry.Values.Add((state, program));
                entry.HasLast = true;
                entry.LastCrc = crc;
                entry.LastShader = matShader;
                entry.LastProgram = program;
                return program;
            }
            internal void RemoveUnusedProgram(Material material)
            {
                if (material == null)
                {
                    return;
                }
                for (int i = 0; i < Emitters.Count; i++)
                {
                    var materials = Emitters[i].Materials;
                    for (int j = 0; j < materials.Length; j++)
                    {
                        if (materials[j] == material)
                        {
                            return;
                        }
                    }
                }
                programs.Remove(material);
            }
            internal int Version;
            internal bool Manual;
            bool disposed;
            internal SourceState(GameObject source)
            {
                if (!source)
                {
                    throw new ArgumentNullException(nameof(source));
                }
                foreach (var component in source.GetComponentsInChildren<Component>(true))
                {
                    if (!(component is Transform || component is ParticleSystem || component is ParticleSystemRenderer))
                    {
                        throw new ArgumentException("粒子源含有非粒子组件：" + component?.GetType().Name, nameof(source));
                    }
                }
                try
                {
                    Instance = UnityEngine.Object.Instantiate(source);
                    Instance.name = "URUI particles: " + source.name;
                    Instance.hideFlags = HideFlags.HideAndDontSave;
                    Instance.transform.position = Vector3.zero;
                    Camera = AcquireBakingCamera();
                    cameraAcquired = true;
                    foreach (var system in Instance.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var renderer = system.GetComponent<ParticleSystemRenderer>();
                        if (!renderer)
                        {
                            throw new ArgumentException("粒子源缺少 Renderer。", nameof(source));
                        }
                        if (system.main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                        {
                            throw new NotSupportedException("自定义模拟空间需先声明坐标映射。");
                        }
                        var materials = renderer.sharedMaterials;
                        if (materials.Length == 0)
                        {
                            throw new ArgumentException("粒子源没有材质。", nameof(source));
                        }
                        foreach (var material in materials)
                        {
                            Program(material);
                        }
                        if (system.trails.enabled && materials.Length < 2)
                        {
                            throw new ArgumentException("开启尾迹的粒子缺少尾迹材质。", nameof(source));
                        }
                        renderer.enabled = false;
                        renderer.minParticleSize = 0;
                        renderer.maxParticleSize = 1;
                        var main = system.main;
                        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var mesh = new Mesh { name = "URUI particle mesh", hideFlags = HideFlags.HideAndDontSave };
                        mesh.MarkDynamic();
                        var trails = new Mesh { name = "URUI particle trails", hideFlags = HideFlags.HideAndDontSave };
                        trails.MarkDynamic();
                        Emitters.Add(new Emitter
                        {
                            System = system,
                            Renderer = renderer,
                            Materials = materials,
                            OriginalSeed = system.randomSeed,
                            OriginalAutoSeed = system.useAutoRandomSeed,
                            Mesh = mesh,
                            Trails = trails
                        });
                    }
                    if (Emitters.Count == 0)
                    {
                        throw new ArgumentException("粒子源没有粒子系统。", nameof(source));
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }
            internal void Apply(ParticlePlaybackState state, bool hidden, bool pauseWhenHidden, bool restart)
            {
                foreach (var emitter in Emitters)
                {
                    var system = emitter.System;
                    if (state == ParticlePlaybackState.Stopped || restart)
                    {
                        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    if (state == ParticlePlaybackState.Playing && (!hidden || !pauseWhenHidden) && !Manual)
                    {
                        system.Play(false);
                    }
                    else if (state != ParticlePlaybackState.Stopped)
                    {
                        system.Pause(false);
                    }
                }
                Version++;
            }
            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                foreach (var emitter in Emitters)
                {
                    EffectObjectLifetime.Destroy(emitter.Mesh);
                    EffectObjectLifetime.Destroy(emitter.Trails);
                }
                Emitters.Clear();
                programs.Clear();
                EffectObjectLifetime.Destroy(Instance);
                Instance = null;
                Camera = null;
                if (cameraAcquired)
                {
                    cameraAcquired = false;
                    ReleaseBakingCamera();
                }
            }
        }
        readonly Element element;
        readonly Document document;
        readonly bool observeLayout;
        SourceState state;
        GameObject source;
        ParticlePlaybackOptions options;
        IDisposable registration;
        bool disposed;
        bool visible = true;
        long bodyBakes;
        long trailBakes;
        long particleReads;
        long drawCalls;
        long readTicks;
        long bodyTicks;
        long trailTicks;
        long recordTicks;
        public ParticlePlaybackState PlaybackState
        {
            get;
            private set;
        }
        public int ParticleCount
        {
            get;
            private set;
        }
        public GameObject Source => source;
        /// <summary>
        /// 显式模拟时间，仅在手动模拟时有意义。
        /// </summary>
        public float SimulationTime
        {
            get;
            private set;
        }
        /// <summary>
        /// 手动模拟禁止 Unity 自动推进自有粒子副本。
        /// </summary>
        public bool ManualSimulation => state != null && state.Manual;
        uint manualSeed = 1;
        sealed class TemporaryOverride : IDisposable
        {
            Action release;
            internal TemporaryOverride(Action release)
            {
                this.release = release;
            }
            public void Dispose()
            {
                var action = release;
                release = null;
                action?.Invoke();
            }
        }
        /// <summary>
        /// 临时播放调试租约；释放时仅恢复仍使用原源的有效实例。
        /// </summary>
        public IDisposable OverridePlayback()
        {
            Require();
            var previousSource = source;
            bool manual = state.Manual;
            uint seed = manualSeed;
            float time = SimulationTime;
            var previousPlayback = PlaybackState;
            float actualTime = state.Emitters.Count > 0 ? state.Emitters[0].System.time : 0;
            return new TemporaryOverride(() =>
            {
                if (disposed || !element.IsValid || source != previousSource)
                {
                    return;
                }
                SetManualSimulation(true, seed);
                Seek(manual ? time : Mathf.Min(600, actualTime), seed);
                if (!manual)
                {
                    state.Manual = false;
                    foreach (var emitter in state.Emitters)
                    {
                        emitter.System.useAutoRandomSeed = emitter.OriginalAutoSeed;
                        if (!emitter.OriginalAutoSeed)
                        {
                            emitter.System.randomSeed = emitter.OriginalSeed;
                        }
                    }
                }
                SimulationTime = time;
                PlaybackState = previousPlayback;
                state.Apply(previousPlayback, !visible, options.PauseWhenHidden, false);
            });
        }
        internal static void ValidateSequenceSource(GameObject value)
        {
            if (!value)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var components = value.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (!(component is Transform || component is ParticleSystem || component is ParticleSystemRenderer))
                {
                    throw new ArgumentException("粒子源含有非粒子组件：" + component?.GetType().Name, nameof(value));
                }
            }
            var systems = value.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                throw new ArgumentException("粒子源没有粒子系统。", nameof(value));
            }
            for (int i = 0; i < systems.Length; i++)
            {
                var system = systems[i];
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (!renderer)
                {
                    throw new ArgumentException("粒子源缺少 Renderer。", nameof(value));
                }
                if (system.main.simulationSpace == ParticleSystemSimulationSpace.Custom)
                {
                    throw new NotSupportedException("自定义模拟空间需先声明坐标映射。");
                }
                var materials = renderer.sharedMaterials;
                if (materials.Length == 0)
                {
                    throw new ArgumentException("粒子源没有材质。", nameof(value));
                }
                for (int m = 0; m < materials.Length; m++)
                {
                    CreateProgram(materials[m]);
                }
                if (system.trails.enabled && materials.Length < 2)
                {
                    throw new ArgumentException("开启尾迹的粒子缺少尾迹材质。", nameof(value));
                }
            }
        }
        public ParticlePainter(Element element, GameObject source, bool restartOnShow = true) : this(element, source, new ParticlePlaybackOptions { RestartOnShow = restartOnShow }) { }
        public ParticlePainter(Element element, GameObject source, ParticlePlaybackOptions options) : this(element, source, options, true) { }
        internal ParticlePainter(Element element, GameObject source, ParticlePlaybackOptions options, bool observeLayout)
        {
            this.element = element ?? throw new ArgumentNullException(nameof(element));
            document = element.Document;
            this.options = (options ?? throw new ArgumentNullException(nameof(options))).Clone();
            this.observeLayout = observeLayout;
            state = new SourceState(source);
            this.source = source;
            PlaybackState = this.options.PlayOnLoad ? ParticlePlaybackState.Playing : ParticlePlaybackState.Paused;
            try
            {
                registration = document.AttachGeometryProvider(element, "particles", Paint, Recorded, Release);
                element.AddToClassList("has-particles");
                if (observeLayout)
                {
                    document.LayoutUpdated += Update;
                }
                state.Apply(PlaybackState, false, this.options.PauseWhenHidden, true);
            }
            catch
            {
                if (registration != null)
                {
                    registration.Dispose();
                }
                else
                {
                    Release();
                }
                throw;
            }
        }
        void Require()
        {
            if (disposed || !element.IsValid)
            {
                throw new ObjectDisposedException(nameof(ParticlePainter));
            }
        }
        public void SetSource(GameObject value)
        {
            Require();
            if (source == value)
            {
                return;
            }
            ReplaceSource(value);
        }
        /// <summary>
        /// 同一源资产变化后重建副本；旧资源沿宿主 GPU fence 退役。
        /// </summary>
        public void RefreshSource()
        {
            Require();
            ReplaceSource(source);
        }
        void ReplaceSource(GameObject value)
        {
            var next = new SourceState(value);
            try
            {
                next.Manual = state.Manual;
                if (next.Manual)
                {
                    ConfigureSeeds(next, manualSeed);
                }
                next.Apply(PlaybackState, !visible, options.PauseWhenHidden, PlaybackState == ParticlePlaybackState.Playing);
                document.RetirePainterResources(state);
                foreach (var emitter in state.Emitters)
                {
                    emitter.System.Pause(false);
                }
                state = next;
                source = value;
                ParticleCount = 0;
                SimulationTime = 0;
            }
            catch
            {
                next.Dispose();
                throw;
            }
        }
        public void SetOptions(ParticlePlaybackOptions value)
        {
            Require();
            options = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
            state.Apply(PlaybackState, !visible, options.PauseWhenHidden, false);
        }
        public void Play()
        {
            Require();
            PlaybackState = ParticlePlaybackState.Playing;
            state.Apply(PlaybackState, !visible, options.PauseWhenHidden, false);
        }
        public void Pause()
        {
            Require();
            PlaybackState = ParticlePlaybackState.Paused;
            state.Apply(PlaybackState, !visible, options.PauseWhenHidden, false);
        }
        public void Stop()
        {
            Require();
            PlaybackState = ParticlePlaybackState.Stopped;
            ParticleCount = 0;
            SimulationTime = 0;
            state.Apply(PlaybackState, !visible, options.PauseWhenHidden, false);
        }
        public void Restart()
        {
            Require();
            PlaybackState = ParticlePlaybackState.Playing;
            SimulationTime = 0;
            state.Apply(PlaybackState, !visible, options.PauseWhenHidden, true);
        }
        /// <summary>
        /// 切换自有副本的手动模拟，不修改源 Prefab 或共享 Unity 时钟。
        /// </summary>
        public void SetManualSimulation(bool enabled, uint randomSeed = 1)
        {
            Require();
            if (randomSeed == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(randomSeed));
            }
            state.Manual = enabled;
            manualSeed = randomSeed;
            SimulationTime = 0;
            if (enabled)
            {
                ConfigureSeeds(state, randomSeed);
            }
            else
            {
                foreach (var emitter in state.Emitters)
                {
                    emitter.System.useAutoRandomSeed = emitter.OriginalAutoSeed;
                    if (!emitter.OriginalAutoSeed)
                    {
                        emitter.System.randomSeed = emitter.OriginalSeed;
                    }
                }
            }
            state.Apply(PlaybackState, !visible, options.PauseWhenHidden, true);
        }
        static void ConfigureSeeds(SourceState value, uint seed)
        {
            uint index = 0;
            foreach (var emitter in value.Emitters)
            {
                emitter.System.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                emitter.System.useAutoRandomSeed = false;
                emitter.System.randomSeed = seed + index++;
            }
        }
        /// <summary>
        /// 从起点推进自有副本；支持 0..600 秒，同平台同配置验证确定性。
        /// </summary>
        public void Seek(float time, uint randomSeed = 1)
        {
            Require();
            if (!float.IsFinite(time) || time < 0 || time > 600)
            {
                throw new ArgumentOutOfRangeException(nameof(time));
            }
            if (randomSeed == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(randomSeed));
            }
            if (!state.Manual || manualSeed != randomSeed)
            {
                SetManualSimulation(true, randomSeed);
            }
            ConfigureSeeds(state, randomSeed);
            foreach (var emitter in state.Emitters)
            {
                emitter.System.Simulate(time, false, true, true);
                emitter.System.Pause(false);
            }
            SimulationTime = time;
            PlaybackState = ParticlePlaybackState.Paused;
            state.Version++;
        }
        /// <summary>
        /// 手动单步并保持播放状态；单步本身不会启动 Unity 自动模拟。
        /// </summary>
        public void Step(float delta)
        {
            Require();
            if (!float.IsFinite(delta) || delta < 0 || delta > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }
            if (!state.Manual)
            {
                SetManualSimulation(true, manualSeed);
            }
            foreach (var emitter in state.Emitters)
            {
                emitter.System.Simulate(delta, false, false, true);
                emitter.System.Pause(false);
            }
            SimulationTime += delta;
            state.Version++;
        }
        void Update(Document owner) => UpdateVisibility();
        internal void UpdateVisibility()
        {
            if (disposed)
            {
                return;
            }
            const ElementState RequiredVisible = ElementState.Valid | ElementState.Attached | ElementState.VisibleInHierarchy;
            var flags = element.StateFlags;
            if ((flags & ElementState.Valid) == 0)
            {
                return;
            }
            var displayed = (flags & RequiredVisible) == RequiredVisible && element.HasLayout;
            if (displayed == visible)
            {
                return;
            }
            visible = displayed;
            state.Apply(PlaybackState, !visible, options.PauseWhenHidden, displayed && options.RestartOnShow && PlaybackState == ParticlePlaybackState.Playing);
        }
        static Shader particleShader;
        readonly struct ProgramState : IEquatable<ProgramState>
        {
            internal readonly Shader Shader;
            internal readonly Vector4 Blend;
            internal readonly Vector4 Other;
            internal ProgramState(Shader shader, Vector4 blend, Vector4 other)
            {
                Shader = shader;
                Blend = blend;
                Other = other;
            }
            public bool Equals(ProgramState other) => Shader == other.Shader && Blend.Equals(other.Blend) && Other.Equals(other.Other);
        }
        static readonly int srcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int dstBlend = Shader.PropertyToID("_DstBlend");
        static readonly int srcAlpha = Shader.PropertyToID("_SrcBlendAlpha");
        static readonly int dstAlpha = Shader.PropertyToID("_DstBlendAlpha");
        static readonly int premultiply = Shader.PropertyToID("_PremultiplyRGB");
        static readonly int cullMode = Shader.PropertyToID("_CullMode");
        static readonly int colorMask = Shader.PropertyToID("_ColorMask");
        static ProgramState ReadProgramState(Material material)
        {
            if (!particleShader)
            {
                particleShader = UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle");
            }
            if (material.shader != particleShader)
            {
                return new ProgramState(material.shader, default, default);
            }
            var alpha = material.GetFloat(premultiply);
            var kind = alpha <= 0 ? 0 : alpha >= 1 ? 1 : 2;
            return new ProgramState(material.shader, new Vector4(material.GetFloat(srcBlend), material.GetFloat(dstBlend), material.GetFloat(srcAlpha), material.GetFloat(dstAlpha)), new Vector4(kind, material.GetFloat(cullMode), material.GetFloat(colorMask), 0));
        }
        static int RenderState(float value, int maximum)
        {
            if (!float.IsFinite(value) || value < 0 || value > maximum || value != Mathf.Floor(value))
            {
                throw new InvalidOperationException("粒子材质的渲染状态必须为有效整数枚举。");
            }
            return (int)value;
        }
        static ExternalGeometryProgram CreateProgram(Material material)
        {
            if (!material)
            {
                throw new ArgumentException("粒子源没有有效材质。", nameof(material));
            }
            var state = ReadProgramState(material);
            // 扩展材质保留全部通道和独立边界；已知程序描述实际 RGBA 混合、裁面与写掩码。
            if (material.shader != particleShader)
            {
                return new ExternalGeometryProgram(material);
            }
            var alpha = state.Other.x == 0 ? RenderAlpha.Straight : state.Other.x == 1 ? RenderAlpha.Premultiplied : RenderAlpha.PartialPremultiplied;
            var descriptor = new RenderProgramDescriptor(RenderTextureEncoding.TextureDeclared, RenderColorSpace.Active, RenderColorSpace.Active,
                alpha, RenderBlendDomain.Active, (BlendMode)RenderState(state.Blend.x, 10), (BlendMode)RenderState(state.Blend.y, 10),
                RenderTargetCapability.Hdr, RenderSampling.LocalAddressing, RenderGeometry.Local, sourceAlphaBlend: (BlendMode)RenderState(state.Blend.z, 10),
                destinationAlphaBlend: (BlendMode)RenderState(state.Blend.w, 10), cull: (CullMode)RenderState(state.Other.y, 2), colorMask: (ColorWriteMask)RenderState(state.Other.z, 15));
            return new ExternalGeometryProgram(material, descriptor, positionComponents: 2);
        }
        static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        /// <summary>
        /// 主线程临时替换本实例中引用 source 的材质槽；不写 Renderer 源资产。调用方保持 replacement 有效直到租约与 GPU 使用结束。
        /// </summary>
        public IDisposable OverrideMaterial(Material source, Material replacement)
        {
            Require();
            if (!source || !replacement)
            {
                throw new ArgumentNullException(!source ? nameof(source) : nameof(replacement));
            }
            var target = state;
            var slots = new List<(Emitter Emitter, int Index)>();
            foreach (var emitter in target.Emitters)
            {
                for (int i = 0; i < emitter.Materials.Length; i++)
                {
                    if (emitter.Materials[i] == source)
                    {
                        slots.Add((emitter, i));
                    }
                }
            }
            if (slots.Count == 0)
            {
                throw new InvalidOperationException("Material is not used by this particle node. Select its body or trail material.");
            }
            target.Program(replacement);
            foreach (var slot in slots)
            {
                slot.Emitter.Materials[slot.Index] = replacement;
            }
            target.Version++;
            return new TemporaryOverride(() =>
            {
                if (disposed || !element.IsValid || !ReferenceEquals(state, target))
                {
                    return;
                }
                foreach (var slot in slots)
                {
                    if (slot.Emitter.Materials[slot.Index] == replacement)
                    {
                        slot.Emitter.Materials[slot.Index] = source;
                    }
                }
                target.RemoveUnusedProgram(replacement);
                target.Version++;
            });
        }
        public ParticlePainterStatistics GetStatistics() => new(bodyBakes, trailBakes, particleReads, drawCalls, Milliseconds(readTicks), Milliseconds(bodyTicks), Milliseconds(trailTicks), Milliseconds(recordTicks));
        void Recorded(int groups) => drawCalls += groups;
        void Paint(ExternalGeometryContext context)
        {
            if (context.IsClippedOut)
            {
                return;
            }
            ParticleCount = 0;
            bool simulating = !state.Manual && PlaybackState == ParticlePlaybackState.Playing && (visible || !options.PauseWhenHidden);
            for (int i = 0; i < state.Emitters.Count; i++)
            {
                var emitter = state.Emitters[i];
                if (!emitter.System.gameObject.activeInHierarchy)
                {
                    continue;
                }
                var trailsModule = emitter.System.trails;
                var trailsEnabled = trailsModule.enabled;
                bool needsBake = emitter.Version != state.Version || (simulating && emitter.Frame != Time.frameCount);
                if (needsBake)
                {
                    var liveParticles = emitter.System.particleCount;
                    if (liveParticles == 0 && (!trailsEnabled || trailsModule.dieWithParticles))
                    {
                        if (emitter.Count != 0 || emitter.Mesh.vertexCount != 0 || (trailsEnabled && emitter.Trails.vertexCount != 0))
                        {
                            emitter.Mesh.Clear(false);
                            if (trailsEnabled)
                            {
                                emitter.Trails.Clear(false);
                            }
                            emitter.Count = 0;
                            if (emitter.GeometryVersion == ulong.MaxValue)
                            {
                                throw new InvalidOperationException("粒子几何版本已耗尽。");
                            }
                            emitter.GeometryVersion++;
                        }
                        emitter.Frame = Time.frameCount;
                        emitter.Version = state.Version;
                        continue;
                    }
                    var start = Stopwatch.GetTimestamp();
                    FitBakingCamera(emitter, liveParticles);
                    readTicks += Stopwatch.GetTimestamp() - start;
                    particleReads++;
                    start = Stopwatch.GetTimestamp();
                    emitter.Renderer.BakeMesh(emitter.Mesh, state.Camera, true);
                    bodyTicks += Stopwatch.GetTimestamp() - start;
                    bodyBakes++;
                    if (trailsEnabled)
                    {
                        start = Stopwatch.GetTimestamp();
                        emitter.Renderer.BakeTrailsMesh(emitter.Trails, state.Camera, true);
                        trailTicks += Stopwatch.GetTimestamp() - start;
                        trailBakes++;
                    }
                    if (emitter.GeometryVersion == ulong.MaxValue)
                    {
                        throw new InvalidOperationException("粒子几何版本已耗尽。");
                    }
                    emitter.GeometryVersion++;
                    emitter.Frame = Time.frameCount;
                    emitter.Version = state.Version;
                }
                ParticleCount += emitter.Count;
                var bodyVerts = emitter.Mesh.vertexCount;
                var trailVerts = trailsEnabled ? emitter.Trails.vertexCount : 0;
                if (bodyVerts == 0 && trailVerts == 0)
                {
                    continue;
                }
                var offset = emitter.System.main.simulationSpace == ParticleSystemSimulationSpace.Local ? emitter.System.transform.position : Vector3.zero;
                offset.z = 0;
                var matrix = Matrix4x4.Scale(new Vector3(1, -1, 1)) * Matrix4x4.Translate(offset);
                var record = Stopwatch.GetTimestamp();
                if (bodyVerts > 0)
                {
                    context.Draw(new ExternalGeometryInput(emitter.Mesh, emitter.GeometryVersion), state.Program(emitter.Materials[0]), matrix);
                }
                if (trailVerts > 0)
                {
                    context.Draw(new ExternalGeometryInput(emitter.Trails, emitter.GeometryVersion), state.Program(emitter.Materials[1]), matrix);
                }
                recordTicks += Stopwatch.GetTimestamp() - record;
            }
        }
        void FitBakingCamera(Emitter emitter, int count)
        {
            if (emitter.Particles.Length < count)
            {
                emitter.Particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            }
            var alive = emitter.System.GetParticles(emitter.Particles);
            emitter.Count = alive;
            var scale = emitter.System.transform.lossyScale;
            var maximumScale = Mathf.Max(1, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            var extent = 1f;
            for (var i = 0; i < alive; i++)
            {
                extent = Mathf.Max(extent, emitter.Particles[i].GetCurrentSize3D(emitter.System).magnitude * maximumScale);
            }
            // 保留原保守尺寸公式；不因二维节点盒或裁剪范围缩小大粒子的烘焙视口。
            state.Camera.orthographicSize = extent;
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (observeLayout)
            {
                document.LayoutUpdated -= Update;
            }
            if (element.IsValid)
            {
                element.RemoveFromClassList("has-particles");
            }
            foreach (var emitter in state.Emitters)
            {
                emitter.System.Pause(false);
            }
            registration?.Dispose();
            registration = null;
        }
        void Release()
        {
            disposed = true;
            if (observeLayout)
            {
                document.LayoutUpdated -= Update;
            }
            state?.Dispose();
            state = null;
        }
    }
}
