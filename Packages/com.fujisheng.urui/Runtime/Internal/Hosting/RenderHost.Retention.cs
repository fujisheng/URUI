using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        CommandBuffer blockedRecording;
        int blockedRecordingFrame=-1;

        // 清空失败后保留资源；同帧不得继续绘制，下一帧显式恢复成功后才允许使用记录。
        void RequireRecordingReady()
        {
            if(blockedRecording==null)return;
            if(!ReferenceEquals(blockedRecording,buffer)){blockedRecording=null;return;}
            if(blockedRecordingFrame==Time.frameCount)throw new InvalidOperationException("URUI 当前帧绘制记录清空失败。");
            try{buffer.Clear();blockedRecording=null;blockedRecordingFrame=-1;}
            catch(Exception error)
            {
                blockedRecordingFrame=Time.frameCount;
                throw new InvalidOperationException("URUI 绘制记录恢复失败。",error);
            }
        }
        void InvalidateRecording(bool clear)
        {
            if(!clear)return;
            effectSubmitted.Clear();
            if(buffer==null)return;
            try{ClearNativeRecording();buffer.Clear();}
            catch(Exception error)
            {
                blockedRecording=buffer;blockedRecordingFrame=Time.frameCount;
                Debug.LogException(error);
            }
        }
        public void NotifyExternalRenderResourcesChanged()=>InvalidateRecording(false);
    }
}
