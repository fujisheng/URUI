using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Input;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Retention;
using URUI.Internal.Rendering.Shaders;
using URUI.Internal.Resources;

namespace URUI.Internal.Hosting
{
    /// <summary>
    /// 由 Document 持有的单文档渲染宿主，负责原生 context、Unity 资源及输入分发。
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

        bool startupAttempted;
        bool resourcesReleased;
        public TextAsset document;
        public ImageBinding[] imageAssets = Array.Empty<ImageBinding>();
        public Shader shader;
        public string overrideFontPath = "";
        public bool useShaderPropertyIds;
        public bool useFrameScratchCollections;
        [Tooltip("Experimental: retain Host-owned command recording for the audited built-in shader only. Do not mutate Host-owned meshes, material or command buffer externally; call NotifyExternalRenderResourcesChanged for external state changes.")]
        public bool retainUnchangedCommands;

        // Runtime-only capability: Unity serialization/Inspector cannot silently grant
        // exclusive ownership. Every public assignment runs the invalidating setter.
        [NonSerialized]
        RecordingOwnership recordingOwnershipValue = RecordingOwnership.ExternalMutable;

        public RecordingOwnership recordingOwnership
        {
            get => recordingOwnershipValue;
            set => SetRecordingOwnership(value);
        }

        public bool deferRetainedStateCapture;
        PlanStability planStability;
        bool? retentionSchedule;
        readonly RecordingState retainedRecording = new RecordingState();
        bool nativeRetention;
        bool retentionApiMissing;
        bool recordingWarmupPending;
        bool recordingFinite;
        int retainedBufferBytes;
        string recordedPassName = "";
        readonly List<TextureDependency> retainedTextures = new List<TextureDependency>();
        public bool reuseEquivalentPropertyState;
        public ShaderPropertyContract propertyContract;

        public bool ShaderContractActive
        {
            get;
            private set;
        }

        readonly ShaderPropertyState shaderPropertyState = new ShaderPropertyState();

        public event Action<ulong> Clicked;
        public event Action<PointerInputEvent> PointerInput;

        public bool reusePropertyBlock;
        public bool cullFullyClipped;
        public bool batchDisjointCommands;
        public bool batchEquivalentTransforms;
        public bool batchOrderedOverlap;
        public string InitialMarkup;

        public bool Ready => active;

        public int UiWidth => Screen.width;

        public int UiHeight => Screen.height;

        public int ElementCount => active ? NativeMethods.urui_element_count() : 0;

        readonly HashSet<ulong> frameUsedGeometry = new HashSet<ulong>();
        readonly List<ulong> frameStaleGeometry = new List<ulong>();

        // Geometry and uploaded texture copies belong to this host. Native IDs decide
        // whether they are still alive; off-screen resources remain cached.
        readonly Dictionary<ulong, MeshCache> meshes = new Dictionary<ulong, MeshCache>();
        bool? meshBatchMode;
        readonly Dictionary<ulong, Texture2D> textures = new Dictionary<ulong, Texture2D>();
        readonly List<ulong> retiredTextureIds = new List<ulong>();
        readonly Dictionary<ulong, BatchCache> batches = new Dictionary<ulong, BatchCache>();
        readonly List<BatchItem> batchGroup = new List<BatchItem>(128);
        readonly List<ulong> staleBatchKeys = new List<ulong>();
        HashSet<ulong> previousBatchKeys = new HashSet<ulong>();
        HashSet<ulong> currentBatchKeys = new HashSet<ulong>();
        CommandBuffer buffer;
        Material material;
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
        static readonly int clipRectId = Shader.PropertyToID("_UruiClipRect");
        static readonly int translationId = Shader.PropertyToID("_UruiTranslation");
    }
}
