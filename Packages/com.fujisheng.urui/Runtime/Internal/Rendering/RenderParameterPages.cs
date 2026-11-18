using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering
{
    /// <summary>参数纹理版本由录制引用和真实GPU提交引用共同保护。</summary>
    internal sealed unsafe class RenderParameterPages : IDisposable
    {
        const int Width = 256, MaximumPages = 9;
        const GraphicsFormat Format = GraphicsFormat.R32G32B32A32_SFloat;
        const long Budget = 48L * 1024 * 1024;
        readonly Page[] pages = new Page[MaximumPages];
        int count;
        long retainedPayloadBytes;
        bool disposed;
        internal long ParameterUploadBytes { get; private set; }
        internal long ParameterCopyBytes { get; private set; }
        internal long RetainedPayloadBytes => retainedPayloadBytes;
        internal Page Current { get; private set; }

        internal sealed class Page
        {
            readonly RenderParameterPages owner;
            NativeArray<Vector4> staging;
            ulong[] rowVersions;
            int[] changedRows;
            Texture2D[] rowUploads;
            internal bool Recording;
            int submissions;
            internal ulong Version;
            internal uint ParameterCount, ExtraParameterCount;
            internal Texture2D Texture { get; private set; }
            long payloadBytes;
            internal bool Free => !Recording && submissions == 0;
            internal string FailureState=>"v="+Version+",refs="+submissions+",record="+Recording;
            internal Page(RenderParameterPages owner) { this.owner = owner; }
            internal void RetainSubmission()
            {
                if (owner.disposed || !Texture) { throw new ObjectDisposedException(nameof(Page)); }
                checked { submissions++; }
            }
            internal void ReleaseSubmission()
            {
                if (submissions == 0) { throw new InvalidOperationException("参数提交引用被重复释放。"); }
                submissions--;
                if (owner.disposed && Free) { ReleasePayload(); }
            }
            internal void ReleasePayload()
            {
                if (!Free) { return; }
                if (Texture) { URUI.Resources.GpuRetirement.DestroyOwnedObject(Texture); Texture = null; }
                if (rowUploads != null)
                {
                    foreach (var row in rowUploads) { if (row) { URUI.Resources.GpuRetirement.DestroyOwnedObject(row); } }
                }
                if (staging.IsCreated) { staging.Dispose(); }
                rowVersions = null; changedRows = null; rowUploads = null;
                owner.retainedPayloadBytes -= payloadBytes; payloadBytes = 0; Version = 0; ParameterCount = ExtraParameterCount = 0;
            }
            static void CopyInput(byte* destination, long offset, long count, in RenderPlanPacket packet)
            {
                var baseBytes=(long)packet.ParameterCount*128;
                var baseCount=offset<baseBytes?Math.Min(count,baseBytes-offset):0;
                if(baseCount>0) { UnsafeUtility.MemCpy(destination,(byte*)packet.Parameters+offset,baseCount); }
                if(count>baseCount) { UnsafeUtility.MemCpy(destination+baseCount,(byte*)packet.ExtraParameters+offset+baseCount-baseBytes,count-baseCount); }
            }
            internal void Upload(in RenderPlanPacket packet)
            {
                if (!Free) { throw new InvalidOperationException("不能改写已录制或在途的参数版本。"); }
                var rows = Math.Max(1, checked(((int)packet.ParameterCount * 8 + (int)packet.ExtraParameterCount + 255) / 256));
                if (!Texture || Texture.height < rows)
                {
                    ReleasePayload();
                    if (!SystemInfo.IsFormatSupported(Format, GraphicsFormatUsage.Sample) || rows > SystemInfo.maxTextureSize)
                    { throw new NotSupportedException("当前图形后端不支持URUI参数纹理契约。"); }
                    // Account for persistent staging, readable Texture2D data and GPU
                    // payload independently of allocator/object overhead measured by Memory Profiler.
                    var bytes = (long)Width * rows * 16 * 3 + rows * 20L;
                    if (bytes > Budget - owner.retainedPayloadBytes) { throw new InvalidOperationException("参数版本超过48MiB常驻及在途数据预算。"); }
                    owner.retainedPayloadBytes += bytes; payloadBytes = bytes;
                    try
                    {
                        staging = new NativeArray<Vector4>(Width * rows, Allocator.Persistent);
                        rowVersions = new ulong[rows]; changedRows = new int[rows]; rowUploads = new Texture2D[rows];
                        // 精确格式保持原有线性RGBAFloat布局，避免旧枚举接口逐次反射校验。
                        Texture = new Texture2D(Width, rows, Format, TextureCreationFlags.None) {
                            name = "URUI render parameters", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }
                    catch { ReleasePayload(); throw; }
                }
                var versions = (ulong*)packet.ParameterPages;
                var changed = 0;
                for (var row = 0; row < rows; row++)
                {
                    if (Version == 0 || rowVersions[row] != versions[row]) { changedRows[changed++] = row; }
                }
                var initialize = Version == 0;
                Version = 0; // A failed upload must never be exposed under the old version.
                var destination = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(staging);
                var used = checked((long)packet.ParameterCount * 128 + (long)packet.ExtraParameterCount * 16);
                // WebGL2 may report CopyTextureSupport.None. This selects a valid
                // transport for the same protocol, never an optional rendering mode.
                if (initialize || changed * 2 >= Texture.height || (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
                {
                    CopyInput(destination, 0, used, packet);
                    UnsafeUtility.MemClear(destination + used, (long)staging.Length * 16 - used);
                    Texture.SetPixelData(staging, 0); Texture.Apply(false);
                    owner.ParameterUploadBytes += (long)Texture.width * Texture.height * 16;
                }
                else
                {
                    for (var changedIndex = 0; changedIndex < changed; changedIndex++)
                    {
                        var row = changedRows[changedIndex];
                        if (!rowUploads[row])
                        {
                            const long bytes = Width * 16L * 2;
                            if (bytes > Budget - owner.retainedPayloadBytes) { throw new InvalidOperationException("局部参数上传超过48MiB在途数据预算。"); }
                            var upload = new Texture2D(Width, 1, Format, TextureCreationFlags.None) {
                                name = "URUI parameter row", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                                hideFlags = HideFlags.HideAndDontSave
                            };
                            rowUploads[row] = upload; owner.retainedPayloadBytes += bytes; payloadBytes += bytes;
                        }
                        const int stride = Width * 16;
                        var bytesToCopy = Math.Min(stride, used - (long)row * stride);
                        UnsafeUtility.MemClear(destination + row * stride, stride);
                        CopyInput(destination + row * stride, (long)row * stride, bytesToCopy, packet);
                        rowUploads[row].SetPixelData(staging, 0, row * Width); rowUploads[row].Apply(false);
                        owner.ParameterUploadBytes += stride;
                        Graphics.CopyTexture(rowUploads[row], 0, 0, 0, 0, Width, 1, Texture, 0, 0, 0, row);
                        owner.ParameterCopyBytes += stride;
                    }
                }
                for (var row = 0; row < rows; row++) { rowVersions[row] = versions[row]; }
                Version = packet.ParameterVersion; ParameterCount = packet.ParameterCount; ExtraParameterCount = packet.ExtraParameterCount;
            }
        }

        internal void Apply(NativeRenderPlanLease lease, in RenderPlanPacket packet)
        {
            if (disposed) { throw new ObjectDisposedException(nameof(RenderParameterPages)); }
            if (lease.Token != packet.Lease || packet.Protocol != NativeRenderPlanLease.ProtocolVersion || packet.ParameterCount > 65536 ||
                packet.ExtraParameterCount > Budget / 16 ||
                packet.ParameterPageCount != ((ulong)packet.ParameterCount * 8 + packet.ExtraParameterCount + 255) / 256 ||
                (packet.ExtraParameterCount != 0 && packet.ExtraParameters == IntPtr.Zero) ||
                (packet.ParameterCount != 0 && (packet.Parameters == IntPtr.Zero || packet.ParameterPages == IntPtr.Zero || packet.ParameterVersion == 0)))
            { throw new InvalidOperationException("参数输入的租约、长度或版本无效。"); }
            if (Current != null && Current.Version == packet.ParameterVersion && Current.ParameterCount == packet.ParameterCount && Current.ExtraParameterCount == packet.ExtraParameterCount) { return; }
            EndRecording();
            if (packet.ParameterCount == 0) { return; }
            Page chosen = null;
            for (var i = 0; i < count; i++)
            {
                if (pages[i].Version == packet.ParameterVersion && pages[i].ParameterCount == packet.ParameterCount && pages[i].ExtraParameterCount == packet.ExtraParameterCount)
                { chosen = pages[i]; break; }
            }
            if (chosen == null)
            {
                for (var i = 0; i < count; i++) { if (pages[i].Free) { chosen = pages[i]; break; } }
                if (chosen == null)
                {
                    if (count == pages.Length)
                    {
                        var detail=new System.Text.StringBuilder("参数提交积压，等待真实GPU fence释放版本。 frame=").Append(Time.frameCount);
                        for(var item=0;item<count;item++)detail.Append(" [").Append(item).Append(":").Append(pages[item].FailureState).Append("]");
                        throw new InvalidOperationException(detail.ToString());
                    }
                    chosen = pages[count++] = new Page(this);
                }
                chosen.Upload(packet);
            }
            chosen.Recording = true; Current = chosen;
        }
        internal void EndRecording()
        {
            if (Current == null) { return; }
            Current.Recording = false;
            if (disposed && Current.Free) { Current.ReleasePayload(); }
            Current = null;
        }
        public void Dispose()
        {
            if (disposed) { return; } disposed = true; EndRecording();
            for (var i = 0; i < count; i++) { pages[i].ReleasePayload(); }
        }
    }
}
