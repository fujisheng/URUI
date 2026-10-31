using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

namespace URUI.Internal
{
    // Synchronous CPU input only. Never owns a Mesh, resource identity, or GPU buffer.
    // One owner/one active lease; no global pool and no render-thread consumption.
    internal sealed partial class BatchScratch
    {
        internal readonly List<Vector3> positions = new List<Vector3>();
        internal readonly List<Vector2> uv = new List<Vector2>();
        internal readonly List<Vector2> slots = new List<Vector2>();
        internal readonly List<Color32> colors = new List<Color32>();
        internal readonly List<int> indices = new List<int>();
        internal const long MaxRetainedBytes = 512 * 1024;
        bool active;

        internal long RetainedBytes => (long)positions.Capacity * 12 + (long)(uv.Capacity + slots.Capacity) * 8 + (long)colors.Capacity * 4 + (long)indices.Capacity * 4;

        // 一个租约内的列表只能由当前 CPU 构建使用；上传完成后 End 才允许复用容量。
        internal void Begin()
        {
            if (active)
            {
                throw new InvalidOperationException("CPU batch scratch already leased");
            }

            active = true;
#if URUI_DEBUG
            Leases++;
            atBegin = RetainedBytes;
            if (atBegin == 0)
            {
                ColdLeases++;
            }
#endif
        }

        internal void End()
        {
            if (!active)
            {
                throw new InvalidOperationException("CPU batch scratch not leased");
            }
#if URUI_DEBUG
            long begin = Stopwatch.GetTimestamp();
#endif
            long bytes = RetainedBytes;
#if URUI_DEBUG
            if (bytes > atBegin)
            {
                CapacityGrowthEvents++;
            }

            HighWaterBytes = Math.Max(HighWaterBytes, bytes);
#endif
            positions.Clear();
            uv.Clear();
            colors.Clear();
            slots.Clear();
            indices.Clear();
            active = false;
            if (bytes > MaxRetainedBytes)
            {
#if URUI_DEBUG
                OverflowCount++;
#endif
                Drop();
            }
#if URUI_DEBUG
            ClearTicks += Stopwatch.GetTimestamp() - begin;
#endif
        }

        // 只释放托管暂存容量，不接触已经提交给 Unity 的 Mesh 或 GPU 资源。
        internal void Drop()
        {
            if (active)
            {
                throw new InvalidOperationException("Cannot drop active CPU batch scratch");
            }

            positions.Capacity = uv.Capacity = colors.Capacity = slots.Capacity = indices.Capacity = 0;
        }
    }
}
