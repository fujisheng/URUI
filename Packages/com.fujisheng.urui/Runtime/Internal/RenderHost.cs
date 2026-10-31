using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal
{
    [Serializable]
    internal struct ImageBinding
    {
        public string source;
        public Texture2D texture;
        public int expectedWidth;
        public int expectedHeight;
    }

    internal enum PointerPhase
    {
        Down,
        Move,
        Up,
        Cancel
    }

    internal struct PointerInputEvent
    {
        public PointerPhase phase;
        public int pointerId;
        public float x;
        public float y;
        public string hitId;
    }

    internal static class BatchRules
    {
        public static bool EquivalentAffine2D(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (float.IsNaN(a[i]) || float.IsInfinity(a[i]) || a[i] != b[i])
                {
                    return false;
                }
            }

            if (a.m02 != 0
                || a.m12 != 0
                || a.m20 != 0
                || a.m21 != 0
                || a.m22 != 1
                || a.m23 != 0
                || a.m30 != 0
                || a.m31 != 0
                || a.m32 != 0
                || a.m33 != 1)
            {
                return false;
            }

            return Math.Abs((double)a.m00 * a.m11 - (double)a.m01 * a.m10) > 1e-12;
        }

        // Touching bounds are kept separate so edge pixels cannot change blending.
        public static bool Disjoint(float ax0, float ay0, float ax1, float ay1, float bx0, float by0, float bx1, float by1) => ax1 < bx0 || bx1 < ax0 || ay1 < by0 || by1 < ay0;
    }

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

        internal void InitializeForDocument()
        {
            Start();
        }

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

        struct TextureDependency
        {
            public Texture texture;
            public uint update;
            public int width;
            public int height;
            public FilterMode filter;
            public TextureWrapMode u;
            public TextureWrapMode v;
            public TextureWrapMode w;
            public float bias;
            public int aniso;
        }

        readonly List<TextureDependency> retainedTextures = new List<TextureDependency>();
        public bool reuseEquivalentPropertyState;
        public ShaderPropertyContract propertyContract;

        public bool ShaderContractActive
        {
            get;
            private set;
        }

        readonly ShaderPropertyState shaderPropertyState = new ShaderPropertyState();

        public event Action<string> Clicked;
        public event Action<PointerInputEvent> PointerInput;

        public bool reusePropertyBlock;
        public bool cullFullyClipped;
        public bool batchDisjointCommands;
        public bool batchEquivalentTransforms;
        public bool batchOrderedOverlap;

        Bounds GeometryBounds(Mesh mesh, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = DetailBegin(ref phase.boundsApiMs, ref phase);
#endif
            var b = mesh.bounds;
#if URUI_DEBUG
            DetailEnd(t, ref phase.boundsApiMs);
            if (phaseDiagnostics)
            {
                phase.boundsReads++;
            }
#endif
            return b;
        }

        public string InitialMarkup;

        public bool Ready => active;

        public int UiWidth => Screen.width;

        public int UiHeight => Screen.height;

        public int ElementCount => active ? NativeMethods.urui_element_count() : 0;

        public void LoadMarkup(string markup)
        {
            if (active)
            {
                InvalidateRecording(true);
                ClearBatches();
                ClearFrameScratch();
                foreach (var mesh in meshes.Values)
                {
                    if (mesh.mesh)
                    {
                        Destroy(mesh.mesh);
                    }
                }

                meshes.Clear();
                foreach (var texture in textures.Values)
                {
                    if (texture)
                    {
                        Destroy(texture);
                    }
                }

                textures.Clear();
                buffer.Clear();
                if (NativeMethods.urui_load_document(markup.Replace("__URUI_FONT__", "Roboto")) == 0)
                {
                    throw new Exception("URUI document load: " + NativeMethods.LastError());
                }
            }
            else
            {
                InitialMarkup = markup;
            }
        }

        sealed class MeshCache
        {
            public Mesh mesh;
            public Vector3[] positions;
            public Vector2[] uv;
            public Color32[] colors;
            public int[] indices;
            public int lastFrame;
        }

        struct BatchItem
        {
            public CommandNative command;
            public MeshCache geometry;
            public Matrix4x4 transform;
            public bool transformed;
            public float x0;
            public float y0;
            public float x1;
            public float y1;
        }

        sealed class BatchCache
        {
            public Mesh mesh;
            public BatchItem[] items;
        }

        readonly HashSet<ulong> frameUsedGeometry = new HashSet<ulong>();
        readonly List<ulong> frameStaleGeometry = new List<ulong>();

        void ClearFrameScratch()
        {
            frameUsedGeometry.Clear();
            frameUsedGeometry.TrimExcess();
            frameStaleGeometry.Clear();
            frameStaleGeometry.TrimExcess();
        }

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
        int referenceThread;
        int lastWidth;
        int lastHeight;
        CommandNative[] commands = Array.Empty<CommandNative>();
        float[] commandTransforms = Array.Empty<float>();
        byte[] eventBuffer = new byte[4096];
        Texture2D white;

        void ClearBatches()
        {
            foreach (var b in batches.Values)
            {
                if (b.mesh)
                {
                    Destroy(b.mesh);
                }
            }

            batches.Clear();
            batchGroup.Clear();
            previousBatchKeys.Clear();
            currentBatchKeys.Clear();
            staleBatchKeys.Clear();
        }

        // Document initializes this component synchronously. Unity may call Start again,
        // so startupAttempted also prevents a second native context from being created.
        void Start()
        {
            if (startupAttempted || resourcesReleased)
            {
                return;
            }

            startupAttempted = true;
            if (!document)
            {
                Debug.LogError("URUI: assign RML TextAsset.");
                enabled = false;
                return;
            }

            if (!shader)
            {
                shader = Resources.Load<Shader>("URUI/Premultiplied");
            }

            if (!shader)
            {
                shader = Shader.Find("URUI/Premultiplied");
            }

            if (!shader)
            {
                Debug.LogError("URUI: shader missing.");
                enabled = false;
                return;
            }

            targetCamera = Camera.main;
            if (!targetCamera)
            {
                Debug.LogError("URUI: camera missing.");
                enabled = false;
                return;
            }

            material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            recordedPassName = material.GetPassName(0);
            if (reusePropertyBlock)
            {
                reusableProperties = new MaterialPropertyBlock();
            }

            white = Texture2D.whiteTexture;
            buffer = new CommandBuffer
            {
                name = "URUI UI"
            };
            targetCamera.AddCommandBuffer(CameraEvent.AfterEverything, buffer);
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            try
            {
                if (NativeMethods.urui_bridge_abi_version() != 2)
                {
                    throw new Exception("URUIBridge ABI version 2 is required");
                }

                if (NativeMethods.urui_create(UiWidth, UiHeight, ResolveFontPath()) == 0)
                {
                    throw new Exception(NativeMethods.LastError());
                }

                active = true;
                ActiveHost = this;
                referenceThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
#if URUI_DEBUG
                if (useInlineLookupQueue && !SetInlineLookupQueue(true))
                {
                    throw new Exception("This native build has no inline lookup queue support");
                }
#endif
                foreach (var asset in imageAssets ?? Array.Empty<ImageBinding>())
                {
                    if (string.IsNullOrEmpty(asset.source) || !asset.texture)
                    {
                        throw new Exception("URUI image asset needs a source and texture");
                    }

                    if ((asset.expectedWidth > 0 && asset.texture.width != asset.expectedWidth)
                        || (asset.expectedHeight > 0 && asset.texture.height != asset.expectedHeight))
                    {
                        throw new Exception("URUI image dimensions differ from declared atlas/source pixels: " + asset.source);
                    }

                    var pixels = asset.texture.GetPixels32();
                    var rgba = new byte[pixels.Length * 4];
                    // Unity GetPixels32 is bottom-row first; RmlUi's image and spritesheet
                    // coordinates are top-row first. Convert row order before registration.
                    int j = 0;
                    for (int y = asset.texture.height - 1; y >= 0; y--)
                    {
                        for (int x = 0; x < asset.texture.width; x++)
                        {
                            var p = pixels[y * asset.texture.width + x];
                            rgba[j++] = p.r;
                            rgba[j++] = p.g;
                            rgba[j++] = p.b;
                            rgba[j++] = p.a;
                        }
                    }

                    if (NativeMethods.urui_register_rgba_texture(asset.source, asset.texture.width, asset.texture.height, rgba, rgba.Length, out _) != 1)
                    {
                        throw new Exception("URUI image registration: " + NativeMethods.LastError());
                    }
                }

                var fontFile = Path.GetFileNameWithoutExtension(ResolveFontPath()).ToLowerInvariant();
                var family = fontFile.Contains("roboto") ? "Roboto" : fontFile.Contains("noto") ? "Noto Sans" : fontFile.Contains("dejavu") ? "DejaVu Sans" : "Arial";
                var rml = (string.IsNullOrEmpty(InitialMarkup) ? document.text : InitialMarkup).Replace("__URUI_FONT__", family);
                if (NativeMethods.urui_load_document(rml) == 0)
                {
                    throw new Exception(NativeMethods.LastError());
                }
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogError("Build Native/URUIBridge first: " + ex.Message);
                AbortStartup();
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError("Update Native/URUIBridge to ABI version 2: " + ex.Message);
                AbortStartup();
            }
            catch (Exception ex)
            {
                Debug.LogError("URUI: " + ex);
                AbortStartup();
            }
        }

        void AbortStartup()
        {
            if (targetCamera && buffer != null)
            {
                targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, buffer);
            }

            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }

            if (material)
            {
                Destroy(material);
                material = null;
            }

            if (active)
            {
                NativeMethods.urui_destroy();
                active = false;
            }

            if (ReferenceEquals(ActiveHost, this))
            {
                ActiveHost = null;
            }

            enabled = false;
        }

        static string ResolveFont()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (var path in new[] {
                "/system/fonts/Roboto-Regular.ttf",
                "/system/fonts/NotoSans-Regular.ttf",
                "/system/fonts/DroidSans.ttf"
            }

            )
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return "/system/fonts/Roboto-Regular.ttf";
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
   return "C:/Windows/Fonts/arial.ttf";
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return "/System/Library/Fonts/Supplemental/Arial.ttf";
#else
            return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
