using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Shaders;
using URUI.Resources;

namespace URUI.Internal.Resources
{
    // A context owns mappings, never caller textures. High-bit IDs are not GPU pointers
    // and never recycle within this managed domain, including after document reload.
    internal sealed class BorrowedTextureRegistry
    {
        internal const ulong ExternalBit = 1UL << 63;
        static ulong nextId = ExternalBit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            nextId = ExternalBit;
        }
        readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        readonly Dictionary<Texture, Entry> sources = new Dictionary<Texture, Entry>();

        readonly Dictionary<ulong, HashSet<Entry>> documents = new();
        readonly Dictionary<Texture, (ulong Identity, int NativeLeases)> sourceIdentities = new();
        ulong nextSourceIdentity;
        ulong validationSerial;
        bool inValidatedFrame;
        internal IEnumerable<Texture> Textures
        {
            get
            {
                foreach (var entry in entries.Values)
                {
                    yield return entry.texture;
                }
            }
        }

        sealed class Entry
        {
            internal ulong id;
            internal Texture texture;
            internal int width;
            internal int height;
            internal GraphicsFormat format;
            internal FilterMode filterMode;
            internal TextureWrapMode wrapMode;
            internal Vector4 sourceInfo;
            internal bool isLive;
            internal bool isUNorm;
            internal bool metaInitialized;
            internal ulong validatedSerial;
            internal ImageLease lease;
            internal bool nativeHeld;
            internal int pins;
            internal int documents;

            internal void InitMeta()
            {
                isLive = texture is RenderTexture;
                isUNorm = GraphicsFormatUtility.IsUNormFormat(GraphicsFormatUtility.GetLinearFormat(format));
                filterMode = texture.filterMode;
                wrapMode = texture.wrapMode;
                sourceInfo = TextureShaderState.ComputeBorrowedSourceInfo(texture, width, height, format, filterMode, wrapMode);
                metaInitialized = true;
            }

            internal void Validate(ulong serial = 0)
            {
                if (!texture)
                {
                    throw new InvalidOperationException("URUI borrowed image was destroyed or unloaded while registered. Unload the document before releasing its images.");
                }
                RequireFullResolution(texture);
                if (texture.width != width || texture.height != height || texture.graphicsFormat != format)
                {
                    throw new InvalidOperationException("URUI borrowed image dimensions/format changed. Unload and reload the document after reinitializing its texture.");
                }
                var fm = texture.filterMode;
                var wm = texture.wrapMode;
                if (!metaInitialized || fm != filterMode || wm != wrapMode)
                {
                    filterMode = fm;
                    wrapMode = wm;
                    isLive = texture is RenderTexture;
                    isUNorm = GraphicsFormatUtility.IsUNormFormat(GraphicsFormatUtility.GetLinearFormat(format));
                    sourceInfo = TextureShaderState.ComputeBorrowedSourceInfo(texture, width, height, format, fm, wm);
                    metaInitialized = true;
                }
                if (serial != 0)
                {
                    validatedSerial = serial;
                }
            }
        }

        internal static void ValidateTexture(Texture texture)
        {
            if (!texture)
            {
                throw new ArgumentException("URUI image needs a valid color texture");
            }
            RequireFullResolution(texture);
            if (texture.width <= 0 || texture.height <= 0 || texture.width > 8192 || texture.height > 8192)
            {
                throw new ArgumentException("URUI image dimensions must be between 1 and 8192");
            }
            ValidateColorFormat(texture.graphicsFormat);
        }

        internal static void ValidateColorFormat(GraphicsFormat format)
        {
            if (format == GraphicsFormat.None || GraphicsFormatUtility.IsIntegerFormat(format)
                || GraphicsFormatUtility.IsDepthStencilFormat(format))
            {
                throw new ArgumentException("URUI images require a sampled color texture, not integer/depth data", nameof(format));
            }
        }

        static void RequireFullResolution(Texture texture)
        {
            if (texture is RenderTexture target)
            {
                if (!target.IsCreated() || target.dimension != UnityEngine.Rendering.TextureDimension.Tex2D ||
                    target.antiAliasing != 1 || target.memorylessMode != RenderTextureMemoryless.None)
                {
                    throw new ArgumentException("URUI RenderTexture needs a created, persistent, single-sample 2D color target.");
                }
                return;
            }
            if (!(texture is Texture2D image))
            {
                throw new ArgumentException("URUI images support Texture2D and RenderTexture only.");
            }
            // Integer texel fetch must address the actual full-resolution mip0. Do not
            // silently override caller quality/streaming settings or create a copy.
            if (image.activeMipmapLimit != 0 || image.streamingMipmaps)
            {
                throw new ArgumentException("URUI borrowed images require resident full-resolution mip0 (no active mipmap limit or texture streaming). Import UI images without mip streaming/limits.");
            }
        }

