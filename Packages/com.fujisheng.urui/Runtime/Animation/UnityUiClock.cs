using UnityEngine;
namespace URUI.Animation
{
    /// <summary>
    /// Unity 的缩放或非缩放时钟；不改变媒体解码策略。
    /// </summary>
    public sealed class UnityUIClock : IUIClock
    {
        static readonly UnityUIClock scaledClock = new(false);
        static readonly UnityUIClock unscaledClock = new(true);
        public static IUIClock Scaled => scaledClock;
        public static IUIClock Unscaled => unscaledClock;
        readonly bool unscaled;
        bool hasDynamicNow;
        bool lastPlaying;
        double lastDynamicNow;

        UnityUIClock(bool unscaled)
        {
            this.unscaled = unscaled;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            scaledClock.Reset();
            unscaledClock.Reset();
        }

        void Reset()
        {
            hasDynamicNow = false;
            lastPlaying = false;
            lastDynamicNow = 0;
        }

        public double Now
        {
            get
            {
                var playing = Application.isPlaying;
                if (playing != lastPlaying)
                {
                    hasDynamicNow = false;
                    lastPlaying = playing;
                }
                // EarlyUpdate 已处于当前帧动态时间，随后同帧进入 FixedUpdate 时 Unity 会把
                // Time.timeAsDouble / Time.unscaledTimeAsDouble 重定向到滞后的 fixed 时间步。
                // 保持最近一次动态帧时间，确保 EarlyUpdate -> FixedUpdate -> Update 全链路单调递增。
                if (Time.inFixedTimeStep && hasDynamicNow)
                {
                    return lastDynamicNow;
                }
                var now = unscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
                if (!Time.inFixedTimeStep)
                {
                    if (!hasDynamicNow || now > lastDynamicNow)
                    {
                        lastDynamicNow = now;
                    }
                    hasDynamicNow = true;
                }
                return now;
            }
        }

        public float Delta => unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
    }
}