#endif
        }

        string ResolveFontPath() => string.IsNullOrEmpty(overrideFontPath) ? ResolveFont() : overrideFontPath;

        void Update()
        {
            if (!active)
            {
                return;
            }
#if URUI_DEBUG
            using var hostScope = hostMarker.Auto();
            long inputTimer = phaseDiagnostics ? Now() : 0;
            using (inputMarker.Auto())
#endif
            {
                if (lastWidth != Screen.width || lastHeight != Screen.height)
                {
                    lastWidth = Screen.width;
                    lastHeight = Screen.height;
                    NativeMethods.urui_resize(UiWidth, UiHeight);
                }
#if UNITY_ANDROID && !UNITY_EDITOR
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    int touchPhase = touch.phase == TouchPhase.Began ? 0 : touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary ? 1 : touch.phase == TouchPhase.Ended ? 2 : 3;
                    float x = touch.position.x * UiWidth / Screen.width;
                    float y = UiHeight - 1 - touch.position.y * UiHeight / Screen.height;
                    if (NativeMethods.urui_touch_event(touchPhase, (ulong)(uint)touch.fingerId, x, y) == 1)
                    {
                        PointerInput?.Invoke(new PointerInputEvent { phase = (PointerPhase)touchPhase, pointerId = touch.fingerId, x = x, y = y, hitId = HitTestId(x, y) });
                    }
                }
#else
                var mouse = Input.mousePosition;
                NativeMethods.urui_mouse_move((int)(mouse.x * UiWidth / Screen.width), UiHeight - 1 - (int)(mouse.y * UiHeight / Screen.height));
                for (int b = 0; b < 3; b++)
                {
                    if (Input.GetMouseButtonDown(b))
                    {
                        NativeMethods.urui_mouse_button(b, 1);
                        if (b == 0)
                        {
                            EmitMousePointer(PointerPhase.Down, mouse);
                        }
                    }

                    if (Input.GetMouseButtonUp(b))
                    {
                        NativeMethods.urui_mouse_button(b, 0);
                        if (b == 0)
                        {
                            EmitMousePointer(PointerPhase.Up, mouse);
                        }
                    }
                }

                if (Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0))
                {
                    EmitMousePointer(PointerPhase.Move, mouse);
                }

                if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.001f)
                {
                    NativeMethods.urui_mouse_wheel(-Input.mouseScrollDelta.y);
                }
#endif
            }
#if URUI_DEBUG
            long timer = (measureBridge || phaseDiagnostics) ? Now() : 0;
            long alloc = phaseDiagnostics ? ThreadAllocationBytes() : -1;
#endif
            var phase = new PhaseFrame();
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.inputMs = Millis(timer - inputTimer);
            }

            long step = phaseDiagnostics ? Now() : 0;
#endif
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
#if URUI_DEBUG
            using (updateMarker.Auto())
