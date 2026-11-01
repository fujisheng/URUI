#if URUI_DEBUG
namespace URUI.Internal.Hosting
{
    // 汇总可选渲染路径的诊断读数，不授予任何资源或 GPU 安全放行能力。
    internal sealed partial class RenderHost
    {
        public long NativePayloadCopyBytes
        {
            get;
            private set;
        }
        public long NativePayloadFullBytes
        {
            get;
            private set;
        }
        public long NativePayloadPossibleBytes
        {
            get;
            private set;
        }
        public int NativePayloadDeltaCopies
        {
            get;
            private set;
        }
        public bool NativeFinalFrameActive
        {
            get;
            private set;
        }
        public int NativeFinalFrameReady
        {
            get;
            private set;
        }
        public int NativeFinalFrameFallback
        {
            get;
            private set;
        }
        public long NativeFinalFrameBytes
        {
            get;
            private set;
        }
        public bool NativePreparedActive
        {
            get;
            private set;
        }
        public int NativePreparedCalls
        {
            get;
            private set;
        }
        public int NativePreparedBuilds
        {
            get;
            private set;
        }
        public long NativePreparedRetainedBytes
        {
            get;
            private set;
        }
        public long NativePreparedStagingBytes
        {
            get;
            private set;
        }
        public int ParameterFrameSnapshotHits
        {
            get;
            private set;
        }
        public int ParameterFramePayloadReads
        {
            get;
            private set;
        }
        public int ParameterFrameDecodedCommands
        {
            get;
            private set;
        }
        public int ParameterSourceMeshBuilds
        {
            get;
            private set;
        }
        public int ParameterSourceMeshDeferred
        {
            get;
            private set;
        }
        public int ParameterSourceMeshPromotions
        {
            get;
            private set;
        }
        public long ParameterSourceGpuBytes
        {
            get;
            private set;
        }
        public int ParameterFastPreparationCommands
        {
            get;
            private set;
        }
        public int ParameterGeneralPreparationCommands
        {
            get;
            private set;
        }
        public long ParameterScratchRetainedBytes
        {
            get;
            private set;
        }
        public long ParameterScratchHighWaterBytes
        {
            get;
            private set;
        }
        public long ParameterScratchOverflow
        {
            get;
            private set;
        }
        public long ParameterScratchLeases
        {
            get;
            private set;
        }
        public long ParameterScratchGrowth
        {
            get;
            private set;
        }
        public long ParameterScratchCold
        {
            get;
            private set;
        }
        public long ParameterScratchClearTicks
        {
            get;
            private set;
        }
        public int ParameterBatchHits
        {
            get;
            private set;
        }
        public int ParameterBatchBuilds
        {
            get;
            private set;
        }
        public int ParameterMeshArrayReads
        {
            get;
            private set;
        }
        public int ParameterBatchUnchanged
        {
            get;
            private set;
        }
        public int ParameterBatchDirtyGeometry
        {
            get;
            private set;
        }
        public int ParameterBatchDirtyOrdinal
        {
            get;
            private set;
        }
        public int ParameterBatchDirtyState
        {
            get;
            private set;
        }
        public int ParameterBatchDirtyEpoch
        {
            get;
            private set;
        }
        public int ParameterBatchNew
        {
            get;
            private set;
        }
        public int ParameterTextureCopyCalls
        {
            get;
            private set;
        }
        public int ParameterGeometryCopyCalls
        {
            get;
            private set;
        }
        public long ParameterTextureCopyBytes
        {
            get;
            private set;
        }
        public long ParameterGeometryCopyBytes
        {
            get;
            private set;
        }
        public int ParameterBatchFallbacks
        {
            get;
            private set;
        }
        public int ParameterRootRecordings
        {
            get;
            private set;
        }
        public int ParameterRootFallbacks
        {
            get;
            private set;
        }
        public bool ParameterRootActive
        {
            get;
            private set;
        }

        void ResetParameterRootMetrics()
        {
            ParameterBatchFallbacks = 0;
            NativeFinalFrameActive = false;
            NativeFinalFrameReady = NativeFinalFrameFallback = 0;
            NativeFinalFrameBytes = 0;
            NativePayloadCopyBytes = NativePayloadFullBytes = NativePayloadPossibleBytes = 0;
            NativePayloadDeltaCopies = 0;
            NativePreparedActive = false;
            NativePreparedCalls = NativePreparedBuilds = 0;
            NativePreparedRetainedBytes = NativePreparedStagingBytes = 0;
            ParameterFrameSnapshotHits = ParameterFramePayloadReads = ParameterFrameDecodedCommands = 0;
            ParameterSourceMeshBuilds = ParameterSourceMeshDeferred = ParameterSourceMeshPromotions = 0;
            ParameterSourceGpuBytes = 0;
            ParameterFastPreparationCommands = ParameterGeneralPreparationCommands = 0;
            ParameterScratchRetainedBytes = ParameterScratchHighWaterBytes = ParameterScratchOverflow = ParameterScratchLeases = ParameterScratchGrowth = ParameterScratchCold = ParameterScratchClearTicks = 0;
            ParameterBatchHits = ParameterBatchBuilds = ParameterMeshArrayReads = ParameterBatchUnchanged = ParameterBatchDirtyGeometry = ParameterBatchDirtyOrdinal = ParameterBatchDirtyState = ParameterBatchDirtyEpoch = ParameterBatchNew = 0;
            ParameterTextureCopyCalls = ParameterGeometryCopyCalls = 0;
            ParameterTextureCopyBytes = ParameterGeometryCopyBytes = 0;
            ParameterRootActive = false;
            ParameterRequestedBytes = 0;
            ParameterGeometryBytes = 0;
            ParameterTextureBytes = 0;
            ParameterDraws = 0;
            ParameterSourceFrame = -1;
            ParameterSubmissionFrame = -1;
        }

        internal string SaveGeometryProbe(string directory) => parameterRoot?.SaveGeometryProbe(directory);

        public int ParameterSubmissions
        {
            get;
            private set;
        }
        public int ParameterFallbacks
        {
            get;
            private set;
        }
        public long ParameterRequestedBytes
        {
            get;
            private set;
        }
        public long ParameterGeometryBytes
        {
            get;
            private set;
        }
        public int ParameterDraws
        {
            get;
            private set;
        }
        public long ParameterTextureBytes
        {
            get;
            private set;
        }
        public int ParameterSourceFrame
        {
            get;
            private set;
        }
        public int ParameterSubmissionFrame
        {
            get;
            private set;
        }
    }
}
#endif
