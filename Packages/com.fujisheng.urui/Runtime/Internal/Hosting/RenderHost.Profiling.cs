using Unity.Profiling;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        static readonly ProfilerMarker synchronizeMarker = new("URUI.SynchronizeLayout");
        static readonly ProfilerMarker nativeUpdatePhase = new("URUI.Native.Update");
        static readonly ProfilerMarker nativeRenderPhase = new("URUI.Native.Render");
        static readonly ProfilerMarker recordingPhase = new("URUI.Render.Record");
        static readonly ProfilerMarker planCompilePhase = new("URUI.Native.Plan.Compile");
        static readonly ProfilerMarker planPreparePhase = new("URUI.Render.Prepare");
        static readonly ProfilerMarker planUploadPhase = new("URUI.Render.Upload");
        static readonly ProfilerMarker commandPhase = new("URUI.Render.Commands");
        static readonly ProfilerMarker recordStatePhase = new("URUI.Render.Record.State");
        static readonly ProfilerMarker effectParametersPhase = new("URUI.Render.Record.Effects");
        static readonly ProfilerMarker readInputPhase = new("URUI.Render.Record.Input");
        static readonly ProfilerMarker readResourcesPhase = new("URUI.Render.Record.Resources");
        static readonly ProfilerMarker regionPhase = new("URUI.Render.Record.Regions");
        static readonly ProfilerMarker externalRecordingPhase = new("URUI.Render.Record.External");
        static readonly ProfilerMarker finalTargetPhase = new("URUI.Render.Record.Targets");
        static readonly ProfilerMarker finishRecordingPhase = new("URUI.Render.Record.Finish");
        internal static readonly ProfilerMarker ElementRefreshPhase = new("URUI.Elements.Refresh");
    }
}
