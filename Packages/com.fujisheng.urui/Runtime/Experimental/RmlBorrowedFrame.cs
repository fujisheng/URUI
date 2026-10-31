using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
namespace RmlUnity {
 // Internal synchronous view; public business APIs expose managed objects only.
 internal sealed unsafe class RmlBorrowedFrame:IDisposable {
  const string Dll="RmlUnityBridge";
  [StructLayout(LayoutKind.Sequential)]internal struct Packet {
   internal ulong session,lease,epoch,commands,resources,plan,frame,retained,builtBytes;
   internal IntPtr draws,values,worlds;
   internal uint count,drawCount,built,reused,affine,fallback,version,flags;
  }
  [StructLayout(LayoutKind.Sequential)]struct MeshView {internal IntPtr vertices,indices;internal uint vertexCount,indexCount,vertexStride,indexStride;}
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int rui_native_frame_api_version();
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern ulong rui_native_frame_create_mode(uint arithmetic);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int rui_native_frame_prepare_matrix_mode(float* matrix,float tx,float ty,uint arithmetic,float* world,float* value);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int rui_native_frame_acquire(ulong session,out Packet packet);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int rui_native_frame_mesh(ulong session,ulong lease,ulong batch,out MeshView view);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int rui_native_frame_release(ulong session,ulong lease);
  [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]static extern int rui_native_frame_destroy(ulong session);
  readonly int thread=System.Threading.Thread.CurrentThread.ManagedThreadId;ulong session,activeLease;
  static readonly uint mathMode=DetectArithmetic();internal static uint ArithmeticMode=>mathMode;
  static uint DetectArithmetic(){try{var m=Matrix4x4.identity;m.m00=-.6485595703125f;m.m01=-.9876708984375f;m.m03=-.3780517578125f;const float tx=.5301513671875f,ty=-.074951171875f;var expected=m*Matrix4x4.Translate(new Vector3(tx,ty,0));float* input=stackalloc float[16];float* result=stackalloc float[16];float* value=stackalloc float[8];for(int i=0;i<16;i++)input[i]=m[i];for(uint mode=1;mode<=2;mode++){if(rui_native_frame_prepare_matrix_mode(input,tx,ty,mode,result,value)!=1)continue;bool same=true;for(int i=0;i<16;i++)if(BitConverter.SingleToInt32Bits(result[i])!=BitConverter.SingleToInt32Bits(expected[i])){same=false;break;}if(same)return mode;}return 0;}catch(EntryPointNotFoundException){return 0;}catch(DllNotFoundException){return 0;}}
  internal static bool Available {get{try{return mathMode!=0&&RuntimeInformation.ProcessArchitecture==Architecture.Arm64&&IntPtr.Size==8&&Marshal.SizeOf<Packet>()==128&&Marshal.OffsetOf<Packet>(nameof(Packet.draws)).ToInt32()==72&&Marshal.OffsetOf<Packet>(nameof(Packet.count)).ToInt32()==96&&Marshal.SizeOf<MeshView>()==32&&Marshal.SizeOf<RmlNativePrepared.Draw>()==56&&Marshal.SizeOf<RmlAffineParameter>()==32&&Marshal.SizeOf<Matrix4x4>()==64&&rui_native_frame_api_version()==1;}catch(EntryPointNotFoundException){return false;}catch(DllNotFoundException){return false;}}}
  internal RmlBorrowedFrame(){if(!Available)throw new InvalidOperationException("Borrowed final-frame ABI unsupported");session=rui_native_frame_create_mode(mathMode);if(session==0)throw new InvalidOperationException("Borrowed final-frame session admission");}
  void Thread(){if(session==0||thread!=System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Borrowed frame thread/disposed");}
  void Check(Packet p){Thread();if(p.session!=session||p.lease==0||p.lease!=activeLease)throw new InvalidOperationException("Stale borrowed frame");}
  static void Address(IntPtr p,uint count,int stride,int alignment){if(count>65536||stride<=0||alignment<=0||count!=0&&p==IntPtr.Zero||p!=IntPtr.Zero&&(unchecked((ulong)p.ToInt64())%(uint)alignment)!=0)throw new InvalidOperationException("Borrowed frame bounds/alignment");checked{ulong end=unchecked((ulong)p.ToInt64())+(ulong)count*(uint)stride;if(count!=0&&end<unchecked((ulong)p.ToInt64()))throw new InvalidOperationException("Borrowed frame pointer overflow");}}
  internal Packet Acquire(){Thread();if(activeLease!=0)throw new InvalidOperationException("Reentrant borrowed frame lease");int result=rui_native_frame_acquire(session,out var p);if(result!=1)throw new InvalidOperationException("Borrowed native acquire "+result);activeLease=p.lease;
   try{if(p.session!=session||p.lease==0||p.version!=1||(p.flags&255)!=1||(p.flags>>8)!=mathMode||p.epoch==0||p.count>65536||p.drawCount>p.count||p.affine+p.fallback!=p.count)throw new InvalidOperationException("Borrowed native packet contract");Address(p.draws,p.drawCount,56,8);Address(p.values,p.count,32,4);Address(p.worlds,p.count,64,4);return p;}catch{ulong token=activeLease;activeLease=0;if(token!=0)rui_native_frame_release(session,token);throw;}}
  internal RmlNativePrepared.Draw Draw(Packet p,int index){Check(p);if((uint)index>=p.drawCount)throw new ArgumentOutOfRangeException(nameof(index));return ((RmlNativePrepared.Draw*)p.draws)[index];}
  internal Matrix4x4 World(Packet p,uint index){Check(p);if(index>=p.count)throw new ArgumentOutOfRangeException(nameof(index));return ((Matrix4x4*)p.worlds)[index];}
  internal readonly struct View<T>:IDisposable where T:unmanaged {
   internal readonly NativeArray<T> array;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
   readonly AtomicSafetyHandle safety;
#endif
   internal View(IntPtr data,int count){if(count<0||data==IntPtr.Zero&&count!=0||data!=IntPtr.Zero&&unchecked((ulong)data.ToInt64())%(uint)UnsafeUtility.AlignOf<T>()!=0)throw new InvalidOperationException("Native view length/alignment");checked{ulong end=unchecked((ulong)data.ToInt64())+(ulong)count*(uint)UnsafeUtility.SizeOf<T>();if(count!=0&&end<unchecked((ulong)data.ToInt64()))throw new InvalidOperationException("Native view overflow");}array=NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((void*)data,count,Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    safety=AtomicSafetyHandle.Create();try{NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array,safety);}catch{AtomicSafetyHandle.Release(safety);throw;}
#endif
   }
   public void Dispose(){
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    AtomicSafetyHandle.Release(safety);
#endif
    // The C++ lease owns memory. Never Dispose this Allocator.None array.
   }
  }
  internal void RecordParameters(Packet p,CommandBuffer target,GraphicsBuffer buffer){Check(p);if(p.count==0)return;if(buffer==null||buffer.stride!=32||buffer.count<p.count)throw new InvalidOperationException("Borrowed parameter buffer size");using var values=new View<RmlAffineParameter>(p.values,(int)p.count);target.SetBufferData(buffer,values.array,0,0,(int)p.count);}
  static readonly VertexAttributeDescriptor[] Layout={new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2),new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,2)};
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  internal static Action<string> UploadFaultForProbe;
  static void UploadProbe(string at){UploadFaultForProbe?.Invoke(at);}
#else
  static void UploadProbe(string at){}
#endif
  internal Mesh Upload(Packet p,RmlNativePrepared.Draw draw,Action<Mesh> retireFailed=null){Check(p);if(draw.vertices==0||draw.vertices>1048576||draw.indices==0||draw.indices>3145728||rui_native_frame_mesh(session,p.lease,draw.batch,out var source)!=1||source.vertexCount!=draw.vertices||source.indexCount!=draw.indices||source.vertexStride!=32||source.indexStride!=4)throw new InvalidOperationException("Borrowed immutable mesh member/stride/count");
   using var vertices=new View<RmlNativePrepared.Vertex>(source.vertices,(int)source.vertexCount);using var indices=new View<uint>(source.indices,(int)source.indexCount);return UploadArrays(vertices.array,indices.array,retireFailed);
  }
  internal static Mesh UploadArrays(NativeArray<RmlNativePrepared.Vertex> vertices,NativeArray<uint> indices,Action<Mesh> retireFailed=null){
   int vc=vertices.Length,ic=indices.Length;if(!vertices.IsCreated||!indices.IsCreated||vc<=0||vc>1048576||ic<=0||ic>3145728)throw new InvalidOperationException("Native Mesh source lengths");var mesh=new Mesh{name="Rml borrowed native final-frame batch",indexFormat=IndexFormat.UInt32};
   try{mesh.SetVertexBufferParams(vc,Layout);if(mesh.GetVertexBufferStride(0)!=32||mesh.GetVertexAttributeOffset(VertexAttribute.Color)!=12||mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0)!=16||mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1)!=24)throw new InvalidOperationException("Unity borrowed mesh layout");mesh.SetVertexBufferData(vertices,0,0,vc,0,MeshUpdateFlags.DontRecalculateBounds);UploadProbe("after-vb");mesh.SetIndexBufferParams(ic,IndexFormat.UInt32);mesh.SetIndexBufferData(indices,0,0,ic,MeshUpdateFlags.DontRecalculateBounds);UploadProbe("after-ib");mesh.subMeshCount=1;mesh.SetSubMesh(0,new SubMeshDescriptor(0,ic,MeshTopology.Triangles){firstVertex=0,vertexCount=vc},MeshUpdateFlags.DontRecalculateBounds);mesh.bounds=new Bounds(Vector3.zero,new Vector3(1e9f,1e9f,1e6f));return mesh;}catch{if(retireFailed!=null)retireFailed(mesh);else UnityEngine.Object.Destroy(mesh);throw;}
  }
  internal void Release(Packet p){Check(p);int result=rui_native_frame_release(session,p.lease);activeLease=0;if(result!=1)throw new InvalidOperationException("Borrowed native release "+result);}
  public void Dispose(){Thread();if(activeLease!=0)throw new InvalidOperationException("Outstanding borrowed native lease");int result=rui_native_frame_destroy(session);if(result!=1)throw new InvalidOperationException("Borrowed native close "+result);session=0;}
 }
}
