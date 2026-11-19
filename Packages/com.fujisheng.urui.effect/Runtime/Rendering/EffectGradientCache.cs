using System;
using System.Collections.Generic;
using System.IO;
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
        static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        internal sealed class Lease : IDisposable
        {
            readonly string key;
            Entry entry;
            internal Texture2D Texture => entry.Texture;
            internal Lease(string key, Entry entry)
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
            string key;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((int)QualitySettings.activeColorSpace);
                    writer.Write((int)gradient.colorSpace);
                    writer.Write((int)gradient.mode);
                    var colors = gradient.colorKeys;
                    var alphas = gradient.alphaKeys;
                    writer.Write(colors.Length);
                    foreach (var item in colors)
                    {
                        writer.Write(item.time);
                        writer.Write(item.color.r);
                        writer.Write(item.color.g);
                        writer.Write(item.color.b);
                        writer.Write(item.color.a);
                    }
                    writer.Write(alphas.Length);
                    foreach (var item in alphas)
                    {
                        writer.Write(item.time);
                        writer.Write(item.alpha);
                    }
                    key = Convert.ToBase64String(stream.ToArray());
                }
            }
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
                    var pixels = new Color32[256];
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = gradient.Evaluate(i / 255f);
                    }
                    texture.SetPixels32(pixels);
                    texture.Apply();
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
