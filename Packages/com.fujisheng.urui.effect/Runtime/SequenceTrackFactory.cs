using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Animation;
using URUI.Elements;
using URUI.Effect.Elements;

namespace URUI.Effect
{
    /// <summary>统一序列的可选粒子轨道工厂。主线程创建；仅模拟自有副本，不修改 Prefab、共享时钟或业务 Update。</summary>
    public sealed class SequenceTrackFactory : ISequenceTrackFactory
    {
        readonly IEffectResourceResolver resources;
        public string Kind => "particles";
        public SequenceTrackFactory(IEffectResourceResolver resources)
        {
            this.resources = resources;
        }
        public ISequenceTrackDriver Create(Element target, IReadOnlyList<SequenceTrack> tracks)
        {
            if (!(target is ParticlesElement particles))
                throw new InvalidOperationException("粒子轨道必须绑定 urui-particles 节点。");
            var sources = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (var track in tracks)
            {
                if (track.Source.Length == 0)
                    continue;
                var source = resources?.ResolveParticles(track.Source);
                if (!source)
                    throw new InvalidOperationException("粒子轨道资源无法解析：" + track.Source);
                if (!sources.ContainsValue(source))
                    ParticlePainter.ValidateSequenceSource(source);
                sources[track.Id] = source;
            }
            return new Driver(particles, sources);
        }
        sealed class Driver : ISequenceTrackDriver
        {
            readonly ParticlesElement node;
            readonly Dictionary<string, GameObject> sources;
            IDisposable playback, source;
            string selected;
            bool disposed;
            internal Driver(ParticlesElement node, Dictionary<string, GameObject> sources)
            {
                this.node = node;
                this.sources = sources;
                playback = node.AcquireSequencePlayback();
            }
            public void Sample(SequenceTrack track, double localTime, bool active)
            {
                if (disposed || !node.IsValid)
                    throw new InvalidOperationException("粒子轨道节点已失效。");
                string next = active ? track.Id : null;
                if (selected != next)
                {
                    source?.Dispose();
                    source = null;
                    selected = null;
                    if (next != null && sources.TryGetValue(next, out var prefab))
                        source = node.OverrideSource(prefab);
                    selected = next;
                }
                if (!active)
                {
                    node.Seek(0);
                    node.Stop();
                    return;
                }
                node.Seek((float)(localTime * track.Speed), track.Seed);
            }
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                try
                {
                    source?.Dispose();
                }
                finally { source = null; playback?.Dispose(); playback = null; }
            }
        }
    }
}
