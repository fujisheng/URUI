#if URUI_DEBUG
using System;
using Unity.Profiling;
using URUI.Internal.Diagnostics;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    // Explicitly opt-in diagnostics. Editor and development builds are quiet by default.
    internal sealed partial class RenderHost
    {
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
        int recordCallsThisFrame;
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

    }
}
#endif
