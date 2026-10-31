using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using URUI.Internal;
using UnityEngine;

namespace URUI
{
    /// <summary>
    /// 在 Unity 场景中加载 RML 文档。所有操作限 Unity 主线程，一次仅支持一个活动文档。
    /// 禁用时释放渲染资源，启用时重新加载最近请求的内容；重载后请重新查询元素。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("URUI/Document")]
    public sealed class Document : MonoBehaviour
    {
        [SerializeField]
        TextAsset source;
        [SerializeField]
        ImageAsset[] images = Array.Empty<ImageAsset>();
        [SerializeField]
        string fontPath = "";
        readonly Dictionary<string, Element> elements = new Dictionary<string, Element>(StringComparer.Ordinal);
        RenderHost host;
        TextAsset ownedMarkup;
        string requestedMarkup;
        int ownerThread;
        bool changingContent;
        bool loadRequested = true;
        uint generation;
        static int unityThread;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CaptureUnityThread()
        {
            unityThread = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// 在启用组件时加载的 RML TextAsset。资产由调用者持有。
        /// </summary>
        /// <remarks>只能在文档未加载时修改；URUI 不销毁此资产。</remarks>
        public TextAsset Source
        {
            get
            {
                CheckThread();
                return source;
            }

            set
            {
                CheckConfiguration();
                source = value;
                requestedMarkup = null;
                loadRequested = value != null;
            }
        }

        /// <summary>
        /// 下次加载使用的图片映射；Texture2D 必须开启 Read/Write。
        /// </summary>
        /// <remarks>数组读写使用副本，数组中的纹理仍由调用者持有。修改前先卸载文档。</remarks>
        public ImageAsset[] Images
        {
            get
            {
                CheckThread();
                return (ImageAsset[])images.Clone();
            }

            set
            {
                CheckConfiguration();
                images = value == null ? Array.Empty<ImageAsset>() : (ImageAsset[])value.Clone();
            }
        }

        /// <summary>
        /// 下次加载使用的字体文件绝对路径；为空时使用平台默认字体。
        /// </summary>
        /// <remarks>修改前先卸载文档。调用者负责文件分发、读取权限与所需字形。</remarks>
        public string FontPath
        {
            get
            {
                CheckThread();
                return fontPath;
            }

            set
            {
                CheckConfiguration();
                if (value != null && value.IndexOf('\0') >= 0)
                {
                    throw new ArgumentException("字体路径不能包含 NUL。", nameof(value));
                }

                fontPath = value ?? "";
            }
        }

        /// <summary>
        /// 文档已同步完成加载，可以查询元素。
        /// </summary>
        /// <remarks>文档可查询不表示当帧布局和绘制已经完成。</remarks>
        public bool IsLoaded
        {
            get
            {
                CheckThread();
                return this && host && host.Ready;
            }
        }

        /// <summary>
        /// 每次成功加载后触发。可查询元素和注册回调，不可在此回调中再次加载或卸载。
        /// </summary>
        public event Action<Document> Loaded;

        /// <summary>
        /// 已加载的文档卸载后触发；所有旧元素和点击订阅已经失效。
        /// </summary>
        /// <remarks>通知仍在加载或卸载流程内，不能在此回调中重新加载或卸载。</remarks>
        public event Action<Document> Unloaded;

        void Awake()
        {
            ownerThread = Thread.CurrentThread.ManagedThreadId;
        }

        void OnEnable()
        {
            if (!loadRequested)
            {
                return;
            }

            if (requestedMarkup != null)
            {
                LoadContent(requestedMarkup, source);
            }
            else if (source)
            {
                Load(source);
            }
        }

        void Start()
        {
            // AddComponent 会先执行 OnEnable；Start 补上调用者随后赋值的 Source。
            if (loadRequested && !IsLoaded && source)
            {
                Load(source);
            }
        }

        void OnDisable()
        {
            ReleaseContent();
        }

        void OnDestroy()
        {
            ReleaseContent();
            Loaded = null;
            Unloaded = null;
        }

        /// <summary>
        /// 同步加载调用者持有的 TextAsset，完成后触发 Loaded。
        /// </summary>
        /// <param name="asset">非空 RML 文本资产，所有权仍属于调用者。</param>
        /// <remarks>必须在启用的组件上调用；重载会使旧元素与订阅失效。</remarks>
        public void Load(TextAsset asset)
        {
            CheckThread();
            if (!asset)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            LoadContent(asset.text, asset);
        }

        /// <summary>
        /// 同步加载 RML 字符串。重载会先卸载旧内容，旧元素不能继续使用。
        /// </summary>
        /// <param name="markup">非空且不包含 NUL 的完整 RML 文档。</param>
        /// <remarks>必须在启用的组件上调用；URUI 管理字符串加载所需的临时资产。</remarks>
        public void LoadMarkup(string markup)
        {
            LoadContent(markup, null);
        }

        void LoadContent(string markup, TextAsset requestedSource)
        {
            CheckThread();
            if (string.IsNullOrWhiteSpace(markup))
            {
                throw new ArgumentException("RML 内容不能为空。", nameof(markup));
            }

            if (markup.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("RML 不能包含 NUL。", nameof(markup));
            }

            if (!isActiveAndEnabled)
            {
                throw new InvalidOperationException("请先启用 URUI Document。");
            }

            if (changingContent)
            {
                throw new InvalidOperationException("不能在文档生命周期回调中重入加载或卸载。");
            }

            if (RenderHost.ActiveHost && RenderHost.ActiveHost != host)
            {
                throw new InvalidOperationException("URUI 当前仅支持一个活动文档，请先卸载已有文档。");
            }

            ValidateImages();
            if (!string.IsNullOrEmpty(fontPath) && (fontPath.IndexOf('\0') >= 0 || !File.Exists(fontPath)))
            {
                throw new ArgumentException("字体路径必须指向可读取的字体文件。", nameof(FontPath));
            }
            // 生命周期通知也属于本次操作，保持门闩直到通知和失败清理全部结束。
            changingContent = true;
            try
            {
                ReleaseContent();
                // Unloaded 允许业务禁用或销毁组件，也可能让另一个文档占用 context。
                // 释放旧文档后重新检查，避免为失效请求继续创建资源。
                if (!this || !isActiveAndEnabled)
                {
                    throw new InvalidOperationException("URUI 加载已取消：组件在 Unloaded 中被禁用或销毁。");
                }

                if (RenderHost.ActiveHost)
                {
                    throw new InvalidOperationException("URUI 加载已取消：另一个文档已占用 context。");
                }

                loadRequested = true;
                source = requestedSource;
                requestedMarkup = markup;
                // 宿主只接收本组件拥有的副本，释放时不会销毁调用者的 Source。
                ownedMarkup = new TextAsset(markup)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                var renderer = new GameObject("URUI Renderer");
                renderer.transform.SetParent(transform, false);
                renderer.hideFlags = HideFlags.HideAndDontSave;
                host = renderer.AddComponent<RenderHost>();
                host.document = ownedMarkup;
                host.overrideFontPath = fontPath;
                host.imageAssets = new ImageBinding[images.Length];
                for (var i = 0; i < images.Length; i++)
                {
                    host.imageAssets[i] = new ImageBinding
                    {
                        source = images[i].Source,
                        texture = images[i].Texture
                    };
                }

                host.InitializeForDocument();
                if (!host.Ready)
                {
                    throw new InvalidOperationException("URUI 文档加载失败，详情见 Unity Console。");
                }

                host.Clicked += DispatchClick;
                Loaded?.Invoke(this);
            }
            catch
            {
                // 原生初始化或 Loaded 回调失败时，回收已经取得的资源，避免继续占用 context。
                ReleaseContent();
                throw;
            }
            finally
            {
                changingContent = false;
            }
        }

        /// <summary>
        /// 卸载内容并同步释放原生 context 和 URUI 自有资源；调用者图片和 TextAsset 不受影响。
        /// </summary>
        /// <remarks>重复调用安全。显式卸载取消下一次启用时的自动恢复，直到再次加载或配置 Source。</remarks>
        public void Unload()
        {
            CheckThread();
            if (changingContent)
            {
                throw new InvalidOperationException("不能在文档生命周期回调中重入加载或卸载。");
            }

            changingContent = true;
            try
            {
                loadRequested = false;
                requestedMarkup = null;
                ReleaseContent();
            }
            finally
            {
                changingContent = false;
            }
        }

        /// <summary>
        /// 按 RML id 查询元素（不带 #）。不存在或尚未加载时返回 null；不是 CSS/UQuery 选择器。
        /// </summary>
        /// <param name="id">非空且唯一的 RML 元素 ID。</param>
        /// <returns>当前节点的引用包装；未加载、节点不存在或引用获取失败时为 null。</returns>
        /// <remarks>同一有效节点复用包装；节点替换或文档重载后需要重新查询。</remarks>
        public Element Q(string id)
        {
            CheckThread();
            RequireName(id, nameof(id));
            if (!IsLoaded)
            {
                return null;
            }

            if (elements.TryGetValue(id, out var cached))
            {
                if (cached.IsValid)
                {
                    return cached;
                }

                cached.Dispose();
            }

            var reference = host.AcquireElementReference(id);
            if (reference == null)
            {
                return null;
            }

            var element = new Element(this, id, generation, reference);
            elements.Add(id, element);
            return element;
        }

        void ValidateImages()
        {
            images = images ?? Array.Empty<ImageAsset>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var image in images)
            {
                RequireName(image.Source, nameof(Images));
                if (!image.Texture || !image.Texture.isReadable)
                {
                    throw new ArgumentException("图片必须是开启 Read/Write 的 Texture2D。", nameof(Images));
                }

                if (!names.Add(image.Source))
                {
                    throw new ArgumentException("图片 Source 不能重复。", nameof(Images));
                }
            }
        }

