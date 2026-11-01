using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Parameters
{
    // 将同步复制的原生几何转换为 Unity 网格，并维护共享几何的按需上传状态。
    internal sealed partial class ParameterTableRenderer
    {
        Mesh EnsureSourceMesh(SharedMesh entry, ref Stats stats)
        {
            if (entry.gpuCreated)
            {
                if (!entry.value)
                {
                    throw new InvalidOperationException("Immutable source GPU Mesh destroyed");
                }

                return entry.value;
            }

            if (!lazySourceMeshes || entry.positions == null || NativeMethods.urui_geometry_exists(entry.key.id) != 1)
            {
                throw new InvalidOperationException("Invalid lazy source promotion");
            }

            long bytes = (long)entry.vertices * 24 + (long)entry.indices * 4;
            AdmitPayload(bytes);
#if URUI_DEBUG
            beforeSourceMeshCreateForProbe?.Invoke();
#endif
            var mesh = CreateSourceMesh(entry.positions, entry.uv, entry.colors, entry.topology);
            entry.value = mesh;
            entry.gpuCreated = true;
            sourcePayload += bytes;
#if URUI_DEBUG
            stats.meshBuilds++;
            stats.sourceMeshBuilds++;
            stats.sourceMeshPromotions++;
            stats.geometryRequestedBytes += bytes;
#endif
            return mesh;
        }

        Mesh Source(Plan plan, ulong id, ref Stats stats, bool requireMesh = true)
        {
            if (plan.source.TryGetValue(id, out var cached))
            {
                if (!ReferenceEquals(cached, null))
                {
                    if (!cached)
                    {
                        throw new InvalidOperationException("Immutable source GPU Mesh destroyed");
                    }

                    return cached;
                }

                if (!lazySourceMeshes)
                {
                    throw new InvalidOperationException("Unexpected CPU-only source");
                }

                if (!requireMesh)
                {
                    return null;
                }

                var promoted = EnsureSourceMesh(sharedMeshes[(plan.epoch, id)], ref stats);
                plan.source[id] = promoted;
                return promoted;
            }

            var key = (plan.epoch, id);
            if (shareResources && sharedMeshes.TryGetValue(key, out var shared))
            {
                if ((shared.gpuCreated && !shared.value)
                    || (!shared.gpuCreated && !lazySourceMeshes)
                    || NativeMethods.urui_geometry_exists(id) != 1)
                {
                    throw new InvalidOperationException("Stale immutable geometry");
                }

                ChargeGeometry(plan, shared.vertices, shared.indices);
                var result = requireMesh ? EnsureSourceMesh(shared, ref stats) : shared.value;
                plan.sharedMeshes.Add(shared);
                shared.references++;
                plan.source.Add(id, result);
#if URUI_DEBUG
                stats.geometryHits++;
#endif
                return result;
            }
#if URUI_DEBUG
            using var sourceMarker = sourceMarkerDefinition.Auto();
            long snapshotStart = geometryProbe != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
#endif
            if (NativeMethods.urui_get_geometry_info(id, out int n, out int k) != 1)
            {
                throw new InvalidOperationException("Invalid native geometry");
            }

            ChargeGeometry(plan, n, k);
            long bytes = (long)n * 24 + (long)k * 4;
            bool createMesh = !lazySourceMeshes || requireMesh;
            AdmitPayload(bytes * ((reuseBatches ? 1 : 0) + (createMesh ? 1 : 0)));
            var vertices = new VertexNative[n];
            var indices = new int[k];
            if (NativeMethods.urui_copy_geometry(id, vertices, n, indices, k) != 1)
            {
                throw new InvalidOperationException("Native geometry copy failed");
            }
#if URUI_DEBUG
            stats.geometryCopies++;
            stats.geometryCopyBytes += (long)n * System.Runtime.InteropServices.Marshal.SizeOf(typeof(VertexNative)) + (long)k * 4;
#endif
            var pos = new Vector3[n];
            var uv = new Vector2[n];
            var colors = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(vertices[i].x, vertices[i].y);
                uv[i] = new Vector2(vertices[i].u, vertices[i].v);
                colors[i] = new Color32(vertices[i].r, vertices[i].g, vertices[i].b, vertices[i].a);
            }

            foreach (int i in indices)
            {
                if (i < 0 || i >= n)
                {
                    throw new InvalidOperationException("Native index out of range");
                }
            }
#if URUI_DEBUG
            long snapshotTicks = geometryProbe != null ? System.Diagnostics.Stopwatch.GetTimestamp() - snapshotStart : 0;
            var probeToken = geometryProbe != null ? geometryProbe.Observe(plan.epoch, id, pos, uv, colors, indices) : default;
            long meshStart = geometryProbe != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
#endif
            Mesh mesh = null;
            if (createMesh)
            {
#if URUI_DEBUG
                beforeSourceMeshCreateForProbe?.Invoke();
#endif
                mesh = CreateSourceMesh(pos, uv, colors, indices);
            }

            if (shareResources)
            {
                var entry = new SharedMesh
                {
                    rendererOwner = this,
                    key = key,
                    value = mesh,
                    gpuCreated = createMesh,
                    vertices = n,
                    indices = k,
                    positions = reuseBatches ? pos : null,
                    uv = reuseBatches ? uv : null,
                    colors = reuseBatches ? colors : null,
                    topology = reuseBatches ? indices : null
                };
                try
                {
                    sharedMeshes.Add(key, entry);
                    if (createMesh)
                    {
                        sourcePayload += bytes;
                    }

                    if (reuseBatches)
                    {
                        sourceCpuPayload += bytes;
                    }
                }
                catch
                {
                    if (mesh)
                    {
                        UnityEngine.Object.Destroy(mesh);
                    }

                    throw;
                }

                try
                {
                    plan.sharedMeshes.Add(entry);
                    entry.references++;
                }
                catch
                {
                    if (entry.references == 0)
                    {
                        sharedMeshes.Remove(key);
                        if (createMesh)
                        {
                            sourcePayload -= bytes;
                        }

                        if (reuseBatches)
                        {
                            sourceCpuPayload -= bytes;
                        }

                        if (mesh)
                        {
                            UnityEngine.Object.Destroy(mesh);
                        }
                    }

                    throw;
                }
            }

            plan.source.Add(id, mesh);
            if (createMesh)
            {
#if URUI_DEBUG
                stats.meshBuilds++;
                stats.sourceMeshBuilds++;
                stats.geometryRequestedBytes += bytes;
#endif
            }
            else
            {
#if URUI_DEBUG
                stats.sourceMeshDeferred++;
#endif
            }
#if URUI_DEBUG
            if (geometryProbe != null)
            {
                geometryProbe.Commit(probeToken, n, k, snapshotTicks, System.Diagnostics.Stopwatch.GetTimestamp() - meshStart, shareResources);
            }
#endif
            return mesh;
        }
    }
}
