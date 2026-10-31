#if URUI_DEBUG
using Unity.Profiling;

namespace URUI.Internal
{
    internal partial struct RecordingKey
    {
        static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("URUI.RetainedKeyCapture");
    }

    internal struct RecordingStats
    {
        public bool reused;
        public bool capability;
        public int nativeCommands;
        public int recordCalls;
        public int retainedDraws;
        public ulong nativeExtraBytes;
        public ulong compareBytes;
    }
}
#endif
