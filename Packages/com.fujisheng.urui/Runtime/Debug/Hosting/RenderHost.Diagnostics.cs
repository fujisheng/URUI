#if URUI_DEBUG
using System;
using Unity.Profiling;
using URUI.Internal.Diagnostics;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Frames;
using URUI.Internal.Rendering.Retention;

namespace URUI.Internal.Hosting
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

        public bool BorrowedNativeFrameActive
        {
            get;
            private set;
        }

        static void NativeHostClearProbe(string at)
        {
            NativeFrameRoot.faultForProbe?.Invoke(at);
        }

        long parameterPreparationBytes;
        long parameterPreparationTextureBytes;
        int parameterPreparedSourceFrame;
        bool parameterPreparationReported;
    }
}
#endif
