#if URUI_DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace URUI.Internal
{
    internal sealed partial class ParameterRootRecording
    {
        static readonly ProfilerMarker commandsMarkerDefinition = new ProfilerMarker("URUI.ParameterCommands");
        static readonly ProfilerMarker retireMarkerDefinition = new ProfilerMarker("URUI.ParameterRetire");
        static readonly ProfilerMarker recordMarker = new ProfilerMarker("URUI.ParameterRootRecord");
        internal bool holdCompletedFencesForProbe;

        // Holds even completed leases; never manufactures GPU completion.
        internal string SaveGeometryProbe(string directory) => geometry.SaveGeometryProbe(directory);

        internal ParameterTableRenderer.Stats Last => stats;
    }
}
#endif
