using System;using System.Runtime.InteropServices;using UnityEngine;using UnityEngine.Rendering;
namespace RmlUnity {
 // Copy-only additive ABI. No pointer is retained across the synchronous C call.
 // CPU lease ownership is separate from Unity Mesh/GraphicsFence ownership.
 internal sealed partial class RmlNativePrepared:IDisposable {
  const string Dll="RmlUnityBridge";
  [StructLayout(LayoutKind.Sequential)] internal struct Vertex {internal float x,y,z;internal byte r,g,b,a;internal float u,v,slot,pad;}
  [StructLayout(LayoutKind.Sequential)] internal struct Draw {internal ulong batch,texture;internal uint slot,count,vertices,indices,kind;internal int enabled,x,y,w,h;}
  [StructLayout(LayoutKind.Sequential)] internal struct Packet {internal ulong lease,session,epoch,plan,resources,retained,builtBytes;internal uint draws,commands,built,reused,verticesCopied,indicesCopied,stride,version;}
  [StructLayout(LayoutKind.Sequential)] internal struct DeltaInfo {internal ulong baseBatch,fullBytes,possibleBytes,copiedBytes;internal uint mode,vertexRanges,indexRanges,reserved;}
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_delta_api_version();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_delta_enable(ulong session,int enabled);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_copy_incremental(ulong session,ulong lease,ulong batch,ulong baseBatch,[Out]Vertex[] vertices,int vc,[Out]uint[] indices,int ic,out DeltaInfo info);
  [DllImport(Dll,EntryPoint="rui_prepared_copy_incremental",CallingConvention=CallingConvention.Cdecl)] static extern int CopyRaw(ulong session,ulong lease,ulong batch,ulong baseBatch,IntPtr vertices,int vc,IntPtr indices,int ic,out DeltaInfo info);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  [ThreadStatic] internal static int PinsForProbe;internal static Action AfterVertexPinForProbe;
#endif
  int CopyPinned(Packet p,Draw d,ulong baseBatch,Vertex[] v,uint[] ix,out DeltaInfo info){
   GCHandle vp=default,ip=default;
   try{vp=GCHandle.Alloc(v,GCHandleType.Pinned);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    PinsForProbe++;AfterVertexPinForProbe?.Invoke();
#endif
    ip=GCHandle.Alloc(ix,GCHandleType.Pinned);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    PinsForProbe++;
#endif
    return CopyRaw(session,p.lease,d.batch,baseBatch,vp.AddrOfPinnedObject(),v.Length,ip.AddrOfPinnedObject(),ix.Length,out info);
   }finally{if(ip.IsAllocated){ip.Free();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    PinsForProbe--;
#endif
   }if(vp.IsAllocated){vp.Free();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    PinsForProbe--;
#endif
   }}
  }
  internal DeltaInfo LastCopy{get;private set;}readonly bool incremental,pinnedPayload; // Private bounded CPU bases; never Unity/GPU buffers.
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_api_version();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern ulong rui_prepared_create();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_destroy(ulong session);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_acquire(ulong session,ref RuiFrameStampNative expected,[In]byte[] mask,int count,out Packet packet);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_copy_draws(ulong session,ulong lease,[Out]Draw[] draws,int capacity);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_copy_batch(ulong session,ulong lease,ulong batch,[Out]Vertex[] vertices,int vc,[Out]uint[] indices,int ic);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern int rui_prepared_release(ulong session,ulong lease);
  internal static bool Available {get{try{return rui_prepared_api_version()==1;}catch(EntryPointNotFoundException){return false;}catch(DllNotFoundException){return false;}}}
  static readonly VertexAttributeDescriptor[] Layout={new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2),new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,2)};
  readonly int thread=System.Threading.Thread.CurrentThread.ManagedThreadId;ulong session,activeLease;bool active;
  byte[] mask=Array.Empty<byte>();internal Draw[] draws=Array.Empty<Draw>();Vertex[] vertices=Array.Empty<Vertex>();uint[] indices=Array.Empty<uint>();
  internal long RetainedBytes{get;private set;}
  internal long StagingBytes=>(long)vertices.Length*32+(long)indices.Length*4+(long)draws.Length*56+mask.Length+(cpuPayloads?.Bytes??0);
  internal RmlNativePrepared(bool incremental=false,bool pinnedPayload=false){if(Marshal.SizeOf<Vertex>()!=32||Marshal.SizeOf<Draw>()!=56||Marshal.SizeOf<Packet>()!=88||Marshal.OffsetOf<Vertex>(nameof(Vertex.r)).ToInt32()!=12||Marshal.OffsetOf<Vertex>(nameof(Vertex.u)).ToInt32()!=16||Marshal.OffsetOf<Vertex>(nameof(Vertex.slot)).ToInt32()!=24)throw new InvalidOperationException("Native prepared ABI layout");session=rui_prepared_create();if(session==0)throw new InvalidOperationException("Native prepared session unavailable");try{this.pinnedPayload=(incremental||pinnedPayload)&&rui_prepared_delta_api_version()==1;this.incremental=incremental&&this.pinnedPayload;if(this.incremental&&rui_prepared_delta_enable(session,1)!=1)throw new InvalidOperationException("Native incremental session");if(this.incremental)cpuPayloads=new PayloadCache();}catch(EntryPointNotFoundException){this.incremental=false;this.pinnedPayload=false;}catch{rui_prepared_destroy(session);session=0;throw;}}
  void Check(){if(session==0||thread!=System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Native prepared owner thread/disposed");}
  internal Packet Acquire(RmlParameterFrame frame){Check();if(active)throw new InvalidOperationException("Native prepared reentrant lease");if(mask.Length<frame.count)mask=new byte[frame.count];for(int i=0;i<frame.count;i++)mask[i]=frame.keys[i].affine?(byte)1:(byte)0;
   int result=rui_prepared_acquire(session,ref frame.stamp,mask,frame.count,out var p);if(result!=1)throw new InvalidOperationException("Native prepared acquire "+result);active=true;activeLease=p.lease;RetainedBytes=(long)p.retained;
   try{if(p.session!=session||p.epoch!=frame.stamp.epoch||p.commands!=frame.count||p.draws>65536||p.stride!=32||p.version!=1)throw new InvalidOperationException("Native prepared packet contract");if(draws.Length<p.draws)draws=new Draw[p.draws];if(rui_prepared_copy_draws(session,p.lease,draws,draws.Length)!=(int)p.draws)throw new InvalidOperationException("Native prepared descriptors");return p;}catch{Release(p);throw;}}
  internal Mesh Upload(Packet p,Draw d){Check();if(!active||p.session!=session||p.lease!=activeLease||d.vertices==0||d.vertices>1048576||d.indices==0||d.indices>3145728)throw new InvalidOperationException("Native prepared upload contract");
   if(incremental)return UploadIncremental(p,d);
   if(vertices.Length<d.vertices)vertices=new Vertex[d.vertices];if(indices.Length<d.indices)indices=new uint[d.indices];if(pinnedPayload){if(CopyPinned(p,d,0,vertices,indices,out var direct)!=1)throw new InvalidOperationException("Native pinned full copy");LastCopy=direct;}else{if(rui_prepared_copy_batch(session,p.lease,d.batch,vertices,vertices.Length,indices,indices.Length)!=1)throw new InvalidOperationException("Native prepared payload copy");LastCopy=new DeltaInfo{fullBytes=(ulong)d.vertices*32+(ulong)d.indices*4,copiedBytes=(ulong)d.vertices*32+(ulong)d.indices*4,possibleBytes=(ulong)d.vertices*32+(ulong)d.indices*4};}return UploadMesh(vertices,(int)d.vertices,indices,(int)d.indices);}
  internal static Mesh UploadMesh(Vertex[] v,int vc,uint[] ix,int ic){var mesh=new Mesh{name="Rml native prepared immutable batch",indexFormat=IndexFormat.UInt32};try{mesh.SetVertexBufferParams(vc,Layout);if(mesh.GetVertexBufferStride(0)!=32||mesh.GetVertexAttributeOffset(VertexAttribute.Color)!=12||mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0)!=16||mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1)!=24)throw new InvalidOperationException("Unity packed vertex layout");mesh.SetVertexBufferData(v,0,0,vc,0,MeshUpdateFlags.DontRecalculateBounds);mesh.SetIndexBufferParams(ic,IndexFormat.UInt32);mesh.SetIndexBufferData(ix,0,0,ic,MeshUpdateFlags.DontRecalculateBounds);mesh.subMeshCount=1;mesh.SetSubMesh(0,new SubMeshDescriptor(0,ic,MeshTopology.Triangles){firstVertex=0,vertexCount=vc},MeshUpdateFlags.DontRecalculateBounds);mesh.bounds=new Bounds(Vector3.zero,new Vector3(1e9f,1e9f,1e6f));return mesh;}catch{UnityEngine.Object.Destroy(mesh);throw;}}
  internal void Release(Packet p){Check();if(!active||p.session!=session||p.lease!=activeLease)throw new InvalidOperationException("Native prepared unbalanced lease");int result=rui_prepared_release(session,p.lease);active=false;activeLease=0;if((long)vertices.Length*32+(long)indices.Length*4>4L*1024*1024){vertices=Array.Empty<Vertex>();indices=Array.Empty<uint>();}if(result!=1)throw new InvalidOperationException("Native prepared release "+result);}
  public void Dispose(){Check();if(active)throw new InvalidOperationException("Native prepared active lease dispose");int result=rui_prepared_destroy(session);if(result!=1)throw new InvalidOperationException("Native prepared close "+result);session=0;RetainedBytes=0;cpuPayloads?.Clear();vertices=Array.Empty<Vertex>();indices=Array.Empty<uint>();draws=Array.Empty<Draw>();mask=Array.Empty<byte>();}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  internal static void DeltaContract(){
   void Need(bool value,string m){if(!value)throw new Exception("NATIVE_DELTA_CONTRACT "+m);}
   Need(Marshal.SizeOf<DeltaInfo>()==48,"info ABI48");var bounded=new PayloadCache(512,2);var one=bounded.Get(0,3,3);one.batch=92;bounded.Get(1,3,3);bounded.Get(1,3,3);bounded.Get(2,3,3);Need(bounded.Evictions==1&&bounded.Bytes==216,"LRU bounded eviction");Need(!ReferenceEquals(bounded.Get(0,3,3),one),"evicted base cannot alias");Need(bounded.Get(9,int.MaxValue,3)==null&&bounded.Bytes<=512,"oversize fallback bounded");bounded.Clear();Need(bounded.Bytes==0,"clear releases arrays");Need(RmlNative.rui_create(256,256,null)==1,"create");RmlNativePrepared owner=null,oracle=null;Mesh a=null,b=null,c=null;Color32 oldColor=default;
   try{RmlNative.rui_render_plan_enable(1);Need(RmlNative.rui_load_document("<rml><head><style>body{margin:0;}div{position:absolute;width:80px;height:80px;background-color:#f02070a0;}</style></head><body><div id='delta-a'/><div id='delta-b' style='left:12px;'/></body></rml>")==1,"load");RmlNative.rui_update();RmlNative.rui_render();var f=new RmlParameterFrame();Need(f.Capture(),"capture");owner=new RmlNativePrepared(true);oracle=new RmlNativePrepared(false,true);var p=owner.Acquire(f);var stale=p;Draw staleDraw=owner.draws[0];try{bool pinFailed=false;AfterVertexPinForProbe=()=>throw new InvalidOperationException("injected second-pin allocation boundary");try{owner.Upload(p,owner.draws[0]);}catch(InvalidOperationException){pinFailed=true;}finally{AfterVertexPinForProbe=null;}Need(pinFailed&&PinsForProbe==0,"partial pin failure cleanup");a=owner.Upload(p,owner.draws[0]);Need(PinsForProbe==0,"synchronous pins released");oldColor=a.colors32[0];Need(owner.LastCopy.mode==0,"first full");}finally{owner.Release(p);}Need(RmlNative.rui_set_style("delta-a","background-color","#80f020c0")==1,"mutate");RmlNative.rui_update();RmlNative.rui_render();Need(f.Capture(),"changed capture");p=owner.Acquire(f);try{bool staleRejected=false,memberRejected=false;try{var wrong=owner.Upload(stale,staleDraw);UnityEngine.Object.DestroyImmediate(wrong);}catch(InvalidOperationException){staleRejected=true;}try{var wrong=owner.Upload(p,staleDraw);UnityEngine.Object.DestroyImmediate(wrong);}catch(InvalidOperationException){memberRejected=true;}Debug.Log("NATIVE_DELTA_REVIEW_CHECK stale="+staleRejected+" member="+memberRejected);Need(staleRejected&&memberRejected,"stale packet and absent member must reject before cache reuse");bool releaseRejected=false;try{owner.Release(stale);}catch(InvalidOperationException){releaseRejected=true;}Need(releaseRejected,"stale release preserves active lease");b=owner.Upload(p,owner.draws[0]);Need(owner.LastCopy.mode==1&&owner.LastCopy.copiedBytes<owner.LastCopy.fullBytes,"expected strict base delta copy");}finally{owner.Release(p);}var fresh=oracle.Acquire(f);try{c=oracle.Upload(fresh,oracle.draws[0]);}finally{oracle.Release(fresh);}Need(b.vertexCount==c.vertexCount,"length");for(int i=0;i<b.vertexCount;i++)Need(b.vertices[i]==c.vertices[i]&&b.uv[i]==c.uv[i]&&b.colors32[i].Equals(c.colors32[i])&&b.uv2[i]==c.uv2[i],"fresh oracle attributes");Need(a.colors32[0].Equals(oldColor),"old Mesh unchanged");for(int i=0;i<b.triangles.Length;i++)Need(b.triangles[i]==c.triangles[i],"fresh oracle indices");Debug.Log("NATIVE_DELTA_CPU_CACHE_ORACLE_PASS copied="+owner.LastCopy.copiedBytes+" full="+owner.LastCopy.fullBytes);
   }finally{owner?.Dispose();oracle?.Dispose();RmlNative.rui_destroy();if(a)UnityEngine.Object.DestroyImmediate(a);if(b)UnityEngine.Object.DestroyImmediate(b);if(c)UnityEngine.Object.DestroyImmediate(c);}
  }
  internal static void Contract(){
   void Need(bool v,string m){if(!v)throw new Exception("NATIVE_PREPARED_CONTRACT "+m);}
   Need(Available,"API1");Need(RmlNative.rui_create(256,256,null)==1,"create");RmlNativePrepared owner=null;Mesh a=null,b=null;
   try{Need(RmlNative.rui_render_plan_enable(1)==1,"enable");Need(RmlNative.rui_load_document("<rml><head><style>body{margin:0;}div{position:absolute;width:80px;height:80px;background-color:#ff008080;}</style></head><body><div/><div style='left:12px;'/></body></rml>")==1,"load");RmlNative.rui_update();RmlNative.rui_render();var f=new RmlParameterFrame();Need(f.Capture()&&f.count==2,"frame");owner=new RmlNativePrepared();var p=owner.Acquire(f);
    try{Need(p.draws==1&&p.built==1,"native grouping hit");var d=owner.draws[0];a=owner.Upload(p,d);Need(a.vertexCount==(int)d.vertices&&a.GetVertexBufferStride(0)==32,"actual upload");var av=a.vertices;var ac=a.colors32;var at=a.triangles;var au=a.uv;int offset=0,index=0;
     for(int n=0;n<f.count;n++){Need(RmlNative.rui_get_geometry_info(f.keys[n].geometry,out int vc,out int ic)==1,"source info");var original=new VertexNative[vc];var ids=new int[ic];Need(RmlNative.rui_copy_geometry(f.keys[n].geometry,original,vc,ids,ic)==1,"oracle copy");for(int j=0;j<vc;j++){var q=original[j];Need(av[offset+j]==new Vector3(q.x,q.y,0)&&au[offset+j]==new Vector2(q.u,q.v)&&ac[offset+j].r==q.r&&ac[offset+j].a==q.a,"packed source oracle");Need(a.uv2[offset+j].x==n,"instance slot");}for(int j=0;j<ic;j++)Need(at[index+j]==offset+ids[j],"ordered index");offset+=vc;index+=ic;}
     // Public Mesh API owns a CPU copy before the same staging arrays are reused.
     owner.vertices[0].x+=91;owner.vertices[0].a=1;owner.indices[0]=owner.indices[1];b=UploadMesh(owner.vertices,(int)d.vertices,owner.indices,(int)d.indices);owner.vertices[0].x=-902;owner.indices[0]=0;Need(a.vertices[0]==av[0]&&a.colors32[0].a==ac[0].a&&a.triangles[0]==at[0],"first upload independent");Need(b.vertices[0].x==av[0].x+91&&b.colors32[0].a==1,"second upload independent");
    }finally{owner.Release(p);}var warm=owner.Acquire(f);Need(warm.builtBytes==0&&warm.reused==warm.draws,"native reuse");owner.Release(warm);Need(RmlNative.rui_remove_element("absent")==0,"missing safe");RmlNative.rui_unload_document();RmlNative.rui_destroy();owner.Dispose();owner=null;Debug.Log("NATIVE_PREPARED_CPU_COPY_ABI_ORACLE_PASS packed32 draw56 packet88");
   }finally{if(owner!=null)owner.Dispose();RmlNative.rui_destroy();if(a)UnityEngine.Object.DestroyImmediate(a);if(b)UnityEngine.Object.DestroyImmediate(b);}
  }
#endif
 }
}
