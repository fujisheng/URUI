#if URUI_DEBUG
using System;
using UnityEngine;
using Unity.Profiling;

namespace URUI.Internal
{
    // Explicitly opt-in diagnostics. Editor and development builds are quiet by default.
    internal sealed partial class RenderHost
    {
        public bool useInlineLookupQueue;
        [NonSerialized]
        public int detailedHostMode;
        public bool measureBridge;
        public bool phaseDiagnostics;
        public bool skipDrawForDiagnosis;
        public bool propertyDependencyDiagnostics;
        public readonly PropertyDependencyCounter propertyDependencies = new PropertyDependencyCounter();

        public PhaseFrame LastPhase
        {
            get;
            private set;
        }

        internal struct ApiFrame
        {
            public double styleMs;
            public double attributeMs;
            public double textMs;
            public int styles;
            public int attributes;
            public int texts;
        }

        ApiFrame apiFrame;

        public ApiFrame TakeApiFrame()
        {
            var result = apiFrame;
            apiFrame = default;
            return result;
        }

        double textMilliseconds;
        double frameTextMilliseconds;
        int textCalls;

        static long ThreadAllocationBytes()
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            // This backend's thread counter is not implemented. Zero is not evidence of
            // zero allocation; use raw Profiler GC.Alloc for scoped IL2CPP measurements.
            return -1;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

        static long Now() => System.Diagnostics.Stopwatch.GetTimestamp();
        static double Millis(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        long DetailBegin(ref double value, ref PhaseFrame phase)
        {
            if (!phaseDiagnostics || detailedHostMode == 0)
            {
                return 0;
            }

            phase.detailClockPairs++;
            if (detailedHostMode == 2)
            {
                long t = Now();
                value += Millis(Now() - t);
                return 0;
            }

            return Now();
        }

        void DetailEnd(long t, ref double value)
        {
            if (phaseDiagnostics && detailedHostMode == 1)
            {
                value += Millis(Now() - t);
            }
        }

        public double TakeFrameTextMilliseconds()
        {
            double result = frameTextMilliseconds;
            frameTextMilliseconds = 0;
            return result;
        }

        public double FrameTextMilliseconds => frameTextMilliseconds;

        public bool SetInlineLookupQueue(bool enabled)
        {
            if (!active)
            {
                return false;
            }

            if (NativeMethods.urui_lookup_inline_enable(enabled ? 1 : 0) != 1)
            {
                return false;
            }

            useInlineLookupQueue = enabled;
            return true;
        }

        public int PropertyCalls
        {
            get;
            private set;
        }
        public int PropertyWriteCalls
        {
            get;
            private set;
        }
        public int PropertyReuseHits
        {
            get;
            private set;
        }
        public double UpdateMilliseconds
        {
            get;
            private set;
        }
        public int DrawCommands
        {
            get;
            private set;
        }
        public RecordingStats LastRecording
        {
            get;
            private set;
        }

        int recordCallsThisFrame;
        int retainedDraws;
        static readonly ProfilerMarker propertyApiMarker = new ProfilerMarker("URUI.PropertyAPI");
        static readonly ProfilerMarker hostMarker = new ProfilerMarker("URUI.Host");
        static readonly ProfilerMarker inputMarker = new ProfilerMarker("URUI.Input");
        static readonly ProfilerMarker updateMarker = new ProfilerMarker("URUI.NativeUpdate");
        static readonly ProfilerMarker renderMarker = new ProfilerMarker("URUI.NativeRender");
        static readonly ProfilerMarker eventMarker = new ProfilerMarker("URUI.Events");
        static readonly ProfilerMarker readMarker = new ProfilerMarker("URUI.ReadCommands");
        static readonly ProfilerMarker loopMarker = new ProfilerMarker("URUI.CommandLoop");
        static readonly ProfilerMarker geometryMarker = new ProfilerMarker("URUI.CreateGeometry");
        static readonly ProfilerMarker flushMarker = new ProfilerMarker("URUI.FlushGroup");
        static readonly ProfilerMarker propertiesMarker = new ProfilerMarker("URUI.Properties");
        static readonly ProfilerMarker submitMarker = new ProfilerMarker("URUI.Submit");
        static readonly ProfilerMarker drawMarker = new ProfilerMarker("URUI.DrawFrame");
        static readonly ProfilerMarker setupMarker = new ProfilerMarker("URUI.UnitySetup");
        static readonly ProfilerMarker cleanupMarker = new ProfilerMarker("URUI.Cleanup");
        static readonly ProfilerMarker retainedResourceMarker = new ProfilerMarker("URUI.RetainedResourceValidation");
        static readonly ProfilerMarker dependencyCaptureMarker = new ProfilerMarker("URUI.RetainedDependencyCapture");
        static readonly ProfilerMarker parameterMarker = new ProfilerMarker("URUI.HostParameterCamera");

        internal struct PhaseFrame
        {
            public int batchGroups;
            public int breakTexture;
            public int breakClip;
            public int breakTransform;
            public int breakOverlap;
            public int vertexLimitGroups;
            public int fallbackExtraSubmissions;
            public double contextUpdateMs;
            public double eventsMs;
            public double nativeRenderMs;
            public double commandCopyMs;
            public double geometryCopyMs;
            public double meshBuildMs;
            public double propertiesMs;
            public double submitMs;
            public double cleanupMs;
            public double hostTotalMs;
            public double setTextMs;
            public double commandSetupMs;
            public double meshConversionMs;
            public double unityMeshApiMs;
            public double batchMeshBuildMs;
            public double batchUnityMeshApiMs;
            public double inputMs;
            public double matrixDecodeMs;
            public double geometryLookupMs;
            public double boundsApiMs;
            public double entryTrackMs;
            public double canJoinMs;
            public double flushGroupMs;
            public double batchMaintenanceMs;
            public int boundsReads;
            public int detailClockPairs;
            public int shaderNameLookups;
            public long managedAllocBytes;
            public int setTextCalls;
            public int nativeCalls;
            public int commandsRead;
            public int drawSubmissions;
            public int fullyClippedCandidates;
            public int propertyBlocksCreated;
            public int meshesCreated;
            public int geometryCopyBytes;
            public int commandCopyBytes;
            public int matrixCopyBytes;
            public int parameterCopyBytes;
            public int totalCommandCopyBytes;
        }

        public bool BorrowedNativeFrameActive
        {
            get;
            private set;
        }

        static void NativeHostClearProbe(string at)
        {
            NativeFrameRoot.faultForProbe?.Invoke(at);
        }

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

        long parameterPreparationBytes;
        long parameterPreparationTextureBytes;
        int parameterPreparedSourceFrame;
        bool parameterPreparationReported;
    }
}
#endif
