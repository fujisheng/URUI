using System;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Effect.Rendering
{
    /// <summary>
    /// 按完整色键、Alpha 键、模式与色彩契约共享源算法的 256 像素 LUT。
    /// </summary>
    internal static class EffectGradientCache
    {
        internal sealed class Entry
        {
            internal Texture2D Texture;
            internal int References;
        }

        // 零 GC 结构化键：命中缓存时仅按哈希 + 原始 key 逐字段比对，不分配 MemoryStream/Base64。
        internal readonly struct GradientKey : IEquatable<GradientKey>
        {
            readonly int activeColorSpace;
            readonly int gradientColorSpace;
            readonly int mode;
            readonly ulong hash;
            readonly GradientColorKey[] colorKeys;
            readonly GradientAlphaKey[] alphaKeys;

            internal GradientKey(Gradient gradient, GradientColorKey[] colors, GradientAlphaKey[] alphas)
            {
                activeColorSpace = (int)QualitySettings.activeColorSpace;
                gradientColorSpace = (int)gradient.colorSpace;
                mode = (int)gradient.mode;
                colorKeys = colors;
                alphaKeys = alphas;
                hash = ComputeHash(activeColorSpace, gradientColorSpace, mode, colors, alphas);
            }

            static ulong ComputeHash(int activeSpace, int gradSpace, int gradMode, GradientColorKey[] colors, GradientAlphaKey[] alphas)
            {
                ulong h = 14695981039346656037UL;
                static void Mix(ref ulong state, uint word) => state = unchecked((state ^ word) * 1099511628211UL);
                Mix(ref h, (uint)activeSpace);
                Mix(ref h, (uint)gradSpace);
                Mix(ref h, (uint)gradMode);
                Mix(ref h, (uint)colors.Length);
                for (int i = 0; i < colors.Length; i++)
                {
                    var item = colors[i];
                    Mix(ref h, (uint)BitConverter.SingleToInt32Bits(item.time));
                    Mix(ref h, (uint)BitConverter.SingleToInt32Bits(item.color.r));
                    Mix(ref h, (uint)BitConverter.SingleToInt32Bits(item.color.g));
                    Mix(ref h, (uint)BitConverter.SingleToInt32Bits(item.color.b));
                    Mix(ref h, (uint)BitConverter.SingleToInt32Bits(item.color.a));
                }
                Mix(ref h, (uint)alphas.Length);
                for (int i = 0; i < alphas.Length; i++)
                {
                    var item = alphas[i];
                    Mix(ref h, (uint)BitConverter.SingleToInt32Bits(item.time));
                    Mix(ref h, (uint)BitConverter.SingleToInt32Bits(item.alpha));
                }
                return h;
            }

            public bool Equals(GradientKey other)
            {
                if (hash != other.hash || activeColorSpace != other.activeColorSpace || gradientColorSpace != other.gradientColorSpace || mode != other.mode)
                {
                    return false;
                }
                if (colorKeys.Length != other.colorKeys.Length || alphaKeys.Length != other.alphaKeys.Length)
                {
                    return false;
                }
                for (int i = 0; i < colorKeys.Length; i++)
                {
                    var a = colorKeys[i];
                    var b = other.colorKeys[i];
                    if (BitConverter.SingleToInt32Bits(a.time) != BitConverter.SingleToInt32Bits(b.time)
                        || BitConverter.SingleToInt32Bits(a.color.r) != BitConverter.SingleToInt32Bits(b.color.r)
                        || BitConverter.SingleToInt32Bits(a.color.g) != BitConverter.SingleToInt32Bits(b.color.g)
                        || BitConverter.SingleToInt32Bits(a.color.b) != BitConverter.SingleToInt32Bits(b.color.b)
                        || BitConverter.SingleToInt32Bits(a.color.a) != BitConverter.SingleToInt32Bits(b.color.a))
                    {
                        return false;
                    }
                }
                for (int i = 0; i < alphaKeys.Length; i++)
                {
                    var a = alphaKeys[i];
                    var b = other.alphaKeys[i];
                    if (BitConverter.SingleToInt32Bits(a.time) != BitConverter.SingleToInt32Bits(b.time)
                        || BitConverter.SingleToInt32Bits(a.alpha) != BitConverter.SingleToInt32Bits(b.alpha))
                    {
                        return false;
                    }
                }
                return true;
            }

            public override bool Equals(object obj) => obj is GradientKey other && Equals(other);
            public override int GetHashCode() => unchecked((int)(hash ^ (hash >> 32)));
        }

        static readonly Dictionary<GradientKey, Entry> entries = new();
        static readonly Color32[] uploadScratch = new Color32[256];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            foreach (var entry in entries.Values)
            {
                EffectObjectLifetime.Destroy(entry.Texture);
            }
            entries.Clear();
        }

        internal sealed class Lease : IDisposable
        {
            readonly GradientKey key;
            Entry entry;
            internal Texture2D Texture => entry.Texture;
            internal Lease(GradientKey key, Entry entry)
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
                    entries.Remove(key);
                    EffectObjectLifetime.Destroy(entry.Texture);
                }
                entry = null;
            }
        }

        internal static Lease Acquire(Gradient gradient)
        {
            if (gradient == null)
            {
                throw new ArgumentNullException(nameof(gradient));
            }
            var colors = gradient.colorKeys;
            var alphas = gradient.alphaKeys;
            var key = new GradientKey(gradient, colors, alphas);
            if (!entries.TryGetValue(key, out var entry))
            {
                var texture = new Texture2D(256, 1, TextureFormat.RGBA32, false, false)
                {
                    name = "URUI Effect Gradient",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 0,
                    filterMode = gradient.mode == GradientMode.Blend ? FilterMode.Bilinear : FilterMode.Point
                };
                try
                {
                    for (var i = 0; i < uploadScratch.Length; i++)
                    {
                        uploadScratch[i] = gradient.Evaluate(i / 255f);
                    }
                    texture.SetPixels32(uploadScratch);
                    texture.Apply(false, false);
                    entry = new Entry { Texture = texture };
                    entries.Add(key, entry);
                }
                catch
                {
                    EffectObjectLifetime.Destroy(texture);
                    throw;
                }
            }
            entry.References++;
            return new Lease(key, entry);
        }
    }
}