        internal static bool IsBorrowed(ulong id) => (id & ExternalBit) != 0;

        internal void Register(ulong surface, ulong owner, ImageBinding[] images)
        {
            if (documents.ContainsKey(owner))
            {
                throw new InvalidOperationException("Document image mappings already registered");
            }
            var held = new HashSet<Entry>();
            documents.Add(owner, held);
            foreach (var image in images ?? Array.Empty<ImageBinding>())
            {
                var texture = image.texture;
                if (string.IsNullOrEmpty(image.source) || !texture)
                {
                    throw new ArgumentException("URUI image asset needs a source and texture");
                }
                ValidateTexture(texture);
                int width = texture.width;
                int height = texture.height;
                if ((image.expectedWidth > 0 && width != image.expectedWidth)
                    || (image.expectedHeight > 0 && height != image.expectedHeight))
                {
                    throw new ArgumentException("URUI image dimensions differ from declared atlas/source pixels: " + image.source);
                }
                if (!sources.TryGetValue(texture, out var entry))
                {
                    if (nextId == ulong.MaxValue)
                    {
                        throw new InvalidOperationException("URUI image resource IDs exhausted");
                    }
                    entry = new Entry
                    {
                        id = ++nextId,
                        texture = texture,
                        width = width,
                        height = height,
                        format = texture.graphicsFormat
                    };
                    entry.InitMeta();
                    entries.Add(entry.id, entry);
                    sources.Add(texture, entry);
                }
                if (held.Add(entry))
                {
                    entry.documents++;
                }
                if (NativeMethods.urui_document_register_external_texture(surface, owner, image.source, width, height, entry.id) != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("URUI borrowed image registration: " + NativeMethods.LastError(surface));
                }
            }
        }

        internal void Unregister(ulong owner)
        {
            if (!documents.Remove(owner, out var held))
            {
                return;
            }
            foreach (var entry in held)
            {
                if (--entry.documents != 0)
                {
                    continue;
                }
                entries.Remove(entry.id);
                sources.Remove(entry.texture);
            }
        }

        internal void Validate()
        {
            var serial = unchecked(++validationSerial);
            if (serial == 0)
            {
                serial = ++validationSerial;
                foreach (var entry in entries.Values)
                {
                    entry.validatedSerial = 0;
                }
            }
            foreach (var entry in entries.Values)
            {
                entry.Validate(serial);
            }
            inValidatedFrame = true;
        }

        internal void EndFrameValidation()
        {
            inValidatedFrame = false;
        }

        internal bool TryResolve(ulong id, out Texture texture)
        {
            if (!TryResolveValidated(id, out var entry))
            {
                texture = null;
                return false;
            }
            texture = entry.texture;
            return true;
        }

        bool TryResolveValidated(ulong id, out Entry resolved)
        {
            resolved = null;
            if (!IsBorrowed(id))
            {
                return false;
            }
            if (!entries.TryGetValue(id, out var entry))
            {
                throw new InvalidOperationException("URUI image belongs to an expired or different document generation");
            }
            if (!inValidatedFrame || entry.validatedSerial != validationSerial)
            {
                entry.Validate(inValidatedFrame ? validationSerial : 0);
            }
            resolved = entry;
            return true;
        }

