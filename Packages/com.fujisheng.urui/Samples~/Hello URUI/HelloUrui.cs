using System;
using UnityEngine;

namespace URUI.Samples
{
    /// <summary>
    /// 添加到空 GameObject，进入 Play Mode 即可体验托管文档与点击接口。
    /// </summary>
    [RequireComponent(typeof(Document))]
    public sealed class HelloUrui : MonoBehaviour
    {
        const string Markup = @"<rml><head><style>
body { display:block; font-family:__URUI_FONT__; color:white; font-size:24px; margin:24px; }
div { display:block; }
#panel { width:360px; height:180px; padding:20px; background-color:#204080; }
#action { width:220px; height:50px; margin-top:24px; background-color:#4070b0; }
</style></head><body><div id='panel'><div id='label'>Hello URUI</div><div id='action'>Click to count</div></div></body></rml>";
        Document document;
        Camera ownedCamera;
        IDisposable clickSubscription;
        int count;

        void OnEnable()
        {
            if (!Camera.main)
            {
                // 仅记录由样例创建的相机，销毁时不接管场景已有相机的所有权。
                ownedCamera = new GameObject("URUI Sample Camera").AddComponent<Camera>();
                ownedCamera.tag = "MainCamera";
                ownedCamera.clearFlags = CameraClearFlags.SolidColor;
                ownedCamera.backgroundColor = Color.black;
            }

            document = GetComponent<Document>();
            document.Loaded += OnLoaded;
            document.LoadMarkup(Markup);
        }

        void OnLoaded(Document loadedDocument)
        {
            // 重载会替换节点树；每次 Loaded 都重新查询，并释放上一轮订阅。
            clickSubscription?.Dispose();
            loadedDocument.Q("label").SetText("Hello URUI · " + count);
            clickSubscription = loadedDocument.Q("action").RegisterClickCallback(OnClicked);
        }

        void OnClicked(ClickEvent notification)
        {
            count++;
            document.Q("label").SetText("Clicks: " + count);
            document.Q("panel").Style.SetBackgroundColor(count % 2 == 0 ? new Color(.12f, .25f, .5f) : new Color(.12f, .4f, .3f));
        }

        void OnDisable()
        {
            clickSubscription?.Dispose();
            clickSubscription = null;
            if (document)
            {
                document.Loaded -= OnLoaded;
                document.Unload();
            }
        }

        void OnDestroy()
        {
            if (ownedCamera)
            {
                Destroy(ownedCamera.gameObject);
            }
        }
    }
}
