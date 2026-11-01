#if URUI_DEBUG
namespace URUI.Internal.Rendering.Retention
{
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
