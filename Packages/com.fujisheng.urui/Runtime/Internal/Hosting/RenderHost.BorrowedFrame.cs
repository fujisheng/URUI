using System;
using UnityEngine;
using URUI.Internal.Rendering.Frames;
using URUI.Internal.Rendering.Parameters;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        // Borrowed CPU buffers live only for a native lease. GPU copies retire through
        // NativeFrameRetirement after camera consumption has finished.
        public bool useBorrowedNativeFrame;
        NativeFrameRoot borrowedRoot;

        bool StopBorrowedNativeRoot()
        {
#if URUI_DEBUG
            BorrowedNativeFrameActive = false;
#endif
            if (borrowedRoot == null)
            {
                return true;
            }

            var old = borrowedRoot;
            borrowedRoot = null;
            try
            {
                NativeFrameRetirement.Retire(old);
            }
            finally
            {
                ObserveNativeStop(old);
            }

            return old.CanFallback && old.CloseComplete;
        }

        bool PrepareBorrowedNativeRoot(ref PhaseFrame phase)
        {
#if URUI_DEBUG
            BorrowedNativeFrameActive = false;
#endif
            if (borrowedRoot == null)
            {
                if (!BorrowedFrame.Available || !NativeFrameRetirement.Admission)
                {
#if URUI_DEBUG
                    ParameterRootFallbacks++;
#endif
                    parameterRootFault = true;
                    return false;
                }

                borrowedRoot = NativeFrameRetirement.Create(buffer, shader, parameterCapability.Parameter, borrowedTextures);
            }

            var writer = borrowedRoot;
            var result = writer.Record(UiWidth, UiHeight);
            // A synchronous Unity callback may revoke this host while recording.
            if (!ReferenceEquals(borrowedRoot, writer))
            {
                ObserveNativeStop(writer);
                if (active && isActiveAndEnabled)
                {
                    RequireNativeStopPermission();
                }

                return true;
            }

            if (result != ParameterTableRenderer.Result.Success)
            {
                string reason = borrowedRoot.FailureReason;
#if URUI_DEBUG
                ParameterRootFallbacks++;
#endif
                parameterRootFault = true;
                bool canFallback = StopBorrowedNativeRoot();
                if (!canFallback)
                {
                    throw new InvalidOperationException("Native final-frame camera attachment state uncertain; fallback recording prohibited: " + reason);
                }

                Debug.LogWarning("Native final-frame preparation rejected: " + reason + "; original path retained until restart");
                return false;
            }
#if URUI_DEBUG
            var stats = borrowedRoot.Last;
            BorrowedNativeFrameActive = true;
            ParameterRootActive = true;
            NativePreparedActive = stats.nativeActive == 1;
            NativePreparedCalls = stats.nativePrepares;
            NativePreparedBuilds = stats.nativeBuilds;
            NativePreparedRetainedBytes = stats.nativeRetainedBytes;
            NativePreparedStagingBytes = 0;
            NativePayloadCopyBytes = 0;
            NativePayloadFullBytes = stats.nativeFullBytes;
            NativePayloadPossibleBytes = stats.nativeFullBytes;
            NativePayloadDeltaCopies = 0;
            ParameterBatchFallbacks = stats.fallbacks;
            ParameterBatchHits = stats.batchHits;
            ParameterBatchBuilds = stats.batchBuilds;
            ParameterTextureCopyCalls = stats.textureCopies;
            ParameterTextureCopyBytes = stats.textureCopyBytes;
            ParameterGeometryCopyCalls = 0;
            ParameterGeometryCopyBytes = 0;
            ParameterRequestedBytes = stats.parameterRequestedBytes;
            ParameterGeometryBytes = stats.geometryRequestedBytes;
            ParameterTextureBytes = stats.textureRequestedBytes;
            ParameterDraws = stats.draws;
            ParameterSourceFrame = Time.frameCount;
            ParameterSubmissionFrame = -1;
            ParameterRootRecordings++;
            DrawCommands = borrowedRoot.CommandCount;
#endif
            InvalidateRecording(false);
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.commandsRead = 0;
                phase.drawSubmissions = stats.draws;
                phase.commandCopyBytes = 128;
                phase.matrixCopyBytes = stats.fallbacks * 64;
                phase.parameterCopyBytes = 0;
                phase.totalCommandCopyBytes = phase.commandCopyBytes + phase.matrixCopyBytes;
                phase.geometryCopyBytes = (int)stats.geometryRequestedBytes;
                phase.meshesCreated = stats.meshBuilds;
            }
#endif
            return true;
        }
    }
}
