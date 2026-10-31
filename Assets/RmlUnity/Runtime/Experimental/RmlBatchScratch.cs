using System;using System.Collections.Generic;using UnityEngine;using System.Diagnostics;
namespace RmlUnity {
 // Synchronous CPU input only. Never owns a Mesh, resource identity, or GPU buffer.
 // One owner/one active lease; no global pool and no render-thread consumption.
 internal sealed class RmlBatchScratch {
  internal readonly List<Vector3> positions=new List<Vector3>();
  internal readonly List<Vector2> uv=new List<Vector2>(),slots=new List<Vector2>();
  internal readonly List<Color32> colors=new List<Color32>();
  internal readonly List<int> indices=new List<int>();
  internal const long MaxRetainedBytes=512*1024;
  bool active;long atBegin;
  internal long RetainedBytes=>(long)positions.Capacity*12+(long)(uv.Capacity+slots.Capacity)*8+(long)colors.Capacity*4+(long)indices.Capacity*4;
  internal long HighWaterBytes{get;private set;}internal long OverflowCount{get;private set;}internal long ClearTicks{get;private set;}internal long Leases{get;private set;}internal long ColdLeases{get;private set;}internal long CapacityGrowthEvents{get;private set;}
  internal void Begin(){if(active)throw new InvalidOperationException("CPU batch scratch already leased");active=true;Leases++;atBegin=RetainedBytes;if(atBegin==0)ColdLeases++;}
  internal void End(){if(!active)throw new InvalidOperationException("CPU batch scratch not leased");long begin=Stopwatch.GetTimestamp();long bytes=RetainedBytes;if(bytes>atBegin)CapacityGrowthEvents++;HighWaterBytes=Math.Max(HighWaterBytes,bytes);positions.Clear();uv.Clear();colors.Clear();slots.Clear();indices.Clear();active=false;if(bytes>MaxRetainedBytes){OverflowCount++;Drop();}ClearTicks+=Stopwatch.GetTimestamp()-begin;}
  internal void Drop(){if(active)throw new InvalidOperationException("Cannot drop active CPU batch scratch");positions.Capacity=uv.Capacity=colors.Capacity=slots.Capacity=indices.Capacity=0;}
 }
}
