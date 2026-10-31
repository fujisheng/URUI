using System;using System.Runtime.InteropServices;using UnityEngine;
namespace RmlUnity {
 internal sealed partial class RmlParameterFrame {
  [StructLayout(LayoutKind.Sequential)] struct CoreKey {internal ulong geometry,texture;internal int enabled,x,y,w,h;internal uint mode;}
  [StructLayout(LayoutKind.Sequential)] struct CoreSummary {internal uint ready,fallback;internal ulong bytes;}
  [DllImport("RmlUnityBridge",CallingConvention=CallingConvention.Cdecl)] static extern int rui_core_frame_api_version();
  [DllImport("RmlUnityBridge",CallingConvention=CallingConvention.Cdecl)] static extern int rui_core_frame_copy(ref RuiFrameStampNative expected,IntPtr keys,IntPtr values,IntPtr worlds,int capacity,out CoreSummary summary);
  CoreKey[] coreKeys=Array.Empty<CoreKey>();bool coreCapturing,coreChecked,coreAvailable;
  internal bool NativeCoreReady{get;private set;}internal int NativeCoreReadyCommands{get;private set;}internal int NativeCoreFallbackCommands{get;private set;}internal long NativeCoreBytes{get;private set;}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  internal static Action AfterCorePinForProbe;internal static int CorePinsForProbe;
#endif
  internal void InvalidateNativeCore(){NativeCoreReady=false;NativeCoreReadyCommands=NativeCoreFallbackCommands=0;NativeCoreBytes=0;}
  internal bool CaptureFinalCore(){
   if(coreCapturing)return false;coreCapturing=true;InvalidateNativeCore();InvalidateOwnedSnapshot();SnapshotReused=NativePayloadReads=DecodedCommands=fastCommands=fallbackCommands=0;
   GCHandle kp=default,vp=default,wp=default;
   try{
    RmlHostCpuProbe.Point(RmlCpuStage.CommandRead);
    if(!coreChecked){coreChecked=true;coreAvailable=Marshal.SizeOf<CoreKey>()==40&&Marshal.SizeOf<RmlAffineParameter>()==32&&Marshal.SizeOf<Matrix4x4>()==64&&rui_core_frame_api_version()==1;}if(!coreAvailable)return false;
    if(RmlNative.rui_render_plan_stamp(out stamp)!=1||stamp.flags!=1||stamp.count>65536)return false;count=(int)stamp.count;
    if(coreKeys.Length<count)coreKeys=new CoreKey[Math.Min(65536,Math.Max(count,coreKeys.Length*2+16))];
    if(worlds.Length<count)worlds=new Matrix4x4[Math.Min(65536,Math.Max(count,worlds.Length*2+16))];if(parameters.Length<count)parameters=new RmlAffineParameter[Math.Min(65536,Math.Max(count,parameters.Length*2+16))];if(keys.Length<count)keys=new RmlParameterKey[Math.Min(65536,Math.Max(count,keys.Length*2+16))];
    kp=GCHandle.Alloc(coreKeys,GCHandleType.Pinned);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    CorePinsForProbe++;AfterCorePinForProbe?.Invoke();
#endif
    vp=GCHandle.Alloc(parameters,GCHandleType.Pinned);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    CorePinsForProbe++;
#endif
    wp=GCHandle.Alloc(worlds,GCHandleType.Pinned);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    CorePinsForProbe++;
#endif
    NativePayloadReads=1;int result=rui_core_frame_copy(ref stamp,kp.AddrOfPinnedObject(),vp.AddrOfPinnedObject(),wp.AddrOfPinnedObject(),coreKeys.Length,out var summary);
    if(result!=count||summary.ready+summary.fallback!=(uint)count||summary.bytes!=(ulong)count*136)return false;
    RmlHostCpuProbe.Point(RmlCpuStage.CommandDecode);
    for(int i=0;i<count;i++){
     var k=coreKeys[i];bool affine=true;
     if(k.mode==0){var m=worlds[i];var p=parameters[i];if(!RmlAffineParameter.TryPrepare(m,p.row0.x,p.row0.y,out worlds[i],out parameters[i],out affine))return false;}
     else if(k.mode!=1)return false;
     keys[i]=new RmlParameterKey{geometry=k.geometry,texture=k.texture,enabled=k.enabled,x=k.x,y=k.y,w=k.w,h=k.h,affine=affine};
    }
    NativeCoreReadyCommands=(int)summary.ready;NativeCoreFallbackCommands=(int)summary.fallback;NativeCoreBytes=(long)summary.bytes;fastCommands=NativeCoreReadyCommands;fallbackCommands=NativeCoreFallbackCommands;DecodedCommands=count;NativeCoreReady=true;return true;
   }catch(EntryPointNotFoundException){coreAvailable=false;return false;}catch(DllNotFoundException){coreAvailable=false;return false;}
   finally{if(wp.IsAllocated){wp.Free();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    CorePinsForProbe--;
#endif
   }if(vp.IsAllocated){vp.Free();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    CorePinsForProbe--;
#endif
   }if(kp.IsAllocated){kp.Free();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    CorePinsForProbe--;
#endif
   }coreCapturing=false;if(!NativeCoreReady)InvalidateNativeCore();}
  }
 }
}
