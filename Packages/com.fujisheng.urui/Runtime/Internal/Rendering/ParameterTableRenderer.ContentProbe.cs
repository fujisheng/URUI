#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;using System.Collections.Generic;using System.Diagnostics;using System.Globalization;using System.IO;using System.Text;using UnityEngine;
namespace URUI.Internal {
 internal sealed partial class ParameterTableRenderer {
  [Serializable] internal sealed class BatchContentTotals {
   public int rebuilt,equalPayload,changedPayload,unchangedIds,noPrevious,epochMismatch,ordinalMismatch,stateMismatch,missingSnapshot,budgetSkipped,eventDrops;public long comparedBytes,compareTicks,stageTicks,uploadTicks,buildTicks,equalStageTicks,equalUploadTicks,equalBuildTicks,equalUploadRequestBytes;public long frequency=Stopwatch.Frequency;
  }
  internal struct BatchContentObservation {internal int frame,slot,count,reason;internal long compared,compareTicks,stageTicks;}
  readonly BatchContentTotals batchContent=new BatchContentTotals();readonly List<string> batchContentEvents=new List<string>();int contentFrame=-1;long contentFrameBytes;
  // Diagnostic only. Full ordered CPU payload, no retained Unity objects or extra getter/copy.
  static uint FloatBits(float x)=>unchecked((uint)BitConverter.SingleToInt32Bits(x));
  internal static bool EqualSourcePayload(SharedMesh a,SharedMesh b,ref long bytes){
   if(a.positions==null||b.positions==null||a.uv==null||b.uv==null||a.colors==null||b.colors==null||a.topology==null||b.topology==null)return false;
   if(a.vertices!=b.vertices||a.indices!=b.indices||a.positions.Length!=a.vertices||b.positions.Length!=b.vertices||a.uv.Length!=a.vertices||b.uv.Length!=b.vertices||a.colors.Length!=a.vertices||b.colors.Length!=b.vertices||a.topology.Length!=a.indices||b.topology.Length!=b.indices)return false;
   for(int i=0;i<a.vertices;i++){bytes+=48;var p=a.positions[i];var q=b.positions[i];var u=a.uv[i];var v=b.uv[i];var c=a.colors[i];var d=b.colors[i];if(FloatBits(p.x)!=FloatBits(q.x)||FloatBits(p.y)!=FloatBits(q.y)||FloatBits(p.z)!=FloatBits(q.z)||FloatBits(u.x)!=FloatBits(v.x)||FloatBits(u.y)!=FloatBits(v.y)||c.r!=d.r||c.g!=d.g||c.b!=d.b||c.a!=d.a)return false;}
   for(int i=0;i<a.indices;i++){bytes+=8;if(a.topology[i]!=b.topology[i])return false;}return true;
  }
  // reasons:0equal,1payload,2sameIDs,3noPrevious,4epoch,5ordinal,6state,7missing,8budget.
  BatchContentObservation ObserveBatchContent(ParameterFrame frame,int ordinal,int start,int end,long stageTicks){
   var t=new BatchContentObservation{frame=Time.frameCount,slot=start,count=end-start,reason=3,stageTicks=stageTicks};long begin=Stopwatch.GetTimestamp();
   if(GeometryContentProbe.Mode!=2)return t;
   if(current==null||ordinal>=current.batches.Count){batchContent.noPrevious++;goto Done;}
   if(current.epoch!=frame.stamp.epoch){t.reason=4;batchContent.epochMismatch++;goto Done;}
   var old=current.batches[ordinal];if(old.slot!=start||old.count!=end-start){t.reason=5;batchContent.ordinalMismatch++;goto Done;}
   if(!old.parameter||!StateOf(old.key).Equals(StateOf(frame.keys[start]))){t.reason=6;batchContent.stateMismatch++;goto Done;}
   bool sameIds=true;for(int i=start;i<end;i++)if(current.keys[i].geometry!=frame.keys[i].geometry){sameIds=false;break;}
   if(sameIds){t.reason=2;batchContent.unchangedIds++;goto Done;}
   if(contentFrame!=Time.frameCount){contentFrame=Time.frameCount;contentFrameBytes=0;}
   for(int i=start;i<end;i++){
    if(!sharedMeshes.TryGetValue((current.epoch,current.keys[i].geometry),out var a)||!sharedMeshes.TryGetValue((frame.stamp.epoch,frame.keys[i].geometry),out var b)||a.positions==null||b.positions==null){t.reason=7;batchContent.missingSnapshot++;goto Done;}
    long maximum=((long)a.vertices+b.vertices)*24+((long)a.indices+b.indices)*4;
    if(maximum<0||contentFrameBytes+maximum>4*1024*1024){t.reason=8;batchContent.budgetSkipped++;goto Done;}contentFrameBytes+=maximum;
    if(!EqualSourcePayload(a,b,ref t.compared)){t.reason=1;batchContent.changedPayload++;goto Done;}
   }
   t.reason=0;batchContent.equalPayload++;
   Done:t.compareTicks=Stopwatch.GetTimestamp()-begin;batchContent.comparedBytes+=t.compared;batchContent.compareTicks+=t.compareTicks;return t;
  }
  void CommitBatchContent(BatchContentObservation t,long uploadTicks,long buildTicks,long payload){
   if(GeometryContentProbe.Mode!=2)return;batchContent.rebuilt++;batchContent.stageTicks+=t.stageTicks;batchContent.uploadTicks+=uploadTicks;batchContent.buildTicks+=buildTicks;
   if(t.reason==0){batchContent.equalStageTicks+=t.stageTicks;batchContent.equalUploadTicks+=uploadTicks;batchContent.equalBuildTicks+=buildTicks;batchContent.equalUploadRequestBytes+=payload;}
   if(batchContentEvents.Count<8192)batchContentEvents.Add(string.Join(",",t.frame,t.slot,t.count,t.reason,t.compared,t.compareTicks,t.stageTicks,uploadTicks,buildTicks,payload));else batchContent.eventDrops++;
  }
  internal static void RunBatchContentTests(){
   SharedMesh Make(float x)=>new SharedMesh{vertices=1,indices=1,positions=new[]{new Vector3(x,0,0)},uv=new[]{Vector2.zero},colors=new[]{new Color32(1,2,3,4)},topology=new[]{0}};
   void Need(bool value,string why){if(!value)throw new Exception("BATCH_CONTENT_FAIL "+why);}long bytes=0;var a=Make(0);var b=Make(0);Need(EqualSourcePayload(a,b,ref bytes),"equal full payload across distinct owners");b.positions[0].x=BitConverter.Int32BitsToSingle(int.MinValue);Need(!EqualSourcePayload(a,b,ref bytes),"signedzero mismatch");b=Make(0);b.uv[0].x=.1f;Need(!EqualSourcePayload(a,b,ref bytes),"UV mismatch");b=Make(0);b.colors[0].a=5;Need(!EqualSourcePayload(a,b,ref bytes),"alpha mismatch");b=Make(0);b.topology[0]=1;Need(!EqualSourcePayload(a,b,ref bytes),"index mismatch");b=Make(2);var old=new[]{a,b};var reordered=new[]{b,a};Need(!EqualSourcePayload(old[0],reordered[0],ref bytes),"ordered sequence not set equality");b=Make(0);b.colors=null;Need(!EqualSourcePayload(a,b,ref bytes),"missing snapshot unknown");UnityEngine.Debug.Log("BATCH_CONTENT_PASS allFields signedzero UV alpha indices orderedSequence missingSnapshot");
  }
  void SaveBatchContent(string directory){if(GeometryContentProbe.Mode!=2)return;Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"batch-summary.json"),JsonUtility.ToJson(batchContent,true));File.WriteAllText(Path.Combine(directory,"batch-events.csv"),"frame,slot,count,reason,compared_bytes,compare_ticks,stage_ticks,upload_ticks,build_ticks,upload_request_bytes\n"+string.Join("\n",batchContentEvents));}
 }
}
#endif
