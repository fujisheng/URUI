using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Parameters
{
    internal sealed partial class ParameterTableRenderer
    {
        internal sealed class NativeMesh
        {
            internal ulong id;
            internal Mesh value;
            internal long bytes;
            internal int references;
        }

        readonly Dictionary<ulong, NativeMesh> nativeMeshes = new Dictionary<ulong, NativeMesh>();
        readonly bool nativePrepared;
        readonly bool nativeIncremental;
        readonly bool nativePinned;
        NativePrepared nativeOwner;
        long nativeGpuPayload;

        // 相同 id 可能已对应新条目；只有字典仍指向当前对象时才移除缓存成员。
        void ReleaseNative(NativeMesh entry)
        {
            if (entry.references <= 0)
            {
                throw new InvalidOperationException("Unbalanced native GPU batch");
            }

            if (--entry.references != 0)
            {
                return;
            }

            if (nativeMeshes.TryGetValue(entry.id, out var active) && ReferenceEquals(entry, active))
            {
                nativeMeshes.Remove(entry.id);
            }

            nativeGpuPayload -= entry.bytes;
            if (entry.value)
            {
                UnityEngine.Object.Destroy(entry.value);
            }
        }

        // 原生租约仅覆盖本次构建；计划持有上传后的 Unity 资源，finally 必须归还原生租约。
        Plan BuildNative(ParameterFrame frame, ref Stats stats)
        {
            if (nativeOwner == null)
            {
                nativeOwner = new NativePrepared(nativeIncremental, nativePinned);
            }

            var packet = nativeOwner.Acquire(frame);
            Plan plan = null;
            try
            {
#if URUI_DEBUG
                beforeNativePlanForProbe?.Invoke();
#endif
                plan = new Plan
                {
                    owner = this,
                    epoch = frame.stamp.epoch,
                    resources = frame.stamp.resources,
                    keys = new ParameterKey[frame.count],
                    nativeMeshes = new List<NativeMesh>(),
                    sharedMeshes = shareResources ? new List<SharedMesh>() : null,
                    sharedTextures = shareResources ? new List<SharedTexture>() : null
                };
                Array.Copy(frame.keys, plan.keys, frame.count);
#if URUI_DEBUG
                stats.nativePrepares++;
                stats.nativeBuilds += (int)packet.built;
                stats.nativeReuses += (int)packet.reused;
                stats.nativeRetainedBytes = (long)packet.retained;
                stats.nativeBuiltBytes = (long)packet.builtBytes;
#endif
                for (int n = 0; n < packet.draws; n++)
                {
                    var d = nativeOwner.draws[n];
                    if (d.slot >= frame.count || d.count == 0 || (ulong)d.slot + d.count > (ulong)frame.count || d.kind > 1)
                    {
                        throw new InvalidOperationException("Native draw ordinal");
                    }

                    var key = frame.keys[d.slot];
                    if (key.geometry == 0
                        || d.texture != key.texture
                        || d.enabled != key.enabled
                        || d.x != key.x
                        || d.y != key.y
                        || d.w != key.w
                        || d.h != key.h
                        || (d.kind == 0) != key.affine)
                    {
                        throw new InvalidOperationException("Native draw state mismatch");
                    }

                    Texture(plan, d.texture, ref stats);
                    long bytes = (long)d.vertices * 32 + (long)d.indices * 4;
                    if (bytes <= 0 || plan.geometryBytes + bytes > MaxPlanGeometryBytes)
                    {
                        throw new InvalidOperationException("Native plan geometry budget");
                    }

                    plan.geometryBytes += bytes;
                    if (!nativeMeshes.TryGetValue(d.batch, out var entry) || !entry.value)
                    {
                        AdmitPayload(bytes);
                        var nextEntry = new NativeMesh
                        {
                            id = d.batch,
                            bytes = bytes
                        };
                        Mesh mesh = null;
                        try
                        {
                            mesh = nativeOwner.Upload(packet, d);
#if URUI_DEBUG
                            var copied = nativeOwner.LastCopy;
#if URUI_DEBUG
                            stats.nativeCopyBytes += (long)copied.copiedBytes;
                            stats.nativeFullBytes += (long)copied.fullBytes;
                            stats.nativePossibleBytes += (long)copied.possibleBytes;
#endif
                            if (copied.mode == 1)
                            {
#if URUI_DEBUG
                                stats.nativeDeltaCopies++;
#endif
                            }

                            afterNativeUploadForProbe?.Invoke(mesh);
#endif
                            nextEntry.value = mesh;
                            nativeMeshes[d.batch] = nextEntry;
                            nativeGpuPayload += bytes;
                            entry = nextEntry;
                        }
                        catch
                        {
                            if (mesh)
                            {
                                UnityEngine.Object.Destroy(mesh);
                            }

                            throw;
                        }
#if URUI_DEBUG
                        stats.meshBuilds++;
                        stats.batchBuilds++;
                        stats.geometryCopies++;
                        stats.geometryCopyBytes += (long)nativeOwner.LastCopy.copiedBytes;
                        stats.geometryRequestedBytes += bytes;
#endif
                    }
                    else
                    {
#if URUI_DEBUG
                        stats.batchHits++;
                        stats.geometryHits++;
#endif
                    }

                    try
                    {
                        plan.nativeMeshes.Add(entry);
                        entry.references++;
                    }
                    catch
                    {
                        if (entry.references == 0)
                        {
                            nativeMeshes.Remove(entry.id);
                            nativeGpuPayload -= entry.bytes;
                            if (entry.value)
                            {
                                UnityEngine.Object.Destroy(entry.value);
                            }
                        }

                        throw;
                    }

                    plan.batches.Add(new Batch { mesh = entry.value, parameter = d.kind == 0, slot = (int)d.slot, count = (int)d.count, key = key, nativeShared = entry });
                }

                if (NativeMethods.urui_render_plan_stamp(out var now) != 1
                    || now.flags != 1
                    || now.epoch != frame.stamp.epoch
                    || now.commands != frame.stamp.commands
                    || now.resources != frame.stamp.resources
                    || now.count != frame.stamp.count)
                {
                    throw new InvalidOperationException("Native preparation invalidated before publish");
                }
#if URUI_DEBUG
                stats.planBuilds++;
                stats.nativeStagingBytes = nativeOwner.StagingBytes;
#endif
                return plan;
            }
            catch
            {
                if (plan != null)
                {
                    DestroyPlan(plan);
                }

                throw;
            }
            finally
            {
                nativeOwner.Release(packet);
            }
        }
    }
}
