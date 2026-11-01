using System;
using UnityEngine;
using URUI.Internal.Rendering.Parameters;

namespace URUI.Internal.Rendering.Frames
{
    // 仅在同步借用窗口中读取原生帧，并把 Unity 资源记录与原生租约分别管理。
    internal sealed partial class NativeFrameRoot
    {
        // 只有确认命令缓冲清空后才能恢复安全回退；清空失败时保留不确定资源。
        bool ClearRoot(string stage)
        {
            try
            {
#if URUI_DEBUG
                Probe(stage);
#endif
                root?.Clear();
                return true;
            }
            catch
            {
                if (CanFallback)
                {
                    UnsafeFrame = Time.frameCount;
                }

                CanFallback = false;
                Faulted = true;
                return false;
            }
        }

        Plan Build(BorrowedFrame.Packet p, ref ParameterTableRenderer.Stats stats)
        {
            var next = new Plan
            {
                epoch = p.epoch,
                id = p.plan
            };
            building = next;
            try
            {
                long total = 0;
                for (int i = 0; i < p.drawCount; i++)
                {
                    var d = source.Draw(p, i);
                    if (d.batch == 0
                        || d.count == 0
                        || d.slot >= p.count
                        || (ulong)d.slot + d.count > p.count
                        || d.kind > 1
                        || d.vertices == 0
                        || d.vertices > 1048576
                        || d.indices == 0
                        || d.indices > 3145728)
                    {
                        throw new InvalidOperationException("Native frame draw contract");
                    }

                    long bytes = (long)d.vertices * 32 + (long)d.indices * 4;
                    total += bytes;
                    if (total > 16L * 1024 * 1024)
                    {
                        throw new InvalidOperationException("Native frame plan budget");
                    }

                    Texture texture = AcquireTexture(next, p.epoch, d.texture, ref stats);
                    bool created = false;
                    if (!meshes.TryGetValue(d.batch, out var mesh))
                    {
                        Charge(bytes);
                        var value = source.Upload(p, d, failedUpload);
                        mesh = new MeshEntry
                        {
                            id = d.batch,
                            mesh = value,
                            bytes = bytes
                        };
                        try
                        {
                            meshes.Add(d.batch, mesh);
                            retainedBytes += bytes;
                            created = true;
                        }
                        catch
                        {
                            UnityEngine.Object.Destroy(value);
                            throw;
                        }
#if URUI_DEBUG
                        stats.meshBuilds++;
                        stats.batchBuilds++;
                        stats.geometryRequestedBytes += bytes;
                        stats.nativeFullBytes += bytes;
#endif
                    }
                    else
                    {
                        if (!mesh.mesh)
                        {
                            throw new InvalidOperationException("Native immutable Mesh destroyed");
                        }
#if URUI_DEBUG
                        stats.batchHits++;
#endif
                    }

                    try
                    {
                        next.draws.Add(new DrawBinding { state = d, mesh = mesh, texture = texture });
                        mesh.refs++;
                    }
                    catch
                    {
                        if (created && mesh.refs == 0)
                        {
                            meshes.Remove(mesh.id);
                            retainedBytes -= mesh.bytes;
                            UnityEngine.Object.Destroy(mesh.mesh);
                        }

                        throw;
                    }
#if URUI_DEBUG
                    Probe("after-mesh");
#endif
                }
#if URUI_DEBUG
                stats.planBuilds++;
#endif
                return next;
            }
            catch
            {
                Retire(next, null);
                throw;
            }
            finally
            {
                building = null;
            }
        }

        void Bind(DrawBinding d, GraphicsBuffer data)
        {
            var s = d.state;
            properties.Clear();
            properties.SetTexture("_MainTex", d.texture);
            properties.SetFloat("_UruiClipEnabled", s.enabled);
            properties.SetVector("_UruiClipRect", new Vector4(s.x, s.y, unchecked(s.x + s.w), unchecked(s.y + s.h)));
            if (s.kind == 0)
            {
                properties.SetBuffer("_UruiTransforms", data);
            }
        }

