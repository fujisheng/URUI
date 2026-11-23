using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering
{
    /// <summary>
    /// 参数纹理版本由录制引用和真实GPU提交引用共同保护。
    /// 支持尾部行版本归零和连续脏行区间合并上传，避免逐行创建 Texture2D 与逐行 CopyTexture。
    /// </summary>
    internal sealed unsafe class RenderParameterPages : IDisposable
    {
        const int Width = 256;
        const int MaximumPages = 9;
        const GraphicsFormat Format = GraphicsFormat.R32G32B32A32_SFloat;
        const long Budget = 48L * 1024 * 1024;
        readonly Page[] pages = new Page[MaximumPages];
        int count;
        long retainedPayloadBytes;
        bool disposed;

        internal long ParameterUploadBytes
        {
            get;
            private set;
        }
        internal long ParameterCopyBytes
        {
            get;
            private set;
        }
        internal long RetainedPayloadBytes => retainedPayloadBytes;
        internal Page Current
        {
            get;
            private set;
        }

        internal sealed class Page
        {
            const int SpanRingCapacity = 3;
            readonly RenderParameterPages owner;
            readonly Texture2D[] spanUploads = new Texture2D[SpanRingCapacity];
            readonly int[] spanUploadHeights = new int[SpanRingCapacity];
            readonly int[] spanLastUsedFrames = { int.MinValue, int.MinValue, int.MinValue };
            readonly int[] spanLastFirstRows = { -1, -1, -1 };
            readonly int[] spanLastRowCounts = { 0, 0, 0 };
            int nextSpanSlot;
            ulong[] rowVersions;
            int[] changedRows;
            internal bool Recording;
            int submissions;
            internal ulong Version;
            internal uint ParameterCount;
            internal uint ExtraParameterCount;

            internal Texture2D Texture
            {
                get;
                private set;
            }
            long payloadBytes;
            internal bool Free => !Recording && submissions == 0;
            internal string FailureState => "v=" + Version + ",refs=" + submissions + ",record=" + Recording;

            internal Page(RenderParameterPages owner)
            {
                this.owner = owner;
            }

            internal void RetainSubmission()
            {
                if (owner.disposed || !Texture)
                {
                    throw new ObjectDisposedException(nameof(Page));
                }
                checked
                {
                    submissions++;
                }
            }

            internal void ReleaseSubmission()
            {
                if (submissions == 0)
                {
                    throw new InvalidOperationException("参数提交引用被重复释放。");
                }
                submissions--;
                if (submissions == 0)
                {
                    ClearSpanInFlightMarkers();
                }
                if (owner.disposed && Free)
                {
                    ReleasePayload();
                }
            }

            void ClearSpanInFlightMarkers()
            {
                for (int s = 0; s < SpanRingCapacity; s++)
                {
                    spanLastUsedFrames[s] = int.MinValue;
                    spanLastFirstRows[s] = -1;
                    spanLastRowCounts[s] = 0;
                }
                nextSpanSlot = 0;
            }

            internal void ReleasePayload()
            {
                if (!Free)
                {
                    return;
                }
                if (Texture)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(Texture);
                    Texture = null;
                }
                for (int s = 0; s < SpanRingCapacity; s++)
                {
                    if (spanUploads[s])
                    {
                        URUI.Resources.GpuRetirement.DestroyOwnedObject(spanUploads[s]);
                        spanUploads[s] = null;
                        spanUploadHeights[s] = 0;
                    }
                }
                ClearSpanInFlightMarkers();
                rowVersions = null;
                changedRows = null;
                owner.retainedPayloadBytes -= payloadBytes;
                payloadBytes = 0;
                Version = 0;
                ParameterCount = ExtraParameterCount = 0;
            }

            static void CopyInput(byte* destination, long offset, long count, in RenderPlanPacket packet)
            {
                var baseBytes = (long)packet.ParameterCount * 128;
                var baseCount = offset < baseBytes ? Math.Min(count, baseBytes - offset) : 0;
                if (baseCount > 0)
                {
                    UnsafeUtility.MemCpy(destination, (byte*)packet.Parameters + offset, baseCount);
                }
                if (count > baseCount)
                {
                    UnsafeUtility.MemCpy(destination + baseCount, (byte*)packet.ExtraParameters + offset + baseCount - baseBytes, count - baseCount);
                }
            }

            Texture2D EnsureSpanUpload(int slot, int requiredHeight)
            {
                if (spanUploads[slot] && spanUploadHeights[slot] >= requiredHeight)
                {
                    return spanUploads[slot];
                }
                int targetHeight = Math.Max(requiredHeight, spanUploadHeights[slot] > 0 ? spanUploadHeights[slot] * 2 : 1);
                if (Texture && targetHeight > Texture.height)
                {
                    targetHeight = Texture.height;
                }
                long oldBytes = spanUploads[slot] ? (long)Width * spanUploadHeights[slot] * 16 * 2 : 0;
                long newBytes = (long)Width * targetHeight * 16 * 2;
                long delta = newBytes - oldBytes;
                if (delta > Budget - owner.retainedPayloadBytes)
                {
                    throw new InvalidOperationException("局部参数上传超过48MiB在途数据预算。");
                }
                var next = new Texture2D(Width, targetHeight, Format, TextureCreationFlags.None)
                {
                    name = "URUI parameter span",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (spanUploads[slot])
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(spanUploads[slot]);
                }
                spanUploads[slot] = next;
                spanUploadHeights[slot] = targetHeight;
                owner.retainedPayloadBytes += delta;
                payloadBytes += delta;
                return next;
            }

            int TryAcquireSpanSlot(int firstRow, int rowCount, int currentFrame)
            {
                // Repeated single-row updates targeting the exact same destination row can safely reuse
                // that slot without allocating additional staging textures or risking cross-row clobbering.
                if (rowCount == 1)
                {
                    for (int slot = 0; slot < SpanRingCapacity; slot++)
                    {
                        if (spanLastRowCounts[slot] == 1 && spanLastFirstRows[slot] == firstRow && spanUploads[slot])
                        {
                            return slot;
                        }
                    }
                }
                // Otherwise pick a slot whose prior CopyTexture is outside the multithreaded render-thread window
                // or already retired by a GPU fence.
                for (int offset = 0; offset < SpanRingCapacity; offset++)
                {
                    int slot = (nextSpanSlot + offset) % SpanRingCapacity;
                    bool inFlight = spanLastUsedFrames[slot] != int.MinValue && Math.Abs(currentFrame - spanLastUsedFrames[slot]) < 3;
                    if (!inFlight)
                    {
                        nextSpanSlot = (slot + 1) % SpanRingCapacity;
                        return slot;
                    }
                }
                return -1;
            }

            internal void Upload(in RenderPlanPacket packet)
            {
                if (!Free)
                {
                    throw new InvalidOperationException("不能改写已录制或在途的参数版本。");
                }
                var rows = Math.Max(1, checked(((int)packet.ParameterCount * 8 + (int)packet.ExtraParameterCount + 255) / 256));
                if (!Texture || Texture.height < rows)
                {
                    ReleasePayload();
                    if (!SystemInfo.IsFormatSupported(Format, GraphicsFormatUsage.Sample) || rows > SystemInfo.maxTextureSize)
                    {
                        throw new NotSupportedException("当前图形后端不支持URUI参数纹理契约。");
                    }
                    var bytes = (long)Width * rows * 16 * 2 + rows * 12L;
                    if (bytes > Budget - owner.retainedPayloadBytes)
                    {
                        throw new InvalidOperationException("参数版本超过48MiB常驻及在途数据预算。");
                    }
                    owner.retainedPayloadBytes += bytes;
                    payloadBytes = bytes;
                    try
                    {
                        rowVersions = new ulong[rows];
                        changedRows = new int[rows];
                        Texture = new Texture2D(Width, rows, Format, TextureCreationFlags.None)
                        {
                            name = "URUI render parameters",
                            filterMode = FilterMode.Point,
                            wrapMode = TextureWrapMode.Clamp,
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }
                    catch
                    {
                        ReleasePayload();
                        throw;
                    }
                }
                var versions = (ulong*)packet.ParameterPages;
                var changed = 0;
                for (var row = 0; row < rows; row++)
                {
                    if (Version == 0 || rowVersions[row] != versions[row])
                    {
                        changedRows[changed++] = row;
                    }
                }
                var initialize = Version == 0;
                // A failed upload must never be exposed under the old version.
                Version = 0;
                var used = checked((long)packet.ParameterCount * 128 + (long)packet.ExtraParameterCount * 16);
                var currentFrame = Time.frameCount;
                var spanSlot = (!initialize && changed > 0 && changed * 2 < Texture.height && (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0)
                    ? TryAcquireSpanSlot(changedRows[0], changed, currentFrame)
                    : -1;
                if (initialize || (changed > 0 && spanSlot < 0))
                {
                    var pixels = Texture.GetPixelData<Vector4>(0);
                    var destination = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(pixels);
                    var capacity = (long)pixels.Length * 16;
                    CopyInput(destination, 0, used, packet);
                    UnsafeUtility.MemClear(destination + used, capacity - used);
                    Texture.Apply(false);
                    owner.ParameterUploadBytes += capacity;
                    ClearSpanInFlightMarkers();
                    // Clear stale tail row hashes whenever MemClear zeroes rows >= rows.
                    if (rows < rowVersions.Length)
                    {
                        Array.Clear(rowVersions, rows, rowVersions.Length - rows);
                    }
                }
                else if (changed > 0)
                {
                    // Pack all dirty spans into disjoint rows of a dedicated staging texture, Apply once,
                    // then CopyTexture each span so multithreaded backends never observe clobbered staging data.
                    const int stride = Width * 16;
                    var spanUpload = EnsureSpanUpload(spanSlot, changed);
                    var stagingPixels = spanUpload.GetPixelData<Vector4>(0);
                    var destination = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(stagingPixels);
                    int srcY = 0;
                    for (int i = 0; i < changed;)
                    {
                        int startRow = changedRows[i];
                        int j = i + 1;
                        while (j < changed && changedRows[j] == changedRows[j - 1] + 1)
                        {
                            j++;
                        }
                        int spanHeight = j - i;
                        long byteOffset = (long)startRow * stride;
                        long spanBytes = (long)spanHeight * stride;
                        long bytesToCopy = Math.Max(0, Math.Min(spanBytes, used - byteOffset));
                        byte* spanDst = destination + (long)srcY * stride;
                        if (bytesToCopy > 0)
                        {
                            CopyInput(spanDst, byteOffset, bytesToCopy, packet);
                        }
                        if (spanBytes > bytesToCopy)
                        {
                            UnsafeUtility.MemClear(spanDst + bytesToCopy, spanBytes - bytesToCopy);
                        }
                        srcY += spanHeight;
                        i = j;
                    }
                    spanUpload.Apply(false);
                    owner.ParameterUploadBytes += (long)spanUploadHeights[spanSlot] * stride;
                    srcY = 0;
                    for (int i = 0; i < changed;)
                    {
                        int startRow = changedRows[i];
                        int j = i + 1;
                        while (j < changed && changedRows[j] == changedRows[j - 1] + 1)
                        {
                            j++;
                        }
                        int spanHeight = j - i;
                        Graphics.CopyTexture(spanUpload, 0, 0, 0, srcY, Width, spanHeight, Texture, 0, 0, 0, startRow);
                        owner.ParameterCopyBytes += (long)spanHeight * stride;
                        srcY += spanHeight;
                        i = j;
                    }
                    spanLastUsedFrames[spanSlot] = currentFrame;
                    spanLastFirstRows[spanSlot] = changedRows[0];
                    spanLastRowCounts[spanSlot] = changed;
                }
                for (var row = 0; row < rows; row++)
                {
                    rowVersions[row] = versions[row];
                }
                Version = packet.ParameterVersion;
                ParameterCount = packet.ParameterCount;
                ExtraParameterCount = packet.ExtraParameterCount;
            }
        }

        internal void Apply(NativeRenderPlanLease lease, in RenderPlanPacket packet)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RenderParameterPages));
            }
            if (lease.Token != packet.Lease || packet.Protocol != NativeRenderPlanLease.ProtocolVersion || packet.ParameterCount > 65536 ||
                packet.ExtraParameterCount > Budget / 16 ||
                packet.ParameterPageCount != ((ulong)packet.ParameterCount * 8 + packet.ExtraParameterCount + 255) / 256 ||
                (packet.ExtraParameterCount != 0 && packet.ExtraParameters == IntPtr.Zero) ||
                (packet.ParameterCount != 0 && (packet.Parameters == IntPtr.Zero || packet.ParameterPages == IntPtr.Zero || packet.ParameterVersion == 0)))
            {
                throw new InvalidOperationException("参数输入的租约、长度或版本无效。");
            }
            if (Current != null && Current.Version == packet.ParameterVersion && Current.ParameterCount == packet.ParameterCount && Current.ExtraParameterCount == packet.ExtraParameterCount)
            {
                return;
            }
            EndRecording();
            if (packet.ParameterCount == 0)
            {
                return;
            }
            Page chosen = null;
            for (var i = 0; i < count; i++)
            {
                if (pages[i].Version == packet.ParameterVersion && pages[i].ParameterCount == packet.ParameterCount && pages[i].ExtraParameterCount == packet.ExtraParameterCount)
                {
                    chosen = pages[i];
                    break;
                }
            }
            if (chosen == null)
            {
                for (var i = 0; i < count; i++)
                {
                    if (pages[i].Free)
                    {
                        chosen = pages[i];
                        break;
                    }
                }
                if (chosen == null)
                {
                    if (count == pages.Length)
                    {
                        var detail = new System.Text.StringBuilder("参数提交积压，等待真实GPU fence释放版本。 frame=").Append(Time.frameCount);
                        for (var item = 0; item < count; item++)
                        {
                            detail.Append(" [").Append(item).Append(":").Append(pages[item].FailureState).Append("]");
                        }
                        throw new InvalidOperationException(detail.ToString());
                    }
                    chosen = pages[count++] = new Page(this);
                }
                chosen.Upload(packet);
            }
            chosen.Recording = true;
            Current = chosen;
        }

        internal void EndRecording()
        {
            if (Current == null)
            {
                return;
            }
            Current.Recording = false;
            if (disposed && Current.Free)
            {
                Current.ReleasePayload();
            }
            Current = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            EndRecording();
            for (var i = 0; i < count; i++)
            {
                pages[i].ReleasePayload();
            }
        }
    }
}
