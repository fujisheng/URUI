using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using URUI.Animation;
using URUI.Documents;
using URUI.Internal.Hosting;
using URUI.Internal.Interop;

namespace URUI.Hosting
{
    /// <summary>拥有共享原生上下文、视口、时钟、输入与绘制资源；顶层页面显式关联此组件。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("URUI/Surface")]
    [DefaultExecutionOrder(-10000)]
    public sealed class Surface : MonoBehaviour
    {
        static readonly Dictionary<ulong, Document> owners = new();
        readonly HashSet<Document> documents = new();
        Document[] documentsSnapshot;
        readonly Queue<Document.DeferredAction> deferred = new();
        RenderHost host;
        SurfaceInput input;
        IUiClock clock = UnityUiClock.Unscaled;
        int ownerThread, dispatchDepth, transactionDepth, effectDepth;
        bool flushing, stopping;
        Vector2Int viewport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOwners() { owners.Clear(); }
        void Awake() { ownerThread = Thread.CurrentThread.ManagedThreadId; }
        internal void CheckThread()
        {
            if (ownerThread == 0 || Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("URUI Surface 必须在 Unity 主线程使用。");
        }
        /// <summary>同一 Surface 的所有页面共享此时钟；加载期间不能更换时基。</summary>
        public IUiClock UiClock
        {
            get { CheckThread(); return clock; }
            set {
                CheckThread();
                if (HasLoadedDocuments) throw new InvalidOperationException("请先卸载 Surface 的所有文档，再更换时钟。");
                var previous = clock;
                clock = value ?? throw new ArgumentNullException(nameof(value));
                try { if (host && host.Ready) host.RebaseUiClock(); }
                catch { clock = previous; throw; }
            }
        }
        /// <summary>逻辑 CSS 像素尺寸；零表示跟随屏幕尺寸。</summary>
        public Vector2Int Viewport
        {
            get { CheckThread(); return viewport; }
            set {
                CheckThread();
                if (value.x < 0 || value.y < 0 || (value.x == 0) != (value.y == 0)) throw new ArgumentOutOfRangeException(nameof(value));
                viewport = value;
                if (host && host.Ready) NativeMethods.urui_resize(Width, Height);
            }
        }
        internal int Width => viewport.x > 0 ? viewport.x : Screen.width;
        internal int Height => viewport.y > 0 ? viewport.y : Screen.height;
        /// <summary>一次分发到整个 Surface 的输入入口。</summary>
        public SurfaceInput Input { get { CheckThread(); return input ??= new SurfaceInput(this); } }
        /// <summary>当前位于同步分发、加载事务或效果回调中，尚不能同步创建顶层页面。</summary>
        public bool IsExecutingCallback { get { CheckThread(); return IsBusy || IsEffectCallback; } }
        /// <summary>在 Surface 的同步分发和原生调用返回后执行；空闲时立即执行。</summary>
        public void RunAfterEvent(Action action)
        {
            CheckThread();
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (IsEffectCallback) throw new InvalidOperationException("不能在绘制效果回调中安排页面变更。");
            if (Defer(() => { try { action(); } catch (Exception error) { Debug.LogException(error); } })) return;
            action();
        }
        internal bool IsDispatching => dispatchDepth != 0;
        internal bool IsBusy => dispatchDepth != 0 || transactionDepth != 0;
        internal bool IsEffectCallback => effectDepth != 0;
        /// <summary>提交同一 Surface 的完整 UI，一次调用覆盖所有活动文档。</summary>
        public bool SubmitRendering(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Rendering.RenderTargetIdentifier target, Rect viewport)
        {
            CheckThread();
            if (!float.IsFinite(viewport.x) || !float.IsFinite(viewport.y) ||
                !float.IsFinite(viewport.width) || !float.IsFinite(viewport.height) || viewport.width <= 0 || viewport.height <= 0)
                throw new ArgumentOutOfRangeException(nameof(viewport), "提交视口必须为有限且宽高为正的目标像素范围。");
            if (!host || !host.Ready || !HasLoadedDocuments) return false;
            // 与 Document 提交一致，页面变更须等到命令和 fence 都已排队后再执行。
            BeginDispatch();
            try { return host.SubmitRendering(context, target, viewport); }
            finally { EndDispatch(); FinishNativeCall(); }
        }
        internal bool HasLoadedDocuments {
            get { foreach (var document in documents) if (document && document.IsLoaded) return true; return false; }
        }
        internal RenderHost Host => host;
        internal bool HasEffects { get { foreach (var document in documents) if (document && document.IsLoaded && document.HasEffects) return true; return false; } }
        internal bool HasPainters { get { foreach (var document in documents) if (document && document.IsLoaded && document.HasPainters) return true; return false; } }
        internal Document FilterOwner(ulong filter)
        {
            if (NativeMethods.urui_filter_api_version() != 3) throw new NotSupportedException("共享 Surface 的滤镜需要所属文档协议 v3。");
            return FindDocument(NativeMethods.urui_filter_document(filter)) ?? throw new InvalidOperationException("滤镜所属文档已失效。");
        }
        internal Document[] DocumentsSnapshot()
        {if(documentsSnapshot==null){documentsSnapshot=new Document[documents.Count];documents.CopyTo(documentsSnapshot);}return documentsSnapshot;}
        internal static Document FindDocument(ulong owner) => owners.TryGetValue(owner, out var document) ? document : null;
        internal Document ElementOwner(ulong token) => FindDocument(NativeMethods.urui_surface_element_owner(token));
        internal Document FocusedDocument => FindDocument(NativeMethods.urui_surface_focused_document());
        internal Document TopDocument {
            get {
                Document result = null;
                foreach (var document in documents) if (document && document.IsLoaded && document.Presented &&
                    (!result || document.Layer > result.Layer || document.Layer == result.Layer && document.Order > result.Order)) result = document;
                return result;
            }
        }
        internal RenderHost EnsureStarted(Document document)
        {
            CheckThread();
            if (!isActiveAndEnabled || stopping) throw new InvalidOperationException("请先启用 URUI Surface。");
            if(documents.Add(document))documentsSnapshot=null;
            if (!host) {
                var renderer = new GameObject("URUI Renderer");
                renderer.transform.SetParent(transform, false); renderer.hideFlags = HideFlags.HideAndDontSave;
                host = renderer.AddComponent<RenderHost>(); host.OwnerSurface = this;
                host.InitializeForSurface();
            }
            if (!host.Ready) throw new InvalidOperationException("URUI Surface 初始化失败，详情见 Unity Console。");
            return host;
        }
        internal void Bind(ulong handle, Document document) { owners.Add(handle, document); }
        internal void Unbind(ulong handle) { owners.Remove(handle); }
        internal void Forget(Document document) { if(documents.Remove(document))documentsSnapshot=null; }
        internal void BeginDispatch() { dispatchDepth++; }
        internal void EndDispatch() { dispatchDepth--; }
        internal void BeginEffect() { effectDepth++; }
        internal void EndEffect() { effectDepth--; }
        internal void BeginTransaction() { transactionDepth++; }
        internal void EndTransaction() { transactionDepth--; }
        internal void Enqueue(Document.DeferredAction action) { deferred.Enqueue(action); }
        internal bool Defer(Action action)
        {
            if (!IsBusy) return false;
            deferred.Enqueue(new Document.DeferredAction(null,action,false,false)); return true;
        }
        internal void FinishNativeCall()
        {
            if (IsBusy || flushing) return;
            flushing = true;
            try {
                if (host && host.Ready) NativeMethods.urui_process_deferred_operations();
                while (deferred.Count > 0) deferred.Dequeue().Invoke();
            } finally { flushing = false; }
        }
        internal void RefreshDocuments(bool layout) { foreach (var document in DocumentsSnapshot()) if (document) document.RefreshElements(layout); }
        internal void NotifyLayoutUpdated() { foreach (var document in DocumentsSnapshot()) if (document) document.NotifyLayoutUpdated(); }
        void OnDisable()
        {
            if (stopping || Defer(OnDisable)) return;
            stopping = true; BeginTransaction();
            try {
                foreach (var document in DocumentsSnapshot()) if (document) document.SuspendForSurface();
                if (host) { host.ReleaseForSurface(); Destroy(host.gameObject); host = null; }
            } finally { EndTransaction(); stopping = false; FinishNativeCall(); }
        }
        void OnEnable()
        {
            foreach (var document in DocumentsSnapshot()) if (document) document.RestoreForSurface();
        }
    }
}
