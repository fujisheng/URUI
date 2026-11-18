using System;

namespace URUI.Animation
{
    /// <summary>与采样顺序无关的轨道时间换算，不访问Unity或改变播放状态。</summary>
    public static class SequenceTiming
    {
        public readonly struct Sample
        {
            public readonly bool Active;
            public readonly float Progress;
            public readonly double LocalTime;
            public readonly int Iteration;
            internal Sample(bool active, float progress, double localTime, int iteration)
            {
                Active = active;
                Progress = progress;
                LocalTime = localTime;
                Iteration = iteration;
            }
        }
        public static Sample At(SequenceTrack track, double time)
        {
            if (track == null || double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                throw new InvalidOperationException("无效采样时间。");
            if (time < track.Start)
                return new Sample(false, 0, 0, 0);
            if (track.Duration == 0)
                return new Sample(true, 1, 0, 0);
            double elapsed = Math.Min(time - track.Start, track.Duration * track.Iterations);
            int iteration = Math.Min(track.Iterations - 1, (int)Math.Floor(elapsed / track.Duration));
            double local = elapsed - iteration * track.Duration;
            float progress = (float)Math.Max(0, Math.Min(1, local / track.Duration));
            if (track.Alternate && iteration % 2 != 0)
            {
                progress = 1 - progress;
                local = track.Duration - local;
            }
            return new Sample(true, progress, local, iteration);
        }
        public static bool Overlaps(SequenceTrack first, SequenceTrack second)
            => first.Start < second.End && second.Start < first.End;
    }
}
