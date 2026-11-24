using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering
{
    /// <summary>
    /// 只上传原生给出的不可变批次；不在托管层重新排序、合批或转换顶点。
    /// </summary>
    internal sealed unsafe partial class RenderPlanResources : IDisposable
    {
        static readonly VertexAttributeDescriptor[] layout = {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2)
        };
        const long SoftBudget = 48L * 1024 * 1024;
        const long Budget = 64L * 1024 * 1024;
        // 每个 Unity Mesh 除原始顶点/索引字节外还有 C++/GPU 包装开销；限制空闲 Mesh 数量以防小批次无限堆积。
        const int MaxIdleEntries = 128;
        const long EstimatedMeshOverheadBytes = 4096;
        readonly Dictionary<ulong, Entry> meshes = new();
        readonly HashSet<ulong> used = new();
        readonly Action<IDisposable> retire;
        long allocatedBytes;
        long use;
        bool disposed;

        internal long GeometryUploadBytes
        {
            get;
            private set;
        }
        internal long RetainedBytes => allocatedBytes;

        internal RenderPlanResources(Action<IDisposable> retire)
        {
            this.retire = retire ?? throw new ArgumentNullException(nameof(retire));
        }

        void Reserve(long bytes)
        {
            if (bytes > Budget)
            {
                throw new InvalidOperationException("原生绘制的单批次超出64MiB几何预算。");
            }
            // First synchronously reclaim oldest unreferenced idle entries (References == 0)
            // so allocatedBytes drops immediately without needlessly waiting on a GPU fence.
            while (allocatedBytes + bytes > Budget)
            {
                ulong oldestIdle = 0;
                Entry idleCandidate = null;
                foreach (var pair in meshes)
                {
                    if (used.Contains(pair.Key) || pair.Value.References != 0)
                    {
                        continue;
                    }
                    if (idleCandidate == null || pair.Value.Used < idleCandidate.Used)
                    {
                        oldestIdle = pair.Key;
                        idleCandidate = pair.Value;
                    }
                }
                if (idleCandidate != null)
                {
                    meshes.Remove(oldestIdle);
                    idleCandidate.Dispose();
                    continue;
                }

                // No idle entries left; retire oldest non-current GPU-referenced entries to free once their fences pass.
                var needed = allocatedBytes + bytes - Budget;
                long scheduled = 0;
                while (allocatedBytes + bytes > Budget && scheduled < needed)
                {
                    ulong oldest = 0;
                    Entry candidate = null;
                    foreach (var pair in meshes)
                    {
                        if (used.Contains(pair.Key))
                        {
                            continue;
                        }
                        if (candidate == null || pair.Value.Used < candidate.Used)
                        {
                            oldest = pair.Key;
                            candidate = pair.Value;
                        }
                    }
                    if (candidate == null)
                    {
                        throw new InvalidOperationException("几何预算仍被当前记录或在途GPU版本占用。");
                    }
                    meshes.Remove(oldest);
                    if (candidate.References == 0)
                    {
                        candidate.Dispose();
                    }
                    else
                    {
                        scheduled += candidate.Bytes;
                        retire(candidate);
                    }
                }
                if (allocatedBytes + bytes > Budget)
                {
                    throw new InvalidOperationException("几何预算等待已退役GPU版本的fence完成。");
                }
            }
        }

        void TrimToSoftBudget()
        {
            while (allocatedBytes + (long)meshes.Count * EstimatedMeshOverheadBytes > SoftBudget ||
                   meshes.Count - used.Count > MaxIdleEntries)
            {
                ulong oldestIdle = 0;
                Entry idleCandidate = null;
                foreach (var pair in meshes)
                {
                    if (used.Contains(pair.Key) || pair.Value.References != 0)
                    {
                        continue;
                    }
                    if (idleCandidate == null || pair.Value.Used < idleCandidate.Used)
                    {
                        oldestIdle = pair.Key;
                        idleCandidate = pair.Value;
                    }
                }
                if (idleCandidate == null)
                {
                    break;
                }
                meshes.Remove(oldestIdle);
                idleCandidate.Dispose();
            }
        }

        internal void Apply(NativeRenderPlanLease lease, in RenderPlanPacket packet, IRawGeometryBindings raw = null)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RenderPlanResources));
            }
            if (lease.Token != packet.Lease || packet.Protocol != NativeRenderPlanLease.ProtocolVersion)
            {
                throw new InvalidOperationException("绘制计划资源世代不匹配。");
            }
            var operations = new ReadOnlySpan<RenderPlanOperation>((void*)packet.Operations, checked((int)packet.OperationCount));
            used.Clear();
            foreach (var operation in operations)
            {
                if (operation.Kind == PlanOperationKind.Geometry || operation.Kind == PlanOperationKind.ExternalGeometry ||
                    operation.Kind == PlanOperationKind.ParameterImage || (operation.Kind == PlanOperationKind.Layer && operation.Program != 0))
                {
                    used.Add(operation.Geometry);
                }
                else if (operation.Kind == PlanOperationKind.ExternalRawGeometry)
                {
                    used.Add((raw ?? throw new InvalidOperationException("缺少原始几何消费窗口。")).RawGeometry(operation.LayerIndex).Identity);
                }
            }
            foreach (var operation in operations)
            {
                if (operation.Kind == PlanOperationKind.ExternalRawGeometry)
                {
                    ApplyRaw(raw.RawGeometry(operation.LayerIndex));
                    continue;
                }
                if (operation.Kind != PlanOperationKind.Geometry && operation.Kind != PlanOperationKind.ExternalGeometry &&
                    operation.Kind != PlanOperationKind.ParameterImage && !(operation.Kind == PlanOperationKind.Layer && operation.Program != 0))
                {
                    continue;
                }
                if (meshes.TryGetValue(operation.Geometry, out var cached))
                {
                    cached.Used = ++use;
                    continue;
                }
                var view = lease.Geometry(operation.Geometry);
                var entry = Acquire(operation.Geometry, checked((int)view.VertexCount), checked((int)view.IndexCount), IndexFormat.UInt32, layout, 32, operation.Kind == PlanOperationKind.ExternalGeometry, out var configure);
                var vertices = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<RenderPlanVertex>((void*)view.Vertices, checked((int)view.VertexCount), Allocator.None);
                var indices = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<uint>((void*)view.Indices, checked((int)view.IndexCount), Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref vertices, AtomicSafetyHandle.GetTempMemoryHandle());
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref indices, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
                var mesh = entry.Mesh;
                try
                {
                    if (configure)
                    {
                        mesh.SetVertexBufferParams(entry.VertexCapacity, layout);
                        mesh.SetIndexBufferParams(entry.IndexCapacity, IndexFormat.UInt32);
                    }
                    const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
                    mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0, flags);
                    mesh.SetIndexBufferData(indices, 0, 0, indices.Length, flags);
                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length, MeshTopology.Triangles) { vertexCount = vertices.Length }, flags);
                    GeometryUploadBytes += (long)vertices.Length * 32 + (long)indices.Length * 4;
                    meshes.Add(operation.Geometry, entry);
                }
                catch
                {
                    if (entry.References == 0)
                    {
                        entry.Dispose();
                    }
                    else
                    {
                        retire(entry);
                    }
                    throw;
                }
            }
            CommitRecording();
            TrimToSoftBudget();
        }

        internal Mesh Mesh(ulong geometry) => meshes.TryGetValue(geometry, out var value) ? value.Mesh : throw new InvalidOperationException("原生批次尚未上传。");

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            EndRecording();
            foreach (var entry in meshes.Values)
            {
                if (entry.References == 0)
                {
                    entry.Dispose();
                }
                else
                {
                    retire(entry);
                }
            }
            meshes.Clear();
        }
    }
}
