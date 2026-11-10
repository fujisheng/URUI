using UnityEngine;
namespace URUI.Animation
{
    /// <summary>Unity 的缩放或非缩放时钟；不改变媒体解码策略。</summary>
    public sealed class UnityUiClock : IUiClock
    {
        public static IUiClock Scaled { get; } = new UnityUiClock(false);
        public static IUiClock Unscaled { get; } = new UnityUiClock(true);
        readonly bool unscaled;
        UnityUiClock(bool unscaled) { this.unscaled = unscaled; }
        public double Now => unscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
        public float Delta => unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
    }
}