#endif
            {
                NativeMethods.urui_update();
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.contextUpdateMs = Millis(Now() - step);
                phase.nativeCalls++;
                step = Now();
            }

            using (eventMarker.Auto())
#endif
            {
                DrainEvents();
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.eventsMs = Millis(Now() - step);
                step = Now();
            }
#endif
            if (!active || !isActiveAndEnabled)
            {
                return;
            }

            DrawFrame(ref phase);
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.hostTotalMs = Millis(Now() - timer);
                phase.managedAllocBytes = alloc < 0 ? -1 : ThreadAllocationBytes() - alloc;
                phase.setTextMs = textMilliseconds;
                phase.setTextCalls = textCalls;
                phase.nativeCalls += textCalls;
                LastPhase = phase;
                textMilliseconds = 0;
                textCalls = 0;
            }

            if (measureBridge)
            {
                UpdateMilliseconds = Millis(Now() - timer);
            }
#endif
        }

        void DrainEvents()
        {
            while (active)
            {
                int n = NativeMethods.urui_pop_event(eventBuffer, eventBuffer.Length);
                if (n == 0)
                {
                    break;
                }

                if (n < 0)
                {
                    int required = checked(-n);
                    if (required <= eventBuffer.Length)
                    {
                        throw new InvalidOperationException("Invalid Rml event buffer requirement: " + required);
                    }

                    Array.Resize(ref eventBuffer, required);
                    continue;
                }

                var id = Encoding.UTF8.GetString(eventBuffer, 0, n);
                Clicked?.Invoke(id);
            }
        }

        public void SetText(string id, string value)
        {
            if (!active)
            {
                return;
            }
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            NativeMethods.urui_set_text(id, value);
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                double ms = Millis(Now() - t);
                textMilliseconds += ms;
                frameTextMilliseconds += ms;
                textCalls++;
                apiFrame.textMs += ms;
                apiFrame.texts++;
            }
#endif
        }

        public float GetScrollTop(string id) => active ? NativeMethods.urui_get_scroll_top(id) : -1f;
        public bool SetPixelStyle(string id, string property, float pixels) => active && NativeMethods.urui_set_pixel_style(id, property, pixels) != 0;
        public bool ElementExists(string id) => active && NativeMethods.urui_element_exists(id) == 1;

        public bool SetAttribute(string id, string name, string value)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_attribute(id, name, value) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.attributeMs += Millis(Now() - t);
                apiFrame.attributes++;
            }
#endif
            return ok;
        }

        public bool SetStyle(string id, string property, string value)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            using var marker = propertyApiMarker.Auto();
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_style(id, property, value) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.styleMs += Millis(Now() - t);
                apiFrame.styles++;
            }
#endif
            return ok;
        }

        public bool SetTranslateScale2D(string id, float x, float y, float sx, float sy)
        {
            if (!active)
            {
                return false;
            }
#if URUI_DEBUG
            using var marker = propertyApiMarker.Auto();
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_set_translate_scale_2d(id, x, y, sx, sy) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.styleMs += Millis(Now() - t);
                apiFrame.styles++;
            }
#endif
            return ok;
        }

        /// <summary>
        /// 按 ID 重新查询并绑定原节点身份；节点缺失、宿主未启用或引用预算耗尽时返回 null。
        /// 返回值由文档释放，不会持有可直接访问的原生元素指针。
        /// </summary>
        public ElementReference AcquireElementReference(string id)
        {
            if (!active)
            {
                return null;
            }

            CheckReferenceThread();
            try
            {
                ulong token = NativeMethods.urui_element_ref_acquire(id);
                return token == 0 ? null : new ElementReference(this, token);
            }
            catch (EntryPointNotFoundException e)
            {
                throw new NotSupportedException("Native bridge has no explicit element reference API", e);
            }
        }

        void CheckReferenceThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != referenceThread)
            {
                throw new InvalidOperationException("URUI element references require their context thread");
            }
        }

        internal bool ReferenceIsValid(ulong token)
        {
            if (!active)
            {
                return false;
            }

            CheckReferenceThread();
            return NativeMethods.urui_element_ref_valid(token) == 1;
        }

        internal void ReleaseReference(ulong token)
        {
            if (!active)
            {
                return;
            }

            CheckReferenceThread();
            NativeMethods.urui_element_ref_release(token);
        }

        internal bool SetReferenceTransform(ulong token, float x, float y, float sx, float sy)
        {
            if (!active)
            {
                return false;
            }

            CheckReferenceThread();
#if URUI_DEBUG
            using var marker = propertyApiMarker.Auto();
            long t = phaseDiagnostics ? Now() : 0;
#endif
            bool ok = NativeMethods.urui_element_ref_set_translate_scale_2d(token, x, y, sx, sy) == 1;
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                apiFrame.styleMs += Millis(Now() - t);
                apiFrame.styles++;
            }
