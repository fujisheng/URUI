using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using URUI.Internal.Interop;

namespace URUI.Internal.Resources
{
    // A context owns mappings, never caller textures. High-bit IDs are not GPU pointers
    // and never recycle within this managed domain, including after document reload.
    internal sealed class BorrowedTextureRegistry
    {
        internal const ulong ExternalBit = 1UL << 63;
        static ulong nextId = ExternalBit;
        readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        readonly Dictionary<Texture2D, Entry> sources = new Dictionary<Texture2D, Entry>();

        internal IEnumerable<Texture2D> Textures => sources.Keys;

        sealed class Entry
        {
            internal ulong id;
            internal Texture2D texture;
            internal int width, height;
            internal GraphicsFormat format;

            internal void Validate()
            {
                if (!texture)
                    throw new InvalidOperationException("URUI borrowed image was destroyed or unloaded while registered. Unload the document before releasing its images.");
                RequireFullResolution(texture);
                if (texture.width != width || texture.height != height || texture.graphicsFormat != format)
                    throw new InvalidOperationException("URUI borrowed image dimensions/format changed. Unload and reload the document after reinitializing its texture.");
            }
        }

        internal static void ValidateTexture(Texture2D texture)
        {
            if (!texture) throw new ArgumentException("URUI image needs a valid Texture2D");
            RequireFullResolution(texture);
            if (texture.width <= 0 || texture.height <= 0 || texture.width > 8192 || texture.height > 8192)
                throw new ArgumentException("URUI image dimensions must be between 1 and 8192");
            ValidateColorFormat(texture.graphicsFormat);
        }

        internal static void ValidateColorFormat(GraphicsFormat format)
        {
            if (format == GraphicsFormat.None || GraphicsFormatUtility.IsIntegerFormat(format)
                || GraphicsFormatUtility.IsDepthStencilFormat(format))
                throw new ArgumentException("URUI images require a sampled color texture, not integer/depth data", nameof(format));
        }

        static void RequireFullResolution(Texture2D texture)
        {
            // Integer texel fetch must address the actual full-resolution mip0. Do not
            // silently override caller quality/streaming settings or create a copy.
            if (texture.activeMipmapLimit != 0 || texture.streamingMipmaps)
                throw new ArgumentException("URUI borrowed images require resident full-resolution mip0 (no active mipmap limit or texture streaming). Import UI images without mip streaming/limits.");
        }

        internal static bool IsBorrowed(ulong id) => (id & ExternalBit) != 0;

        internal void Register(ImageBinding[] images)
        {
            if (entries.Count != 0) throw new InvalidOperationException("Image mappings already registered");
            foreach (var image in images ?? Array.Empty<ImageBinding>())
            {
                var texture = image.texture;
                if (string.IsNullOrEmpty(image.source) || !texture)
                    throw new ArgumentException("URUI image asset needs a source and texture");
                ValidateTexture(texture);
                int width = texture.width, height = texture.height;
                if ((image.expectedWidth > 0 && width != image.expectedWidth)
                    || (image.expectedHeight > 0 && height != image.expectedHeight))
                    throw new ArgumentException("URUI image dimensions differ from declared atlas/source pixels: " + image.source);
                if (!sources.TryGetValue(texture, out var entry))
                {
                    if (nextId == ulong.MaxValue) throw new InvalidOperationException("URUI image resource IDs exhausted");
                    entry = new Entry { id = ++nextId, texture = texture, width = width, height = height, format = texture.graphicsFormat };
                    entries.Add(entry.id, entry);
                    sources.Add(texture, entry);
                }
                if (NativeMethods.urui_register_external_texture(image.source, width, height, entry.id) != 1)
                    throw new InvalidOperationException("URUI borrowed image registration: " + NativeMethods.LastError());
            }
        }

        internal void Validate()
        {
            foreach (var entry in entries.Values) entry.Validate();
        }

        internal bool TryResolve(ulong id, out Texture2D texture)
        {
            texture = null;
            if (!IsBorrowed(id)) return false;
            if (!entries.TryGetValue(id, out var entry))
                throw new InvalidOperationException("URUI image belongs to an expired or different document generation");
            entry.Validate();
            texture = entry.texture;
            return true;
        }

        // Render plans retain their borrowed Texture references until existing GPU
        // retirement completes. Clearing registration transfers no destruction rights.
        internal void Clear()
        {
            entries.Clear();
            sources.Clear();
        }
    }
}
