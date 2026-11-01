#if URUI_DEBUG
using System;

namespace URUI.Internal.Hosting
{
    // 可选计时与分配采样；关闭 URUI_DEBUG 后这些调用不进入正式程序集。
    internal sealed partial class RenderHost
    {
        static long ThreadAllocationBytes()
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            // This backend's thread counter is not implemented. Zero is not evidence of
            // zero allocation; use raw Profiler GC.Alloc for scoped IL2CPP measurements.
            return -1;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

        static long Now() => System.Diagnostics.Stopwatch.GetTimestamp();
        static double Millis(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        long DetailBegin(ref double value, ref PhaseFrame phase)
        {
            if (!phaseDiagnostics || detailedHostMode == 0)
            {
                return 0;
            }

            phase.detailClockPairs++;
            if (detailedHostMode == 2)
            {
                long t = Now();
                value += Millis(Now() - t);
                return 0;
            }

            return Now();
        }

        void DetailEnd(long t, ref double value)
        {
            if (phaseDiagnostics && detailedHostMode == 1)
            {
                value += Millis(Now() - t);
            }
        }

        public double TakeFrameTextMilliseconds()
        {
            double result = frameTextMilliseconds;
            frameTextMilliseconds = 0;
            return result;
        }

        public double FrameTextMilliseconds => frameTextMilliseconds;
    }
}
#endif
