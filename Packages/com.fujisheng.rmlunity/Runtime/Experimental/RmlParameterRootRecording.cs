using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.Rendering;using Unity.Profiling;
namespace RmlUnity {
 // Updates the existing camera attachment. Payload updates execute on the same
 // graphics queue before draws; no CPU SetData or per-camera mutable packet.
 internal sealed class RmlParameterRootRecording {
  internal sealed class Lease {internal RmlParameterTableRenderer.Plan plan;internal GraphicsBuffer buffer;internal CommandBuffer barrier;internal GraphicsFence fence;internal bool uncertain;}
  readonly RmlParameterTableRenderer geometry;readonly List<Lease> retired=new List<Lease>(5);readonly int thread;readonly Action append;
  CommandBuffer root;RmlParameterTableRenderer.Plan plan;GraphicsBuffer parameters;int capacity;bool closing,disposed,recording;RmlParameterFrame frame;int width,height;
  RmlParameterTableRenderer.Stats stats;internal RmlParameterTableRenderer.Stats Last=>stats;internal bool Faulted{get;private set;}internal bool Registered;internal string FailureReason{get;private set;}
  #if UNITY_EDITOR || DEVELOPMENT_BUILD
  static readonly ProfilerMarker CommandsMarker=new ProfilerMarker("Rml.ParameterCommands"),RetireMarker=new ProfilerMarker("Rml.ParameterRetire");
  #endif
  static readonly ProfilerMarker Marker=new ProfilerMarker("Rml.ParameterRootRecord");
  internal bool NativePinned{get;}internal bool NativeIncremental{get;}internal bool NativePrepared{get;}internal bool DirectSourceValidation{set=>geometry.DirectSourceValidation=value;}internal bool LazySources{get;}internal bool ReusesScratch{get;}internal bool ReusesBatches{get;}internal bool SharesResources{get;}internal int RetiredLimit=>SharesResources?32:4;
  internal RmlParameterRootRecording(CommandBuffer root,Shader legacy,Shader parameter,bool shareResources=false,bool reuseBatches=false,bool reuseBatchScratch=false,bool lazySourceMeshes=false,bool nativePrepared=false,bool nativeIncremental=false,bool nativePinned=false){NativePinned=shareResources&&nativePrepared&&nativePinned;NativeIncremental=shareResources&&nativePrepared&&nativeIncremental;NativePrepared=shareResources&&nativePrepared;LazySources=shareResources&&reuseBatches&&lazySourceMeshes;ReusesScratch=shareResources&&reuseBatches&&reuseBatchScratch;ReusesBatches=shareResources&&reuseBatches;SharesResources=shareResources;this.root=root;geometry=new RmlParameterTableRenderer(legacy,parameter,shareResources:shareResources,reuseBatches:reuseBatches,reuseBatchScratch:reuseBatchScratch,lazySourceMeshes:lazySourceMeshes,nativePrepared:nativePrepared,nativeIncremental:nativeIncremental,nativePinned:nativePinned);thread=System.Threading.Thread.CurrentThread.ManagedThreadId;append=Append;}
  bool CorrectThread=>thread==System.Threading.Thread.CurrentThread.ManagedThreadId;
  void Append(){
   #if UNITY_EDITOR || DEVELOPMENT_BUILD
   using var commandsMarker=CommandsMarker.Auto();
   #endif
root.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.Ortho(0,width,height,0,-1,1));if(frame.count>0){root.SetBufferData(parameters,frame.parameters,0,0,frame.count);stats.parameterRequestedBytes=(long)frame.count*32;}geometry.AppendDraws(root,plan,frame,parameters,ref stats);}
  internal static void RecordAtomically(CommandBuffer target,Action record){target.Clear();try{record();}catch{target.Clear();throw;}}
  bool Retire(RmlParameterTableRenderer.Plan oldPlan,GraphicsBuffer oldBuffer){
   #if UNITY_EDITOR || DEVELOPMENT_BUILD
   using var retireMarker=RetireMarker.Auto();
   #endif
   var lease=new Lease{plan=oldPlan,buffer=oldBuffer};retired.Add(lease);
   try{lease.barrier=new CommandBuffer{name="Rml root resource retirement"};lease.fence=lease.barrier.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation,SynchronisationStageFlags.PixelProcessing);Graphics.ExecuteCommandBuffer(lease.barrier);return true;}catch{lease.uncertain=true;Faulted=true;return false;}
  }
  #if UNITY_EDITOR || DEVELOPMENT_BUILD
  internal bool HoldCompletedFencesForProbe; // Holds even completed leases; never manufactures GPU completion.
  #endif
  void Collect(){
   #if UNITY_EDITOR || DEVELOPMENT_BUILD
   if(HoldCompletedFencesForProbe)return;
   #endif
   for(int i=retired.Count-1;i>=0;i--){var lease=retired[i];if(lease.uncertain)continue;bool passed;try{passed=lease.fence.passed;}catch{lease.uncertain=true;Faulted=true;continue;}if(!passed)continue;if(lease.plan!=null)geometry.ReleasePlan(lease.plan);lease.buffer?.Dispose();lease.barrier.Release();retired.RemoveAt(i);}geometry.Collect();}
  internal RmlParameterTableRenderer.Result Record(RmlParameterFrame value,int uiWidth,int uiHeight){
   using var marker=Marker.Auto();stats=default;
   if(disposed||closing)return RmlParameterTableRenderer.Result.Disposed;if(!CorrectThread||recording||Faulted||value==null||value.count<0||value.count>65536||value.stamp.count!=value.count||(!value.NativeCoreReady&&value.commands.Length<value.count)||value.keys.Length<value.count||value.worlds.Length<value.count||value.parameters.Length<value.count||uiWidth<=0||uiHeight<=0)return RmlParameterTableRenderer.Result.Unsupported;
   Collect();if(Faulted)return RmlParameterTableRenderer.Result.Unsupported;
   bool same=plan!=null&&plan.epoch==value.stamp.epoch&&RmlParameterKey.SameSequence(plan.keys,plan.keys.Length,value.keys,value.count);
   if(retired.Count>=RetiredLimit&&(!same||value.count>capacity)){FailureReason="retired lease budget count="+retired.Count;Faulted=true;return RmlParameterTableRenderer.Result.Backpressure;}
   RmlParameterTableRenderer.Plan next=null;GraphicsBuffer allocated=null;bool transferred=false;recording=true;
   try{
    var result=geometry.AcquirePlan(value,out next);stats=geometry.Last;if(result!=RmlParameterTableRenderer.Result.Success){FailureReason=geometry.LastFailure;return result;}
        int newCapacity=capacity;if(value.count>capacity){newCapacity=Math.Min(65536,Math.Max(value.count,Math.Max(16,capacity*2)));allocated=new GraphicsBuffer(GraphicsBuffer.Target.Structured,newCapacity,32);}
    bool changed=next!=plan;root.Clear();
    if(changed||allocated!=null){var oldPlan=changed?plan:null;var oldBuffer=allocated!=null?parameters:null;if(changed)plan=null;if(allocated!=null)parameters=null;if((oldPlan!=null||oldBuffer!=null)&&!Retire(oldPlan,oldBuffer))throw new InvalidOperationException("Resource retirement enqueue uncertain");}
    if(changed){plan=next;transferred=true;}else{geometry.ReleasePlan(next);transferred=true;}
    if(allocated!=null){parameters=allocated;allocated=null;capacity=newCapacity;}
    frame=value;width=uiWidth;height=uiHeight;RecordAtomically(root,append);stats.retainedParameterBytes=(long)capacity*32;stats.plans=retired.Count+1;return RmlParameterTableRenderer.Result.Success;
   }catch(Exception error){FailureReason=error.Message;if(!transferred&&next!=null)geometry.ReleasePlan(next);allocated?.Dispose();Faulted=true;Close();return RmlParameterTableRenderer.Result.InvalidResource;}
   finally{frame=null;recording=false;}
  }
  internal void Close(){if(closing||disposed)return;closing=true;if(!CorrectThread){Faulted=true;return;}try{root?.Clear();}catch{Faulted=true;var unknown=new Lease{plan=plan,buffer=parameters,uncertain=true};retired.Add(unknown);plan=null;parameters=null;root=null;return;}root=null;var oldPlan=plan;var oldBuffer=parameters;plan=null;parameters=null;Retire(oldPlan,oldBuffer); // Also covers uploads for a cached plan never drawn.
  }
  #if UNITY_EDITOR || DEVELOPMENT_BUILD
  internal string SaveGeometryProbe(string directory)=>geometry.SaveGeometryProbe(directory);
  #endif
  internal bool TryDispose(){if(disposed)return true;if(!CorrectThread)return false;if(!closing)Close();Collect();if(retired.Count!=0)return false;if(!geometry.TryDispose())return false;disposed=true;return true;}
 }
 internal sealed class RmlParameterRootRetirement:MonoBehaviour {
  static RmlParameterRootRetirement owner;static int live;readonly List<RmlParameterRootRecording> pending=new List<RmlParameterRootRecording>(4);
  internal static int LiveCount=>live;
  internal static bool Admission=>live<4;
  internal static RmlParameterRootRecording Create(CommandBuffer target,Shader legacy,Shader parameter,bool shareResources=false,bool reuseBatches=false,bool reuseBatchScratch=false,bool lazySourceMeshes=false,bool nativePrepared=false,bool nativeIncremental=false,bool nativePinned=false){if(!Admission)return null;var result=new RmlParameterRootRecording(target,legacy,parameter,shareResources,reuseBatches,reuseBatchScratch,lazySourceMeshes,nativePrepared,nativeIncremental,nativePinned){Registered=true};live++;return result;}
  static void Completed(RmlParameterRootRecording value){if(value.Registered){value.Registered=false;live--;}}
  internal static void Retire(RmlParameterRootRecording value){value.Close();if(value.TryDispose()){Completed(value);return;}if(!owner){var go=new GameObject("Rml root retirement"){hideFlags=HideFlags.HideAndDontSave};DontDestroyOnLoad(go);owner=go.AddComponent<RmlParameterRootRetirement>();}owner.pending.Add(value);}
  void Update(){for(int i=pending.Count-1;i>=0;i--)if(pending[i].TryDispose()){Completed(pending[i]);pending.RemoveAt(i);}}
 }
}
