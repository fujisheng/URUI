using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
namespace URUI.Internal.Rendering
{
    // Leases are returned only by the host's last-reference/GPU-fence retirement path.
    internal sealed class EffectTargetPool : IDisposable
    {
        // 淘汰优先级附在已有尺寸堆栈上，不为每张纹理另建节点。
        sealed class IdleTargets : Stack<RenderTexture>
        {
            internal ulong LastUsed;
            internal double Priority;
        }
        readonly Dictionary<(int, int, RenderTextureFormat, bool, RenderTextureMemoryless), IdleTargets> free = new();
        static int liveTargets;
        static int leasedTargets;
        static long liveColorBytes;
        static long leasedColorBytes;
        static long idleColorBytes;
        static long createdTargets;
        static long reuses;
        internal static URUI.Rendering.Effects.EffectTargetStatistics Statistics => new(liveTargets, leasedTargets, liveColorBytes, leasedColorBytes, idleColorBytes, createdTargets, reuses);
        bool disposed;
        readonly Dictionary<RenderTextureFormat, bool> supportedFormats = new();
        readonly Stack<Lease> idleLeases = new();
        const int MaximumFreeTargets = 2048;
        const long DefaultFreeBytes = 56L * 1024 * 1024;
        const long MaximumFreeBytes = 64L * 1024 * 1024;
        int freeTargets;
        long freeBytes;
        long freeByteBudget = DefaultFreeBytes;
        ulong useSerial;
        double evictionAge;
        static long Bytes(RenderTexture texture) => (texture.memorylessMode & RenderTextureMemoryless.Color) != 0 ? 0 :
            (long)texture.width * texture.height * GraphicsFormatUtility.GetBlockSize(texture.graphicsFormat);
        internal sealed class Lease : IDisposable
        {
            EffectTargetPool owner;
            bool linear;
            bool retired;
            int references;
            internal RenderTexture Texture
            {
                get;
                private set;
            }
            internal void RetainReference()
            {
                if (owner == null || retired)
                {
                    throw new ObjectDisposedException(nameof(Lease));
                }
                checked
                {
                    references++;
                }
            }
            internal void ReleaseReference()
            {
                if (references <= (retired ? 0 : 1))
                {
                    throw new InvalidOperationException("目标租约引用被重复释放。");
                }
                references--;
                if (references != 0)
                {
                    return;
                }
                var previous = owner;
                var texture = Texture;
                owner = null;
                Texture = null;
                previous.Return(texture, linear);
                if (!previous.disposed && previous.idleLeases.Count < 2048)
                {
                    previous.idleLeases.Push(this);
                }
            }
            internal void Open(EffectTargetPool owner, RenderTexture texture, bool linear)
            {
                if (this.owner != null)
                {
                    throw new InvalidOperationException("目标租约仍在使用。");
                }
                this.owner = owner;
                this.linear = linear;
                retired = false;
                references = 1;
                Texture = texture;
                leasedTargets++;
                leasedColorBytes += Bytes(texture);
            }
            public void Dispose()
            {
                if (owner == null || retired)
                {
                    return;
                }
                retired = true;
                // 所有者撤销后，编码、背景和临时目标也须等待每个消费提交结束。
                ReleaseReference();
            }
        }
        internal Lease Rent(int width, int height, RenderTextureFormat format, bool linear = false, RenderTextureMemoryless memoryless = RenderTextureMemoryless.None)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EffectTargetPool));
            }
            if (width < 1 || height < 1 || width > 8192 || height > 8192)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (!supportedFormats.TryGetValue(format, out var supported))
            {
                supported = SystemInfo.SupportsRenderTextureFormat(format);
                supportedFormats.Add(format, supported);
            }
            if (!supported)
            {
                throw new NotSupportedException("图层目标格式不受支持：" + format);
            }
            if (memoryless != RenderTextureMemoryless.None && (memoryless != RenderTextureMemoryless.Color || !SystemInfo.supportsMemorylessTextures))
            {
                throw new NotSupportedException("颜色附件的瞬时存储不受支持。");
            }
            var key = (width, height, format, linear, memoryless);
            if (free.TryGetValue(key, out var entries) && entries.Count > 0)
            {
                var reused = entries.Pop();
                var bytes = Bytes(reused);
                Touch(entries, bytes);
                freeTargets--;
                freeBytes -= bytes;
                idleColorBytes -= bytes;
                reuses++;
                return Open(reused, linear);
            }
            var texture = new RenderTexture(width, height, 0, format, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default)
            {
                name = "URUI pooled effect target",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                memorylessMode = memoryless
            };
            // 同尺寸目标也有独立身份，Frame Debugger 可据事件中的名称核对真实目标切换。
            texture.name = "URUI pooled effect target #" + texture.GetInstanceID();
            if (!texture.Create())
            {
                DestroyObject(texture);
                throw new InvalidOperationException("无法创建图层效果目标。");
            }
            liveTargets++;
            liveColorBytes += Bytes(texture);
            createdTargets++;
            return Open(texture, linear);
        }
        Lease Open(RenderTexture texture, bool linear)
        {
            var lease = idleLeases.Count > 0 ? idleLeases.Pop() : new Lease();
            lease.Open(this, texture, linear);
            return lease;
        }
        void Return(RenderTexture texture, bool linear)
        {
            var bytes = Bytes(texture);
            leasedTargets--;
            leasedColorBytes -= bytes;
            if (disposed)
            {
                Destroy(texture);
                return;
            }
            // 单体超过预算时不驱逐缓存；其余归还先淘汰冷目标，避免旧尺寸永久占满池。
            if (bytes > MaximumFreeBytes)
            {
                Destroy(texture);
                return;
            }
            // 大目标必须仍能和小附件一起复用；容量只随真实输入增长，
            // 不超过原上限，普通尺寸保持更小的空闲持有量。
            freeByteBudget = Math.Max(freeByteBudget,
                Math.Min(MaximumFreeBytes, bytes + MaximumFreeBytes - DefaultFreeBytes));
            var key = (texture.width, texture.height, texture.format, linear, texture.memorylessMode);
            var cached = free.TryGetValue(key, out var entries);
            if (cached)
            {
                Touch(entries, bytes);
            }
            while (freeTargets >= MaximumFreeTargets || freeBytes + bytes > freeByteBudget)
            {
                if (!EvictLeastValuable())
                {
                    Destroy(texture);
                    return;
                }
            }
            if (!cached)
            {
                if (free.Count >= MaximumFreeTargets)
                {
                    (int, int, RenderTextureFormat, bool, RenderTextureMemoryless) empty = default;
                    var found = false;
                    foreach (var pair in free)
                    {
                        if (pair.Value.Count == 0)
                        {
                            empty = pair.Key;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        Destroy(texture);
                        return;
                    }
                    free.Remove(empty);
                }
                free.Add(key, entries = new IdleTargets());
            }
            // 淘汰可能推进全局年龄，归还目标按新的年龄计分，不能立即变成旧项。
            Touch(entries, bytes);
            entries.Push(texture);
            freeTargets++;
            freeBytes += bytes;
            idleColorBytes += bytes;
        }
        void Touch(IdleTargets entries, long bytes)
        {
            if (evictionAge >= 1e12)
            {
                foreach (var pair in free)
                {
                    pair.Value.Priority = Math.Max(0, pair.Value.Priority - evictionAge);
                }
                evictionAge = 0;
            }
            if (useSerial == ulong.MaxValue)
            {
                foreach (var pair in free)
                {
                    pair.Value.LastUsed = 0;
                }
                useSerial = 0;
            }
            entries.LastUsed = ++useSerial;
            // GDS：相同重建成本下，优先保留更省空间的目标。零字节瞬时附件
            // 仍消耗对象名额，按每个名额的预算计费，避免无穷优先级。
            entries.Priority = evictionAge + (double)DefaultFreeBytes /
                Math.Max(bytes, DefaultFreeBytes / MaximumFreeTargets);
        }
        bool EvictLeastValuable()
        {
            IdleTargets oldest = null;
            foreach (var pair in free)
            {
                var candidate = pair.Value;
                if (candidate.Count > 0 && (oldest == null || candidate.Priority < oldest.Priority ||
                    (candidate.Priority == oldest.Priority && candidate.LastUsed < oldest.LastUsed)))
                {
                    oldest = candidate;
                }
            }
            if (oldest == null)
            {
                return false;
            }
            evictionAge = oldest.Priority;
            // 相同尺寸仍可服务完整提交窗口，容量压力只释放其中一张。
            var texture = oldest.Pop();
            var bytes = Bytes(texture);
            freeTargets--;
            freeBytes -= bytes;
            idleColorBytes -= bytes;
            Destroy(texture);
            return true;
        }
        static void Destroy(RenderTexture texture)
        {
            if (texture)
            {
                liveTargets--;
                liveColorBytes -= Bytes(texture);
                texture.Release();
                DestroyObject(texture);
            }
        }
        static void DestroyObject(RenderTexture texture)
        {
            // 退役可能跨越退出 PlayMode；fence 已完成后按实际执行模式销毁。
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            idleColorBytes -= freeBytes;
            foreach (var entries in free.Values)
            {
                foreach (var texture in entries)
                {
                    Destroy(texture);
                }
            }
            free.Clear();
            freeBytes = 0;
            freeTargets = 0;
            idleLeases.Clear();
            supportedFormats.Clear();
        }
    }
}
