using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace URUI.Effect.Rendering
{
    /// <summary>按固定 GPU 状态共享程序，不以每帧强度或颜色创建材质。</summary>
    internal static class EffectProgramCache
    {
        internal sealed class Entry
        {
            internal UiEffectProgram Program;
            internal int References;
        }
        static readonly Dictionary<(BlendMode, BlendMode, int), Entry> programs = new();
        internal sealed class Lease : IDisposable
        {
            readonly (BlendMode, BlendMode, int) key;
            Entry entry;
            internal UiEffectProgram Program => entry.Program;
            internal Lease((BlendMode, BlendMode, int) key, Entry entry) { this.key = key; this.entry = entry; }
            public void Dispose()
            {
                if (entry == null) { return; }
                if (--entry.References == 0) { programs.Remove(key); entry.Program.Dispose(); }
                entry = null;
            }
        }
        internal static Lease Acquire(BlendMode source, BlendMode destination, int samplingQuality = 0)
        {
            var key = (source, destination, samplingQuality);
            if (!programs.TryGetValue(key, out var entry))
            { entry = new Entry { Program = new UiEffectProgram(source, destination, samplingQuality) }; programs.Add(key, entry); }
            entry.References++; return new Lease(key, entry);
        }
    }
}
