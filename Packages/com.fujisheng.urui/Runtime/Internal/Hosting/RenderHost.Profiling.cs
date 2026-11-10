using Unity.Profiling;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        static readonly ProfilerMarker synchronizeMarker = new("URUI.SynchronizeLayout");
        static readonly ProfilerMarker nativeUpdatePhase = new("URUI.Native.Update");
        static readonly ProfilerMarker nativeRenderPhase = new("URUI.Native.Render");
        static readonly ProfilerMarker recordingPhase = new("URUI.Render.Record");
        static readonly ProfilerMarker commandPhase = new("URUI.Render.Commands");
        static readonly ProfilerMarker batchPhase = new("URUI.Render.Batch");
        internal static readonly ProfilerMarker ElementRefreshPhase = new("URUI.Elements.Refresh");
    }
}
