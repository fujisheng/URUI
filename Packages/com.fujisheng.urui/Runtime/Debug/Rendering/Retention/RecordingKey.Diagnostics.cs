#if URUI_DEBUG
using Unity.Profiling;

namespace URUI.Internal.Rendering.Retention
{
    internal partial struct RecordingKey
    {
        static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("URUI.RetainedKeyCapture");
    }
}
#endif
