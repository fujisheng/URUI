using System;using UnityEngine;using UnityEngine.Rendering;using Unity.Profiling;
namespace URUI.Internal {
 internal sealed partial class RenderHost {
  [Tooltip("Experimental: audited shaders, runtime HostExclusive resource ownership and SetParameterCameraStageOwnership(true) for a reserved camera stage are required. Defaults to legacy rendering.")]
  public bool useParameterTable;public ParameterShaderCapability parameterCapability;
  public int ParameterSubmissions{get;private set;}public int ParameterFallbacks{get;private set;}public long ParameterRequestedBytes{get;private set;}public long ParameterGeometryBytes{get;private set;}public int ParameterDraws{get;private set;}public long ParameterTextureBytes{get;private set;}public int ParameterSourceFrame{get;private set;}public int ParameterSubmissionFrame{get;private set;}
  static readonly ProfilerMarker ParameterMarker=new ProfilerMarker("URUI.HostParameterCamera");
  ParameterTableRenderer parameterRenderer;readonly ParameterFrame parameterFrame=new ParameterFrame();[NonSerialized] bool parameterStageExclusive;
  // Independent from Host-owned resource ownership: the caller also guarantees
  // no custom post-render/image effects or stage mutation during camera render.
  /// <summary>Declares a reserved camera stage; this is not inferred from resource ownership.</summary>
  /// <remarks>The caller owns the sole Host AfterEverything attachment and guarantees
  /// no image effects/custom post-render work or composition mutation during render.
  /// Leave false for unknown composition. Revoked on disable; explicitly reassert
  /// after enabling. This promise is not a security boundary.</remarks>
  public void SetParameterCameraStageOwnership(bool exclusive){parameterStageExclusive=exclusive;InvalidateRecording(false);}
  bool OnlyOwnedCameraCommands(){if(targetCamera.commandBufferCount!=1)return false;var list=targetCamera.GetCommandBuffers(CameraEvent.AfterEverything);return list.Length==1; // Unity returns alias wrappers, not CLR-identical objects.
   // The separate runtime stage capability asserts that this sole attachment is
   // the Host recording and will not be replaced during camera consumption.
  }
  long parameterPreparationBytes,parameterPreparationTextureBytes;int parameterPreparedSourceFrame;bool parameterPreparationReported;
  Camera.CameraCallback parameterCallback,parameterPreCallback;bool parameterSubscribed;bool parameterPrepared,parameterRendering,parameterSuppress,parameterFault;int parameterWidth,parameterHeight;ParameterShaderCapability preparedCapability;
  // Withdrawn: combined fallback/ordering defects reproduced; preserved for research.
  static readonly bool ParameterImmediateRejected=true;
  bool ParameterEligible()=>!ParameterImmediateRejected&&!parameterSuppress&&!parameterFault&&!skipDrawForDiagnosis&&parameterStageExclusive&&useParameterTable&&recordingOwnership==RecordingOwnership.HostExclusive&&active&&isActiveAndEnabled&&targetCamera&&material&&parameterCapability&&parameterCapability.Supports(material,propertyContract)&&material.passCount==1&&material.shader==shader&&material.GetShaderPassEnabled(recordedPassName)&&!GraphicsSettings.currentRenderPipeline&&!targetCamera.stereoEnabled&&!targetCamera.forceIntoRenderTexture&&!targetCamera.allowHDR&&!targetCamera.allowMSAA&&!targetCamera.allowDynamicResolution&&targetCamera.rect==new Rect(0,0,1,1)&&ScalableBufferManager.widthScaleFactor==1&&ScalableBufferManager.heightScaleFactor==1&&targetCamera.targetDisplay==0&&targetCamera.actualRenderingPath==RenderingPath.Forward&&(!targetCamera.targetTexture||(targetCamera.targetTexture.format==RenderTextureFormat.ARGB32&&targetCamera.targetTexture.antiAliasing==1&&!targetCamera.targetTexture.useDynamicScale&&targetCamera.targetTexture.dimension==TextureDimension.Tex2D))&&OnlyOwnedCameraCommands()&&ParameterRetirement.Admission;
  bool PrepareParameterHost(ref PhaseFrame phase){
   if(!parameterFrame.Capture(optimizeAffinePreparation))return false;ParameterFastPreparationCommands=parameterFrame.fastCommands;ParameterGeneralPreparationCommands=parameterFrame.fallbackCommands;
   if(parameterRenderer==null||preparedCapability!=parameterCapability){RetireParameterRenderer();parameterRenderer=new ParameterTableRenderer(shader,parameterCapability.Parameter);preparedCapability=parameterCapability;}
   var preparation=parameterRenderer.Supported?parameterRenderer.Prepare(parameterFrame):ParameterTableRenderer.Result.Unsupported;
   if(preparation!=ParameterTableRenderer.Result.Success){parameterFault=true;RetireParameterRenderer();Debug.LogWarning("URUI parameter snapshot unsupported or resource budget reached; baseline retained until component restart");return false;}
   parameterPreparationBytes=parameterRenderer.Last.geometryRequestedBytes;parameterPreparationTextureBytes=parameterRenderer.Last.textureRequestedBytes;parameterPreparedSourceFrame=Time.frameCount;parameterPreparationReported=false;
   parameterPrepared=true;parameterWidth=UiWidth;parameterHeight=UiHeight;buffer.Clear();InvalidateRecording(false);DrawCommands=parameterFrame.count;
   if(phaseDiagnostics){phase.CommandsRead=parameterFrame.count;phase.CommandCopyBytes=parameterFrame.count*System.Runtime.InteropServices.Marshal.SizeOf(typeof(CommandNative));phase.MatrixCopyBytes=parameterFrame.count*64;phase.TotalCommandCopyBytes=phase.CommandCopyBytes+phase.MatrixCopyBytes;}
   return true;
  }
  static readonly Action<CommandBuffer> ExecuteGraphics=Graphics.ExecuteCommandBuffer;
  static void ExecuteParameterFallback(CommandBuffer commands,Action<CommandBuffer> execute){try{execute(commands);}finally{commands.Clear();}}
  void ParameterCameraRendered(Camera camera){
   if(camera!=targetCamera||!parameterPrepared||!active||!isActiveAndEnabled)return;
   if(parameterRendering){Debug.LogError("URUI parameter camera reentry rejected");return;}
   parameterRendering=true;ParameterRequestedBytes=0;ParameterGeometryBytes=0;ParameterTextureBytes=0;ParameterSourceFrame=parameterPreparedSourceFrame;ParameterSubmissionFrame=Time.frameCount;ParameterDraws=0;long timer=Now();using var scope=ParameterMarker.Auto();bool submitted=false;
   try{
    bool valid=ParameterEligible()&&preparedCapability==parameterCapability;

    if(valid){var result=parameterRenderer.TryRecordCamera(parameterFrame,camera,parameterWidth,parameterHeight,out var ticket);if(result==ParameterTableRenderer.Result.Success){
     submitted=true; // An exception may have enqueued work. Never draw fallback twice.
     parameterRenderer.Submit(ticket);ParameterSubmissions++;ParameterRequestedBytes=parameterRenderer.Last.parameterRequestedBytes;ParameterGeometryBytes=parameterRenderer.Last.geometryRequestedBytes+(parameterPreparationReported?0:parameterPreparationBytes);ParameterTextureBytes=parameterRenderer.Last.textureRequestedBytes+(parameterPreparationReported?0:parameterPreparationTextureBytes);parameterPreparationReported=true;ParameterDraws=parameterRenderer.Last.draws;return;
    }if(parameterRenderer.Faulted){parameterFault=true;RetireParameterRenderer();Debug.LogError("URUI parameter fence query fault; resources retained, future frames use baseline");}}
    // The candidate CB is never attached. The regular camera buffer was empty
    // for this callback, so this is exactly one fallback execution.
    parameterSuppress=true;var fallback=new PhaseFrame();DrawFrameCore(ref fallback);ExecuteParameterFallback(buffer,ExecuteGraphics);InvalidateRecording(false);parameterPrepared=true;ParameterFallbacks++;ParameterGeometryBytes=-1;ParameterDraws=recordCallsThisFrame;
   }catch(Exception error){parameterFault=true;parameterPrepared=false;Debug.LogError("URUI parameter camera fault (resources retained): "+error);if(submitted)RetireParameterRenderer();}
   finally{parameterSuppress=false;parameterRendering=false;if(measureBridge)UpdateMilliseconds+=Millis(Now()-timer);}
  }
  void RetireParameterRenderer(){if(parameterRenderer!=null){ParameterRetirement.Retire(parameterRenderer);parameterRenderer=null;}preparedCapability=null;}
  void ParameterCameraPreparing(Camera camera){if(camera!=targetCamera||!parameterPrepared||!active||!isActiveAndEnabled)return;if(ParameterEligible()&&preparedCapability==parameterCapability)return;parameterSuppress=true;try{var fallback=new PhaseFrame();DrawFrameCore(ref fallback);}finally{parameterSuppress=false;parameterPrepared=false;}}
  void SetParameterCameraSubscription(bool on){if(parameterSubscribed==on)return;parameterSubscribed=on;if(on){if(parameterCallback==null)parameterCallback=ParameterCameraRendered;Camera.onPostRender+=parameterCallback;if(parameterPreCallback==null)parameterPreCallback=ParameterCameraPreparing;Camera.onPreCull+=parameterPreCallback;}else{Camera.onPostRender-=parameterCallback;Camera.onPreCull-=parameterPreCallback;}}
  void StopParameterHost(){parameterStageExclusive=false;SetParameterCameraSubscription(false);parameterPrepared=false;RetireParameterRenderer();}
 }
 // Keeps leases alive after producer teardown. No polling interval is treated
 // as evidence of completion; Uncertain packets deliberately remain retained.
 internal sealed class ParameterRetirement:MonoBehaviour {
  static ParameterRetirement owner;readonly System.Collections.Generic.List<ParameterTableRenderer> retired=new System.Collections.Generic.List<ParameterTableRenderer>();
  internal static int Count=>owner?owner.retired.Count:0;
  internal static bool Admission=>!owner||owner.retired.Count<4;
  internal static void Retire(ParameterTableRenderer renderer){if(renderer.TryDispose())return;if(!owner){var obj=new GameObject("URUI parameter fence retirement"){hideFlags=HideFlags.HideAndDontSave};DontDestroyOnLoad(obj);owner=obj.AddComponent<ParameterRetirement>();}owner.retired.Add(renderer);}
  void Update(){for(int i=retired.Count-1;i>=0;i--)if(retired[i].TryDispose())retired.RemoveAt(i);}
 }
}
