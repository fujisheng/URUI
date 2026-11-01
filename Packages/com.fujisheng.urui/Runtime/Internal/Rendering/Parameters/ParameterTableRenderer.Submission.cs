using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Parameters
{
    // 向命令缓冲追加计划、提交 fence，并按实际状态取消或关闭录制包。
    internal sealed partial class ParameterTableRenderer
    {
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
