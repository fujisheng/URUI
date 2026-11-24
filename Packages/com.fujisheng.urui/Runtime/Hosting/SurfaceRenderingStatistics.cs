namespace URUI.Hosting
{
    /// <summary>
    /// Snapshot of URUI rendering and native plan statistics for a <see cref="Surface"/>.
    /// Safe to read in both the Unity Editor and release Players.
    /// </summary>
    public readonly struct SurfaceRenderingStatistics
    {
        public SurfaceRenderingStatistics(
            bool ready,
            int loadedDocuments,
            int recordedGeometryDraws,
            int recordedExternalGeometryDraws,
            uint nativePlanCommandCount,
            ulong nativePlanGeometryBytes,
            ulong nativePlanFrameBytes,
            ulong nativePlanAllocations,
            ulong nativePlanFrees,
            ulong nativePlanAllocatedBytes,
            long geometryUploadBytes,
            long parameterUploadBytes,
            long parameterCopyBytes)
        {
            Ready = ready;
            LoadedDocuments = loadedDocuments;
            RecordedGeometryDraws = recordedGeometryDraws;
            RecordedExternalGeometryDraws = recordedExternalGeometryDraws;
            NativePlanCommandCount = nativePlanCommandCount;
            NativePlanGeometryBytes = nativePlanGeometryBytes;
            NativePlanFrameBytes = nativePlanFrameBytes;
            NativePlanAllocations = nativePlanAllocations;
            NativePlanFrees = nativePlanFrees;
            NativePlanAllocatedBytes = nativePlanAllocatedBytes;
            GeometryUploadBytes = geometryUploadBytes;
            ParameterUploadBytes = parameterUploadBytes;
            ParameterCopyBytes = parameterCopyBytes;
        }

        public bool Ready { get; }
        public int LoadedDocuments { get; }
        public int RecordedGeometryDraws { get; }
        public int RecordedExternalGeometryDraws { get; }
        public uint NativePlanCommandCount { get; }
        public ulong NativePlanGeometryBytes { get; }
        public ulong NativePlanFrameBytes { get; }
        public ulong NativePlanAllocations { get; }
        public ulong NativePlanFrees { get; }
        public ulong NativePlanAllocatedBytes { get; }
        public long GeometryUploadBytes { get; }
        public long ParameterUploadBytes { get; }
        public long ParameterCopyBytes { get; }
    }
}
