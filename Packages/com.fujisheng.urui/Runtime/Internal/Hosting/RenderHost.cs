using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Input;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Shaders;
using URUI.Internal.Resources;

namespace URUI.Internal.Hosting
{
    /// <summary>
    /// 由 Surface 持有的共享渲染宿主，负责原生 context、Unity 资源及输入分发。
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
        // Document owns renderer startup and teardown; this type is not a user API.
        internal static RenderHost ActiveHost
        {
            get;
            private set;
        }

        internal URUI.Hosting.Surface OwnerSurface;
        bool startupAttempted;
        bool resourcesReleased;
        public TextAsset document;
        public ImageBinding[] imageAssets = Array.Empty<ImageBinding>();
        internal URUI.Resources.TextResource[] textResources = Array.Empty<URUI.Resources.TextResource>();
        public Shader shader;
        ShaderPropertyContract propertyContract;

        public bool ShaderContractActive
        {
            get;
            private set;
        }

        readonly ShaderPropertyState shaderPropertyState = new ShaderPropertyState();

        public event Action<PointerInputEvent> PointerInput;

        public string InitialMarkup;

        public bool Ready => active;
        internal Camera InspectionCamera => targetCamera;

        public int UiWidth => OwnerSurface ? OwnerSurface.Width : Screen.width;

        public int UiHeight => OwnerSurface ? OwnerSurface.Height : Screen.height;

        public int ElementCount { get { var count = 0; if (active) foreach (var document in OwnerSurface.DocumentsSnapshot()) if (document && document.IsLoaded) count += NativeMethods.urui_element_count(document.NativeHandle); return count; } }

        readonly HashSet<ulong> frameUsedGeometry = new HashSet<ulong>();
        readonly List<ulong> frameStaleGeometry = new List<ulong>();

        // Geometry and uploaded texture copies belong to this host. Native IDs decide
        // whether they are still alive; off-screen resources remain cached.
        readonly Dictionary<ulong, MeshCache> meshes = new Dictionary<ulong, MeshCache>();
        readonly BorrowedTextureRegistry borrowedTextures = new BorrowedTextureRegistry();
        readonly Dictionary<ulong, Texture2D> textures = new Dictionary<ulong, Texture2D>();
        readonly List<ulong> retiredTextureIds = new List<ulong>();
        readonly List<MeshCache> idleGeometry = new();
        Vector2[] zeroTextureSlots = Array.Empty<Vector2>();
        int pooledGeometryBytes;
        CommandBuffer buffer;
        Material material;
        internal int RecordedGeometryDraws { get; private set; }
        internal int RecordedRasterResolves { get; private set; }
        MaterialPropertyBlock reusableProperties;
        Camera targetCamera;
        bool active;
        int lastWidth;
        int lastHeight;
        CommandNative[] commands = Array.Empty<CommandNative>();
        float[] commandTransforms = Array.Empty<float>();
        Texture2D white;
        static readonly int mainTextureId = Shader.PropertyToID("_MainTex");
        static readonly int clipEnabledId = Shader.PropertyToID("_UruiClipEnabled");
        static readonly int textureSamplingId = Shader.PropertyToID("_UruiTextureSampling");
        static readonly int clipRectId = Shader.PropertyToID("_UruiClipRect");
        static readonly int translationId = Shader.PropertyToID("_UruiTranslation");
    }
}
