using System;using System.Runtime.InteropServices;using System.Threading;using System.Diagnostics;
namespace RmlUnity {
 // Diagnostic, own-thread public Darwin clocks only. No Unity/private native API.
 internal sealed class MacThreadCpuClock {
  [StructLayout(LayoutKind.Sequential)] struct Timespec {internal long sec,nsec;}
  [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="clock_gettime",SetLastError=true)] static extern int GetTime(int id,out Timespec value);
  [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="clock_getres",SetLastError=true)] static extern int GetResolution(int id,out Timespec value);
  internal struct Stamp {internal bool valid;internal int thread;internal long cpuNs,wallNs,bracketNs;}
  internal readonly int Owner=Thread.CurrentThread.ManagedThreadId;internal readonly bool Available;internal readonly long ResolutionNs;internal int Error{get;private set;}
  internal MacThreadCpuClock(){
   #if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
   try{if(GetResolution(16,out var r)!=0||!Convert(r,out long n)){Error=1;return;}ResolutionNs=n;Available=true;}catch(DllNotFoundException){Error=2;}catch(EntryPointNotFoundException){Error=3;}
   #else
   Error=4;
   #endif
  }
  static bool Convert(Timespec t,out long ns){ns=-1;if(t.sec<0||t.nsec<0||t.nsec>=1000000000)return false;try{ns=checked(t.sec*1000000000+t.nsec);return true;}catch(OverflowException){return false;}}
  internal bool TryRead(out Stamp s){s=default;if(!Available){Error=5;return false;}if(Thread.CurrentThread.ManagedThreadId!=Owner){Error=6;return false;}
   try{if(GetTime(4,out var a)!=0||GetTime(16,out var c)!=0||GetTime(4,out var b)!=0||!Convert(a,out long before)||!Convert(c,out long cpu)||!Convert(b,out long after)||after<before){Error=7;return false;}
   s=new Stamp{valid=true,thread=Owner,cpuNs=cpu,wallNs=before+(after-before)/2,bracketNs=after-before};Error=0;return true;}catch(DllNotFoundException){Error=2;return false;}catch(EntryPointNotFoundException){Error=3;return false;}
  }
  internal static bool TryDelta(Stamp a,Stamp b,out long cpu,out long wall){cpu=wall=-1;if(!a.valid||!b.valid||a.thread!=b.thread||a.cpuNs>b.cpuNs||a.wallNs>=b.wallNs)return false;cpu=b.cpuNs-a.cpuNs;wall=b.wallNs-a.wallNs;return true;}
 }
 public sealed class MacThreadCpuOracle:UnityEngine.MonoBehaviour {
  static volatile int sink;static void Need(bool x,string why){if(!x)throw new Exception("THREAD_CPU_FAIL "+why);}
  static void Burn(int ms){long end=Stopwatch.GetTimestamp()+Stopwatch.Frequency*ms/1000;int x=1;while(Stopwatch.GetTimestamp()<end){for(int j=0;j<100;j++)x=unchecked(x*1664525+1013904223);}sink=x;}
  public static void Run(){var c=new MacThreadCpuClock();Need(c.Available,"clock unavailable error="+c.Error);Need(c.TryRead(out var a),"read");Thread.Sleep(50);Need(c.TryRead(out var b),"sleep read");Need(MacThreadCpuClock.TryDelta(a,b,out var sleepC,out var sleepW),"sleep delta");Need(c.TryRead(out a),"busy start");Burn(40);Need(c.TryRead(out b),"busy read");Need(MacThreadCpuClock.TryDelta(a,b,out var busyC,out var busyW),"busy delta");
   OtherThreadSample(c,false,out var otherC,out var otherW);bool injected=false;try{OtherThreadSample(c,true,out _,out _);}catch(InvalidOperationException e){injected=e.Message=="injected worker-scope failure";}Need(injected&&Volatile.Read(ref activeWorkers)==0,"failure path joined worker");Match3ThreadCpuWindows.TestDistributions();
   Need(sleepC<sleepW/5&&otherC<otherW/5&&busyC>busyW/2,"CPU clock distinguishes own work and sleeps");var invalid=a;invalid.valid=false;Need(!MacThreadCpuClock.TryDelta(invalid,b,out _,out _),"invalid unknown");invalid=a;invalid.thread++;Need(!MacThreadCpuClock.TryDelta(invalid,b,out _,out _),"cross-thread unknown");invalid=a;invalid.cpuNs=b.cpuNs+1;Need(!MacThreadCpuClock.TryDelta(invalid,b,out _,out _),"decreasing CPU unknown");
   var times=new double[11];for(int t=0;t<times.Length;t++){long start=Stopwatch.GetTimestamp();for(int i=0;i<10000;i++)Need(c.TryRead(out _),"overhead read");times[t]=(Stopwatch.GetTimestamp()-start)*1e9/Stopwatch.Frequency/10000;}Array.Sort(times);
   UnityEngine.Debug.Log("THREAD_CPU_ORACLE_PASS resolutionNs="+c.ResolutionNs+" sleepCpu="+sleepC+" sleepWall="+sleepW+" busyCpu="+busyC+" busyWall="+busyW+" otherBusyMainCpu="+otherC+" otherBusyWall="+otherW+" stampReadNsP50="+times[5].ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" stampReadNsP95="+times[10].ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" snapshotCalls=3 invalidIsUnknown=True");
  }
  static int activeWorkers;
  static void OtherThreadSample(MacThreadCpuClock c,bool inject,out long cpu,out long wall){
   cpu=wall=-1;int running=1;bool rejected=false,started=false,joined=false;Exception failure=null,workerFailure=null;var ready=new ManualResetEventSlim(false);
   var worker=new Thread(()=>{Interlocked.Increment(ref activeWorkers);try{rejected=!c.TryRead(out _);ready.Set();while(Volatile.Read(ref running)!=0)Burn(5);}catch(Exception e){workerFailure=e;}finally{ready.Set();Interlocked.Decrement(ref activeWorkers);}}){IsBackground=true};
   try{worker.Start();started=true;Need(ready.Wait(2000),"worker ready timeout");if(inject)throw new InvalidOperationException("injected worker-scope failure");Need(c.TryRead(out var a),"other start");Thread.Sleep(50);Need(c.TryRead(out var b),"other read");Need(MacThreadCpuClock.TryDelta(a,b,out cpu,out wall),"other delta");}
   catch(Exception e){failure=e;}
   finally{Volatile.Write(ref running,0);joined=!started||worker.Join(2000);if(joined)ready.Dispose();}
   if(!joined)throw new Exception("THREAD_CPU_FAIL worker cleanup timeout",failure);if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();if(workerFailure!=null)throw new Exception("THREAD_CPU_FAIL worker exception",workerFailure);Need(rejected,"owner-thread mismatch rejected");
  }
  void Start(){try{Run();Finish(0);}catch(Exception e){UnityEngine.Debug.LogException(e);Finish(1);}}
  static void Finish(int n){
   #if UNITY_EDITOR
   UnityEditor.EditorApplication.Exit(n);
   #else
   UnityEngine.Application.Quit(n);
   #endif
  }
 }
}
