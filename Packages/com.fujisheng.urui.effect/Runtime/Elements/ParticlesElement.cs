using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
namespace URUI.Effect.Elements
{
    /// <summary>
    /// 声明式 Unity 粒子节点；HTML 的 src 由模块资源解析器映射，模拟仍由 Unity 驱动。
    /// </summary>
    public sealed class ParticlesElement : Element
    {
        readonly IEffectResourceResolver resources;
        readonly Action<ParticlesElement> invalidated;
        readonly Func<bool> moduleDisposed;
        ParticlePainter painter;
        IDisposable detached;
        string currentSource = string.Empty;
        /// <summary>
        /// 统一序列接管期间，独立预览器应停止推进和改写该节点。
        /// </summary>
        public bool SequenceControlled
        {
            get;
            private set;
        }
        internal IDisposable AcquireSequencePlayback()
        {
            if (SequenceControlled)
            {
                throw new InvalidOperationException("粒子节点已被另一条序列接管。");
            }
            var lease = OverridePlayback();
            SequenceControlled = true;
            return new SourceOverride(() =>
            {
                try
                {
                    lease.Dispose();
                }
                finally
                {
                    SequenceControlled = false;
                }
            });
        }
        internal ParticlesElement(ElementContext context, IEffectResourceResolver resources, Action<ParticlesElement> invalidated, Func<bool> moduleDisposed) : base(context)
        {
            this.resources = resources;
            this.invalidated = invalidated;
            this.moduleDisposed = moduleDisposed;
        }
        public string Source
        {
            get => GetAttribute("src") ?? string.Empty;
            set => SetAttribute("src", value ?? throw new ArgumentNullException(nameof(value)));
        }
        public string CurrentSource => currentSource;
        public int ParticleCount => painter?.ParticleCount ?? 0;
        public ParticlePlaybackState PlaybackState => painter?.PlaybackState ?? ParticlePlaybackState.Stopped;
        public ParticlePainterStatistics GetStatistics() => painter?.GetStatistics() ?? default;
        ParticlePainter RequirePainter()
        {
            if (moduleDisposed())
            {
                throw new ObjectDisposedException(nameof(EffectModule));
            }
            return painter ?? throw new InvalidOperationException("请先为粒子节点提供有效 src。");
        }
        public void Play() => RequirePainter().Play();
        public void Pause() => RequirePainter().Pause();
        public void Stop() => RequirePainter().Stop();
        public void Restart() => RequirePainter().Restart();
        /// <summary>
        /// 显式模拟预览，不推进游戏时钟。
        /// </summary>
        public void Seek(float time, uint randomSeed = 1) => RequirePainter().Seek(time, randomSeed);
        /// <summary>
        /// 单步自有粒子副本。
        /// </summary>
        public void Step(float delta) => RequirePainter().Step(delta);
        /// <summary>
        /// 同源资产修改后刷新自有副本。
        /// </summary>
        public void RefreshSource() => RequirePainter().RefreshSource();
        /// <summary>
        /// 当前手动模拟时间。
        /// </summary>
        public float SimulationTime => painter?.SimulationTime ?? 0;
        /// <summary>
        /// 临时播放调试租约，退出检查会话时释放。
        /// </summary>
        public IDisposable OverridePlayback() => RequirePainter().OverridePlayback();
        /// <summary>
        /// 临时替换当前粒子实例中使用指定材质的槽位；释放时恢复仍由租约拥有的槽位。
        /// </summary>
        public IDisposable OverrideMaterial(Material source, Material replacement) => RequirePainter().OverrideMaterial(source, replacement);
        /// <summary>
        /// 临时覆盖源；释放时仅恢复仍由该预览拥有的绑定。
        /// </summary>
        public IDisposable OverrideSource(GameObject value)
        {
            var target = RequirePainter();
            var previous = target.Source;
            target.SetSource(value);
            return new SourceOverride(() =>
            {
                if (IsValid && ReferenceEquals(painter, target) && target.Source == value)
                {
                    target.SetSource(previous);
                }
            });
        }
        sealed class SourceOverride : IDisposable
        {
            Action release;
            internal SourceOverride(Action release)
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
        protected override void OnInitialize()
        {
            detached = RegisterCallback<DetachFromDocumentEvent>(_ => painter?.UpdateVisibility());
            ApplySource(Source);
        }
        static bool Flag(string value, bool fallback)
        {
            if (value == null)
            {
                return fallback;
            }
            if (value.Length == 0)
            {
                return true;
            }
            if (bool.TryParse(value, out var result))
            {
                return result;
            }
            throw new ArgumentException("粒子 data 属性只接受 true/false。");
        }
        ParticlePlaybackOptions Options() => new()
        {
            PlayOnLoad = Flag(GetAttribute("data-urui-play-on-load"), true),
            PauseWhenHidden = Flag(GetAttribute("data-urui-pause-when-hidden"), true),
            RestartOnShow = Flag(GetAttribute("data-urui-restart-on-show"), true)
        };
        void ApplySource(string key)
        {
            if (moduleDisposed())
            {
                throw new ObjectDisposedException(nameof(EffectModule));
            }
            if (key == currentSource)
            {
                return;
            }
            if (key.Length == 0)
            {
                Release();
                return;
            }
            var source = resources?.ResolveParticles(key);
            if (!source)
            {
                throw new InvalidOperationException("粒子源无法解析：" + key);
            }
            if (painter == null)
            {
                painter = new ParticlePainter(this, source, Options(), false);
            }
            else
            {
                painter.SetSource(source);
            }
            currentSource = key;
        }
        protected override void OnAttributeChanged(string name)
        {
            if (name == "src")
            {
                var requested = Source;
                Document.RunAfterEvent(() =>
                {
                    if (IsValid && Source == requested)
                    {
                        ApplySource(requested);
                    }
                });
            }
            else if (name is "data-urui-play-on-load" or "data-urui-pause-when-hidden" or "data-urui-restart-on-show")
            {
                if (painter != null)
                {
                    painter.SetOptions(Options());
                }
            }
        }
        protected override void OnUpdate() => painter?.UpdateVisibility();
        internal void Release()
        {
            painter?.Dispose();
            painter = null;
            currentSource = string.Empty;
        }
        protected override void OnInvalidated()
        {
            detached?.Dispose();
            detached = null;
            Release();
            invalidated(this);
        }
    }
}
