using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace URUI.Internal {
 internal sealed partial class RenderHost {
  // The gate belongs to this actual attachment. Enable/disable and a successful
  // cleanup retry cannot erase a failed-frame rejection. Clear never retires GPU resources.
  CommandBuffer nativeBlockedAttachment;int nativeBlockedFrame=-1;NativeFrameRoot nativePendingStop;
  void ObserveNativeStop(NativeFrameRoot writer){
   if(writer==null||!ReferenceEquals(writer.Attachment,buffer))return;
   if(!writer.CloseComplete){nativePendingStop=writer;return;}
   if(ReferenceEquals(nativePendingStop,writer))nativePendingStop=null;
   if(!writer.CanFallback){nativeBlockedAttachment=buffer;nativeBlockedFrame=writer.UnsafeFrame>=0?writer.UnsafeFrame:Time.frameCount;}
  }
  static InvalidOperationException NativeRecordingBlocked(string reason)=>new InvalidOperationException("Native frame recording blocked: "+reason);
  void RequireNativeStopPermission(){
   if(nativeBlockedAttachment==null&&nativePendingStop==null)return;
   if(buffer==null)throw NativeRecordingBlocked("no current attachment");
   if(nativePendingStop!=null&&!ReferenceEquals(nativePendingStop.Attachment,buffer))nativePendingStop=null;
   if(nativeBlockedAttachment!=null&&!ReferenceEquals(nativeBlockedAttachment,buffer)){nativeBlockedAttachment=null;nativeBlockedFrame=-1;}
   if(nativePendingStop!=null){var pending=nativePendingStop;if(!pending.CloseComplete)throw NativeRecordingBlocked("CPU recording close is pending");ObserveNativeStop(pending);}
   if(nativeBlockedAttachment==null)return;
   if(nativeBlockedFrame==Time.frameCount)throw NativeRecordingBlocked("cleanup failed in this frame");
   try{NativeHostClearProbe("host-clear-recover");buffer.Clear();}
   catch{nativeBlockedFrame=Time.frameCount;throw NativeRecordingBlocked("explicit recovery Clear failed");}
   nativeBlockedAttachment=null;nativeBlockedFrame=-1;
  }
  static void NativeHostClearProbe(string at){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
   NativeFrameRoot.FaultForProbe?.Invoke(at);
#endif
  }
  void TryClearNativeHostRecording(){
   var target=buffer;if(target==null)return;
   try{NativeHostClearProbe("host-clear-cleanup");target.Clear();}
   catch{nativeBlockedAttachment=target;nativeBlockedFrame=Time.frameCount;}
  }
 }
}
