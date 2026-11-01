using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Meshes;

namespace URUI.Internal.Hosting
{
    // 按原有命令顺序组合兼容几何，保留裁剪、变换与混合顺序的判定边界。
    internal sealed partial class RenderHost
    {
        Bounds GeometryBounds(Mesh mesh, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = DetailBegin(ref phase.boundsApiMs, ref phase);
#endif
            var b = mesh.bounds;
#if URUI_DEBUG
            DetailEnd(t, ref phase.boundsApiMs);
            if (phaseDiagnostics)
            {
                phase.boundsReads++;
            }
#endif
            return b;
        }

        void ClearFrameScratch()
        {
            frameUsedGeometry.Clear();
            frameUsedGeometry.TrimExcess();
            frameStaleGeometry.Clear();
            frameStaleGeometry.TrimExcess();
        }

        void ClearBatches()
        {
            foreach (var b in batches.Values)
            {
                if (b.mesh)
                {
                    Destroy(b.mesh);
                }
            }

            batches.Clear();
            batchGroup.Clear();
            previousBatchKeys.Clear();
            currentBatchKeys.Clear();
            staleBatchKeys.Clear();
        }

        static void Mix(ref ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }

        ulong GroupHash()
        {
            ulong hash = 1469598103934665603UL;
            foreach (var item in batchGroup)
            {
                var c = item.command;
                Mix(ref hash, c.geometry);
                Mix(ref hash, c.texture);
                Mix(ref hash, (uint)c.tx.GetHashCode());
                Mix(ref hash, (uint)c.ty.GetHashCode());
                Mix(ref hash, (uint)c.clip_enabled);
                Mix(ref hash, (uint)c.clip_x);
                Mix(ref hash, (uint)c.clip_y);
                Mix(ref hash, (uint)c.clip_w);
                Mix(ref hash, (uint)c.clip_h);
            }

            return hash;
        }

        static bool SameCommand(CommandNative a, CommandNative b) => a.geometry == b.geometry
            && a.texture == b.texture
            && a.tx == b.tx
            && a.ty == b.ty
            && a.clip_enabled == b.clip_enabled
            && a.clip_x == b.clip_x
            && a.clip_y == b.clip_y
            && a.clip_w == b.clip_w
            && a.clip_h == b.clip_h;

        bool SameBatch(BatchCache cache)
        {
            if (cache.items.Length != batchGroup.Count)
            {
                return false;
            }

            for (int i = 0; i < cache.items.Length; i++)
            {
                if (!SameCommand(cache.items[i].command, batchGroup[i].command))
                {
                    return false;
                }
            }

            return true;
        }

        bool CanJoin(BatchItem next, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = DetailBegin(ref phase.canJoinMs, ref phase);
#endif
            bool result = CanJoinCore(next, ref phase);
#if URUI_DEBUG
            DetailEnd(t, ref phase.canJoinMs);
#endif
            return result;
        }

        bool CanJoinCore(BatchItem next, ref PhaseFrame phase)
        {
            if (batchGroup.Count == 0)
            {
                return true;
            }

            var a = batchGroup[0].command;
            var b = next.command;
            if (a.texture != b.texture)
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.breakTexture++;
                }
#endif
                return false;
            }

            if (a.clip_enabled != b.clip_enabled
                || a.clip_x != b.clip_x
                || a.clip_y != b.clip_y
                || a.clip_w != b.clip_w
                || a.clip_h != b.clip_h)
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.breakClip++;
                }
#endif
                return false;
            }

            if (!BatchRules.EquivalentAffine2D(batchGroup[0].transform, next.transform))
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.breakTransform++;
                }
