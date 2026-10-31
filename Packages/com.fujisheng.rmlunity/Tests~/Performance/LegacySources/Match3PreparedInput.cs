using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using Unity.Profiling;
namespace RmlUnity {
 // Benchmark fixture only. No framework fast path and no default gameplay change.
 // Immutable records are generated before warmup; both real renderers consume them.
 public static class Match3PreparedInput {
  struct Entry {public bool opacity;public float x,y,s,ox,oy,os;public string text;}
  static Entry[] entries;static int cursor;static ulong consumed=1469598103934665603UL;
  static readonly ProfilerMarker ReadMarker=new ProfilerMarker("Match3.PreparedRead");
  public static bool Enabled{get;private set;}
  public static string Failure{get;private set;}
  public static int Count=>entries==null?0:entries.Length;
  public static int Consumed=>cursor;
  public static ulong Hash=>consumed;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset()=>Configure(false);
  public static void Configure(bool enable){Failure=null;Enabled=enable;entries=null;cursor=0;consumed=1469598103934665603UL;}
  static uint Bits(float v)=>unchecked((uint)BitConverter.SingleToInt32Bits(v));
  static ulong Mix(ulong h,uint v){unchecked{return(h^v)*1099511628211UL;}}
  static float Q(float v)=>float.Parse(v.ToString("0.###",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
  public static void Prepare(uint seed,int moves,int target,int gem,int rate,bool injectMismatch=false){
   if(!Enabled)return;if(rate<1||rate>1000)throw new ArgumentOutOfRangeException(nameof(rate));
   long begin=Stopwatch.GetTimestamp();var list=new List<Entry>();var game=new Match3Game(seed,moves,target,gem);
   for(int turn=0;turn<8&&game.Outcome==Match3Outcome.Playing;turn++){
    if(!Match3Rules.FindLegalMove(game.Snapshot(),out int from,out int to))throw new InvalidOperationException("Prepared replay no legal move");
    Append(list,game.TrySwap(from,to),1f/rate);
   }
   entries=list.ToArray();if(injectMismatch&&entries.Length>0)entries[0].x+=.25f;cursor=0;consumed=1469598103934665603UL;
   ulong hash=1469598103934665603UL;long textBytes=0;
   foreach(var e in entries){hash=Mix(hash,e.opacity?1u:0u);hash=Mix(Mix(Mix(hash,Bits(e.ox)),Bits(e.oy)),Bits(e.os));if(e.text!=null)textBytes+=e.text.Length*2L;}
   UnityEngine.Debug.Log("PREPARED_INPUT_READY entries="+entries.Length+" outputHash="+hash+" preparationMs="+((Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency).ToString("R",CultureInfo.InvariantCulture)+" valuePayloadBytes="+(entries.Length*28L)+" textUtf16Bytes="+textBytes+" managedOverheadUnknown=true");
  }
  static void T(List<Entry> a,float x,float y,float s){if(a.Count>=1000000)throw new InvalidOperationException("Prepared record budget exceeded");a.Add(new Entry{x=x,y=y,s=s,ox=Q(x),oy=Q(y),os=Q(s)});}
  static void O(List<Entry>a,float v){string text=v.ToString("0.###",CultureInfo.InvariantCulture);a.Add(new Entry{opacity=true,x=v,ox=float.Parse(text,CultureInfo.InvariantCulture),text=text});}
  static void Append(List<Entry>a,Match3Move move,float delta){foreach(var step in move.Steps){float elapsed=0;
   if(step.Type==Match3StepType.Swap||step.Type==Match3StepType.Rebound){int dx=(step.To%8-step.From%8)*102,dy=(step.To/8-step.From/8)*102;while(elapsed<Match3Motion.SwapSeconds){elapsed+=delta;float t=Match3Motion.EaseInOut(elapsed/Match3Motion.SwapSeconds),arc=Mathf.Sin(Mathf.PI*t)*14f,s=1f+Mathf.Sin(Mathf.PI*t)*.12f;T(a,dx*t,dy*t-arc,s);T(a,-dx*t,-dy*t+arc,s);}}
   else if(step.Type==Match3StepType.Clear){while(elapsed<Match3Motion.ClearSeconds){elapsed+=delta;float t=Mathf.Clamp01(elapsed/Match3Motion.ClearSeconds);foreach(int cell in step.Cells){O(a,1f-.78f*Match3Motion.EaseInOut(t));T(a,0,0,1f-.32f*Match3Motion.EaseInOut(t));O(a,Mathf.Sin(Mathf.PI*t));T(a,0,0,.55f+.75f*Match3Motion.EaseOutCubic(t));}}}
   else if(step.Type==Match3StepType.Fall){while(elapsed<Match3Motion.FallSeconds){elapsed+=delta;for(int i=0;i<64;i++){int source=step.SourceForCell==null?-1:step.SourceForCell[i];float distance=Mathf.Min(56f,source<0?102:Mathf.Max(0,(i/8-source/8)*102));float t=Match3Motion.FallProgress(elapsed,i%8);T(a,0,-distance*(1f-t),1f+.08f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t)));}}}
  }}
  static InvalidOperationException Reject(string message){Failure=message;return new InvalidOperationException(message);}
  static Entry Read(bool opacity,float x,float y,float s){
   if(!Enabled||entries==null||cursor>=entries.Length)throw Reject("Prepared stream unavailable/exhausted at "+cursor);
   var e=entries[cursor];if(e.opacity!=opacity||Bits(e.x)!=Bits(x)||Bits(e.y)!=Bits(y)||Bits(e.s)!=Bits(s))throw Reject("Prepared stream value/order mismatch at "+cursor);
   cursor++;consumed=Mix(consumed,e.opacity?1u:0u);consumed=Mix(Mix(Mix(consumed,Bits(e.ox)),Bits(e.oy)),Bits(e.os));return e;
  }
  public static Vector3 Transform(float x,float y,float scale){using(ReadMarker.Auto()){var e=Read(false,x,y,scale);return new Vector3(e.ox,e.oy,e.os);}}
  public static float Opacity(float raw,out string text){using(ReadMarker.Auto()){var e=Read(true,raw,0,0);text=e.text;return e.ox;}}
  public static void Complete(){if(!Enabled)return;if(cursor!=Count)throw Reject("Prepared stream incomplete "+cursor+"/"+Count);UnityEngine.Debug.Log("PREPARED_INPUT_DONE entries="+cursor+" outputHash="+consumed);}
  public static void RunOracle(){
   try{Configure(true);Prepare(0x4D4F4F4E,18,18,2,240);if(Count<1000)throw new Exception("Prepared oracle empty");
   var first=entries[0];bool rejected=false;try{Read(!first.opacity,first.x,first.y,first.s);}catch(InvalidOperationException){rejected=true;}if(!rejected||cursor!=0)throw new Exception("Prepared mismatch advanced cursor");
   foreach(var e in entries){if(Bits(Q(e.x))!=Bits(e.ox)||(!e.opacity&&(Bits(Q(e.y))!=Bits(e.oy)||Bits(Q(e.s))!=Bits(e.os))))throw new Exception("Prepared oracle precision mismatch");Read(e.opacity,e.x,e.y,e.s);}
   Complete();rejected=false;try{Read(false,0,0,1);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Prepared exhaustion accepted");
   int count=Count;Configure(false);if(Count!=0||Consumed!=0)throw new Exception("Prepared reset retained data");UnityEngine.Debug.Log("PREPARED_ORACLE_PASS entries="+count+" bitExact order mismatch exhaustion reset");}finally{Configure(false);}
  }
 }
}
