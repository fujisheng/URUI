using System;using System.IO;using System.Text;using UnityEngine;
namespace RmlUnity {
 // Diagnostic-only sequential intervals. Never interpreted as language/GPU time.
 internal sealed class RmlCpuIntervals {
  readonly long[] cpu,wall;bool open,valid;int stage;long firstCpu,firstWall,lastCpu,lastWall;
  public RmlCpuIntervals(int stages){if(stages<=0||stages>64)throw new ArgumentOutOfRangeException(nameof(stages));cpu=new long[stages];wall=new long[stages];}
  public bool Begin(int next,long c,long w){if(open)return false;valid=false;if(next<0||next>=cpu.Length||c<0||w<0)return false;Array.Clear(cpu,0,cpu.Length);Array.Clear(wall,0,wall.Length);stage=next;firstCpu=lastCpu=c;firstWall=lastWall=w;open=valid=true;return true;}
  public bool Point(int next,long c,long w){if(!open||!valid)return false;if(next<0||next>=cpu.Length||c<lastCpu||w<lastWall){valid=false;return false;}cpu[stage]+=c-lastCpu;wall[stage]+=w-lastWall;lastCpu=c;lastWall=w;stage=next;return true;}
  public bool End(long c,long w){bool ok=Point(stage,c,w);open=false;valid=ok&&lastWall>firstWall;return valid;}
  public long Cpu(int i)=>valid&&!open&&i>=0&&i<cpu.Length?cpu[i]:-1;
  public long Wall(int i)=>valid&&!open&&i>=0&&i<wall.Length?wall[i]:-1;
  public long TotalCpu()=>valid&&!open?lastCpu-firstCpu:-1;
  public long TotalWall()=>valid&&!open?lastWall-firstWall:-1;
 }
 internal enum RmlCpuStage {Input,NativeUpdate,Events,DrawSetup,NativeRender,ParameterSetup,CommandRead,CommandDecode,PlanPrepare,RecordCommands,HostMetrics,Legacy,NativeFramePrepare,Count}
 internal sealed class RmlHostCpuProbe {
  const int Capacity=8192;static RmlHostCpuProbe active;
  struct Row {internal string phase;internal int frame,step,host,commands,copies,builds;internal long cpu,wall,brackets;internal bool valid,complete;}
  readonly MacThreadCpuClock clock=new MacThreadCpuClock();readonly RmlCpuIntervals intervals=new RmlCpuIntervals((int)RmlCpuStage.Count);readonly Row[] rows=new Row[Capacity];readonly long[] cpus=new long[Capacity*(int)RmlCpuStage.Count],walls=new long[Capacity*(int)RmlCpuStage.Count];readonly int period,offset;readonly Func<int> step;
  string phase;int eligible,depth,count,dropped,errors;bool sampled,read,complete;long brackets;Row current;
  RmlHostCpuProbe(int every,int offset,Func<int> step){if(every<0||every>1024||offset<0||(every>0&&offset>=every))throw new ArgumentOutOfRangeException(nameof(every));period=every;this.offset=offset;this.step=step;}
  internal static void Configure(int every,int offset,Func<int> step){if(active!=null&&active.depth!=0)throw new InvalidOperationException("Probe configuration inside Host scope");active=new RmlHostCpuProbe(every,offset,step);if(!active.clock.Available)throw new InvalidOperationException("Host CPU clock unavailable");Debug.Log("HOST_CPU_CONFIG period="+every+" offset="+offset+" capacity="+Capacity+" owner="+active.clock.Owner);}
  internal static void Phase(string value){if(active!=null)active.phase=value;}
  internal readonly struct Scope:IDisposable {readonly RmlHostCpuProbe owner;readonly RmlUnityHost host;internal Scope(RmlHostCpuProbe owner,RmlUnityHost host){this.owner=owner;this.host=host;}public void Dispose(){owner?.Leave(host);}}
  internal static Scope Enter(RmlUnityHost host){var p=active;if(p==null)return default;if(System.Threading.Thread.CurrentThread.ManagedThreadId!=p.clock.Owner)return default;p.depth++;if(p.depth==1)p.Start(host);return new Scope(p,host);}
  void Start(RmlUnityHost host){sampled=false;complete=false;if(period==0||phase==null)return;int n=eligible++;if(n%period!=offset)return;if(count>=Capacity){dropped++;return;}current=new Row{phase=phase,frame=Time.frameCount,step=step!=null?step():-1,host=host.GetInstanceID()};brackets=0;read=clock.TryRead(out var a);if(read){brackets=a.bracketNs;read=intervals.Begin((int)RmlCpuStage.Input,a.cpuNs,a.wallNs);}sampled=true;}
  internal static void Point(RmlCpuStage next){var p=active;if(p==null||p.depth!=1||!p.sampled||!p.read)return;if(!p.clock.TryRead(out var b)){p.read=false;return;}p.brackets+=b.bracketNs;p.read=p.intervals.Point((int)next,b.cpuNs,b.wallNs);}
  internal static void Complete(){if(active!=null&&active.depth==1)active.complete=true;}
  void Leave(RmlUnityHost host){if(depth<=0){errors++;return;}if(depth--!=1)return;if(!sampled)return;bool end=clock.TryRead(out var b);brackets+=b.bracketNs;bool ok=read&&end&&intervals.End(b.cpuNs,b.wallNs);if(!ok)intervals.End(-1,-1);current.valid=ok;current.complete=complete;current.cpu=ok?intervals.TotalCpu():-1;current.wall=ok?intervals.TotalWall():-1;current.brackets=brackets;current.commands=host?host.DrawCommands:-1;current.copies=host?host.ParameterGeometryCopyCalls:-1;current.builds=host?host.ParameterBatchBuilds:-1;rows[count]=current;int at=count*(int)RmlCpuStage.Count;for(int i=0;i<(int)RmlCpuStage.Count;i++){cpus[at+i]=ok?intervals.Cpu(i):-1;walls[at+i]=ok?intervals.Wall(i):-1;}count++;sampled=false;}
  internal static void Save(string path){var p=active;if(p==null)return;if(p.depth!=0)throw new InvalidOperationException("Cannot serialize active Host CPU scope");var b=new StringBuilder();b.AppendLine("phase,frame,step,host,valid,complete,total_cpu_ns,total_wall_ns,bracket_ns,commands,geometry_copies,batch_builds"+Header());for(int r=0;r<p.count;r++){var x=p.rows[r];b.Append(x.phase).Append(',').Append(x.frame).Append(',').Append(x.step).Append(',').Append(x.host).Append(',').Append(x.valid?1:0).Append(',').Append(x.complete?1:0).Append(',').Append(x.cpu).Append(',').Append(x.wall).Append(',').Append(x.brackets).Append(',').Append(x.commands).Append(',').Append(x.copies).Append(',').Append(x.builds);for(int i=0;i<(int)RmlCpuStage.Count;i++)b.Append(',').Append(p.cpus[r*(int)RmlCpuStage.Count+i]).Append(',').Append(p.walls[r*(int)RmlCpuStage.Count+i]);b.AppendLine();}File.WriteAllText(path,b.ToString());Debug.Log("HOST_CPU_SAVED "+path+" rows="+p.count+" dropped="+p.dropped+" errors="+p.errors+" period="+p.period+" offset="+p.offset);active=null;}
  static string Header(){var b=new StringBuilder();for(int i=0;i<(int)RmlCpuStage.Count;i++)b.Append(',').Append(((RmlCpuStage)i).ToString()).Append("_cpu_ns,").Append(((RmlCpuStage)i).ToString()).Append("_wall_ns");return b.ToString();}
 }
}
