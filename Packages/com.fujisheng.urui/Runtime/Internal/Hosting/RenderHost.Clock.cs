using System;
using URUI.Animation;
using URUI.Internal.Interop;
namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        bool clockProtocolChecked;
        double lastUiTime=double.NaN;
        internal void RebaseUiClock() { SynchronizeUiClock(true); }
        void SynchronizeUiClock(bool reset=false)
        {
            if(!clockProtocolChecked)
            {
                try
                {
                    if(NativeMethods.urui_ui_clock_api_version()!=1)throw new NotSupportedException("需要URUI时钟协议v1插件。");
                }
                catch(EntryPointNotFoundException error){throw new NotSupportedException("当前插件缺少UI时钟协议v1，请重建此目标插件。",error);}
                clockProtocolChecked=true;
            }
            var clock=OwnerSurface?OwnerSurface.UiClock:UnityUiClock.Unscaled;
            var now=clock.Now;
            // Frame Debugger 重放时 Unity 的 double 采样实测会回退约21ns。
            // 只吸收内置时钟的微秒内舍入误差，向原生提交的时间仍单调；自定义时钟保持严格检查。
            if(!reset&&clock is UnityUiClock&&double.IsFinite(now)&&now>=0&&now<lastUiTime&&lastUiTime-now<=.000001)
                now=lastUiTime;
            if(!double.IsFinite(now)||now<0||!reset&&!double.IsNaN(lastUiTime)&&now<lastUiTime)
                throw new InvalidOperationException($"UI时钟必须为非负、有限且单调递增的时间；更换时基前请卸载 Surface 的全部文档。当前={now:R}，上次={lastUiTime:R}，帧={UnityEngine.Time.frameCount}，时钟={clock.GetType().FullName}");
            if(NativeMethods.urui_set_ui_time(now)!=1)throw new InvalidOperationException("无法提交UI时钟。");
            lastUiTime=now;
        }
    }
}
