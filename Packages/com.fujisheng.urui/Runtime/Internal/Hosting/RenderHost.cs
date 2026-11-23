using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Shaders;
using URUI.Internal.Resources;

namespace URUI.Internal.Hosting
{
    /// <summary>
    /// 由 Surface 持有的共享渲染宿主，负责原生 Surface、Unity 资源及输入分发。
    /// 自有 mesh、纹理副本和命令记录须按提交顺序释放；借用的源资产不在此销毁。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    internal sealed partial class RenderHost : MonoBehaviour
    {
        // Empty in production: render helpers share a signature without carrying statistics.
#if !URUI_DEBUG
        internal struct PhaseFrame
        {
        }
#endif
        static readonly Dictionary<ulong, RenderHost> activeHosts = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHostRegistry()
        {
            activeHosts.Clear();
        }

        internal static bool TryGetHost(ulong surface, out RenderHost host)
        {
            if (activeHosts.TryGetValue(surface, out host) && host)
            {
                return true;
            }
            host = null;
            return false;
        }

        internal static bool TryGetHostIncludingDestroyed(ulong surface, out RenderHost host)
        {
            if (activeHosts.TryGetValue(surface, out host) && !ReferenceEquals(host, null))
            {
                return true;
            }
            host = null;
            return false;
        }

        // Legacy/diagnostic accessor returning any currently active host.
        internal static RenderHost ActiveHost
        {
            get
            {
                foreach (var host in activeHosts.Values)
                {
                    if (host)
                    {
                        return host;
                    }
                }
                return null;
            }
        }

        [field: NonSerialized]
        internal ulong SurfaceHandle
        {
            get;
            private set;
        }

        [NonSerialized] internal URUI.Hosting.Surface OwnerSurface;
        [NonSerialized] bool startupAttempted;
        [NonSerialized] bool resourcesReleased;
        public Shader shader;
        ShaderPropertyContract propertyContract;

        public bool ShaderContractActive
        {
            get;
            private set;
        }

        public bool Ready => active;
        internal Camera InspectionCamera => targetCamera;
        public string LastError => active ? NativeMethods.LastError(SurfaceHandle) : "URUI host is not active";

        public int UIWidth => OwnerSurface ? OwnerSurface.Width : Screen.width;
        public int UIHeight => OwnerSurface ? OwnerSurface.Height : Screen.height;

        readonly HashSet<ulong> frameUsedGeometry = new HashSet<ulong>();
        readonly List<ulong> frameStaleGeometry = new List<ulong>();

        // Geometry and uploaded texture copies belong to this host. Native IDs decide
        // whether they are still alive; off-screen resources remain cached.
        readonly Dictionary<ulong, MeshCache> meshes = new Dictionary<ulong, MeshCache>();
        readonly BorrowedTextureRegistry borrowedTextures = new BorrowedTextureRegistry();
        readonly Dictionary<ulong, NativeTextureEntry> textures = new Dictionary<ulong, NativeTextureEntry>();
        readonly List<ulong> retiredTextureIds = new List<ulong>();
        readonly List<MeshCache> idleGeometry = new();
        Vector2[] zeroTextureSlots = Array.Empty<Vector2>();
        int pooledGeometryBytes;
        CommandBuffer buffer;
        CommandRenderState renderState;
        bool layerBound;
        bool screenCaptureValid;
        bool hasExternalSurfaceWrites;
        bool hasReleasedResolvedImages;
        Material material;
        internal int RecordedGeometryDraws
        {
            get;
            private set;
        }
        MaterialPropertyBlock reusableProperties;
        Camera targetCamera;
        [NonSerialized] bool active;
        int lastWidth;
        int lastHeight;
        Texture2D white;
        static readonly int mainTextureId = Shader.PropertyToID("_MainTex");
    }
}
