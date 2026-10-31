using System;using System.Collections.Generic;using UnityEngine;
namespace URUI.Internal {
 internal sealed partial class ParameterTableRenderer {
  internal sealed class NativeMesh {internal ulong id;internal Mesh value;internal long bytes;internal int references;}
  readonly Dictionary<ulong,NativeMesh> nativeMeshes=new Dictionary<ulong,NativeMesh>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  internal Action BeforeNativePlanForProbe;internal Action<Mesh> AfterNativeUploadForProbe;
#endif
  readonly bool nativePrepared,nativeIncremental,nativePinned;NativePrepared nativeOwner;long nativeGpuPayload;
  void ReleaseNative(NativeMesh entry){if(entry.references<=0)throw new InvalidOperationException("Unbalanced native GPU batch");if(--entry.references!=0)return;if(nativeMeshes.TryGetValue(entry.id,out var active)&&ReferenceEquals(entry,active))nativeMeshes.Remove(entry.id);nativeGpuPayload-=entry.bytes;if(entry.value)UnityEngine.Object.Destroy(entry.value);}
  Plan BuildNative(ParameterFrame frame,ref Stats stats){
   if(nativeOwner==null)nativeOwner=new NativePrepared(nativeIncremental,nativePinned);var packet=nativeOwner.Acquire(frame);Plan plan=null;
   try{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
   BeforeNativePlanForProbe?.Invoke();
#endif
   plan=new Plan{owner=this,epoch=frame.stamp.epoch,resources=frame.stamp.resources,keys=new ParameterKey[frame.count],nativeMeshes=new List<NativeMesh>(),sharedMeshes=shareResources?new List<SharedMesh>():null,sharedTextures=shareResources?new List<SharedTexture>():null};Array.Copy(frame.keys,plan.keys,frame.count);
    stats.nativePrepares++;stats.nativeBuilds+=(int)packet.built;stats.nativeReuses+=(int)packet.reused;stats.nativeRetainedBytes=(long)packet.retained;stats.nativeBuiltBytes=(long)packet.builtBytes;
    for(int n=0;n<packet.draws;n++){
     var d=nativeOwner.draws[n];if(d.slot>=frame.count||d.count==0||(ulong)d.slot+d.count>(ulong)frame.count||d.kind>1)throw new InvalidOperationException("Native draw ordinal");var key=frame.keys[d.slot];
     if(key.geometry==0||d.texture!=key.texture||d.enabled!=key.enabled||d.x!=key.x||d.y!=key.y||d.w!=key.w||d.h!=key.h||(d.kind==0)!=key.affine)throw new InvalidOperationException("Native draw state mismatch");
     Texture(plan,d.texture,ref stats);long bytes=(long)d.vertices*32+(long)d.indices*4;if(bytes<=0||plan.geometryBytes+bytes>MaxPlanGeometryBytes)throw new InvalidOperationException("Native plan geometry budget");plan.geometryBytes+=bytes;
     if(!nativeMeshes.TryGetValue(d.batch,out var entry)||!entry.value){
      AdmitPayload(bytes);var nextEntry=new NativeMesh{id=d.batch,bytes=bytes};Mesh mesh=null;
      try{mesh=nativeOwner.Upload(packet,d);var copied=nativeOwner.LastCopy;stats.nativeCopyBytes+=(long)copied.copiedBytes;stats.nativeFullBytes+=(long)copied.fullBytes;stats.nativePossibleBytes+=(long)copied.possibleBytes;if(copied.mode==1)stats.nativeDeltaCopies++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
       AfterNativeUploadForProbe?.Invoke(mesh);
#endif
       nextEntry.value=mesh;nativeMeshes[d.batch]=nextEntry;nativeGpuPayload+=bytes;entry=nextEntry;
      }catch{if(mesh)UnityEngine.Object.Destroy(mesh);throw;}stats.meshBuilds++;stats.batchBuilds++;stats.geometryCopies++;stats.geometryCopyBytes+=(long)nativeOwner.LastCopy.copiedBytes;stats.geometryRequestedBytes+=bytes;
     }else{stats.batchHits++;stats.geometryHits++;}
     try{plan.nativeMeshes.Add(entry);entry.references++;}catch{if(entry.references==0){nativeMeshes.Remove(entry.id);nativeGpuPayload-=entry.bytes;if(entry.value)UnityEngine.Object.Destroy(entry.value);}throw;}
     plan.batches.Add(new Batch{mesh=entry.value,parameter=d.kind==0,slot=(int)d.slot,count=(int)d.count,key=key,nativeShared=entry});
    }
    if(NativeMethods.urui_render_plan_stamp(out var now)!=1||now.flags!=1||now.epoch!=frame.stamp.epoch||now.commands!=frame.stamp.commands||now.resources!=frame.stamp.resources||now.count!=frame.stamp.count)throw new InvalidOperationException("Native preparation invalidated before publish");
    stats.planBuilds++;stats.nativeStagingBytes=nativeOwner.StagingBytes;return plan;
   }catch{if(plan!=null)DestroyPlan(plan);throw;}finally{nativeOwner.Release(packet);}
  }
 }
}