#endif
            return ok;
        }

        public bool SetInnerRml(string id, string markup) => active && NativeMethods.urui_set_inner_rml(id, markup) == 1;
        public bool AppendElement(string parentId, string tag, string id) => active && NativeMethods.urui_append_element(parentId, tag, id) == 1;
        public bool RemoveElement(string id) => active && NativeMethods.urui_remove_element(id) == 1;

        public string HitTestId(float logicalX, float logicalY)
        {
            if (!active)
            {
                return null;
            }

            var bytes = new byte[512];
            int n = NativeMethods.urui_hit_test_id(logicalX, logicalY, bytes, bytes.Length);
            if (n < 0)
            {
                int required = -n;
                if (required > 1024 * 1024)
                {
                    throw new Exception("URUI hit ID exceeds 1 MiB");
                }

                bytes = new byte[required];
                n = NativeMethods.urui_hit_test_id(logicalX, logicalY, bytes, bytes.Length);
            }

            return n > 0 ? Encoding.UTF8.GetString(bytes, 0, n) : null;
        }

        public string LastError => active ? NativeMethods.LastError() : "URUI host is not active";

        void EmitMousePointer(PointerPhase pointerPhase, Vector3 mouse)
        {
            if (PointerInput == null)
            {
                return;
            }

            float x = mouse.x * UiWidth / Screen.width;
            float y = UiHeight - 1 - mouse.y * UiHeight / Screen.height;
            PointerInput(new PointerInputEvent { phase = pointerPhase, pointerId = 0, x = x, y = y, hitId = HitTestId(x, y) });
        }

        static void Mix(ref ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
        }

        ulong GroupHash()
        {
            ulong hash = 1469598103934665603UL;
            foreach (var item in batchGroup)
            {
                var c = item.command;
                Mix(ref hash, c.geometry);
                Mix(ref hash, c.texture);
                Mix(ref hash, (uint)c.tx.GetHashCode());
                Mix(ref hash, (uint)c.ty.GetHashCode());
                Mix(ref hash, (uint)c.clip_enabled);
                Mix(ref hash, (uint)c.clip_x);
                Mix(ref hash, (uint)c.clip_y);
                Mix(ref hash, (uint)c.clip_w);
                Mix(ref hash, (uint)c.clip_h);
            }

            return hash;
        }

        static bool SameCommand(CommandNative a, CommandNative b) => a.geometry == b.geometry
            && a.texture == b.texture
            && a.tx == b.tx
            && a.ty == b.ty
            && a.clip_enabled == b.clip_enabled
            && a.clip_x == b.clip_x
            && a.clip_y == b.clip_y
            && a.clip_w == b.clip_w
            && a.clip_h == b.clip_h;

        bool SameBatch(BatchCache cache)
        {
            if (cache.items.Length != batchGroup.Count)
            {
                return false;
            }

            for (int i = 0; i < cache.items.Length; i++)
            {
                if (!SameCommand(cache.items[i].command, batchGroup[i].command))
                {
                    return false;
                }
            }

            return true;
        }

        bool CanJoin(BatchItem next, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = DetailBegin(ref phase.canJoinMs, ref phase);
#endif
            bool result = CanJoinCore(next, ref phase);
#if URUI_DEBUG
            DetailEnd(t, ref phase.canJoinMs);
#endif
            return result;
        }

        bool CanJoinCore(BatchItem next, ref PhaseFrame phase)
        {
            if (batchGroup.Count == 0)
            {
                return true;
            }

            var a = batchGroup[0].command;
            var b = next.command;
            if (a.texture != b.texture)
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.breakTexture++;
                }
#endif
                return false;
            }

            if (a.clip_enabled != b.clip_enabled
                || a.clip_x != b.clip_x
                || a.clip_y != b.clip_y
                || a.clip_w != b.clip_w
                || a.clip_h != b.clip_h)
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.breakClip++;
                }
#endif
                return false;
            }

            if (!BatchRules.EquivalentAffine2D(batchGroup[0].transform, next.transform))
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.breakTransform++;
                }
#endif
                return false;
            }

            if (!batchOrderedOverlap)
            {
                foreach (var prior in batchGroup)
                {
                    if (!BatchRules.Disjoint(prior.x0, prior.y0, prior.x1, prior.y1, next.x0, next.y0, next.x1, next.y1))
                    {
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.breakOverlap++;
                        }
#endif
                        return false;
                    }
                }
            }

            return true;
        }

        Mesh BuildBatchMesh(ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long start = phaseDiagnostics ? Now() : 0;
#endif
            int vertices = 0;
            int indices = 0;
            foreach (var item in batchGroup)
            {
                if (item.geometry.positions == null)
                {
                    return null;
                }

                vertices += item.geometry.positions.Length;
                indices += item.geometry.indices.Length;
            }

            if (vertices == 0 || indices == 0 || vertices > 100000 || indices > 300000)
            {
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.vertexLimitGroups++;
                }
#endif
                return null;
            }

            var positions = new Vector3[vertices];
            var uv = new Vector2[vertices];
            var colors = new Color32[vertices];
            var ids = new int[indices];
            int vo = 0;
            int io = 0;
            foreach (var item in batchGroup)
            {
                var g = item.geometry;
                var c = item.command;
                for (int i = 0; i < g.positions.Length; i++)
                {
                    var p = g.positions[i];
                    positions[vo + i] = new Vector3(p.x + c.tx, p.y + c.ty, p.z);
                    uv[vo + i] = g.uv[i];
                    colors[vo + i] = g.colors[i];
                }

                for (int i = 0; i < g.indices.Length; i++)
                {
                    ids[io + i] = vo + g.indices[i];
                }

                vo += g.positions.Length;
                io += g.indices.Length;
            }
#if URUI_DEBUG
            long unityStart = phaseDiagnostics ? Now() : 0;
#endif
            var mesh = new Mesh
            {
                name = "DisjointBatch"
            };
            if (vertices > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.vertices = positions;
            mesh.uv = uv;
            mesh.colors32 = colors;
            mesh.triangles = ids;
            mesh.RecalculateBounds();
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.batchUnityMeshApiMs += Millis(Now() - unityStart);
                phase.batchMeshBuildMs += Millis(Now() - start);
            }
#endif
            return mesh;
        }

        static readonly int mainTextureId = Shader.PropertyToID("_MainTex");
        static readonly int clipEnabledId = Shader.PropertyToID("_UruiClipEnabled");
        static readonly int clipRectId = Shader.PropertyToID("_UruiClipRect");
        static readonly int translationId = Shader.PropertyToID("_UruiTranslation");

        void WriteShaderProperties(MaterialPropertyBlock properties, Texture texture, CommandNative cmd, bool batch, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            using var marker = propertiesMarker.Auto();
#endif
            var clip = new Vector4(cmd.clip_x, cmd.clip_y, cmd.clip_x + cmd.clip_w, cmd.clip_y + cmd.clip_h);
            var translation = batch ? Vector4.zero : new Vector4(cmd.tx, cmd.ty, 0, 0);
            if (useShaderPropertyIds)
            {
                properties.SetTexture(mainTextureId, texture);
                properties.SetFloat(clipEnabledId, cmd.clip_enabled);
                properties.SetVector(clipRectId, clip);
                properties.SetVector(translationId, translation);
            }
            else
            {
                properties.SetTexture("_MainTex", texture);
                properties.SetFloat("_UruiClipEnabled", cmd.clip_enabled);
                properties.SetVector("_UruiClipRect", clip);
                properties.SetVector("_UruiTranslation", translation);
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.shaderNameLookups += 4;
                }
#endif
            }
        }

        MaterialPropertyBlock PrepareShaderProperties(Texture texture, CommandNative cmd, bool batch, ref PhaseFrame phase)
        {
            if (reusePropertyBlock && reusableProperties == null)
            {
                reusableProperties = new MaterialPropertyBlock();
            }

            var properties = reusePropertyBlock ? reusableProperties : new MaterialPropertyBlock();
#if URUI_DEBUG
            if (!reusePropertyBlock && phaseDiagnostics)
            {
                phase.propertyBlocksCreated++;
            }

            if (propertyDependencyDiagnostics)
            {
                propertyDependencies.Observe(properties, texture, cmd, batch);
            }

            PropertyCalls++;
#endif
            if (shaderPropertyState.NeedsWrite(properties, texture, cmd, ShaderContractActive && reusePropertyBlock, material))
            {
                if (reusePropertyBlock)
                {
                    properties.Clear();
                }

                WriteShaderProperties(properties, texture, cmd, batch, ref phase);
#if URUI_DEBUG
                PropertyWriteCalls++;
#endif
            }
#if URUI_DEBUG
            else
            {
                PropertyReuseHits++;
            }
#endif
            return properties;
        }

        void DrawOne(BatchItem item, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            var cmd = item.command;
            var tex = LoadTexture(cmd.texture);
            var properties = PrepareShaderProperties(tex ? tex : white, cmd, false, ref phase);
            var m = item.transform * Matrix4x4.Translate(new Vector3(cmd.tx, cmd.ty, 0));
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.propertiesMs += Millis(Now() - t);
                t = Now();
            }

            if (!skipDrawForDiagnosis)