        void ReleaseContent()
        {
            var wasLoaded = host && host.Ready;
            // 先推进世代并使引用失效，再释放宿主与通知业务，避免回调继续使用旧节点。
            generation++;
            foreach (var element in elements.Values)
            {
                element.Invalidate();
            }

            elements.Clear();
            if (host)
            {
                host.Clicked -= DispatchClick;
                host.ReleaseForDocument();
                Destroy(host.gameObject);
                host = null;
            }

            if (ownedMarkup)
            {
                Destroy(ownedMarkup);
                ownedMarkup = null;
            }

            if (wasLoaded)
            {
                Unloaded?.Invoke(this);
            }
        }

        void DispatchClick(string id)
        {
            if (isActiveAndEnabled && elements.TryGetValue(id, out var element) && element.IsValid)
            {
                element.DispatchClick();
            }
        }

        void CheckConfiguration()
        {
            CheckThread();
            if (IsLoaded || changingContent)
            {
                throw new InvalidOperationException("请先卸载文档，再修改加载资源配置。");
            }
        }

        internal void CheckThread()
        {
            // 未执行 Awake 的禁用组件也必须遵守主线程约束，使用运行时登记线程兜底。
            var expected = ownerThread == 0 ? unityThread : ownerThread;
            if (expected == 0 || Thread.CurrentThread.ManagedThreadId != expected)
            {
                throw new InvalidOperationException("URUI 文档和元素必须在 Unity 主线程使用。");
            }
        }

        internal bool IsCurrent(uint version) => IsLoaded && generation == version;

        internal RenderHost Host => host;

        internal void Forget(Element element)
        {
            // 同一个 ID 可能已经被重新查询，旧包装释放时不能移除新的缓存项。
            if (elements.TryGetValue(element.Id, out var existing) && ReferenceEquals(existing, element))
            {
                elements.Remove(element.Id);
            }
        }

        internal static void RequireName(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("名称不能为空或包含 NUL。", parameter);
            }
        }
    }
}
