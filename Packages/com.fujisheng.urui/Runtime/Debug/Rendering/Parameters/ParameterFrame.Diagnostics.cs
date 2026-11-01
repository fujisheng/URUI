#if URUI_DEBUG
namespace URUI.Internal.Rendering.Parameters
{
    internal sealed partial class ParameterFrame
    {
        static readonly Unity.Profiling.ProfilerMarker captureMarkerDefinition = new Unity.Profiling.ProfilerMarker("URUI.ParameterCapture");
        static readonly Unity.Profiling.ProfilerMarker nativeReadMarkerDefinition = new Unity.Profiling.ProfilerMarker("URUI.ParameterCapture.NativeRead");
        static readonly Unity.Profiling.ProfilerMarker prepareLoopMarkerDefinition = new Unity.Profiling.ProfilerMarker("URUI.ParameterCapture.PrepareLoop");
        public int fastCommands;
        public int fallbackCommands;
        public int snapshotReused;
        public int nativePayloadReads;
        public int decodedCommands;
    }
}
#endif
