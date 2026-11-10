using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        // 与正常帧共用记录/资源所有权；初始化读取不能绕开GPU资源的安全退役。
        internal void SynchronizeLayout()
        {
            using var profile = synchronizeMarker.Auto();
            SynchronizeLayoutState();
            if(!active||!isActiveAndEnabled){return;}
            var phase=new PhaseFrame();DrawFrame(ref phase);
        }

        void SynchronizeLayoutState()
        {
            if(lastWidth!=Screen.width||lastHeight!=Screen.height)
            {lastWidth=Screen.width;lastHeight=Screen.height;NativeMethods.urui_resize(UiWidth,UiHeight);}
            SynchronizeUiClock();
            using(nativeUpdatePhase.Auto()){NativeMethods.urui_update();}
            // 与正常帧的 DrainEvents 相同，绘制回调读取样式前先发布布局版本。
            OwnerSurface.RefreshDocuments(true);
        }
    }
}
