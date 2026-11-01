using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Frames
{
    // 等待 GPU 完成后回收已提交资源；关闭失败时保留无法确认安全的资源。
    internal sealed partial class NativeFrameRoot
    {
        bool Retire(Plan plan, GraphicsBuffer data)
        {
            if (plan == null && data == null)
            {
                return true;
            }

            var lease = new Retirement
            {
                plan = plan,
                buffer = data
            };
            retired.Add(lease);
            try
            {
                lease.barrier = new CommandBuffer
                {
                    name = "URUI native final-frame resource retirement"
                };
                lease.fence = lease.barrier.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.PixelProcessing);
                Graphics.ExecuteCommandBuffer(lease.barrier);
                return true;
            }
            catch
            {
                lease.uncertain = true;
                Faulted = true;
                return false;
            }
        }

        // GPU 完成状态不确定时保留整个租约；不能为了回退而提前释放仍可能被绘制引用的资源。
        void Collect()
        {
#if URUI_DEBUG
            if (holdCompletedFencesForProbe)
            {
                return;
            }
#endif
            for (int i = retired.Count - 1; i >= 0; i--)
            {
                var e = retired[i];
                if (e.uncertain)
                {
                    continue;
                }

                bool done;
                try
                {
                    done = e.fence.passed;
                }
                catch
                {
                    e.uncertain = true;
                    Faulted = true;
                    continue;
                }

                if (!done)
                {
                    continue;
                }

                Release(e.plan);
                e.buffer?.Dispose();
                e.barrier?.Release();
                retired.RemoveAt(i);
            }
        }

        internal void Close()
        {
            if (closed || disposed)
            {
                return;
            }

            closing = true;
            if (!CorrectThread)
            {
                Faulted = true;
                CanFallback = false;
                return;
            }

            if (recording)
            {
                return;
            }

            CompleteClose();
        }

        void CompleteClose()
        {
            closed = true;
            if (!ClearRoot("clear-close"))
            {
                retired.Add(new Retirement { plan = current, buffer = buffer, uncertain = true });
                current = null;
                buffer = null;
                root = null;
                source?.Dispose();
                source = null;
                return;
            }

            root = null;
            var old = current;
            var data = buffer;
            current = null;
            buffer = null;
            Retire(old, data);
            source?.Dispose();
            source = null;
        }

        internal bool TryDispose()
        {
            if (disposed)
            {
                return true;
            }

            if (!CorrectThread || recording)
            {
                return false;
            }

            if (!closed)
            {
                Close();
            }

            if (!closed)
            {
                return false;
            }

            Collect();
            if (retired.Count != 0)
            {
                return false;
            }

            source?.Dispose();
            source = null;
            if (meshes.Count != 0 || textures.Count != 0)
            {
                throw new InvalidOperationException("Native frame resource ownership leak");
            }

            UnityEngine.Object.Destroy(parameter);
            UnityEngine.Object.Destroy(legacy);
            disposed = true;
            return true;
        }
    }
}
