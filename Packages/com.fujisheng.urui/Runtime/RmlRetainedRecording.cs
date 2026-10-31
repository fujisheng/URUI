using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
namespace RmlUnity {
 public enum RmlRecordingOwnership { ExternalMutable=0, HostExclusive=1 }
 // Full state equality, never hash-only. Unknown rendering paths do not opt in.
 public struct RmlRecordingKey:IEquatable<RmlRecordingKey> {
  static readonly ProfilerMarker CaptureMarker=new ProfilerMarker("Rml.RetainedKeyCapture");
  public int width,height,screenWidth,screenHeight,orientation,fullscreen,colorSpace,camera,material,shader,contract,options,renderQueue,display,renderingPath;
  public float dpi,widthScale,heightScale;
  public Rect viewport;
  public Matrix4x4 view,projection;
  public bool hdr,msaa,instancing,doubleSided,passEnabled;
  public MaterialGlobalIlluminationFlags illumination;
  static bool MatrixEqual(Matrix4x4 a,Matrix4x4 b){for(int i=0;i<16;i++)if(a[i]!=b[i])return false;return true;}
  public bool Equals(RmlRecordingKey x)=>width==x.width&&height==x.height&&screenWidth==x.screenWidth&&screenHeight==x.screenHeight&&orientation==x.orientation&&fullscreen==x.fullscreen&&colorSpace==x.colorSpace&&camera==x.camera&&material==x.material&&shader==x.shader&&contract==x.contract&&options==x.options&&renderQueue==x.renderQueue&&display==x.display&&renderingPath==x.renderingPath&&dpi==x.dpi&&widthScale==x.widthScale&&heightScale==x.heightScale&&viewport.Equals(x.viewport)&&MatrixEqual(view,x.view)&&MatrixEqual(projection,x.projection)&&hdr==x.hdr&&msaa==x.msaa&&instancing==x.instancing&&doubleSided==x.doubleSided&&passEnabled==x.passEnabled&&illumination==x.illumination;
  static bool IsMaterialVariant(Material material){
   #if UNITY_EDITOR
   return material.isVariant;
   #else
   // Host creates a fresh Material(shader); no material variants are loaded.
   return false;
   #endif
  }
  public static bool TryCapture(RmlUnityHost host,Material material,Camera camera,string passName,out RmlRecordingKey key){using(CaptureMarker.Auto())return Capture(host,material,camera,passName,out key);}
  static bool Capture(RmlUnityHost host,Material material,Camera camera,string passName,out RmlRecordingKey key){
   key=default;
   if(host.recordingOwnership!=RmlRecordingOwnership.HostExclusive||!material||!camera||!host.propertyContract||!host.propertyContract.Supports(material)||material.passCount!=1||IsMaterialVariant(material)||GraphicsSettings.currentRenderPipeline||camera.targetTexture||camera.stereoEnabled||camera.forceIntoRenderTexture)return false;
   var v=camera.worldToCameraMatrix;var p=camera.projectionMatrix;
   for(int i=0;i<16;i++)if(float.IsNaN(v[i])||float.IsInfinity(v[i])||float.IsNaN(p[i])||float.IsInfinity(p[i]))return false;
   int options=(host.batchDisjointCommands?1:0)|(host.batchEquivalentTransforms?2:0)|(host.batchOrderedOverlap?4:0)|(host.reusePropertyBlock?8:0)|(host.cullFullyClipped?16:0)|(host.skipDrawForDiagnosis?32:0)|(host.reuseEquivalentPropertyState?64:0)|(host.useShaderPropertyIds?128:0)|(host.useFrameScratchCollections?256:0)|(host.showcase?512:0);
   key=new RmlRecordingKey{width=host.UiWidth,height=host.UiHeight,screenWidth=Screen.width,screenHeight=Screen.height,orientation=(int)Screen.orientation,fullscreen=(int)Screen.fullScreenMode,colorSpace=(int)QualitySettings.activeColorSpace,camera=camera.GetInstanceID(),material=material.GetInstanceID(),shader=material.shader.GetInstanceID(),contract=host.propertyContract.GetInstanceID(),options=options,renderQueue=material.renderQueue,display=camera.targetDisplay,renderingPath=(int)camera.actualRenderingPath,dpi=Screen.dpi,widthScale=ScalableBufferManager.widthScaleFactor,heightScale=ScalableBufferManager.heightScaleFactor,viewport=camera.pixelRect,view=v,projection=p,hdr=camera.allowHDR,msaa=camera.allowMSAA,instancing=material.enableInstancing,doubleSided=material.doubleSidedGI,passEnabled=material.GetShaderPassEnabled(passName),illumination=material.globalIlluminationFlags};
   return true;
  }
 }
 // No hash or element pointer: a native exact-plan version must repeat before
 // an expensive state capture is eligible. Invalid snapshots reset observation.
 public struct RmlPlanStability {
  bool seen;RuiFrameStampNative previous;
  public static bool Valid(RuiFrameStampNative s)=>s.flags==1&&s.count<=int.MaxValue&&s.epoch!=0&&s.commands!=0&&s.resources!=0;
  public static bool Same(RuiFrameStampNative a,RuiFrameStampNative b)=>a.epoch==b.epoch&&a.commands==b.commands&&a.resources==b.resources&&a.count==b.count;
  public bool Observe(RuiFrameStampNative current){bool valid=Valid(current);bool repeated=valid&&seen&&Same(previous,current);seen=valid;previous=current;return repeated;}
  public void Reset(){seen=false;previous=default;}
 }
 public sealed class RmlRecordingState {
  bool valid;RuiFrameStampNative stamp;RmlRecordingKey key;
  public bool CanReuse(RuiFrameStampNative current,RmlRecordingKey state)=>valid&&current.flags==1&&current.count<=int.MaxValue&&current.epoch!=0&&current.commands!=0&&current.resources!=0&&stamp.epoch==current.epoch&&stamp.commands==current.commands&&stamp.resources==current.resources&&stamp.count==current.count&&key.Equals(state);
  public void Commit(RuiFrameStampNative current,RmlRecordingKey state){stamp=current;key=state;valid=current.flags==1&&current.count<=int.MaxValue&&current.epoch!=0&&current.commands!=0&&current.resources!=0;}
  public void Invalidate(){valid=false;}
 }
 public struct RmlRecordingStats {
  public bool reused,capability;public int nativeCommands,recordCalls,retainedDraws;public ulong nativeExtraBytes,compareBytes;
 }
}