#endif
            {
#if URUI_DEBUG
                using (submitMarker.Auto())
#endif
                {
                    buffer.DrawMesh(item.geometry.mesh, m, material, 0, 0, properties);
#if URUI_DEBUG
                    recordCallsThisFrame++;
#endif
                }
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.submitMs += Millis(Now() - t);
                if (!skipDrawForDiagnosis)
                {
                    phase.drawSubmissions++;
                }
            }
#endif
        }

        void DrawBatch(BatchCache cached, ref PhaseFrame phase)
        {
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
#endif
            var cmd = batchGroup[0].command;
            var tex = LoadTexture(cmd.texture);
            var properties = PrepareShaderProperties(tex ? tex : white, cmd, true, ref phase);
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.propertiesMs += Millis(Now() - t);
                t = Now();
            }

            if (!skipDrawForDiagnosis)
#endif
            {
#if URUI_DEBUG
                using (submitMarker.Auto())
#endif
                {
                    buffer.DrawMesh(cached.mesh, batchGroup[0].transform, material, 0, 0, properties);
#if URUI_DEBUG
                    recordCallsThisFrame++;
#endif
                }
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.submitMs += Millis(Now() - t);
                if (!skipDrawForDiagnosis)
                {
                    phase.drawSubmissions++;
                }
            }
