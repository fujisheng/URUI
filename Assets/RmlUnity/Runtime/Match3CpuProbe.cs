using System.Collections;
using System.Diagnostics;
using Unity.Profiling;
namespace RmlUnity {
 public static class Match3CpuProbe {
  static readonly ProfilerMarker ViewStep=new ProfilerMarker("Match3.ViewStep");
  public static bool Enabled;
  public static double ViewMs,RulesMs,PrepareMs;
  public static long Start()=>Enabled?Stopwatch.GetTimestamp():0;
  public static void EndRules(long t){if(Enabled)RulesMs+=(Stopwatch.GetTimestamp()-t)*1000.0/Stopwatch.Frequency;}
  public static void EndPrepare(long t){if(Enabled)PrepareMs+=(Stopwatch.GetTimestamp()-t)*1000.0/Stopwatch.Frequency;}
  public static bool Step(IEnumerator routine){using var marker=ViewStep.Auto();if(!Enabled)return routine.MoveNext();long t=Stopwatch.GetTimestamp();try{return routine.MoveNext();}finally{ViewMs+=(Stopwatch.GetTimestamp()-t)*1000.0/Stopwatch.Frequency;}}
  public static void Reset(){ViewMs=RulesMs=PrepareMs=0;}
 }
}
