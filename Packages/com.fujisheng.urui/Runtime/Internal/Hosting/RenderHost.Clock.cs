using System;
using URUI.Animation;
using URUI.Internal.Interop;
namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        bool clockProtocolChecked;
        double lastUITime = double.NaN;
        internal void RebaseUIClock()
        {
            SynchronizeUIClock(true);
        }
        void SynchronizeUIClock(bool reset = false)
        {
            if (!clockProtocolChecked)
            {
                try
                {
                    if (NativeMethods.urui_ui_clock_api_version() != 1)
                    {
                        throw new NotSupportedException("需要URUI时钟协议v1插件。");
                    }
                }
                catch (EntryPointNotFoundException error)
                {
                    throw new NotSupportedException("当前插件缺少UI时钟协议v1，请重建此目标插件。", error);
                }
                clockProtocolChecked = true;
            }
            var clock = OwnerSurface ? OwnerSurface.UIClock : UnityUIClock.Unscaled;
            var now = clock.Now;
            // Frame Debugger 重放时 Unity 的 double 采样实测会回退约21ns。
            // 只吸收内置时钟的微秒内舍入误差，向原生提交的时间仍单调；自定义时钟保持严格检查。
            if (!reset && clock is UnityUIClock && double.IsFinite(now) && now >= 0 && now < lastUITime && lastUITime - now <= .000001)
            {
                now = lastUITime;
            }
            if (!double.IsFinite(now) || now < 0 || !reset && !double.IsNaN(lastUITime) && now < lastUITime)
            {
                throw new InvalidOperationException($"UI时钟必须为非负、有限且单调递增的时间；更换时基前请卸载 Surface 的全部文档。当前={now:R}，上次={lastUITime:R}，帧={UnityEngine.Time.frameCount}，时钟={clock.GetType().FullName}");
            }
            if (NativeMethods.urui_set_ui_time(now) != 1)
            {
                throw new InvalidOperationException("无法提交UI时钟。");
            }
            lastUITime = now;
        }
    }
}