#endif
        }

        void FlushGroup(ref PhaseFrame phase)
        {
#if URUI_DEBUG
            using var marker = flushMarker.Auto();
            long t = DetailBegin(ref phase.flushGroupMs, ref phase);
#endif
            FlushGroupCore(ref phase);
#if URUI_DEBUG
            DetailEnd(t, ref phase.flushGroupMs);
#endif
        }

        // 仅合并顺序与混合结果均可证明等价的组；无法合并时按原始顺序逐条提交。
        void FlushGroupCore(ref PhaseFrame phase)
        {
            if (batchGroup.Count == 0)
            {
                return;
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.batchGroups++;
            }
#endif
            if (batchGroup.Count == 1)
            {
                DrawOne(batchGroup[0], ref phase);
                batchGroup.Clear();
                return;
            }

            ulong hash = GroupHash();
            currentBatchKeys.Add(hash);
            if (batches.TryGetValue(hash, out var cached) && !cached.mesh)
            {
                batches.Remove(hash);
                cached = null;
                recordingWarmupPending = true;
            }

            if (cached != null)
            {
                if (SameBatch(cached))
                {
                    DrawBatch(cached, ref phase);
                    batchGroup.Clear();
                    return;
                }
                // Hash collision remains the original ordered fallback.
            }
            else if (previousBatchKeys.Contains(hash))
            {
                var mesh = BuildBatchMesh(ref phase);
                if (mesh)
                {
                    cached = new BatchCache
                    {
                        mesh = mesh,
                        items = batchGroup.ToArray()
                    };
                    batches.Add(hash, cached);
                    DrawBatch(cached, ref phase);
                    batchGroup.Clear();
                    return;
                }
            }
            else
            {
                recordingWarmupPending = true;
                // Do not freeze first-frame unbatched warmup.
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.fallbackExtraSubmissions += batchGroup.Count - 1;
            }
#endif
            foreach (var item in batchGroup)
            {
                DrawOne(item, ref phase);
            }

            batchGroup.Clear();
        }

        // Retained commands depend on the exact meshes, textures and camera state.
        // Clearing revokes parameter recordings before dropping those dependencies.
        void InvalidateRecording(bool clear)
        {
            if (clear)
            {
                parameterPrepared = false;
                StopParameterRoot();
            }

            retainedRecording.Invalidate();
            planStability.Reset();
            retainedTextures.Clear();
#if URUI_DEBUG
            retainedDraws = 0;
#endif
            if (clear)
            {
                retainedTextures.TrimExcess();
#if URUI_DEBUG
                LastRecording = default;
#endif
                TryClearNativeHostRecording();
            }
        }

        // Explicit notification for consumers managing external render state. Existing
        // current-frame Unity snapshots stay alive; next Host Update records anew.
        public void NotifyExternalRenderResourcesChanged()
        {
            InvalidateRecording(false);
        }

        // Camera.GetCommandBuffers exposes mutable references. Exclusive ownership
        // cannot be inferred from private fields or byte size; unknown is fallback.
        public void SetRecordingOwnership(RecordingOwnership ownership)
        {
            if (ownership != RecordingOwnership.ExternalMutable && ownership != RecordingOwnership.HostExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(ownership));
            }

            if (recordingOwnershipValue == ownership)
            {
                return;
            }

            recordingOwnershipValue = ownership;
            InvalidateRecording(false);
        }

        // This validation is production behavior: stale GPU resources must never be
        // accepted merely because the native command sequence has not changed.
        bool RetainedResourcesAlive()
        {
#if URUI_DEBUG
            using (retainedResourceMarker.Auto())
#endif
            {
                return CheckRetainedResources();
            }
        }

        bool CheckRetainedResources()
        {
            if (buffer == null || buffer.sizeInBytes != retainedBufferBytes)
            {
                return false;
            }

            foreach (var pair in meshes)
            {
                if (!pair.Value.mesh)
                {
                    return false;
                }
            }

            foreach (var pair in batches)
            {
                if (!pair.Value.mesh)
                {
                    return false;
                }
            }

            foreach (var d in retainedTextures)
            {
                var t = d.texture;
                if (!t
                    || t.updateCount != d.update
                    || t.width != d.width
                    || t.height != d.height
                    || t.filterMode != d.filter
                    || t.wrapModeU != d.u
                    || t.wrapModeV != d.v
                    || t.wrapModeW != d.w
                    || t.mipMapBias != d.bias
                    || t.anisoLevel != d.aniso)
                {
                    return false;
                }
            }

            return true;
        }

        void AddTextureDependency(Texture texture)
        {
            if (!texture)
            {
                return;
            }

            retainedTextures.Add(new TextureDependency { texture = texture, update = texture.updateCount, width = texture.width, height = texture.height, filter = texture.filterMode, u = texture.wrapModeU, v = texture.wrapModeV, w = texture.wrapModeW, bias = texture.mipMapBias, aniso = texture.anisoLevel });
        }

        void CaptureTextureDependencies()
        {
#if URUI_DEBUG
#if URUI_DEBUG
            using var dependencyScope = dependencyCaptureMarker.Auto();
#endif
#endif
            retainedTextures.Clear();
            AddTextureDependency(white);
            foreach (var pair in textures)
            {
                AddTextureDependency(pair.Value);
            }
        }

        bool ConfigureNativeRetention(bool enable)
        {
            if (retentionApiMissing)
            {
                return false;
            }

            if (enable == nativeRetention)
            {
                return enable;
            }

            try
            {
                if (enable && NativeMethods.urui_render_plan_version() != 1)
                {
                    retentionApiMissing = true;
                    return false;
                }

                if (NativeMethods.urui_render_plan_enable(enable ? 1 : 0) != 1)
                {
                    return false;
                }

                nativeRetention = enable;
                InvalidateRecording(false);
                return enable;
            }
            catch (EntryPointNotFoundException)
            {
                retentionApiMissing = true;
                nativeRetention = false;
                InvalidateRecording(false);
                return false;
            }
        }

        void OnDisable()
        {
            StopParameterHost();
            InvalidateRecording(true);
            if (active && nativeRetention)
            {
                ConfigureNativeRetention(false);
            }
        }

        void OnEnable()
        {
#if URUI_DEBUG
            ResetParameterRootMetrics();
#endif
            parameterRootFault = false;
            parameterFault = false;
            retainedRecording.Invalidate();
        }

        // A partial recording is never reusable. Preserve this invalidation even when
        // debug instrumentation is compiled out or a resource upload throws.
        void DrawFrame(ref PhaseFrame phase)
        {
            bool completed = false;
            try
            {
                DrawFrameCore(ref phase);
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    InvalidateRecording(true);
                }
            }
        }

        /// <summary>
        /// 优先尝试满足资源/相机约定的记录路径，其余情况重新读取原生有序命令。
        /// 快路径失败后，只有明确取得回退许可才能重绘，避免一次帧内重复提交。
        /// </summary>
        void DrawFrameCore(ref PhaseFrame phase)
        {
#if URUI_DEBUG
            using var drawScope = drawMarker.Auto();
            ResetParameterRootMetrics();
#endif
            RequireNativeStopPermission();
            shaderPropertyState.Reset();
#if URUI_DEBUG
            PropertyCalls = PropertyWriteCalls = PropertyReuseHits = 0;
            recordCallsThisFrame = 0;
#endif
            recordingWarmupPending = false;
            recordingFinite = true;
#if URUI_DEBUG
            LastRecording = default;
#endif
            if (retentionSchedule.HasValue && retentionSchedule.Value != deferRetainedStateCapture)
            {
                InvalidateRecording(false);
            }

            retentionSchedule = deferRetainedStateCapture;
            bool requested = retainUnchangedCommands && recordingOwnership == RecordingOwnership.HostExclusive;
            RecordingKey recordingKey = default;
            bool eligible = !deferRetainedStateCapture
                && requested
                && RecordingKey.TryCapture(this, material, targetCamera, recordedPassName, out recordingKey);
            bool parameterRequested = ParameterRootEligible();
            if (!parameterRequested)
            {
                StopParameterRoot();
                RequireNativeStopPermission();
            }

            bool tracking = ConfigureNativeRetention(parameterRequested || (deferRetainedStateCapture ? requested : eligible));
            if (!tracking)
            {
                retainedRecording.Invalidate();
                planStability.Reset();
            }

            ShaderContractActive = reuseEquivalentPropertyState && propertyContract && propertyContract.Supports(material);
#if URUI_DEBUG
            if (propertyDependencyDiagnostics)
            {
                propertyDependencies.Reset();
            }
#endif
            if (meshBatchMode.HasValue && meshBatchMode.Value != batchDisjointCommands)
            {
                ClearBatches();
                foreach (var geometry in meshes.Values)
                {
                    if (geometry.mesh)
                    {
                        Destroy(geometry.mesh);
                    }
                }

                meshes.Clear();
            }

            meshBatchMode = batchDisjointCommands;
#if URUI_DEBUG
            long t = phaseDiagnostics ? Now() : 0;
            using (renderMarker.Auto())
#endif
            {
                NativeMethods.urui_render();
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.nativeRenderMs = Millis(Now() - t);
                phase.nativeCalls++;
                t = Now();
            }
#endif
            parameterPrepared = false;
            if (parameterRequested && tracking && PrepareParameterRoot(ref phase))
            {
                return;
            }

            RequireNativeStopPermission();
            UruiFrameStampNative stamp = default;
            if (tracking)
            {
                if (NativeMethods.urui_render_plan_stamp(out stamp) != 1)
                {
                    retainedRecording.Invalidate();
                }
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.nativeCalls++;
                }
#endif
                bool repeated = planStability.Observe(stamp);
                if (deferRetainedStateCapture)
                {
                    eligible = repeated && RecordingKey.TryCapture(this, material, targetCamera, recordedPassName, out recordingKey);
                }
#if URUI_DEBUG
                LastRecording = new RecordingStats
                {
                    capability = eligible && stamp.flags == 1,
                    nativeCommands = (int)stamp.count,
                    nativeExtraBytes = stamp.retained_bytes,
                    compareBytes = stamp.compare_bytes
                };
#endif
                if (eligible && retainedRecording.CanReuse(stamp, recordingKey) && RetainedResourcesAlive())
                {
#if URUI_DEBUG
                    DrawCommands = (int)stamp.count;
                    LastRecording = new RecordingStats
                    {
                        reused = true,
                        capability = true,
                        nativeCommands = DrawCommands,
                        recordCalls = 0,
                        retainedDraws = retainedDraws,
                        nativeExtraBytes = stamp.retained_bytes,
                        compareBytes = stamp.compare_bytes
                    };
#endif
                    return;
                }
            }

            retainedRecording.Invalidate();
            retainedTextures.Clear();
            int count;
            int read;
#if URUI_DEBUG
            using (readMarker.Auto())
