using UnityEngine;
namespace URUI.Animation
{
    /// <summary>
    /// Unity 的缩放或非缩放时钟；不改变媒体解码策略。
    /// </summary>
    public sealed class UnityUIClock : IUIClock
    {
        public static IUIClock Scaled
        {
            get;
        } = new UnityUIClock(false);
        public static IUIClock Unscaled
        {
            get;
        } = new UnityUIClock(true);
        readonly bool unscaled;
        UnityUIClock(bool unscaled)
        {
            this.unscaled = unscaled;
        }
        public double Now => unscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
        public float Delta => unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
    }
}
