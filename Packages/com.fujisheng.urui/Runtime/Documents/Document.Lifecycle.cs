using System.Threading;

namespace URUI.Documents
{
    // 先使旧节点与订阅失效，再释放宿主和通知业务，避免生命周期回调继续使用旧对象。
    public sealed partial class Document
    {
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
    }
}
