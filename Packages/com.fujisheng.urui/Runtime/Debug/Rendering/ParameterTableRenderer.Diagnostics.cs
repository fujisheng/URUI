#if URUI_DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace URUI.Internal
{
    internal sealed partial class ParameterTableRenderer
    {
        internal int SourceGpuMeshCount
        {
            get
            {
                int n = 0;
                foreach (var v in sharedMeshes.Values)
                {
                    if (v.gpuCreated)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        internal int SharedMeshCount => sharedMeshes.Count;

        internal int SharedTextureCount => sharedTextures.Count;

        static readonly ProfilerMarker planMarkerDefinition = new ProfilerMarker("URUI.ParameterPlanBuild");
        static readonly ProfilerMarker sourceMarkerDefinition = new ProfilerMarker("URUI.ParameterSourceSnapshot");
        static readonly ProfilerMarker textureMarkerDefinition = new ProfilerMarker("URUI.ParameterTextureSnapshot");
        static readonly ProfilerMarker batchMarkerDefinition = new ProfilerMarker("URUI.ParameterBatchBuild");
        static readonly ProfilerMarker stageMarkerDefinition = new ProfilerMarker("URUI.ParameterBatchStage");
        static readonly ProfilerMarker uploadMarkerDefinition = new ProfilerMarker("URUI.ParameterBatchMeshSetters");
        static readonly ProfilerMarker identityMarkerDefinition = new ProfilerMarker("URUI.ParameterBatchIdentity");
        static readonly ProfilerMarker validateMarkerDefinition = new ProfilerMarker("URUI.ParameterPlanValidate");
        static readonly ProfilerMarker sourceValidateMarkerDefinition = new ProfilerMarker("URUI.ParameterSourceValidate");

        // 仅延后回收，绝不能将未完成的 GPU 栅栏伪造成完成。
        internal bool holdCompletedFencesForProbe;
        readonly GeometryContentProbe geometryProbe = GeometryContentProbe.Mode == 0 ? null : new GeometryContentProbe(GeometryContentProbe.Mode, 0);

        internal string SaveGeometryProbe(string directory)
        {
            if (geometryProbe == null)
            {
                return null;
            }

            geometryProbe.Save(directory);
            SaveBatchContent(directory);
            return directory;
        }

        internal Action beforeSourceMeshCreateForProbe;
        internal bool countSourceValidation;
        internal int sourceValidationVisits;
        internal int sourceValidationLookups;

        internal bool SourceValidationForProbe(Plan plan) => ValidateSources(plan);
        internal bool FullMatchForProbe(ParameterFrame frame) => Match(frame);

        void ScratchStats(ref Stats stats)
        {
            stats.nativeActive = nativePrepared && nativeOwner != null ? 1 : 0;
            if (nativeOwner != null)
            {
                stats.nativeStagingBytes = nativeOwner.StagingBytes;
                stats.nativeRetainedBytes = nativeOwner.RetainedBytes;
            }

            stats.sourceGpuBytes = sourcePayload;
            if (scratch == null)
            {
                return;
            }

            stats.scratchRetainedBytes = scratch.RetainedBytes;
            stats.scratchHighWaterBytes = scratch.HighWaterBytes;
            stats.scratchOverflow = scratch.OverflowCount;
            stats.scratchLeases = scratch.Leases;
            stats.scratchGrowth = scratch.CapacityGrowthEvents;
            stats.scratchCold = scratch.ColdLeases;
#if URUI_DEBUG
            stats.scratchClearTicks = scratch.ClearTicks;
#endif
        }

        void ClassifyBatch(ParameterFrame frame, int ordinal, int start, int count, ref Stats stats)
        {
            if (current == null || ordinal >= current.batches.Count)
            {
                stats.batchNew++;
                return;
            }

            if (current.epoch != frame.stamp.epoch)
            {
                stats.batchDirtyEpoch++;
                return;
            }

            var old = current.batches[ordinal];
            if (old.slot != start || old.count != count)
            {
                stats.batchDirtyOrdinal++;
                return;
            }

            if (!old.parameter || !StateOf(old.key).Equals(StateOf(frame.keys[start])))
            {
                stats.batchDirtyState++;
                return;
            }

            for (int j = 0; j < count; j++)
            {
                if (current.keys[start + j].geometry != frame.keys[start + j].geometry)
                {
                    stats.batchDirtyGeometry++;
                    return;
                }
            }

            stats.batchUnchanged++;
        }

        internal Stats Last
        {
            get;
            private set;
        }

        internal partial struct Stats
        {
            public long nativeCopyBytes;
            public long nativeFullBytes;
            public long nativePossibleBytes;
            public int nativeDeltaCopies;
            public int nativeActive;
            public int nativePrepares;
            public int nativeBuilds;
            public int nativeReuses;
            public long nativeRetainedBytes;
            public long nativeBuiltBytes;
            public long nativeStagingBytes;
            public int sourceMeshBuilds;
            public int sourceMeshDeferred;
            public int sourceMeshPromotions;
            public long sourceGpuBytes;
            public long scratchRetainedBytes;
            public long scratchHighWaterBytes;
            public long scratchOverflow;
            public long scratchLeases;
            public long scratchGrowth;
            public long scratchCold;
            public long scratchClearTicks;
            public int batchHits;
            public int batchBuilds;
            public int batchNew;
            public int batchDirtyEpoch;
            public int batchDirtyOrdinal;
            public int batchDirtyState;
            public int batchDirtyGeometry;
            public int batchUnchanged;
            public int meshArrayReads;
            public int meshBuilds;
            public int planBuilds;
            public int draws;
            public int fallbacks;
            public int packets;
            public int plans;
            public int textureCopies;
            public int geometryCopies;
            public int textureHits;
            public int geometryHits;
            public long textureCopyBytes;
            public long geometryCopyBytes;
            public long geometryRequestedBytes;
            public long textureRequestedBytes;
            public long parameterRequestedBytes;
            public long retainedParameterBytes;
        }
    }
}
#endif