        internal ulong RegisterResolved(ImageLease lease, out ulong sourceIdentity)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }
            var texture = lease.Texture;
            ValidateTexture(texture);
            if (nextId == ulong.MaxValue)
            {
                throw new InvalidOperationException("URUI image resource IDs exhausted");
            }
            if (!sourceIdentities.TryGetValue(texture, out var source))
            {
                if (nextSourceIdentity == ulong.MaxValue)
                {
                    throw new InvalidOperationException("URUI image source identities exhausted");
                }
                source.Identity = ++nextSourceIdentity;
            }
            source.NativeLeases = checked(source.NativeLeases + 1);
            var entry = new Entry
            {
                id = ++nextId,
                texture = texture,
                width = texture.width,
                height = texture.height,
                format = texture.graphicsFormat,
                lease = lease,
                nativeHeld = true
            };
            entry.InitMeta();
            entries.Add(entry.id, entry);
            try
            {
                sourceIdentities[texture] = source;
            }
            catch
            {
                entries.Remove(entry.id);
                throw;
            }
            sourceIdentity = source.Identity;
            return entry.id;
        }

        // SRP 重放可能沿用旧录制；提交自身必须持有租约，不能依赖录制路径的临时 pin。
        internal sealed class SubmissionPins
        {
            readonly List<Entry> pinned = new();
            readonly HashSet<ulong> ids = new();
            BorrowedTextureRegistry owner;
            internal int Count => pinned.Count;
            internal bool AcquireOne(BorrowedTextureRegistry registry, ulong id, out Texture texture)
                => AcquireOneValidated(registry, id, out texture, out _, out _, out _);

            internal bool AcquireOneValidated(BorrowedTextureRegistry registry, ulong id, out Texture texture, out Vector4 sourceInfo, out bool isLive, out bool isUNorm)
            {
                if (!registry.TryResolveValidated(id, out var entry))
                {
                    texture = null;
                    sourceInfo = default;
                    isLive = false;
                    isUNorm = false;
                    return false;
                }
                texture = entry.texture;
                sourceInfo = entry.sourceInfo;
                isLive = entry.isLive;
                isUNorm = entry.isUNorm;
                if (entry.lease == null)
                {
                    return true;
                }
                if (owner != null && !ReferenceEquals(owner, registry))
                {
                    throw new InvalidOperationException("图片 pin 不能跨注册表使用。");
                }
                owner = registry;
                if (!ids.Add(id))
                {
                    return true;
                }
                try
                {
                    pinned.Add(entry);
                    entry.pins++;
                }
                catch
                {
                    ids.Remove(id);
                    throw;
                }
                return true;
            }
            internal void Acquire(BorrowedTextureRegistry registry)
            {
                if (owner != null)
                {
                    throw new InvalidOperationException("URUI 提交仍持有上一批图片。");
                }
                try
                {
                    Append(registry);
                }
                catch
                {
                    Release();
                    throw;
                }
            }
            // 即时相机队列上的后续 fence 覆盖较早提交；只追加尚未持有的资源版本。
            internal void Append(BorrowedTextureRegistry registry)
            {
                if (owner != null && !ReferenceEquals(owner, registry))
                {
                    throw new InvalidOperationException("图片 pin 不能跨注册表使用。");
                }
                owner = registry;
                foreach (var entry in registry.entries.Values)
                {
                    // 原生已释放的图片只属于既有录制/提交；未来提交不能再次延长其寿命。
                    if (entry.lease == null || !entry.nativeHeld || !ids.Add(entry.id))
                    {
                        continue;
                    }
                    try
                    {
                        if (!registry.inValidatedFrame || entry.validatedSerial != registry.validationSerial)
                        {
                            entry.Validate(registry.inValidatedFrame ? registry.validationSerial : 0);
                        }
                        pinned.Add(entry);
                        entry.pins++;
                    }
                    catch
                    {
                        ids.Remove(entry.id);
                        throw;
                    }
                }
            }
            internal void Release()
            {
                var previous = owner;
                if (previous == null)
                {
                    return;
                }
                owner = null;
                foreach (var entry in pinned)
                {
                    entry.pins--;
                    try
                    {
                        previous.Collect(entry);
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                    }
                }
                pinned.Clear();
                ids.Clear();
            }
        }

        internal void ReleaseResolved(ulong id)
        {
            if (!entries.TryGetValue(id, out var entry) || !entry.nativeHeld)
            {
                return;
            }
            entry.nativeHeld = false;
            // URI 声明只需要对仍被原生持有的来源维持相同身份。在途提交继续持有 Entry。
            var texture = entry.texture;
            var source = sourceIdentities[texture];
            if (--source.NativeLeases == 0)
            {
                sourceIdentities.Remove(texture);
            }
            else
            {
                sourceIdentities[texture] = source;
            }
            Collect(entry);
        }

        void Collect(Entry entry)
        {
            if (entry.lease == null || entry.nativeHeld || entry.pins != 0)
            {
                return;
            }
            entries.Remove(entry.id);
            var lease = entry.lease;
            entry.lease = null;
            try
            {
                lease.Dispose();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
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
            sources.Clear();
            documents.Clear();
            sourceIdentities.Clear();
        }
    }
}
