using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine;

namespace URUI.Effect.Rendering
{
    /// <summary>
    /// 按固定 GPU 状态共享程序，不以每帧强度或颜色创建材质。
    /// </summary>
    internal static class EffectProgramCache
    {
        internal sealed class Entry
        {
            internal UIEffectProgram Program;
            internal int References;
        }
        static readonly Dictionary<(BlendMode, BlendMode, int, Texture, Texture, Texture, Texture, bool), Entry> programs = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            foreach (var entry in programs.Values)
            {
                entry.Program?.Dispose();
            }
            programs.Clear();
        }
        internal sealed class Lease : IDisposable
        {
            readonly (BlendMode, BlendMode, int, Texture, Texture, Texture, Texture, bool) key;
            Entry entry;
            internal UIEffectProgram Program => entry.Program;
            internal Lease((BlendMode, BlendMode, int, Texture, Texture, Texture, Texture, bool) key, Entry entry)
            {
                this.key = key;
                this.entry = entry;
            }
            public void Dispose()
            {
                if (entry == null)
                {
                    return;
                }
                if (--entry.References == 0)
                {
                    programs.Remove(key);
                    entry.Program.Dispose();
                }
                entry = null;
            }
        }
        internal static Lease Acquire(BlendMode source, BlendMode destination, int samplingQuality = 0, Texture transition = null, Texture detail = null, Texture gradient = null, Texture triangles = null, bool complexPixel = false)
        {
            var key = (source, destination, samplingQuality, transition, detail, gradient, triangles, complexPixel);
            if (!programs.TryGetValue(key, out var entry))
            {
                entry = new Entry { Program = new UIEffectProgram(source, destination, samplingQuality, transition, detail, gradient, triangles, complexPixel) };
                programs.Add(key, entry);
            }
            entry.References++;
            return new Lease(key, entry);
        }
    }
}
