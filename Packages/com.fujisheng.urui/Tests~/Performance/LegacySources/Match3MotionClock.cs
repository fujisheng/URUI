using System;using UnityEngine;
namespace RmlUnity {
 // Shared game replay clock, not a renderer optimization. Normal play uses real delta.
 public static class Match3MotionClock {
  static int rate;static ulong sequence=1469598103934665603UL;static Match3Step last;static ulong stepHash;
  public static bool Fixed=>rate>0;public static float Delta=>Fixed?1f/rate:Time.unscaledDeltaTime;
  public static long Ticks{get;private set;}public static ulong Sequence=>sequence;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Initialize()=>Reset(0);
  public static void Reset(int ticksPerSecond){if(ticksPerSecond<0||ticksPerSecond>1000)throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));rate=ticksPerSecond;Ticks=0;sequence=1469598103934665603UL;last=null;stepHash=0;}
  static ulong Mix(ulong hash,ulong value){unchecked{return(hash^value)*1099511628211UL;}}
  static ulong ArrayHash(ulong h,int[] values){h=Mix(h,(ulong)(values==null?0:values.Length+1));if(values!=null)foreach(int value in values)h=Mix(h,unchecked((uint)value));return h;}
  public static void Record(Match3Step step,float elapsed){if(!Fixed)return;if(!ReferenceEquals(last,step)){last=step;ulong h=Mix(1469598103934665603UL,(uint)step.Type);h=Mix(h,unchecked((uint)step.From));h=Mix(h,unchecked((uint)step.To));h=Mix(h,(uint)step.Chain);h=ArrayHash(h,step.Board);h=ArrayHash(h,step.Cells);stepHash=ArrayHash(h,step.SourceForCell);}sequence=Mix(Mix(sequence,stepHash),unchecked((uint)BitConverter.SingleToInt32Bits(elapsed)));Ticks++;}
  public static void RecordDelay(float seconds,float elapsed){if(!Fixed)return;sequence=Mix(Mix(Mix(sequence,ulong.MaxValue),unchecked((uint)BitConverter.SingleToInt32Bits(seconds))),unchecked((uint)BitConverter.SingleToInt32Bits(elapsed)));Ticks++;}
 }
}
