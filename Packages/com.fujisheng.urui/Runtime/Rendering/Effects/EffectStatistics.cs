namespace URUI.Rendering.Effects
{
    /// <summary>效果目标的实际所有权计数；颜色字节不含驱动对齐、深度缓冲或视频解码输入。</summary>
    public readonly struct EffectTargetStatistics
    {
        public readonly int LiveTargets,LeasedTargets;
        public readonly long LiveColorBytes,LeasedColorBytes,IdleColorBytes,CreatedTargets,Reuses;
        internal EffectTargetStatistics(int live,int leased,long bytes,long leasedBytes,long idle,long created,long reuses)
        {LiveTargets=live;LeasedTargets=leased;LiveColorBytes=bytes;LeasedColorBytes=leasedBytes;IdleColorBytes=idle;CreatedTargets=created;Reuses=reuses;}
    }
    public static class EffectStatistics
    {
        public static long FilterProcessingPasses {get; internal set;}
        public static EffectTargetStatistics GetTargets()=>URUI.Internal.Rendering.EffectTargetPool.Statistics;
    }
}
