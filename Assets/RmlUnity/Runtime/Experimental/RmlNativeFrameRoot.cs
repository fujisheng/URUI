using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace RmlUnity {
 // An attached camera buffer owns draw recording. Native CPU views never outlive
 // the synchronous Unity API calls; immutable Unity resources retire separately.
 internal sealed class RmlNativeFrameRoot {
  sealed class MeshEntry {internal ulong id;internal Mesh mesh;internal int refs;internal long bytes;}
  sealed class TextureEntry {internal (ulong,ulong) id;internal Texture2D texture;internal int refs;internal long bytes;}
  sealed class DrawBinding {internal RmlNativePrepared.Draw state;internal MeshEntry mesh;internal Texture texture;}
  sealed class Plan {internal ulong epoch,id;internal readonly List<DrawBinding> draws=new List<DrawBinding>();internal readonly List<TextureEntry> textures=new List<TextureEntry>();internal readonly List<Mesh> failedMeshes=new List<Mesh>(1);}
  sealed class Retirement {internal Plan plan;internal GraphicsBuffer buffer;internal CommandBuffer barrier;internal GraphicsFence fence;internal bool uncertain;}
  readonly Dictionary<ulong,MeshEntry> meshes=new Dictionary<ulong,MeshEntry>();
  readonly Dictionary<(ulong,ulong),TextureEntry> textures=new Dictionary<(ulong,ulong),TextureEntry>();
  readonly List<Retirement> retired=new List<Retirement>();readonly Material parameter,legacy;readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
  readonly Action<Mesh> failedUpload;Plan building;
  readonly int thread=System.Threading.Thread.CurrentThread.ManagedThreadId;RmlBorrowedFrame source;readonly CommandBuffer attachment;CommandBuffer root;Plan current;GraphicsBuffer buffer;int capacity;long retainedBytes;bool recording,closing,closed,disposed;internal bool Registered;internal int RetiredLimit=>32;
  internal CommandBuffer Attachment=>attachment;internal bool CloseComplete=>closed;internal int UnsafeFrame{get;private set;}=-1;internal bool Faulted{get;private set;}internal bool CanFallback{get;private set;}=true;internal string FailureReason{get;private set;}internal RmlParameterTableRenderer.Stats Last{get;private set;}internal int CommandCount{get;private set;}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  internal static Action<string> FaultForProbe;internal bool HoldCompletedFencesForProbe;internal int RetiredForProbe=>retired.Count;
  static void Probe(string at){FaultForProbe?.Invoke(at);}
#else
  static void Probe(string at){}
#endif
  internal RmlNativeFrameRoot(CommandBuffer target,Shader legacyShader,Shader parameterShader){attachment=target;root=target;failedUpload=RetireFailedUpload;source=new RmlBorrowedFrame();try{legacy=new Material(legacyShader){hideFlags=HideFlags.HideAndDontSave};parameter=new Material(parameterShader){hideFlags=HideFlags.HideAndDontSave};}catch{if(legacy)UnityEngine.Object.Destroy(legacy);if(parameter)UnityEngine.Object.Destroy(parameter);source.Dispose();throw;}}
  bool ClearRoot(string stage){try{Probe(stage);root?.Clear();return true;}catch{if(CanFallback)UnsafeFrame=Time.frameCount;CanFallback=false;Faulted=true;return false;}}
  bool CorrectThread=>thread==System.Threading.Thread.CurrentThread.ManagedThreadId;
  void Charge(long bytes){if(bytes<0||retainedBytes+bytes>64L*1024*1024)throw new InvalidOperationException("Native frame Unity immutable resource budget");}
  void RetireFailedUpload(Mesh mesh){if(building==null)throw new InvalidOperationException("No native upload transaction");building.failedMeshes.Add(mesh);}
  void Release(Plan plan){if(plan==null)return;foreach(var mesh in plan.failedMeshes)if(mesh)UnityEngine.Object.Destroy(mesh);foreach(var d in plan.draws){var e=d.mesh;if(--e.refs<0)throw new InvalidOperationException("Native frame mesh ownership");if(e.refs==0){meshes.Remove(e.id);retainedBytes-=e.bytes;UnityEngine.Object.Destroy(e.mesh);}}foreach(var e in plan.textures){if(--e.refs<0)throw new InvalidOperationException("Native frame texture ownership");if(e.refs==0){textures.Remove(e.id);retainedBytes-=e.bytes;UnityEngine.Object.Destroy(e.texture);}}}
  bool Retire(Plan plan,GraphicsBuffer data){if(plan==null&&data==null)return true;var lease=new Retirement{plan=plan,buffer=data};retired.Add(lease);try{lease.barrier=new CommandBuffer{name="Rml native final-frame resource retirement"};lease.fence=lease.barrier.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,SynchronisationStageFlags.PixelProcessing);Graphics.ExecuteCommandBuffer(lease.barrier);return true;}catch{lease.uncertain=true;Faulted=true;return false;}}
  void Collect(){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
   if(HoldCompletedFencesForProbe)return;
#endif
   for(int i=retired.Count-1;i>=0;i--){var e=retired[i];if(e.uncertain)continue;bool done;try{done=e.fence.passed;}catch{e.uncertain=true;Faulted=true;continue;}if(!done)continue;Release(e.plan);e.buffer?.Dispose();e.barrier?.Release();retired.RemoveAt(i);}}
  Texture AcquireTexture(Plan plan,ulong epoch,ulong id,ref RmlParameterTableRenderer.Stats stats){if(id==0)return Texture2D.whiteTexture;var key=(epoch,id);foreach(var t in plan.textures)if(t.id==key)return t.texture;
   if(textures.TryGetValue(key,out var existing)){plan.textures.Add(existing);existing.refs++;stats.textureHits++;return existing.texture;}
   if(RmlNative.rui_get_texture_info(id,out int w,out int h,out int bytes)!=1||w<=0||h<=0||(long)w*h*4!=bytes||bytes>32*1024*1024)throw new InvalidOperationException("Native frame texture snapshot size");Charge(bytes);var pixels=new byte[bytes];if(RmlNative.rui_copy_texture(id,pixels,bytes)!=1)throw new InvalidOperationException("Native frame texture snapshot copy");var value=new Texture2D(w,h,TextureFormat.RGBA32,false,false){name="Rml native final-frame immutable texture",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
   try{value.LoadRawTextureData(pixels);value.Apply(false,true);}catch{UnityEngine.Object.Destroy(value);throw;}
   var entry=new TextureEntry{id=key,texture=value,bytes=bytes};bool added=false;try{textures.Add(key,entry);retainedBytes+=bytes;added=true;plan.textures.Add(entry);entry.refs++;}catch{if(added){textures.Remove(key);retainedBytes-=bytes;}UnityEngine.Object.Destroy(value);throw;}stats.textureCopies++;stats.textureCopyBytes+=bytes;stats.textureRequestedBytes+=bytes;return value;
  }
  Plan Build(RmlBorrowedFrame.Packet p,ref RmlParameterTableRenderer.Stats stats){var next=new Plan{epoch=p.epoch,id=p.plan};building=next;try{long total=0;for(int i=0;i<p.drawCount;i++){var d=source.Draw(p,i);if(d.batch==0||d.count==0||d.slot>=p.count||(ulong)d.slot+d.count>p.count||d.kind>1||d.vertices==0||d.vertices>1048576||d.indices==0||d.indices>3145728)throw new InvalidOperationException("Native frame draw contract");long bytes=(long)d.vertices*32+(long)d.indices*4;total+=bytes;if(total>16L*1024*1024)throw new InvalidOperationException("Native frame plan budget");Texture texture=AcquireTexture(next,p.epoch,d.texture,ref stats);
     bool created=false;if(!meshes.TryGetValue(d.batch,out var mesh)){Charge(bytes);var value=source.Upload(p,d,failedUpload);mesh=new MeshEntry{id=d.batch,mesh=value,bytes=bytes};try{meshes.Add(d.batch,mesh);retainedBytes+=bytes;created=true;}catch{UnityEngine.Object.Destroy(value);throw;}stats.meshBuilds++;stats.batchBuilds++;stats.geometryRequestedBytes+=bytes;stats.nativeFullBytes+=bytes;}else{if(!mesh.mesh)throw new InvalidOperationException("Native immutable Mesh destroyed");stats.batchHits++;}
     try{next.draws.Add(new DrawBinding{state=d,mesh=mesh,texture=texture});mesh.refs++;}catch{if(created&&mesh.refs==0){meshes.Remove(mesh.id);retainedBytes-=mesh.bytes;UnityEngine.Object.Destroy(mesh.mesh);}throw;}Probe("after-mesh");
    }stats.planBuilds++;return next;
   }catch{Retire(next,null);throw;}finally{building=null;}}
  void Bind(DrawBinding d,GraphicsBuffer data){var s=d.state;properties.Clear();properties.SetTexture("_MainTex",d.texture);properties.SetFloat("_RmlClipEnabled",s.enabled);properties.SetVector("_RmlClipRect",new Vector4(s.x,s.y,unchecked(s.x+s.w),unchecked(s.y+s.h)));if(s.kind==0)properties.SetBuffer("_RmlTransforms",data);}
  internal RmlParameterTableRenderer.Result Record(int width,int height,RenderTexture target=null){
   var stats=new RmlParameterTableRenderer.Stats();Last=stats;if(!CorrectThread||closing||disposed||recording||Faulted||width<=0||height<=0)return RmlParameterTableRenderer.Result.Unsupported;Collect();if(Faulted)return RmlParameterTableRenderer.Result.InvalidResource;
   RmlBorrowedFrame.Packet packet=default;bool leased=false,published=false;Plan next=null;GraphicsBuffer allocated=null;recording=true;
   try{RmlHostCpuProbe.Point(RmlCpuStage.NativeFramePrepare);packet=source.Acquire();leased=true;CommandCount=(int)packet.count;stats.nativeActive=1;stats.nativePrepares=1;stats.nativeBuilds=(int)packet.built;stats.nativeReuses=(int)packet.reused;stats.nativeBuiltBytes=(long)packet.builtBytes;stats.nativeRetainedBytes=(long)packet.retained;
    RmlHostCpuProbe.Point(RmlCpuStage.PlanPrepare);bool changed=current==null||current.epoch!=packet.epoch||current.id!=packet.plan;bool grow=packet.count>capacity;if(retired.Count>=32&&(changed||grow))throw new InvalidOperationException("Native frame retired resource backpressure");Probe("after-acquire");if(closing)throw new InvalidOperationException("Native frame closed during acquire");
    if(changed)next=Build(packet,ref stats);else stats.batchHits=(int)packet.drawCount;
    int nextCapacity=capacity;if(grow){nextCapacity=Math.Min(65536,Math.Max((int)packet.count,Math.Max(16,capacity*2)));allocated=new GraphicsBuffer(GraphicsBuffer.Target.Structured,nextCapacity,32);}Probe("before-record");
    var recordingPlan=changed?next:current;var recordingBuffer=allocated??buffer;
    RmlHostCpuProbe.Point(RmlCpuStage.RecordCommands);if(!ClearRoot("clear-initial"))throw new InvalidOperationException("Native frame attachment clear uncertain");try{if(target){root.SetRenderTarget(target);root.SetViewport(new Rect(0,0,width,height));}root.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(0,width,height,0,-1,1));source.RecordParameters(packet,root,recordingBuffer);stats.parameterRequestedBytes=(long)packet.count*32;Probe("after-parameters");foreach(var d in recordingPlan.draws){Bind(d,recordingBuffer);if(d.state.kind==0)root.DrawMesh(d.mesh.mesh,Matrix4x4.identity,parameter,0,0,properties);else{root.DrawMesh(d.mesh.mesh,source.World(packet,d.state.slot),legacy,0,0,properties);stats.fallbacks++;}stats.draws++;Probe("after-draw");}}
    catch{ClearRoot("clear-rollback");throw;}
    RmlHostCpuProbe.Point(RmlCpuStage.HostMetrics);if(closing)throw new InvalidOperationException("Native frame closed during recording");
    // Publication has no graphics execution. Resources referenced by the sole
    // camera attachment become owned before old-resource barriers are enqueued.
    var oldPlan=changed?current:null;var oldBuffer=grow?buffer:null;if(changed){current=next;next=null;}if(grow){buffer=allocated;allocated=null;capacity=nextCapacity;}published=true;stats.retainedParameterBytes=(long)capacity*32;stats.plans=retired.Count+1;Last=stats;
    if(!Retire(oldPlan,oldBuffer))throw new InvalidOperationException("Native frame retirement enqueue uncertain");return RmlParameterTableRenderer.Result.Success;
   }catch(Exception error){FailureReason=error.Message;Faulted=true;ClearRoot("clear-reject");if(next!=null||allocated!=null){if(CanFallback)Retire(next,allocated);else retired.Add(new Retirement{plan=next,buffer=allocated,uncertain=true});next=null;allocated=null;}if(!published)Last=stats;return RmlParameterTableRenderer.Result.InvalidResource;}
   finally{try{if(leased)source.Release(packet);}finally{recording=false;if(closing&&!closed&&CorrectThread)CompleteClose();}}
  }
  internal void Close(){if(closed||disposed)return;closing=true;if(!CorrectThread){Faulted=true;CanFallback=false;return;}if(recording)return;CompleteClose();}
  void CompleteClose(){closed=true;if(!ClearRoot("clear-close")){retired.Add(new Retirement{plan=current,buffer=buffer,uncertain=true});current=null;buffer=null;root=null;source?.Dispose();source=null;return;}root=null;var old=current;var data=buffer;current=null;buffer=null;Retire(old,data);source?.Dispose();source=null;}
  internal bool TryDispose(){if(disposed)return true;if(!CorrectThread||recording)return false;if(!closed)Close();if(!closed)return false;Collect();if(retired.Count!=0)return false;source?.Dispose();source=null;if(meshes.Count!=0||textures.Count!=0)throw new InvalidOperationException("Native frame resource ownership leak");UnityEngine.Object.Destroy(parameter);UnityEngine.Object.Destroy(legacy);disposed=true;return true;}
 }
 internal sealed class RmlNativeFrameRetirement:MonoBehaviour {
  static RmlNativeFrameRetirement owner;static int live;readonly List<RmlNativeFrameRoot> pending=new List<RmlNativeFrameRoot>();internal static int LiveCount=>live;internal static bool Admission=>live<4;
  internal static RmlNativeFrameRoot Create(CommandBuffer target,Shader legacy,Shader parameter){if(!Admission)return null;var value=new RmlNativeFrameRoot(target,legacy,parameter){Registered=true};live++;return value;}
  static void Done(RmlNativeFrameRoot value){if(value.Registered){value.Registered=false;live--;}}
  internal static void Retire(RmlNativeFrameRoot value){value.Close();if(value.TryDispose()){Done(value);return;}if(!owner){var go=new GameObject("Rml native final-frame retirement"){hideFlags=HideFlags.HideAndDontSave};DontDestroyOnLoad(go);owner=go.AddComponent<RmlNativeFrameRetirement>();}owner.pending.Add(value);}
  void Update(){for(int i=pending.Count-1;i>=0;i--)if(pending[i].TryDispose()){Done(pending[i]);pending.RemoveAt(i);}}
 }
}
