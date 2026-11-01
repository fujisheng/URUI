using System;
using System.Threading;
using UnityEngine;
using URUI.Internal.Hosting;
using URUI.Resources;

namespace URUI.Documents
{
    /// <summary>
    /// 在 Unity 场景中加载 RML 文档。所有操作限 Unity 主线程，一次仅支持一个活动文档。
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
        string fontPath = "";
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
    }
}
