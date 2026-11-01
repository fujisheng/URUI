#if URUI_DEBUG
namespace URUI.Internal.Hosting
{
    // 所属类型的内部状态记录；保留嵌套访问边界与原有资源引用关系。
    internal sealed partial class RenderHost
    {
        internal struct ApiFrame
        {
            public double styleMs;
            public double attributeMs;
            public double textMs;
            public int styles;
            public int attributes;
            public int texts;
        }

        internal struct PhaseFrame
        {
            public int batchGroups;
            public int breakTexture;
            public int breakClip;
            public int breakTransform;
            public int breakOverlap;
            public int vertexLimitGroups;
            public int fallbackExtraSubmissions;
            public double contextUpdateMs;
            public double eventsMs;
            public double nativeRenderMs;
            public double commandCopyMs;
            public double geometryCopyMs;
            public double meshBuildMs;
            public double propertiesMs;
            public double submitMs;
            public double cleanupMs;
            public double hostTotalMs;
            public double setTextMs;
            public double commandSetupMs;
            public double meshConversionMs;
            public double unityMeshApiMs;
            public double batchMeshBuildMs;
            public double batchUnityMeshApiMs;
            public double inputMs;
            public double matrixDecodeMs;
            public double geometryLookupMs;
            public double boundsApiMs;
            public double entryTrackMs;
            public double canJoinMs;
            public double flushGroupMs;
            public double batchMaintenanceMs;
            public int boundsReads;
            public int detailClockPairs;
            public int shaderNameLookups;
            public long managedAllocBytes;
            public int setTextCalls;
            public int nativeCalls;
            public int commandsRead;
            public int drawSubmissions;
            public int fullyClippedCandidates;
            public int propertyBlocksCreated;
            public int meshesCreated;
            public int geometryCopyBytes;
            public int commandCopyBytes;
            public int matrixCopyBytes;
            public int parameterCopyBytes;
            public int totalCommandCopyBytes;
        }
    }
}
#endif
