using System;
using UnityEngine;
namespace RmlUnity {
 public sealed partial class RmlUnityHost {
  // Additive experiment. The old managed/native batch/delta/core paths and all
  // serialized defaults remain selectable and unchanged.
  public bool useBorrowedNativeFrame;
  public bool BorrowedNativeFrameActive{get;private set;}
  RmlNativeFrameRoot borrowedRoot;
  bool StopBorrowedNativeRoot(){BorrowedNativeFrameActive=false;if(borrowedRoot==null)return true;var old=borrowedRoot;borrowedRoot=null;try{RmlNativeFrameRetirement.Retire(old);}finally{ObserveNativeStop(old);}return old.CanFallback&&old.CloseComplete;}
  bool PrepareBorrowedNativeRoot(ref RmlPhaseFrame phase){
   BorrowedNativeFrameActive=false;
   if(borrowedRoot==null){if(!RmlBorrowedFrame.Available||!RmlNativeFrameRetirement.Admission){ParameterRootFallbacks++;parameterRootFault=true;return false;}borrowedRoot=RmlNativeFrameRetirement.Create(buffer,shader,parameterCapability.Parameter);}
   var writer=borrowedRoot;var result=writer.Record(UiWidth,UiHeight);if(!ReferenceEquals(borrowedRoot,writer)){ObserveNativeStop(writer);if(active&&isActiveAndEnabled)RequireNativeStopPermission();return true;} // A synchronous Unity callback revoked this Host during preparation.
   if(result!=RmlParameterTableRenderer.Result.Success){string reason=borrowedRoot.FailureReason;ParameterRootFallbacks++;parameterRootFault=true;bool canFallback=StopBorrowedNativeRoot();if(!canFallback)throw new InvalidOperationException("Native final-frame camera attachment state uncertain; fallback recording prohibited: "+reason);Debug.LogWarning("Native final-frame preparation rejected: "+reason+"; original path retained until restart");return false;}
   var stats=borrowedRoot.Last;BorrowedNativeFrameActive=true;ParameterRootActive=true;NativePreparedActive=stats.nativeActive==1;NativePreparedCalls=stats.nativePrepares;NativePreparedBuilds=stats.nativeBuilds;NativePreparedRetainedBytes=stats.nativeRetainedBytes;NativePreparedStagingBytes=0;NativePayloadCopyBytes=0;NativePayloadFullBytes=stats.nativeFullBytes;NativePayloadPossibleBytes=stats.nativeFullBytes;NativePayloadDeltaCopies=0;
   ParameterBatchFallbacks=stats.fallbacks;ParameterBatchHits=stats.batchHits;ParameterBatchBuilds=stats.batchBuilds;ParameterTextureCopyCalls=stats.textureCopies;ParameterTextureCopyBytes=stats.textureCopyBytes;ParameterGeometryCopyCalls=0;ParameterGeometryCopyBytes=0;ParameterRequestedBytes=stats.parameterRequestedBytes;ParameterGeometryBytes=stats.geometryRequestedBytes;ParameterTextureBytes=stats.textureRequestedBytes;ParameterDraws=stats.draws;ParameterSourceFrame=Time.frameCount;ParameterSubmissionFrame=-1;ParameterRootRecordings++;DrawCommands=borrowedRoot.CommandCount;InvalidateRecording(false);
   if(phaseDiagnostics){phase.CommandsRead=0;phase.DrawSubmissions=stats.draws;phase.CommandCopyBytes=128;phase.MatrixCopyBytes=stats.fallbacks*64;phase.ParameterCopyBytes=0;phase.TotalCommandCopyBytes=phase.CommandCopyBytes+phase.MatrixCopyBytes;phase.GeometryCopyBytes=(int)stats.geometryRequestedBytes;phase.MeshesCreated=stats.meshBuilds;}
   return true;
  }
 }
}
