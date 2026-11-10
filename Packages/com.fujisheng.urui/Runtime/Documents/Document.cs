using System;
using System.Threading;
using UnityEngine;
using URUI.Internal.Hosting;
using URUI.Resources;
using URUI.Hosting;

namespace URUI.Documents
{
    /// <summary>
    /// 在 Unity 场景中加载 HTML 文档。所有操作限 Unity 主线程；多个文档共享显式关联的 Surface。
    /// 禁用时释放渲染资源，启用时重新加载最近请求的内容；重载后请重新查询元素。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("URUI/Document")]
    public sealed partial class Document : MonoBehaviour
    {
        [SerializeField]
        TextAsset source;
        [SerializeField]
        ImageAsset[] images = Array.Empty<ImageAsset>();
        [SerializeField]
        TextResource[] textResources = Array.Empty<TextResource>();
        RenderHost host;
        [SerializeField] Surface surface;
        [SerializeField] string sourceUrl = "";
        string requestedMarkup;
        int ownerThread;
        bool changingContent;
        bool loadRequested = true;
        static int unityThread;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CaptureUnityThread()
        {
            unityThread = Thread.CurrentThread.ManagedThreadId;
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
                return this && content != null && !content.Retired && host && host.Ready;
            }
        }

        /// <summary>
        /// 每次成功加载后触发。可查询元素和注册回调，加载与卸载请求在通知结束后执行。
        /// </summary>
        public event Action<Document> Loaded;

        /// <summary>
        /// 已加载的文档卸载后触发；所有旧元素和点击订阅已经失效。
        /// </summary>
        /// <remarks>通知中的加载与卸载请求进入所属 Surface 的有序延迟队列。</remarks>
        public event Action<Document> Unloaded;

        /// <summary>所有现存元素完成本轮原生布局刷新后触发；祖先移动或滚动也会通知。</summary>
        public event Action<Document> LayoutUpdated
        {
            add { layoutUpdated += value; layoutCallbacks = null; }
            remove { layoutUpdated -= value; layoutCallbacks = null; }
        }
        Action<Document> layoutUpdated;
        Delegate[] layoutCallbacks;
    }
}
