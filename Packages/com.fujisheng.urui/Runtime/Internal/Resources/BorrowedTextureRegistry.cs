using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using URUI.Internal.Interop;
using URUI.Resources;

namespace URUI.Internal.Resources
{
    // A context owns mappings, never caller textures. High-bit IDs are not GPU pointers
    // and never recycle within this managed domain, including after document reload.
    internal sealed class BorrowedTextureRegistry
    {
        internal const ulong ExternalBit = 1UL << 63;
        static ulong nextId = ExternalBit;
        readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        readonly Dictionary<Texture, Entry> sources = new Dictionary<Texture, Entry>();

        readonly Dictionary<ulong, HashSet<Entry>> documents = new();
        readonly Dictionary<Texture, ulong> sourceIdentities = new();
        ulong nextSourceIdentity;
        internal ulong SourceIdentity(Texture texture)
        {
            if (!sourceIdentities.TryGetValue(texture, out var identity))
            {
                if (nextSourceIdentity == ulong.MaxValue) throw new InvalidOperationException("URUI image source identities exhausted");
                sourceIdentities.Add(texture, identity = ++nextSourceIdentity);
            }
            return identity;
        }
        internal IEnumerable<Texture> Textures
        {
            get { foreach (var entry in entries.Values) yield return entry.texture; }
        }

        sealed class Entry
        {
            internal ulong id;
            internal Texture texture;
            internal int width, height;
            internal GraphicsFormat format;
            internal ImageLease lease;
            internal bool nativeHeld;
            internal int pins, documents;

            internal void Validate()
            {
                if (!texture)
                    throw new InvalidOperationException("URUI borrowed image was destroyed or unloaded while registered. Unload the document before releasing its images.");
                RequireFullResolution(texture);
                if (texture.width != width || texture.height != height || texture.graphicsFormat != format)
                    throw new InvalidOperationException("URUI borrowed image dimensions/format changed. Unload and reload the document after reinitializing its texture.");
            }
        }

        internal static void ValidateTexture(Texture texture)
        {
            if (!texture) throw new ArgumentException("URUI image needs a valid color texture");
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

        static void RequireFullResolution(Texture texture)
        {
            if (texture is RenderTexture target)
            {
                if (!target.IsCreated() || target.dimension != UnityEngine.Rendering.TextureDimension.Tex2D ||
                    target.antiAliasing != 1 || target.memorylessMode != RenderTextureMemoryless.None)
                    throw new ArgumentException("URUI RenderTexture needs a created, persistent, single-sample 2D color target.");
                return;
            }
            if (!(texture is Texture2D image))
                throw new ArgumentException("URUI images support Texture2D and RenderTexture only.");
            // Integer texel fetch must address the actual full-resolution mip0. Do not
            // silently override caller quality/streaming settings or create a copy.
            if (image.activeMipmapLimit != 0 || image.streamingMipmaps)
                throw new ArgumentException("URUI borrowed images require resident full-resolution mip0 (no active mipmap limit or texture streaming). Import UI images without mip streaming/limits.");
        }

        internal static bool IsBorrowed(ulong id) => (id & ExternalBit) != 0;

        internal void Register(ulong owner, ImageBinding[] images)
        {
            if (documents.ContainsKey(owner)) throw new InvalidOperationException("Document image mappings already registered");
            var held = new HashSet<Entry>();
            documents.Add(owner, held);
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
                if (held.Add(entry)) entry.documents++;
                if (NativeMethods.urui_document_register_external_texture(owner, image.source, width, height, entry.id) != 1)
                    throw new InvalidOperationException("URUI borrowed image registration: " + NativeMethods.LastError());
            }
        }

        internal void Unregister(ulong owner)
        {
            if (!documents.Remove(owner, out var held)) return;
            foreach (var entry in held)
            {
                if (--entry.documents != 0) continue;
                entries.Remove(entry.id); sources.Remove(entry.texture);
            }
        }

        internal void Validate()
        {
            foreach (var entry in entries.Values) entry.Validate();
        }

        internal bool TryResolve(ulong id, out Texture texture)
        {
            texture = null;
            if (!IsBorrowed(id)) return false;
            if (!entries.TryGetValue(id, out var entry))
                throw new InvalidOperationException("URUI image belongs to an expired or different document generation");
            entry.Validate();
            texture = entry.texture;
            return true;
        }

        internal ulong RegisterResolved(ImageLease lease)
        {
            if (lease == null) throw new ArgumentNullException(nameof(lease));
            var texture = lease.Texture;
            ValidateTexture(texture);
            if (nextId == ulong.MaxValue) throw new InvalidOperationException("URUI image resource IDs exhausted");
            var entry = new Entry { id = ++nextId, texture = texture, width = texture.width, height = texture.height,
                format = texture.graphicsFormat, lease = lease, nativeHeld = true };
            entries.Add(entry.id, entry);
            return entry.id;
        }

        // SRP 重放可能沿用旧录制；提交自身必须持有租约，不能依赖录制路径的临时 pin。
        internal sealed class SubmissionPins
        {
            readonly List<Entry> pinned=new();
            readonly HashSet<ulong> ids=new();
            BorrowedTextureRegistry owner;
            internal int Count=>pinned.Count;
            internal bool AcquireOne(BorrowedTextureRegistry registry,ulong id,out Texture texture)
            {
                if(!registry.TryResolve(id,out texture))return false;
                var entry=registry.entries[id];
                if(entry.lease==null)return true;
                if(owner!=null&&!ReferenceEquals(owner,registry))throw new InvalidOperationException("图片 pin 不能跨注册表使用。");
                owner=registry;
                if(!ids.Add(id))return true;
                try{pinned.Add(entry);entry.pins++;}
                catch{ids.Remove(id);throw;}
                return true;
            }
            internal void Acquire(BorrowedTextureRegistry registry)
            {
                if(owner!=null)throw new InvalidOperationException("URUI 提交仍持有上一批图片。");
                owner=registry;
                try
                {
                    foreach(var entry in registry.entries.Values)if(entry.lease!=null)
                    {entry.Validate();pinned.Add(entry);entry.pins++;}
                }
                catch{Release();throw;}
            }
            internal void Release()
            {
                var previous=owner;if(previous==null)return;owner=null;
                foreach(var entry in pinned)
                {
                    entry.pins--;
                    try{previous.Collect(entry);}catch(Exception error){Debug.LogException(error);}
                }
                pinned.Clear();
                ids.Clear();
            }
        }

        internal void ReleaseResolved(ulong id)
        {
            if (!entries.TryGetValue(id, out var entry) || entry.lease == null) return;
            entry.nativeHeld = false;
            Collect(entry);
        }

        void Collect(Entry entry)
        {
            if (entry.lease == null || entry.nativeHeld || entry.pins != 0) return;
            entries.Remove(entry.id);
            var lease = entry.lease;
            entry.lease = null;
            try { lease.Dispose(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        // Render plans retain their borrowed Texture references until existing GPU
        // retirement completes. Clearing registration transfers no destruction rights.
        internal void Clear()
        {
            // pin 直接持有 Entry；清空映射后，它仍能在 GPU fence 完成时归还租约。
            foreach (var entry in new List<Entry>(entries.Values))
            {
                entry.nativeHeld = false;
                Collect(entry);
            }
            entries.Clear();
            sources.Clear(); documents.Clear(); sourceIdentities.Clear();
        }
    }
}