        internal ParameterTableRenderer.Result Record(int width, int height, RenderTexture target = null)
        {
            var stats = new ParameterTableRenderer.Stats();
#if URUI_DEBUG
            Last = stats;
#endif
            if (!CorrectThread || closing || disposed || recording || Faulted || width <= 0 || height <= 0)
            {
                return ParameterTableRenderer.Result.Unsupported;
            }

            Collect();
            if (Faulted)
            {
                return ParameterTableRenderer.Result.InvalidResource;
            }

            BorrowedFrame.Packet packet = default;
            bool leased = false;
            bool published = false;
            Plan next = null;
            GraphicsBuffer allocated = null;
            recording = true;
            try
            {
                packet = source.Acquire();
                leased = true;
#if URUI_DEBUG
                CommandCount = (int)packet.count;
                stats.nativeActive = 1;
                stats.nativePrepares = 1;
                stats.nativeBuilds = (int)packet.built;
                stats.nativeReuses = (int)packet.reused;
                stats.nativeBuiltBytes = (long)packet.builtBytes;
                stats.nativeRetainedBytes = (long)packet.retained;
#endif
                bool changed = current == null || current.epoch != packet.epoch || current.id != packet.plan;
                bool grow = packet.count > capacity;
                if (retired.Count >= 32 && (changed || grow))
                {
                    throw new InvalidOperationException("Native frame retired resource backpressure");
                }
#if URUI_DEBUG
                Probe("after-acquire");
#endif
                if (closing)
                {
                    throw new InvalidOperationException("Native frame closed during acquire");
                }

                if (changed)
                {
                    next = Build(packet, ref stats);
                }
                else
                {
#if URUI_DEBUG
                    stats.batchHits = (int)packet.drawCount;
#endif
                }

                int nextCapacity = capacity;
                if (grow)
                {
                    nextCapacity = Math.Min(65536, Math.Max((int)packet.count, Math.Max(16, capacity * 2)));
                    allocated = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nextCapacity, 32);
                }
#if URUI_DEBUG
                Probe("before-record");
#endif
                var recordingPlan = changed ? next : current;
                var recordingBuffer = allocated ?? buffer;
                if (!ClearRoot("clear-initial"))
                {
                    throw new InvalidOperationException("Native frame attachment clear uncertain");
                }

                try
                {
                    if (target)
                    {
                        root.SetRenderTarget(target);
                        root.SetViewport(new Rect(0, 0, width, height));
                    }

                    root.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, width, height, 0, -1, 1));
                    source.RecordParameters(packet, root, recordingBuffer);
#if URUI_DEBUG
                    stats.parameterRequestedBytes = (long)packet.count * 32;
                    Probe("after-parameters");
#endif
                    foreach (var d in recordingPlan.draws)
                    {
                        Bind(d, recordingBuffer);
                        if (d.state.kind == 0)
                        {
                            root.DrawMesh(d.mesh.mesh, Matrix4x4.identity, parameter, 0, 0, properties);
                        }
                        else
                        {
                            root.DrawMesh(d.mesh.mesh, source.World(packet, d.state.slot), legacy, 0, 0, properties);
#if URUI_DEBUG
                            stats.fallbacks++;
#endif
                        }
#if URUI_DEBUG
                        stats.draws++;
                        Probe("after-draw");
#endif
                    }
                }
                catch
                {
                    ClearRoot("clear-rollback");
                    throw;
                }

                if (closing)
                {
                    throw new InvalidOperationException("Native frame closed during recording");
                }
                // Publication has no graphics execution. Resources referenced by the sole
                // camera attachment become owned before old-resource barriers are enqueued.
                var oldPlan = changed ? current : null;
                var oldBuffer = grow ? buffer : null;
                if (changed)
                {
                    current = next;
                    next = null;
                }

                if (grow)
                {
                    buffer = allocated;
                    allocated = null;
                    capacity = nextCapacity;
                }

                published = true;
#if URUI_DEBUG
                stats.retainedParameterBytes = (long)capacity * 32;
                stats.plans = retired.Count + 1;
                Last = stats;
#endif
                if (!Retire(oldPlan, oldBuffer))
                {
                    throw new InvalidOperationException("Native frame retirement enqueue uncertain");
                }

                return ParameterTableRenderer.Result.Success;
            }
            catch (Exception error)
            {
                FailureReason = error.Message;
                Faulted = true;
                ClearRoot("clear-reject");
                if (next != null || allocated != null)
                {
                    if (CanFallback)
                    {
                        Retire(next, allocated);
                    }
                    else
                    {
                        retired.Add(new Retirement { plan = next, buffer = allocated, uncertain = true });
                    }

                    next = null;
                    allocated = null;
                }

                if (!published)
                {
#if URUI_DEBUG
                    Last = stats;
#endif
                }

                return ParameterTableRenderer.Result.InvalidResource;
            }
            finally
            {
                try
                {
                    if (leased)
                    {
                        source.Release(packet);
                    }
                }
                finally
                {
                    recording = false;
                    if (closing && !closed && CorrectThread)
                    {
                        CompleteClose();
                    }
                }
            }
        }
    }
}