#endif
            {
                count = NativeMethods.urui_command_count();
#if URUI_DEBUG
                DrawCommands = count;
#endif
                if (commands.Length < count)
                {
                    commands = new CommandNative[Math.Max(count, commands.Length * 2 + 16)];
                }

                if (commandTransforms.Length < count * 16)
                {
                    commandTransforms = new float[Math.Max(count * 16, commandTransforms.Length * 2 + 256)];
                }

                read = NativeMethods.urui_get_commands(commands, count);
                int transformRead = NativeMethods.urui_get_command_transforms(commandTransforms, commandTransforms.Length);
                if (transformRead != read)
                {
                    throw new Exception("URUI command transform count mismatch: " + transformRead + " / " + read);
                }
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.commandCopyMs = Millis(Now() - t);
                    phase.commandCopyBytes = read * System.Runtime.InteropServices.Marshal.SizeOf(typeof(CommandNative));
                    phase.matrixCopyBytes = read * 16 * sizeof(float);
                    phase.totalCommandCopyBytes = phase.commandCopyBytes + phase.matrixCopyBytes;
                    phase.nativeCalls += 3;
                    phase.commandsRead = read;
                }
#endif
            }
#if URUI_DEBUG
            long unitySetup = phaseDiagnostics ? Now() : 0;
            using (setupMarker.Auto())
#endif
            {
                buffer.Clear();
                // The prior recording is detached before retiring Host-owned Unity textures.
                // Native existence, not this frame's visibility, is the lifetime authority.
                RetireReleasedTextures(ref phase);
                // Construct a screen-space projection; RmlUi has top-left UI coordinates.
                Matrix4x4 projection = Matrix4x4.Ortho(0, UiWidth, UiHeight, 0, -1, 1);
                buffer.SetViewProjectionMatrices(Matrix4x4.identity, projection);
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.commandSetupMs += Millis(Now() - unitySetup);
                }
#endif
            }

            var used = useFrameScratchCollections ? frameUsedGeometry : new HashSet<ulong>();
            used.Clear();
#if URUI_DEBUG
            using (loopMarker.Auto())
#endif
            {
                for (int c = 0; c < read; c++)
                {
                    var cmd = commands[c];
#if URUI_DEBUG
                    long detail = DetailBegin(ref phase.matrixDecodeMs, ref phase);
#endif
                    var transform = new Matrix4x4();
                    bool transformed = false;
                    for (int col = 0; col < 4; col++)
                    {
                        for (int row = 0; row < 4; row++)
                        {
                            float value = commandTransforms[c * 16 + col * 4 + row];
                            transform[row, col] = value;
                            if (float.IsNaN(value) || float.IsInfinity(value))
                            {
                                recordingFinite = false;
                            }

                            if (Mathf.Abs(value - (row == col ? 1f : 0f)) > 0.000001f)
                            {
                                transformed = true;
                            }
                        }
                    }
#if URUI_DEBUG
                    DetailEnd(detail, ref phase.matrixDecodeMs);
#endif
                    if (float.IsNaN(cmd.tx) || float.IsInfinity(cmd.tx) || float.IsNaN(cmd.ty) || float.IsInfinity(cmd.ty))
                    {
                        recordingFinite = false;
                    }

                    if (cmd.geometry == 0)
                    {
                        continue;
                    }
#if URUI_DEBUG
                    detail = DetailBegin(ref phase.geometryLookupMs, ref phase);
#endif
                    bool found = meshes.TryGetValue(cmd.geometry, out var entry);
                    if (found && !entry.mesh)
                    {
                        meshes.Remove(cmd.geometry);
                        found = false;
                    }
#if URUI_DEBUG
                    DetailEnd(detail, ref phase.geometryLookupMs);
#endif
                    if (!found)
                    {
#if URUI_DEBUG
                        using var geometryScope = geometryMarker.Auto();
                        t = phaseDiagnostics ? Now() : 0;
#endif
                        if (NativeMethods.urui_get_geometry_info(cmd.geometry, out int nv, out int ni) == 0)
                        {
                            continue;
                        }

                        if (nv == 0 || ni == 0)
                        {
                            continue;
                        }

                        var vs = new VertexNative[nv];
                        var ids = new int[ni];
                        if (NativeMethods.urui_copy_geometry(cmd.geometry, vs, nv, ids, ni) == 0)
                        {
                            continue;
                        }
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.geometryCopyMs += Millis(Now() - t);
                            phase.nativeCalls += 2;
                            phase.geometryCopyBytes += nv * 20 + ni * 4;
                            t = Now();
                        }

                        long convertStart = phaseDiagnostics ? Now() : 0;
#endif
                        var positions = new Vector3[nv];
                        var uv = new Vector2[nv];
                        var colors = new Color32[nv];
                        for (int v = 0; v < nv; v++)
                        {
                            positions[v] = new Vector3(vs[v].x, vs[v].y, 0);
                            uv[v] = new Vector2(vs[v].u, vs[v].v);
                            colors[v] = new Color32(vs[v].r, vs[v].g, vs[v].b, vs[v].a);
                        }
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.meshConversionMs += Millis(Now() - convertStart);
                        }

                        long unityMeshStart = phaseDiagnostics ? Now() : 0;
#endif
                        var mesh = new Mesh
                        {
                            name = "Geometry"
                        };
                        if (nv > 65535)
                        {
                            mesh.indexFormat = IndexFormat.UInt32;
                        }

                        mesh.vertices = positions;
                        mesh.uv = uv;
                        mesh.colors32 = colors;
                        mesh.triangles = ids;
                        mesh.RecalculateBounds();
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.unityMeshApiMs += Millis(Now() - unityMeshStart);
                            phase.meshBuildMs += Millis(Now() - t);
                            phase.meshesCreated++;
                        }
#endif
                        entry = new MeshCache
                        {
                            mesh = mesh,
                            positions = batchDisjointCommands ? positions : null,
                            uv = batchDisjointCommands ? uv : null,
                            colors = batchDisjointCommands ? colors : null,
                            indices = batchDisjointCommands ? ids : null
                        };
                        meshes.Add(cmd.geometry, entry);
                    }
#if URUI_DEBUG
                    detail = DetailBegin(ref phase.entryTrackMs, ref phase);
#endif
                    entry.lastFrame = Time.frameCount;
                    used.Add(cmd.geometry);
#if URUI_DEBUG
                    DetailEnd(detail, ref phase.entryTrackMs);