#endif
                return false;
            }

            if (!batchOrderedOverlap)
            {
                foreach (var prior in batchGroup)
                {
                    if (!BatchRules.Disjoint(prior.x0, prior.y0, prior.x1, prior.y1, next.x0, next.y0, next.x1, next.y1))
                    {
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.breakOverlap++;
                        }
#endif
                        return false;
                    }
                }
            }

            return true;
        }

        Mesh BuildBatchMesh(ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long start = phaseDiagnostics ? Now() : 0;
#endif
            int vertices = 0;
            int indices = 0;
            foreach (var item in batchGroup)
            {
                if (item.geometry.positions == null)
                {
                    return null;
                }

                vertices += item.geometry.positions.Length;
                indices += item.geometry.indices.Length;
            }

            if (vertices == 0 || indices == 0 || vertices > 100000 || indices > 300000)
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.vertexLimitGroups++;
                }
#endif
                return null;
            }

            var positions = new Vector3[vertices];
            var uv = new Vector2[vertices];
            var colors = new Color32[vertices];
            var ids = new int[indices];
            int vo = 0;
            int io = 0;
            foreach (var item in batchGroup)
            {
                var g = item.geometry;
                var c = item.command;
                for (int i = 0; i < g.positions.Length; i++)
                {
                    var p = g.positions[i];
                    positions[vo + i] = new Vector3(p.x + c.tx, p.y + c.ty, p.z);
                    uv[vo + i] = g.uv[i];
                    colors[vo + i] = g.colors[i];
                }

                for (int i = 0; i < g.indices.Length; i++)
                {
                    ids[io + i] = vo + g.indices[i];
                }

                vo += g.positions.Length;
                io += g.indices.Length;
            }
#if URUI_DEBUG
            long unityStart = phaseDiagnostics ? Now() : 0;
#endif
            var mesh = new Mesh
            {
                name = "DisjointBatch"
            };
            if (vertices > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.vertices = positions;
            mesh.uv = uv;
            mesh.colors32 = colors;
            mesh.triangles = ids;
            mesh.RecalculateBounds();
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.batchUnityMeshApiMs += Millis(Now() - unityStart);
                phase.batchMeshBuildMs += Millis(Now() - start);
            }
#endif
            return mesh;
        }

        void FlushGroup(ref PhaseFrame phase)
        {
#if URUI_DEBUG
            using var marker = flushMarker.Auto();
            long t = DetailBegin(ref phase.flushGroupMs, ref phase);
#endif
            FlushGroupCore(ref phase);
#if URUI_DEBUG
            DetailEnd(t, ref phase.flushGroupMs);
#endif
        }

        // 仅合并顺序与混合结果均可证明等价的组；无法合并时按原始顺序逐条提交。
        void FlushGroupCore(ref PhaseFrame phase)
        {
            if (batchGroup.Count == 0)
            {
                return;
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.batchGroups++;
            }
#endif
            if (batchGroup.Count == 1)
            {
                DrawOne(batchGroup[0], ref phase);
                batchGroup.Clear();
                return;
            }

            ulong hash = GroupHash();
            currentBatchKeys.Add(hash);
            if (batches.TryGetValue(hash, out var cached) && !cached.mesh)
            {
                batches.Remove(hash);
                cached = null;
                recordingWarmupPending = true;
            }

            if (cached != null)
            {
                if (SameBatch(cached))
                {
                    DrawBatch(cached, ref phase);
                    batchGroup.Clear();
                    return;
                }
                // Hash collision remains the original ordered fallback.
            }
            else if (previousBatchKeys.Contains(hash))
            {
                var mesh = BuildBatchMesh(ref phase);
                if (mesh)
                {
                    cached = new BatchCache
                    {
                        mesh = mesh,
                        items = batchGroup.ToArray()
                    };
                    batches.Add(hash, cached);
                    DrawBatch(cached, ref phase);
                    batchGroup.Clear();
                    return;
                }
            }
            else
            {
                recordingWarmupPending = true;
                // Do not freeze first-frame unbatched warmup.
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.fallbackExtraSubmissions += batchGroup.Count - 1;
            }
#endif
            foreach (var item in batchGroup)
            {
                DrawOne(item, ref phase);
            }

            batchGroup.Clear();
        }
    }
}
