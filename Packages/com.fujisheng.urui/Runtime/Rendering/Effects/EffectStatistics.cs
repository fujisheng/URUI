namespace URUI.Rendering.Effects
{
    /// <summary>
    /// 效果目标的实际所有权计数；颜色字节为持久颜色容量，不含memoryless片上存储、驱动对齐、深度缓冲或视频解码输入。
    /// </summary>
    public readonly struct EffectTargetStatistics
    {
        public readonly int LiveTargets;
        public readonly int LeasedTargets;
        public readonly long LiveColorBytes;
        public readonly long LeasedColorBytes;
        public readonly long IdleColorBytes;
        public readonly long CreatedTargets;
        public readonly long Reuses;
        internal EffectTargetStatistics(int live, int leased, long bytes, long leasedBytes, long idle, long created, long reuses)
        {
            LiveTargets = live;
            LeasedTargets = leased;
            LiveColorBytes = bytes;
            LeasedColorBytes = leasedBytes;
            IdleColorBytes = idle;
            CreatedTargets = created;
            Reuses = reuses;
        }
    }
    public static class EffectStatistics
    {
        public static long FilterProcessingPasses
        {
            get;
            internal set;
        }
        public static EffectTargetStatistics GetTargets() => URUI.Internal.Rendering.EffectTargetPool.Statistics;
    }
}
