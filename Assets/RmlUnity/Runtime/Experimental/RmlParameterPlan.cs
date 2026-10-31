using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace RmlUnity {
 [StructLayout(LayoutKind.Sequential)]
 public struct RmlAffineParameter:IEquatable<RmlAffineParameter> {
  public Vector4 row0,row1;
  public Vector2 Apply(Vector2 p)=>new Vector2(row0.x*p.x+row0.y*p.y+row0.z,row1.x*p.x+row1.y*p.y+row1.z);
  public bool Equals(RmlAffineParameter p)=>row0.Equals(p.row0)&&row1.Equals(p.row1);
  public static bool TryCreate(Matrix4x4 matrix,float tx,float ty,out RmlAffineParameter value)=>TryPrepare(matrix,tx,ty,out _,out value,out bool affine)&&affine;
  static bool FastValue(float value){uint bits=unchecked((uint)BitConverter.SingleToInt32Bits(value)),a=bits&0x7fffffff;return bits==0||(a>=0x00800000&&a<0x7f800000);}
  internal static bool TryPrepareOptimized(Matrix4x4 matrix,float tx,float ty,out Matrix4x4 world,out RmlAffineParameter value,out bool affine,out bool fast){
   fast=false;
   // Fresh value snapshot only: no validation result survives this call.
   if(!(matrix.m02==0&&matrix.m12==0&&matrix.m20==0&&matrix.m21==0&&matrix.m22==1&&matrix.m23==0&&matrix.m30==0&&matrix.m31==0&&matrix.m32==0&&matrix.m33==1))return TryPrepare(matrix,tx,ty,out world,out value,out affine);
   for(int i=0;i<16;i++)if(!FastValue(matrix[i]))return TryPrepare(matrix,tx,ty,out world,out value,out affine);
   if(!FastValue(tx)||!FastValue(ty))return TryPrepare(matrix,tx,ty,out world,out value,out affine);
   // Use Unity's public dot-product implementation, preserving its operation order.
   Vector4 column=matrix*new Vector4(tx,ty,0,1);
   if(!FastValue(column.x)||!FastValue(column.y)||BitConverter.SingleToInt32Bits(column.z)!=0||column.w!=1)return TryPrepare(matrix,tx,ty,out world,out value,out affine);
   world=matrix;world.m03=column.x;world.m13=column.y;world.m23=column.z;world.m33=column.w;
   affine=fast=true;value=new RmlAffineParameter{row0=new Vector4(world.m00,world.m01,world.m03,0),row1=new Vector4(world.m10,world.m11,world.m13,0)};return true;
  }
  internal static bool TryPrepare(Matrix4x4 matrix,float tx,float ty,out Matrix4x4 world,out RmlAffineParameter value,out bool affine){
   world=default;value=default;affine=false;for(int i=0;i<16;i++)if(float.IsNaN(matrix[i])||float.IsInfinity(matrix[i]))return false;
   if(float.IsNaN(tx)||float.IsInfinity(tx)||float.IsNaN(ty)||float.IsInfinity(ty))return false;
   world=matrix*Matrix4x4.Translate(new Vector3(tx,ty,0));for(int i=0;i<16;i++)if(float.IsNaN(world[i])||float.IsInfinity(world[i]))return false;
   affine=world.m02==0&&world.m12==0&&world.m20==0&&world.m21==0&&world.m22==1&&world.m23==0&&world.m30==0&&world.m31==0&&world.m32==0&&world.m33==1;
   if(affine)value=new RmlAffineParameter{row0=new Vector4(world.m00,world.m01,world.m03,0),row1=new Vector4(world.m10,world.m11,world.m13,0)};return true;
  }
 }
 public struct RmlParameterKey:IEquatable<RmlParameterKey> {
  public ulong geometry,texture;public int enabled,x,y,w,h;public bool affine;
  public static RmlParameterKey From(CommandNative c,bool affine)=>new RmlParameterKey{geometry=c.geometry,texture=c.texture,enabled=c.clip_enabled,x=c.clip_x,y=c.clip_y,w=c.clip_w,h=c.clip_h,affine=affine};
  public Vector4 ClipVector=>new Vector4(x,y,unchecked(x+w),unchecked(y+h));
  public bool Equals(RmlParameterKey b)=>geometry==b.geometry&&texture==b.texture&&enabled==b.enabled&&x==b.x&&y==b.y&&w==b.w&&h==b.h&&affine==b.affine;
  public static bool CanJoin(RmlParameterKey a,RmlParameterKey b)=>a.affine&&b.affine&&a.texture==b.texture&&a.enabled==b.enabled&&a.x==b.x&&a.y==b.y&&a.w==b.w&&a.h==b.h;
  public static bool SameSequence(RmlParameterKey[] a,int an,RmlParameterKey[] b,int bn){if(an!=bn||an<0||a==null||b==null||a.Length<an||b.Length<bn)return false;for(int i=0;i<an;i++)if(!a[i].Equals(b[i]))return false;return true;}
 }
 internal sealed partial class RmlParameterFrame {
  #if UNITY_EDITOR || DEVELOPMENT_BUILD
  static readonly Unity.Profiling.ProfilerMarker CaptureMarker=new Unity.Profiling.ProfilerMarker("Rml.ParameterCapture"), NativeReadMarker=new Unity.Profiling.ProfilerMarker("Rml.ParameterCapture.NativeRead"), PrepareLoopMarker=new Unity.Profiling.ProfilerMarker("Rml.ParameterCapture.PrepareLoop");
  #endif
  public int fastCommands,fallbackCommands;
  public int count;public RuiFrameStampNative stamp;public CommandNative[] commands=Array.Empty<CommandNative>();public float[] matrixData=Array.Empty<float>();public Matrix4x4[] matrices=Array.Empty<Matrix4x4>();public Matrix4x4[] worlds=Array.Empty<Matrix4x4>();public RmlAffineParameter[] parameters=Array.Empty<RmlAffineParameter>();public RmlParameterKey[] keys=Array.Empty<RmlParameterKey>();
  // Optional cache is valid only for the Host's private owned frame arrays.
  // Ordinary Capture never reuses: callers retaining/mutating its public arrays keep old semantics.
  bool snapshotReady,snapshotOptimized;RuiFrameStampNative snapshotStamp;
  CommandNative[] snapshotCommands;float[] snapshotMatrixData;Matrix4x4[] snapshotMatrices,snapshotWorlds;RmlAffineParameter[] snapshotParameters;RmlParameterKey[] snapshotKeys;
  public int SnapshotReused,NativePayloadReads,DecodedCommands;
  internal void InvalidateOwnedSnapshot(){snapshotReady=false;snapshotStamp=default;snapshotCommands=null;snapshotMatrixData=null;snapshotMatrices=null;snapshotWorlds=null;snapshotParameters=null;snapshotKeys=null;}
  bool SameOwnedSnapshot(bool optimized)=>snapshotOptimized==optimized&&snapshotStamp.epoch==stamp.epoch&&snapshotStamp.commands==stamp.commands&&snapshotStamp.resources==stamp.resources&&snapshotStamp.count==stamp.count&&stamp.count==(uint)count&&ReferenceEquals(snapshotCommands,commands)&&ReferenceEquals(snapshotMatrixData,matrixData)&&ReferenceEquals(snapshotMatrices,matrices)&&ReferenceEquals(snapshotWorlds,worlds)&&ReferenceEquals(snapshotParameters,parameters)&&ReferenceEquals(snapshotKeys,keys);
  bool CompletedCapture(bool owned,bool optimized){DecodedCommands=count;if(owned){snapshotStamp=stamp;snapshotOptimized=optimized;snapshotCommands=commands;snapshotMatrixData=matrixData;snapshotMatrices=matrices;snapshotWorlds=worlds;snapshotParameters=parameters;snapshotKeys=keys;snapshotReady=true;}return true;}
  public bool Capture(bool optimized=false)=>CaptureCore(optimized,false);
  internal bool CaptureOwned(bool optimized,bool reuse)=>CaptureCore(optimized,reuse);
  bool CaptureCore(bool optimized,bool owned){
   InvalidateNativeCore();bool mayReuse=owned&&snapshotReady;snapshotReady=false;SnapshotReused=NativePayloadReads=DecodedCommands=0;
   try{

   fastCommands=fallbackCommands=0;
   #if UNITY_EDITOR || DEVELOPMENT_BUILD
   using var captureMarker=CaptureMarker.Auto();
   #endif
   #if UNITY_EDITOR || DEVELOPMENT_BUILD
   using(NativeReadMarker.Auto()){
   #endif
   RmlHostCpuProbe.Point(RmlCpuStage.CommandRead);   count=RmlNative.rui_command_count();if(count<0||count>65536||RmlNative.rui_render_plan_stamp(out stamp)!=1||stamp.flags!=1)return false;
   if(mayReuse&&SameOwnedSnapshot(optimized)){snapshotReady=true;SnapshotReused=1;return true;}
   InvalidateOwnedSnapshot();
   if(commands.Length<count){int cap=Math.Max(count,commands.Length*2+16);commands=new CommandNative[cap];matrixData=new float[cap*16];matrices=new Matrix4x4[cap];worlds=new Matrix4x4[cap];parameters=new RmlAffineParameter[cap];keys=new RmlParameterKey[cap];}
   NativePayloadReads++;if(RmlNative.rui_get_commands(commands,count)!=count)return false;NativePayloadReads++;if(RmlNative.rui_get_command_transforms(matrixData,matrixData.Length)!=count)return false;
   #if UNITY_EDITOR || DEVELOPMENT_BUILD
   }
   using var prepareLoopMarker=PrepareLoopMarker.Auto();
   #endif
   RmlHostCpuProbe.Point(RmlCpuStage.CommandDecode);
   if(optimized){for(int i=0;i<count;i++){int o=i*16;var m=new Matrix4x4(new Vector4(matrixData[o],matrixData[o+1],matrixData[o+2],matrixData[o+3]),new Vector4(matrixData[o+4],matrixData[o+5],matrixData[o+6],matrixData[o+7]),new Vector4(matrixData[o+8],matrixData[o+9],matrixData[o+10],matrixData[o+11]),new Vector4(matrixData[o+12],matrixData[o+13],matrixData[o+14],matrixData[o+15]));matrices[i]=m;if(!RmlAffineParameter.TryPrepareOptimized(m,commands[i].tx,commands[i].ty,out worlds[i],out parameters[i],out bool affine,out bool fast))return false;if(fast)fastCommands++;else fallbackCommands++;keys[i]=RmlParameterKey.From(commands[i],affine);}return CompletedCapture(owned,optimized);}
   for(int i=0;i<count;i++){if(float.IsNaN(commands[i].tx)||float.IsInfinity(commands[i].tx)||float.IsNaN(commands[i].ty)||float.IsInfinity(commands[i].ty))return false;var m=new Matrix4x4();for(int c=0;c<4;c++)for(int r=0;r<4;r++){float v=matrixData[i*16+c*4+r];if(float.IsNaN(v)||float.IsInfinity(v))return false;m[r,c]=v;}matrices[i]=m;if(!RmlAffineParameter.TryPrepare(m,commands[i].tx,commands[i].ty,out worlds[i],out parameters[i],out bool affine))return false;keys[i]=RmlParameterKey.From(commands[i],affine);}
   return CompletedCapture(owned,optimized);
   }finally{if(!snapshotReady)InvalidateOwnedSnapshot();}
  }
 }
}