#endif
                    if (!transformed && cmd.clip_enabled != 0 && cmd.clip_w >= 0 && cmd.clip_h >= 0)
                    {
                        var bounds = GeometryBounds(entry.mesh, ref phase);
                        float minX = bounds.min.x + cmd.tx;
                        float maxX = bounds.max.x + cmd.tx;
                        float minY = bounds.min.y + cmd.ty;
                        float maxY = bounds.max.y + cmd.ty;
                        bool outside = maxX < cmd.clip_x || minX > cmd.clip_x + cmd.clip_w || maxY < cmd.clip_y || minY > cmd.clip_y + cmd.clip_h;
                        if (outside)
                        {
#if URUI_DEBUG
                            if (phaseDiagnostics)
                            {
                                phase.fullyClippedCandidates++;
                            }
#endif
                            if (cullFullyClipped)
                            {
                                continue;
                            }
                        }
                    }

                    var renderBounds = GeometryBounds(entry.mesh, ref phase);
                    var item = new BatchItem
                    {
                        command = cmd,
                        geometry = entry,
                        transform = transform,
                        transformed = transformed,
                        x0 = renderBounds.min.x + cmd.tx,
                        y0 = renderBounds.min.y + cmd.ty,
                        x1 = renderBounds.max.x + cmd.tx,
                        y1 = renderBounds.max.y + cmd.ty
                    };
                    if (batchDisjointCommands
                        && (!transformed || (batchEquivalentTransforms
                        && BatchRules.EquivalentAffine2D(transform, transform))))
                    {
                        if (!CanJoin(item, ref phase))
                        {
                            FlushGroup(ref phase);
                        }

                        batchGroup.Add(item);
                    }
                    else
                    {
                        if (batchDisjointCommands)
                        {
                            FlushGroup(ref phase);
                        }
#if URUI_DEBUG
                        if (phaseDiagnostics)
                        {
                            phase.batchGroups++;
                            if (batchDisjointCommands)
                            {
                                phase.breakTransform++;
                            }
                        }
#endif
                        DrawOne(item, ref phase);
                    }
                }
            }

            if (batchDisjointCommands)
            {
                FlushGroup(ref phase);
#if URUI_DEBUG
                long maintenance = DetailBegin(ref phase.batchMaintenanceMs, ref phase);
#endif
                staleBatchKeys.Clear();
                foreach (var pair in batches)
                {
                    if (!currentBatchKeys.Contains(pair.Key))
                    {
                        staleBatchKeys.Add(pair.Key);
                    }
                }

                foreach (var key in staleBatchKeys)
                {
                    Destroy(batches[key].mesh);
                    batches.Remove(key);
                }

                var swap = previousBatchKeys;
                previousBatchKeys = currentBatchKeys;
                currentBatchKeys = swap;
                currentBatchKeys.Clear();
#if URUI_DEBUG
                DetailEnd(maintenance, ref phase.batchMaintenanceMs);
#endif
            }
#if URUI_DEBUG
            using var cleanupScope = cleanupMarker.Auto();
#endif
            // Avoid leaking meshes invalidated by RmlUi.
#if URUI_DEBUG
            t = phaseDiagnostics ? Now() : 0;
#endif
            var stale = useFrameScratchCollections ? frameStaleGeometry : new List<ulong>();
            stale.Clear();
            foreach (var pair in meshes)
            {
                if (!pair.Value.mesh || (!used.Contains(pair.Key) && NativeMethods.urui_geometry_exists(pair.Key) == 0))
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (var id in stale)
            {
                Destroy(meshes[id].mesh);
                meshes.Remove(id);
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.cleanupMs = Millis(Now() - t);
                phase.nativeCalls += stale.Count;
            }

            retainedDraws = recordCallsThisFrame;
            LastRecording = new RecordingStats
            {
                capability = eligible && tracking && stamp.flags == 1,
                nativeCommands = read,
                recordCalls = recordCallsThisFrame,
                retainedDraws = recordCallsThisFrame,
                nativeExtraBytes = stamp.retained_bytes,
                compareBytes = stamp.compare_bytes
            };
#endif
            if (eligible && tracking && stamp.flags == 1 && !recordingWarmupPending && recordingFinite)
            {
                CaptureTextureDependencies();
                retainedBufferBytes = buffer.sizeInBytes;
                retainedRecording.Commit(stamp, recordingKey);
            }
        }

        Texture2D LoadTexture(ulong id)
        {
            if (id == 0)
            {
                return white;
            }

            if (textures.TryGetValue(id, out var old))
            {
                if (old)
                {
                    return old;
                }

                textures.Remove(id);
            }

            if (NativeMethods.urui_get_texture_info(id, out int w, out int h, out int length) == 0)
            {
                return white;
            }

            if (w <= 0 || h <= 0 || length != w * h * 4)
            {
                return white;
            }

            var rgba = new byte[length];
            if (NativeMethods.urui_copy_texture(id, rgba, length) == 0)
            {
                return white;
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, true);
            textures[id] = texture;
            return texture;
        }

        void RetireReleasedTextures(ref PhaseFrame phase)
        {
            retiredTextureIds.Clear();
            foreach (var pair in textures)
            {
                if (!pair.Value || NativeMethods.urui_texture_exists(pair.Key) == 0)
                {
                    retiredTextureIds.Add(pair.Key);
                }
#if URUI_DEBUG
                if (phaseDiagnostics)
                {
                    phase.nativeCalls++;
                }
#endif
            }

            foreach (var id in retiredTextureIds)
            {
                if (textures[id])
                {
                    Destroy(textures[id]);
                }

                textures.Remove(id);
            }
        }

        void OnDestroy()
        {
            ReleaseForDocument();
        }

        // Detach submitted commands before destroying host-owned GPU resources. The
        // document's TextAsset and source textures belong to its caller, not this host.
        internal void ReleaseForDocument()
        {
            if (resourcesReleased)
            {
                return;
            }

            resourcesReleased = true;
            enabled = false;
            StopParameterHost();
            InvalidateRecording(true);
            if (active && nativeRetention)
            {
                ConfigureNativeRetention(false);
            }

            ClearFrameScratch();
            ClearBatches();
            if (targetCamera && buffer != null)
            {
                targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, buffer);
            }

            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }

            foreach (var item in meshes.Values)
            {
                if (item.mesh)
                {
                    Destroy(item.mesh);
                }
            }

            meshes.Clear();
            foreach (var item in textures.Values)
            {
                if (item)
                {
                    Destroy(item);
                }
            }

            textures.Clear();
            if (material)
            {
                Destroy(material);
            }

            if (active)
            {
                NativeMethods.urui_destroy();
                active = false;
            }

            if (ReferenceEquals(ActiveHost, this))
            {
                ActiveHost = null;
            }
        }
    }
}
