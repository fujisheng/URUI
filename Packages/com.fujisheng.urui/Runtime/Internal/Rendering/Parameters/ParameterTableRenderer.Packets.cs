using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Parameters
{
    // 管理录制包与参数缓冲；GPU 完成状态不明的提交不能被重新使用。
    internal sealed partial class ParameterTableRenderer
    {
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
    }
}
