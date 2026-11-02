using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Parameters;
using URUI.Internal.Rendering.Retention;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        public bool useNativePinnedPayloads;

        // Optional synchronous pinned full-copy control.
        public bool useNativeIncrementalPayloads;

        // Optional generation-checked CPU delta protocol, defaultoff.
        public bool useNativeFinalFramePacket;

        // Optional native core preparation; existing paths/default remain unchanged.
        bool lastNativeFinalFrameMode;
        bool lastBorrowedNativeMode;
        public bool useNativePreparedBatches;

        // Isolated opt-in C++ packed batch preparation; existing C# renderer remains default.
        public bool reuseOwnedFrameSnapshot;

        // Opt-in private CPU snapshot; exact native epochs/commands/resources/mode required.
        public bool directParameterSourceValidation;

        // Owned-entry validation experiment, requires lazy sources.
        public bool lazyParameterSourceMeshes;

        // Opt-in generic CPU snapshot / GPU Mesh lifetime separation.
        public bool optimizeAffinePreparation;

        // Generic fresh-snapshot affine algorithm; default off.
        public bool reuseParameterBatchScratch;

        // Bounded synchronous CPU staging only; opt-in, requires batch reuse.
        public bool reuseParameterBatches;

        // Optional exact immutable batch reuse; requires resource sharing.
        public bool shareParameterResources;

        // Optional immutable snapshot sharing; default false.
        ParameterRootRecording parameterRoot;
        readonly ParameterFrame rootFrame = new ParameterFrame();
        bool parameterRootFault;

        // The optimized path requires both audited shaders and exclusive ownership.
        // Unsupported cameras/backends continue through the regular recording path.
        bool ParameterRootEligible()
        {
#if URUI_DEBUG
            if (skipDrawForDiagnosis)
            {
                return false;
            }
#endif
            bool eligible = useParameterTable
                && !parameterRootFault
                && !retainUnchangedCommands
                && recordingOwnership == RecordingOwnership.HostExclusive
                && active
                && isActiveAndEnabled
                && targetCamera
                && material
                && parameterCapability
                && parameterCapability.Supports(material, propertyContract)
                && material.passCount == 1
                && material.shader == shader
                && material.GetShaderPassEnabled(recordedPassName)
                && !GraphicsSettings.currentRenderPipeline
                && !targetCamera.stereoEnabled
                && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal
                && SystemInfo.supportsGraphicsFence
                && SystemInfo.graphicsShaderLevel >= 45
                && SystemInfo.supports32bitsIndexBuffer;
            return eligible;
        }

        // Mode changes retire the old owner first. A fallback may record only after
        // the previous native lease has closed and its attachment is safe to clear.
        bool PrepareParameterRoot(ref PhaseFrame phase)
        {
            if (lastBorrowedNativeMode != useBorrowedNativeFrame)
            {
                StopParameterRoot();
                RequireNativeStopPermission();
                lastBorrowedNativeMode = useBorrowedNativeFrame;
            }

            if (useBorrowedNativeFrame)
            {
                return PrepareBorrowedNativeRoot(ref phase);
            }

            if (lastNativeFinalFrameMode != useNativeFinalFramePacket)
            {
                StopParameterRoot();
                RequireNativeStopPermission();
                lastNativeFinalFrameMode = useNativeFinalFramePacket;
            }

            if (parameterRoot != null
                && (parameterRoot.NativePinned != (shareParameterResources
                && useNativePreparedBatches
                && useNativePinnedPayloads)
                || parameterRoot.NativeIncremental != (shareParameterResources
                && useNativePreparedBatches
                && useNativeIncrementalPayloads)
                || parameterRoot.NativePrepared != (shareParameterResources
                && useNativePreparedBatches)
                || parameterRoot.SharesResources != shareParameterResources
                || parameterRoot.ReusesBatches != (shareParameterResources
                && reuseParameterBatches)
                || parameterRoot.ReusesScratch != (shareParameterResources
                && reuseParameterBatches
                && reuseParameterBatchScratch)
                || parameterRoot.LazySources != (shareParameterResources
                && reuseParameterBatches
                && lazyParameterSourceMeshes)))
            {
                StopParameterRoot();
            }

            if (parameterRoot == null && !ParameterRootRetirement.Admission)
            {
#if URUI_DEBUG
                ParameterRootFallbacks++;
#endif
                parameterRootFault = true;
                return false;
            }

            if (!(useNativeFinalFramePacket
                && rootFrame.CaptureFinalCore())
                && !rootFrame.CaptureOwned(optimizeAffinePreparation, reuseOwnedFrameSnapshot))
            {
#if URUI_DEBUG
                ParameterRootFallbacks++;
#endif
                return false;
            }
#if URUI_DEBUG
            NativeFinalFrameActive = rootFrame.NativeCoreReady;
            NativeFinalFrameReady = rootFrame.NativeCoreReadyCommands;
            NativeFinalFrameFallback = rootFrame.NativeCoreFallbackCommands;
            NativeFinalFrameBytes = rootFrame.NativeCoreBytes;
            ParameterFrameSnapshotHits = rootFrame.snapshotReused;
            ParameterFramePayloadReads = rootFrame.nativePayloadReads;
            ParameterFrameDecodedCommands = rootFrame.decodedCommands;
            ParameterFastPreparationCommands = rootFrame.fastCommands;
            ParameterGeneralPreparationCommands = rootFrame.fallbackCommands;
#endif
            if (parameterRoot == null)
            {
                parameterRoot = ParameterRootRetirement.Create(buffer, shader, parameterCapability.Parameter, shareParameterResources, reuseParameterBatches, reuseParameterBatchScratch, lazyParameterSourceMeshes, useNativePreparedBatches, useNativeIncrementalPayloads, useNativePinnedPayloads, borrowedTextures);
            }

            if (parameterRoot == null)
            {
#if URUI_DEBUG
                ParameterRootFallbacks++;
#endif
                parameterRootFault = true;
                return false;
            }

            parameterRoot.DirectSourceValidation = directParameterSourceValidation;
            var result = parameterRoot.Record(rootFrame, UiWidth, UiHeight);
            if (result != ParameterTableRenderer.Result.Success)
            {
                var reason = parameterRoot.FailureReason;
#if URUI_DEBUG
                ParameterRootFallbacks++;
#endif
                parameterRootFault = true;
                StopParameterRoot();
                Debug.LogWarning("URUI parameter root unsupported/faulted: " + result + " " + reason + "; original path retained until component restart");
                return false;
            }
#if URUI_DEBUG
            var stats = parameterRoot.Last;
            ParameterBatchFallbacks = stats.fallbacks;
            NativePayloadCopyBytes = stats.nativeCopyBytes;
            NativePayloadFullBytes = stats.nativeFullBytes;
            NativePayloadPossibleBytes = stats.nativePossibleBytes;
            NativePayloadDeltaCopies = stats.nativeDeltaCopies;
            NativePreparedActive = stats.nativeActive == 1;
            NativePreparedCalls = stats.nativePrepares;
            NativePreparedBuilds = stats.nativeBuilds;
            NativePreparedRetainedBytes = stats.nativeRetainedBytes;
            NativePreparedStagingBytes = stats.nativeStagingBytes;
            ParameterSourceMeshBuilds = stats.sourceMeshBuilds;
            ParameterSourceMeshDeferred = stats.sourceMeshDeferred;
            ParameterSourceMeshPromotions = stats.sourceMeshPromotions;
            ParameterSourceGpuBytes = stats.sourceGpuBytes;
            ParameterScratchRetainedBytes = stats.scratchRetainedBytes;
            ParameterScratchHighWaterBytes = stats.scratchHighWaterBytes;
            ParameterScratchOverflow = stats.scratchOverflow;
            ParameterScratchLeases = stats.scratchLeases;
            ParameterScratchGrowth = stats.scratchGrowth;
            ParameterScratchCold = stats.scratchCold;
            ParameterScratchClearTicks = stats.scratchClearTicks;
            ParameterBatchHits = stats.batchHits;
            ParameterBatchBuilds = stats.batchBuilds;
            ParameterMeshArrayReads = stats.meshArrayReads;
            ParameterBatchUnchanged = stats.batchUnchanged;
            ParameterBatchDirtyGeometry = stats.batchDirtyGeometry;
            ParameterBatchDirtyOrdinal = stats.batchDirtyOrdinal;
            ParameterBatchDirtyState = stats.batchDirtyState;
            ParameterBatchDirtyEpoch = stats.batchDirtyEpoch;
            ParameterBatchNew = stats.batchNew;
            ParameterTextureCopyCalls = stats.textureCopies;
            ParameterGeometryCopyCalls = stats.geometryCopies;
            ParameterTextureCopyBytes = stats.textureCopyBytes;
            ParameterGeometryCopyBytes = stats.geometryCopyBytes;
            ParameterRootActive = true;
            ParameterRootRecordings++;
            ParameterRequestedBytes = stats.parameterRequestedBytes;
            ParameterGeometryBytes = stats.geometryRequestedBytes;
            ParameterTextureBytes = stats.textureRequestedBytes;
            ParameterDraws = stats.draws;
            ParameterSourceFrame = Time.frameCount;
            ParameterSubmissionFrame = -1;
            // Recording is not proof that a camera executed it.
            DrawCommands = rootFrame.count;
#endif
            InvalidateRecording(false);
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                int copiedCommands = rootFrame.nativePayloadReads == 0 ? 0 : rootFrame.count;
                phase.commandsRead = copiedCommands;
                phase.drawSubmissions = stats.draws;
                phase.commandCopyBytes = copiedCommands == 0 ? 0 : copiedCommands * (rootFrame.NativeCoreReady ? 40 : System.Runtime.InteropServices.Marshal.SizeOf(typeof(CommandNative)));
                phase.parameterCopyBytes = rootFrame.NativeCoreReady ? copiedCommands * 32 : 0;
                phase.matrixCopyBytes = copiedCommands * 64;
                phase.totalCommandCopyBytes = phase.commandCopyBytes + phase.matrixCopyBytes + phase.parameterCopyBytes;
                phase.geometryCopyBytes = (int)stats.geometryRequestedBytes;
                phase.meshesCreated = stats.meshBuilds;
            }
#endif
            return true;
        }

        bool StopParameterRoot()
        {
            bool safe = StopBorrowedNativeRoot();
            rootFrame.InvalidateNativeCore();
            rootFrame.InvalidateOwnedSnapshot();
#if URUI_DEBUG
            ResetParameterRootMetrics();
#endif
            if (parameterRoot != null)
            {
                var old = parameterRoot;
                parameterRoot = null;
                ParameterRootRetirement.Retire(old);
            }

            return safe;
        }
    }
}
