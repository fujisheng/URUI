using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace URUI.Internal
{
    // 参数表渲染器私有持有计划与资源；调用者负责独占提交，Dispose 前必须完成 GPU 退役。
    internal sealed partial class ParameterTableRenderer
    {
        // 空载体保留内部调用签名；统计字段只存在于 URUI_DEBUG 构建。
        internal partial struct Stats
        {
        }

        internal struct Ticket
        {
            internal Packet packet;
            internal ulong serial;
        }

        internal enum Result
        {
            Success,
            Unsupported,
            InvalidResource,
            Backpressure,
            Disposed
        }

        internal enum State
        {
            Free,
            Recorded,
            Submitted,
            Uncertain
        }

        internal sealed class Packet
        {
            internal CommandBuffer commands = new CommandBuffer
            {
                name = "URUI parameter prototype"
            };
            internal GraphicsBuffer parameters;
            internal int capacity;
            internal int lastCount;
            internal AffineParameter[] last = Array.Empty<AffineParameter>();
            internal bool hasData;
            internal ulong serial;
            internal Plan plan;
            internal GraphicsFence fence;
            internal State state;
#if URUI_DEBUG
            internal Stats stats;
#endif
        }

        internal sealed class Plan
        {
            internal ParameterTableRenderer owner;
            internal ulong epoch;
            internal ulong resources;
            internal long textureBytes;
            internal long geometryBytes;
            internal long combinedBytes;
            internal List<SharedBatch> sharedBatches;
            internal List<SharedMesh> sharedMeshes;
            internal List<SharedTexture> sharedTextures;
            internal List<NativeMesh> nativeMeshes;
            internal ParameterKey[] keys;
            internal int references;
            internal bool retired;
            internal readonly Dictionary<ulong, Mesh> source = new Dictionary<ulong, Mesh>();
            internal readonly Dictionary<ulong, Texture2D> textures = new Dictionary<ulong, Texture2D>();
            internal readonly List<Batch> batches = new List<Batch>();
        }

        internal sealed class Batch
        {
            internal Mesh mesh;
            internal bool parameter;
            internal int slot;
            internal int count;
            internal ParameterKey key;
            internal SharedBatch shared;
            internal NativeMesh nativeShared;
        }

        internal sealed class SharedMesh
        {
            internal ParameterTableRenderer rendererOwner;
            internal (ulong epoch, ulong id) key;
            internal Mesh value;
            internal bool gpuCreated;
            internal int references;
            internal int vertices;
            internal int indices;
            internal Vector3[] positions;
            internal Vector2[] uv;
            internal Color32[] colors;
            internal int[] topology;
        }

        internal sealed class SharedTexture
        {
            internal (ulong epoch, ulong id) key;
            internal Texture2D value;
            internal int references;
            internal int bytes;
        }

        readonly Dictionary<(ulong epoch, ulong id), SharedMesh> sharedMeshes = new Dictionary<(ulong, ulong), SharedMesh>();
        readonly Dictionary<(ulong epoch, ulong id), SharedTexture> sharedTextures = new Dictionary<(ulong, ulong), SharedTexture>();
        readonly bool lazySourceMeshes;
        readonly bool shareResources;
        long texturePayload;
        long sourcePayload;
        long combinedPayload;
        internal const long MaxSharedPayloadBytes = 64L * 1024 * 1024;

        internal long SharedPayloadBytes => texturePayload + sourcePayload + combinedPayload + nativeGpuPayload + sourceCpuPayload + batchIdentityPayload;

        void AdmitPayload(long bytes)
        {
            if (shareResources && (bytes < 0 || SharedPayloadBytes + bytes > MaxSharedPayloadBytes))
            {
                throw new InvalidOperationException("Shared immutable payload budget");
            }
        }

        const long MaxPlanGeometryBytes = 16L * 1024 * 1024;
        readonly Shader expectedLegacy;
        readonly Shader expectedParameter;
        readonly Material legacy;
        readonly Material parameterMaterial;
        readonly int maxPackets;
        readonly List<Packet> packets = new List<Packet>();
        readonly List<Plan> plans = new List<Plan>();
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        readonly BatchScratch scratch;
        Plan current;
        ulong serial;
        bool disposed;

        internal string LastFailure
        {
            get;
            private set;
        }

        internal bool Supported => legacy
            && parameterMaterial
            && legacy.shader == expectedLegacy
            && parameterMaterial.shader == expectedParameter
            && parameterMaterial.shader.isSupported
            && SystemInfo.supportsGraphicsFence
            && SystemInfo.graphicsShaderLevel >= 45
            && SystemInfo.supports32bitsIndexBuffer;

        internal ParameterTableRenderer(
            Shader legacyShader,
            Shader parameterShader,
            int maxPackets = 8,
            bool shareResources = false,
            bool reuseBatches = false,
            bool reuseBatchScratch = false,
            bool lazySourceMeshes = false,
            bool nativePrepared = false,
            bool nativeIncremental = false,
            bool nativePinned = false)
        {
            this.nativePinned = nativePinned;
            this.nativeIncremental = nativeIncremental;
            this.nativePrepared = shareResources && nativePrepared && NativePrepared.Available;
            this.lazySourceMeshes = shareResources && reuseBatches && lazySourceMeshes;
            scratch = shareResources && reuseBatches && reuseBatchScratch ? new BatchScratch() : null;
            this.reuseBatches = shareResources && reuseBatches;
            this.shareResources = shareResources;
            if (maxPackets < 1 || maxPackets > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPackets));
            }

            this.maxPackets = maxPackets;
            expectedLegacy = legacyShader;
            expectedParameter = parameterShader;
            legacy = new Material(legacyShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            parameterMaterial = new Material(parameterShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        void Release(Packet p)
        {
            p.commands.Clear();
            if (p.plan != null)
            {
                p.plan.references--;
                p.plan = null;
            }

            p.state = State.Free;
        }

        internal void Collect()
        {
            foreach (var p in packets)
            {
                if (p.state == State.Submitted)
                {
#if URUI_DEBUG
                    if (holdCompletedFencesForProbe)
                    {
                        continue;
                    }
#endif
                    try
                    {
                        if (p.fence.passed)
                        {
                            Release(p);
                        }
                    }
                    catch
                    {
                        p.state = State.Uncertain;
                    }
                }
            }

            for (int i = plans.Count - 1; i >= 0; i--)
            {
                if (plans[i].retired && plans[i].references == 0)
                {
                    DestroyPlan(plans[i]);
                    plans.RemoveAt(i);
                }
            }
        }

        internal bool Faulted
        {
            get
            {
                // 栅栏查询失败后不能证明 GPU 已停止读取；正式路径必须报告故障并保留资源。
                foreach (var p in packets)
                {
                    if (p.state == State.Uncertain)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal bool Pending
        {
            get
            {
                Collect();
                foreach (var p in packets)
                {
                    if (p.state != State.Free)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        void Release(SharedMesh entry)
        {
            if (entry.references <= 0)
            {
                throw new InvalidOperationException("Unbalanced immutable geometry ownership");
            }

            if (--entry.references != 0)
            {
                return;
            }

            if (!sharedMeshes.TryGetValue(entry.key, out var currentEntry) || !ReferenceEquals(currentEntry, entry))
            {
                throw new InvalidOperationException("Immutable geometry identity mismatch");
            }

            sharedMeshes.Remove(entry.key);
#if URUI_DEBUG
            geometryProbe?.Released(entry.key.epoch, entry.key.id);
#endif
            if (entry.positions != null)
            {
                sourceCpuPayload -= (long)entry.vertices * 24 + (long)entry.indices * 4;
            }

            if (entry.gpuCreated)
            {
                sourcePayload -= (long)entry.vertices * 24 + (long)entry.indices * 4;
            }

            if (entry.value)
            {
                UnityEngine.Object.Destroy(entry.value);
            }
        }

        void Release(SharedTexture entry)
        {
            if (entry.references <= 0)
            {
                throw new InvalidOperationException("Unbalanced immutable texture ownership");
            }

            if (--entry.references != 0)
            {
                return;
            }

            if (!sharedTextures.TryGetValue(entry.key, out var currentEntry) || !ReferenceEquals(currentEntry, entry))
            {
                throw new InvalidOperationException("Immutable texture identity mismatch");
            }

            sharedTextures.Remove(entry.key);
            texturePayload -= entry.bytes;
            if (entry.value)
            {
                UnityEngine.Object.Destroy(entry.value);
            }
        }

        void DestroyPlan(Plan plan)
        {
            if (plan.nativeMeshes != null)
            {
                foreach (var entry in plan.nativeMeshes)
                {
                    ReleaseNative(entry);
                }
            }

            if (reuseBatches && plan.sharedBatches != null)
            {
                foreach (var batch in plan.sharedBatches)
                {
                    Release(batch);
                }
            }

            if (shareResources)
            {
                if (plan.sharedMeshes != null)
                {
                    foreach (var resource in plan.sharedMeshes)
                    {
                        Release(resource);
                    }
                }

                if (plan.sharedTextures != null)
                {
                    foreach (var resource in plan.sharedTextures)
                    {
                        Release(resource);
                    }
                }
            }
            else
            {
                foreach (var mesh in plan.source.Values)
                {
                    if (mesh)
                    {
                        UnityEngine.Object.Destroy(mesh);
                    }
                }

                foreach (var tex in plan.textures.Values)
                {
                    if (tex)
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                }
            }

            foreach (var b in plan.batches)
            {
                if (b.parameter && b.shared == null && b.nativeShared == null && b.mesh)
                {
                    UnityEngine.Object.Destroy(b.mesh);
                }
            }

            if (shareResources)
            {
                combinedPayload -= plan.combinedBytes;
            }
        }

        static void ChargeGeometry(Plan plan, int n, int k)
        {
            long bytes = (long)n * 24 + (long)k * 4;
            if (n <= 0 || k <= 0 || n > 1048576 || k > 3145728 || plan.geometryBytes + bytes > MaxPlanGeometryBytes)
            {
                throw new InvalidOperationException("Parameter plan geometry budget");
            }

            plan.geometryBytes += bytes;
        }

        static void ChargeTexture(Plan plan, int bytes)
        {
            if (bytes <= 0 || plan.textureBytes + bytes > 32L * 1024 * 1024)
            {
                throw new InvalidOperationException("Parameter texture snapshot budget");
            }

            plan.textureBytes += bytes;
        }

        static Mesh CreateSourceMesh(Vector3[] positions, Vector2[] uv, Color32[] colors, int[] topology)
        {
            var value = new Mesh
            {
                name = "URUI parameter immutable source",
                indexFormat = IndexFormat.UInt32
            };
            try
            {
                value.vertices = positions;
                value.uv = uv;
                value.colors32 = colors;
                value.triangles = topology;
                value.RecalculateBounds();
                return value;
            }
            catch
            {
                UnityEngine.Object.Destroy(value);
                throw;
            }
        }

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

        Texture Texture(Plan plan, ulong id, ref Stats stats)
        {
            if (id == 0)
            {
                return Texture2D.whiteTexture;
            }

            if (plan.textures.TryGetValue(id, out var t))
            {
                return t;
            }

            var key = (plan.epoch, id);
            if (shareResources && sharedTextures.TryGetValue(key, out var shared))
            {
                if (!shared.value || NativeMethods.urui_texture_exists(id) != 1)
                {
                    throw new InvalidOperationException("Stale immutable texture");
                }

                ChargeTexture(plan, shared.bytes);
                plan.sharedTextures.Add(shared);
                shared.references++;
                plan.textures.Add(id, shared.value);
#if URUI_DEBUG
                stats.textureHits++;
#endif
                return shared.value;
            }

            if (NativeMethods.urui_get_texture_info(id, out int w, out int h, out int count) != 1
                || w <= 0
                || h <= 0
                || (long)w * h * 4 != count)
            {
                throw new InvalidOperationException("Invalid native texture");
            }

            ChargeTexture(plan, count);
            AdmitPayload(count);
            var data = new byte[count];
            if (NativeMethods.urui_copy_texture(id, data, count) != 1)
            {
                throw new InvalidOperationException("Texture copy failed");
            }
#if URUI_DEBUG
            stats.textureCopies++;
            stats.textureCopyBytes += count;
#endif
            t = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "URUI parameter owned snapshot"
            };
            try
            {
                t.LoadRawTextureData(data);
                t.Apply(false, true);
            }
            catch
            {
                UnityEngine.Object.Destroy(t);
                throw;
            }

            if (shareResources)
            {
                var entry = new SharedTexture
                {
                    key = key,
                    value = t,
                    bytes = count
                };
                try
                {
                    sharedTextures.Add(key, entry);
                    texturePayload += count;
                }
                catch
                {
                    UnityEngine.Object.Destroy(t);
                    throw;
                }

                try
                {
                    plan.sharedTextures.Add(entry);
                    entry.references++;
                }
                catch
                {
                    if (entry.references == 0)
                    {
                        sharedTextures.Remove(key);
                        texturePayload -= count;
                        UnityEngine.Object.Destroy(t);
                    }

                    throw;
                }
            }

            plan.textures.Add(id, t);
#if URUI_DEBUG
            stats.textureRequestedBytes += count;
#endif
            return t;
        }

        Plan Build(ParameterFrame frame, ref Stats stats)
        {
            if (nativePrepared)
            {
                return BuildNative(frame, ref stats);
            }
#if URUI_DEBUG
            using var planMarker = planMarkerDefinition.Auto();
#endif
            var p = new Plan
            {
                owner = this,
                epoch = frame.stamp.epoch,
                resources = frame.stamp.resources,
                keys = new ParameterKey[frame.count],
                sharedBatches = reuseBatches ? new List<SharedBatch>() : null,
                sharedMeshes = shareResources ? new List<SharedMesh>() : null,
                sharedTextures = shareResources ? new List<SharedTexture>() : null
            };
            Array.Copy(frame.keys, p.keys, frame.count);
            try
            {
                for (int i = 0; i < frame.count;)
                {
                    var key = frame.keys[i];
                    if (key.geometry == 0)
                    {
                        i++;
                        continue;
                    }

                    Texture(p, key.texture, ref stats);
                    if (!key.affine)
                    {
                        p.batches.Add(new Batch { mesh = Source(p, key.geometry, ref stats), parameter = false, slot = i, count = 1, key = key });
                        i++;
                        continue;
                    }

                    int begin = i;
                    int end = i + 1;
                    while (end < frame.count && frame.keys[end].geometry != 0 && ParameterKey.CanJoin(key, frame.keys[end]))
                    {
                        end++;
                    }
#if URUI_DEBUG
                    ClassifyBatch(frame, p.batches.Count, begin, end - begin, ref stats);
#endif
                    if (reuseBatches && BorrowBatch(p, frame, begin, end, ref stats))
                    {
                        i = end;
                        continue;
                    }
#if URUI_DEBUG
                    using var batchMarker = batchMarkerDefinition.Auto();
                    long contentBuildStart = GeometryContentProbe.Mode == 2 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
#endif
                    scratch?.Begin();
                    try
                    {
                        var pos = scratch?.positions ?? new List<Vector3>();
                        var uv = scratch?.uv ?? new List<Vector2>();
                        var color = scratch?.colors ?? new List<Color32>();
                        var slots = scratch?.slots ?? new List<Vector2>();
                        var indices = scratch?.indices ?? new List<int>();
#if URUI_DEBUG
                        using (stageMarkerDefinition.Auto())
                        {
#endif
                            for (; i < end; i++)
                            {
                                var mesh = Source(p, frame.keys[i].geometry, ref stats, !lazySourceMeshes);
                                Vector3[] a;
                                Vector2[] b;
                                Color32[] c;
                                int[] ids;
                                if (reuseBatches)
                                {
                                    var data = sharedMeshes[(p.epoch, frame.keys[i].geometry)];
                                    a = data.positions;
                                    b = data.uv;
                                    c = data.colors;
                                    ids = data.topology;
                                }
                                else
                                {
                                    a = mesh.vertices;
                                    b = mesh.uv;
                                    c = mesh.colors32;
                                    ids = mesh.triangles;
#if URUI_DEBUG
                                    stats.meshArrayReads += 4;
#endif
                                }

                                int offset = pos.Count;
                                if (offset + a.Length > 1048576
                                    || indices.Count + ids.Length > 3145728
                                    || p.geometryBytes + (long)(offset + a.Length) * 32 + (indices.Count + ids.Length) * 4 > MaxPlanGeometryBytes)
                                {
                                    throw new InvalidOperationException("Prototype batch budget");
                                }

                                AdmitPayload((long)(offset + a.Length) * 32 + (indices.Count + ids.Length) * 4);
                                pos.AddRange(a);
                                uv.AddRange(b);
                                color.AddRange(c);
                                for (int v = 0; v < a.Length; v++)
                                {
                                    slots.Add(new Vector2(i, 0));
                                }

                                foreach (int id in ids)
                                {
                                    indices.Add(offset + id);
                                }
                            }
#if URUI_DEBUG
                        }

                        long contentStageTicks = GeometryContentProbe.Mode == 2 ? System.Diagnostics.Stopwatch.GetTimestamp() - contentBuildStart : 0;
                        var contentObservation = GeometryContentProbe.Mode == 2 ? ObserveBatchContent(frame, p.batches.Count, begin, end, contentStageTicks) : default;
                        long contentUploadStart = GeometryContentProbe.Mode == 2 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
#endif
                        var combined = new Mesh
                        {
                            name = "URUI parameter immutable batch",
                            indexFormat = IndexFormat.UInt32
                        };
                        long payload = (long)pos.Count * 32 + indices.Count * 4;
                        var batch = new Batch
                        {
                            mesh = combined,
                            parameter = true,
                            slot = begin,
                            count = end - begin,
                            key = key
                        };
                        p.batches.Add(batch);
#if URUI_DEBUG
                        using (uploadMarkerDefinition.Auto())
                        {
#endif
                            combined.SetVertices(pos);
                            combined.SetUVs(0, uv);
                            combined.SetColors(color);
                            combined.SetUVs(1, slots);
                            combined.SetTriangles(indices, 0);
                            combined.bounds = new Bounds(Vector3.zero, new Vector3(1e9f, 1e9f, 1e6f));
#if URUI_DEBUG
                            stats.meshBuilds++;
                            stats.batchBuilds++;
                        }

                        long contentUploadTicks = GeometryContentProbe.Mode == 2 ? System.Diagnostics.Stopwatch.GetTimestamp() - contentUploadStart : 0;
#endif
                        if (shareResources)
                        {
                            combinedPayload += payload;
                            if (reuseBatches)
                            {
                                try
                                {
#if URUI_DEBUG
                                    using (identityMarkerDefinition.Auto())
                                    {
#endif
                                        batch.shared = PublishBatch(p, frame, begin, end, combined, payload);
#if URUI_DEBUG
                                    }
#endif
                                }
                                catch
                                {
                                    combinedPayload -= payload;
                                    throw;
                                }
                            }
                            else
                            {
                                p.combinedBytes += payload;
                            }
                        }
#if URUI_DEBUG
                        stats.geometryRequestedBytes += payload;
#endif
                        p.geometryBytes += payload;
#if URUI_DEBUG
                        if (GeometryContentProbe.Mode == 2)
                        {
                            CommitBatchContent(contentObservation, contentUploadTicks, System.Diagnostics.Stopwatch.GetTimestamp() - contentBuildStart - contentObservation.compareTicks, payload);
                        }
#endif
                    }
                    finally
                    {
                        scratch?.End();
                    }
                }
#if URUI_DEBUG
                stats.planBuilds++;
#endif
                return p;
            }
            catch
            {
                DestroyPlan(p);
                throw;
            }
        }

        void Bind(Plan plan, ParameterKey key)
        {
            properties.Clear();
            properties.SetTexture("_MainTex", key.texture == 0 ? Texture2D.whiteTexture : plan.textures[key.texture]);
            properties.SetFloat("_UruiClipEnabled", key.enabled);
            properties.SetVector("_UruiClipRect", key.ClipVector);
        }

        // 可选的直接成员校验仍检查计划所有权；默认使用字典成员校验。
        internal bool DirectSourceValidation
        {
            get;
            set;
        }

        // 验证计划自身持有的资源身份与存活状态，不能仅凭几何 id 存在就接受缓存。
        bool ValidateSources(Plan plan)
        {
#if URUI_DEBUG
            using var sourceValidateMarker = sourceValidateMarkerDefinition.Auto();
#endif
            if (lazySourceMeshes && DirectSourceValidation)
            {
                // Entries were admitted by Source and remain strongly owned by this immutable Plan.
                // Native liveness checks on stamp changes remain in Match, below this helper.
                if (plan == null
                    || !ReferenceEquals(plan.owner, this)
                    || plan.sharedMeshes == null
                    || plan.sharedMeshes.Count != plan.source.Count)
                {
                    return false;
                }

                bool ordered = true;
                int at = 0;
                foreach (var pair in plan.source)
                {
                    var entry = plan.sharedMeshes[at++];
#if URUI_DEBUG
                    if (countSourceValidation)
                    {
                        sourceValidationVisits++;
                    }
#endif
                    if (entry == null
                        || !ReferenceEquals(entry.rendererOwner, this)
                        || entry.references <= 0
                        || entry.key.epoch != plan.epoch
                        || entry.positions == null)
                    {
                        return false;
                    }

                    if (entry.gpuCreated)
                    {
                        if (!entry.value)
                        {
                            return false;
                        }
                    }
                    else if (!ReferenceEquals(entry.value, null))
                    {
                        return false;
                    }

                    if (pair.Key != entry.key.id)
                    {
                        ordered = false;
                        continue;
                    }

                    if (!ReferenceEquals(pair.Value, null) && !ReferenceEquals(pair.Value, entry.value))
                    {
                        return false;
                    }
                }
                // Dictionary enumeration order is not an API promise. Preserve the original
                // membership validation if it differs from the private insertion list.
                return ordered || ValidateSourcesLookup(plan);
            }

            return ValidateSourcesLookup(plan);
        }

        bool ValidateSourcesLookup(Plan plan)
        {
            foreach (var m in plan.source)
            {
#if URUI_DEBUG
                if (countSourceValidation)
                {
                    sourceValidationVisits++;
                }
#endif
                if (m.Value)
                {
                    continue;
                }

                if (!lazySourceMeshes || !ReferenceEquals(m.Value, null))
                {
                    return false;
                }
#if URUI_DEBUG
                if (countSourceValidation)
                {
                    sourceValidationLookups++;
                }
#endif
                if (!sharedMeshes.TryGetValue((plan.epoch, m.Key), out var cpu) || cpu.positions == null || (cpu.gpuCreated && !cpu.value))
                {
                    return false;
                }
            }

            return true;
        }

        bool Match(ParameterFrame frame)
        {
#if URUI_DEBUG
            using var validateMarker = validateMarkerDefinition.Auto();
#endif
            if (current == null
                || current.epoch != frame.stamp.epoch
                || !ParameterKey.SameSequence(current.keys, current.keys.Length, frame.keys, frame.count))
            {
                return false;
            }

            if (!ValidateSources(current))
            {
                return false;
            }

            foreach (var b in current.batches)
            {
                if (!b.mesh)
                {
                    return false;
                }
            }

            foreach (var t in current.textures.Values)
            {
                if (!t)
                {
                    return false;
                }
            }

            if (current.resources != frame.stamp.resources)
            {
                foreach (var key in current.keys)
                {
                    if (key.geometry != 0
                        && (NativeMethods.urui_geometry_exists(key.geometry) == 0
                        || (key.texture != 0
                        && NativeMethods.urui_texture_exists(key.texture) == 0)))
                    {
                        return false;
                    }
                }

                current.resources = frame.stamp.resources;
            }

            return true;
        }

        internal Result AcquirePlan(ParameterFrame frame, out Plan plan)
        {
            plan = null;
            var result = Prepare(frame);
            if (result != Result.Success)
            {
                return result;
            }

            plan = current;
            plan.references++;
            return result;
        }

        internal void ReleasePlan(Plan plan)
        {
            if (plan == null || plan.references <= 0)
            {
                throw new InvalidOperationException("Unbalanced parameter plan lease");
            }

            plan.references--;
        }

        internal void AppendDraws(CommandBuffer cb, Plan plan, ParameterFrame frame, GraphicsBuffer parameters, ref Stats stats)
        {
            foreach (var batch in plan.batches)
            {
                Bind(plan, batch.key);
                if (batch.parameter)
                {
                    properties.SetBuffer("_UruiTransforms", parameters);
                    cb.DrawMesh(batch.mesh, Matrix4x4.identity, parameterMaterial, 0, 0, properties);
                }
                else
                {
                    cb.DrawMesh(batch.mesh, frame.worlds[batch.slot], legacy, 0, 0, properties);
#if URUI_DEBUG
                    stats.fallbacks++;
#endif
                }
#if URUI_DEBUG
                stats.draws++;
#endif
            }
        }

        internal Result Prepare(ParameterFrame frame)
        {
#if URUI_DEBUG
            Last = default;
#endif
            LastFailure = null;
            if (disposed)
            {
                return Result.Disposed;
            }

            if (!Supported
                || frame == null
                || frame.count < 0
                || frame.count > 65536
                || frame.stamp.flags != 1
                || frame.stamp.count != frame.count
                || frame.keys.Length < frame.count)
            {
                return Result.Unsupported;
            }

            Collect();
            if (Faulted)
            {
                return Result.Unsupported;
            }

            Stats stats = default;
            try
            {
                if (!Match(frame))
                {
                    var next = Build(frame, ref stats);
                    if (current != null)
                    {
                        current.retired = true;
                    }

                    current = next;
                    plans.Add(next);
                }
#if URUI_DEBUG
                ScratchStats(ref stats);
                Last = stats;
#endif
                return Result.Success;
            }
            catch (InvalidOperationException e)
            {
                LastFailure = e.Message;
#if URUI_DEBUG
                ScratchStats(ref stats);
                Last = stats;
#endif
                return Result.InvalidResource;
            }
        }

        internal Result TryRecord(ParameterFrame frame, RenderTexture target, bool reference, out Ticket ticket)
        {
            if (!target)
            {
                ticket = default;
                return Result.Unsupported;
            }

            return TryRecordOutput(frame, target, new Rect(0, 0, target.width, target.height), target.width, target.height, true, reference, out ticket);
        }

        internal Result TryRecordCamera(ParameterFrame frame, Camera camera, int width, int height, out Ticket ticket)
        {
            ticket = default;
            if (!camera || width <= 0 || height <= 0)
            {
                return Result.Unsupported;
            }

            RenderTargetIdentifier output = camera.targetTexture ? new RenderTargetIdentifier(camera.targetTexture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            var viewport = camera.targetTexture ? new Rect(0, 0, camera.targetTexture.width, camera.targetTexture.height) : camera.pixelRect;
            return TryRecordOutput(frame, output, viewport, width, height, false, false, out ticket);
        }

        Result TryRecordOutput(
            ParameterFrame frame,
            RenderTargetIdentifier target,
            Rect viewport,
            int width,
            int height,
            bool clear,
            bool reference,
            out Ticket ticket)
        {
            ticket = default;
#if URUI_DEBUG
            Last = default;
#endif
            if (disposed)
            {
                return Result.Disposed;
            }

            if (!Supported
                || frame == null
                || frame.stamp.flags != 1
                || frame.count < 0
                || frame.count > 65536
                || frame.stamp.count != frame.count
                || frame.commands.Length < frame.count
                || frame.worlds.Length < frame.count
                || frame.keys.Length < frame.count
                || frame.parameters.Length < frame.count)
            {
                return Result.Unsupported;
            }

            Collect();
            if (Faulted)
            {
                return Result.Unsupported;
            }

            Packet packet = null;
            foreach (var p in packets)
            {
                if (p.state == State.Free)
                {
                    packet = p;
                    break;
                }
            }

            if (packet == null)
            {
                if (packets.Count >= maxPackets)
                {
                    return Result.Backpressure;
                }

                packet = new Packet();
                packets.Add(packet);
            }

            if (serial == ulong.MaxValue)
            {
                return Result.Unsupported;
            }

            Stats stats = default;
            try
            {
                if (!Match(frame))
                {
                    var next = Build(frame, ref stats);
                    if (current != null)
                    {
                        current.retired = true;
                    }

                    current = next;
                    plans.Add(next);
                }

                packet.commands.Clear();
                packet.plan = current;
                current.references++;
                packet.serial = ++serial;
                packet.state = State.Recorded;
                var cb = packet.commands;
                cb.SetRenderTarget(target);
                cb.SetViewport(viewport);
                if (clear)
                {
                    cb.ClearRenderTarget(false, true, new Color(.01f, .02f, .03f, 1));
                }

                cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, width, height, 0, -1, 1));
                if (!reference && frame.count > 0)
                {
                    if (packet.capacity < frame.count)
                    {
                        var nextData = new AffineParameter[frame.count];
                        var nextBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, frame.count, 32);
                        if (packet.parameters != null)
                        {
                            packet.parameters.Dispose();
                        }

                        packet.capacity = frame.count;
                        packet.parameters = nextBuffer;
                        packet.last = nextData;
                        packet.hasData = false;
                    }

                    bool changed = !packet.hasData || packet.lastCount != frame.count;
                    if (!changed)
                    {
                        for (int i = 0; i < frame.count; i++)
                        {
                            if (!packet.last[i].Equals(frame.parameters[i]))
                            {
                                changed = true;
                                break;
                            }
                        }
                    }

                    if (changed)
                    {
                        Array.Copy(frame.parameters, packet.last, frame.count);
                        cb.SetBufferData(packet.parameters, packet.last, 0, 0, frame.count);
                        packet.lastCount = frame.count;
                        packet.hasData = true;
#if URUI_DEBUG
                        stats.parameterRequestedBytes = (long)frame.count * 32;
#endif
                    }
                }

                if (reference)
                {
                    for (int i = 0; i < frame.count; i++)
                    {
                        var key = frame.keys[i];
                        if (key.geometry == 0)
                        {
                            continue;
                        }

                        Bind(current, key);
                        cb.DrawMesh(Source(current, key.geometry, ref stats), frame.worlds[i], legacy, 0, 0, properties);
#if URUI_DEBUG
                        stats.draws++;
#endif
                    }
                }
                else
                {
                    foreach (var batch in current.batches)
                    {
                        Bind(current, batch.key);
                        if (batch.parameter)
                        {
                            properties.SetBuffer("_UruiTransforms", packet.parameters);
                            cb.DrawMesh(batch.mesh, Matrix4x4.identity, parameterMaterial, 0, 0, properties);
                        }
                        else
                        {
                            int i = batch.slot;
                            cb.DrawMesh(batch.mesh, frame.worlds[i], legacy, 0, 0, properties);
#if URUI_DEBUG
                            stats.fallbacks++;
#endif
                        }
#if URUI_DEBUG
                        stats.draws++;
#endif
                    }
                }

                packet.fence = cb.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
                ticket = new Ticket
                {
                    packet = packet,
                    serial = packet.serial
                };
#if URUI_DEBUG
                stats.packets = packets.Count;
                stats.plans = plans.Count;
#endif
                foreach (var p in packets)
                {
#if URUI_DEBUG
                    stats.retainedParameterBytes += (long)p.capacity * 32;
#endif
                }
#if URUI_DEBUG
                ScratchStats(ref stats);
                packet.stats = stats;
                Last = stats;
#endif
                return Result.Success;
            }
            catch (InvalidOperationException)
            {
                if (packet.state == State.Recorded)
                {
                    packet.hasData = false;
                    Release(packet);
                }
#if URUI_DEBUG
                ScratchStats(ref stats);
                Last = stats;
#endif
                return Result.InvalidResource;
            }
            catch
            {
                if (packet.state == State.Recorded)
                {
                    packet.hasData = false;
                    Release(packet);
                }

                throw;
            }
        }

        Packet Check(Ticket ticket)
        {
            if (ticket.packet == null || ticket.packet.serial != ticket.serial || ticket.packet.state != State.Recorded)
            {
                throw new InvalidOperationException("Stale or submitted parameter ticket");
            }

            return ticket.packet;
        }

        internal GraphicsFence Submit(Ticket ticket)
        {
            var p = Check(ticket);
            p.state = State.Submitted;
            try
            {
                Graphics.ExecuteCommandBuffer(p.commands);
            }
            catch
            {
                p.state = State.Uncertain;
                throw;
            }
#if URUI_DEBUG
            Last = p.stats;
#endif
            return p.fence;
        }

        internal void Cancel(Ticket ticket)
        {
            var p = Check(ticket);
            p.hasData = false;
            Release(p);
        }

        internal bool TryDispose()
        {
            if (disposed)
            {
                return true;
            }

            foreach (var p in packets)
            {
                if (p.state == State.Recorded)
                {
                    p.hasData = false;
                    Release(p);
                }
            }

            Collect();
            foreach (var p in packets)
            {
                if (p.state == State.Submitted || p.state == State.Uncertain)
                {
                    return false;
                }
            }

            foreach (var p in plans)
            {
                if (p.references > 0)
                {
                    return false;
                }
            }

            foreach (var p in packets)
            {
                p.commands.Release();
                p.parameters?.Dispose();
            }

            foreach (var p in plans)
            {
                DestroyPlan(p);
            }

            plans.Clear();
            packets.Clear();
            current = null;
            UnityEngine.Object.Destroy(legacy);
            UnityEngine.Object.Destroy(parameterMaterial);
            scratch?.Drop();
#if URUI_DEBUG
            geometryProbe?.Clear();
#endif
            nativeOwner?.Dispose();
            nativeOwner = null;
            disposed = true;
            return true;
        }
    }
}
