using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if URUI_DEBUG
using URUI.Internal.Diagnostics;
#endif
namespace URUI.Internal.Rendering.Parameters
{
    // 构造并持有绘制计划，保持批次、参数顺序与资源引用一致。
    internal sealed partial class ParameterTableRenderer
    {
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
    }
}
