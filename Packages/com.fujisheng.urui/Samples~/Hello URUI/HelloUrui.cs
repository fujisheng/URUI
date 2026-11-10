using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using URUI.Documents;
using URUI.Events;
using URUI.Hosting;
using URUI.Resources;
using UnityResources = UnityEngine.Resources;

namespace URUI.Samples
{
    /// <summary>
    /// 添加到空 GameObject，进入 Play Mode 即可体验托管文档与点击接口。
    /// </summary>
    [RequireComponent(typeof(Document))]
    public sealed class HelloUrui : MonoBehaviour
    {
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
            if (!document.Surface)
            {
                document.Surface = GetComponent<Surface>() ?? gameObject.AddComponent<Surface>();
            }
            document.Loaded += OnLoaded;
            if (document.IsLoaded)
            {
                OnLoaded(document);
            }
            else
            {
                StartCoroutine(LoadWhenEnabled());
            }
        }

        IEnumerator LoadWhenEnabled()
        {
            while (document && !document.isActiveAndEnabled)
            {
                yield return null;
            }

            if (!document || !isActiveAndEnabled || document.IsLoaded)
            {
                yield break;
            }

            var source = document.Source ? document.Source : UnityResources.Load<TextAsset>("URUI/Samples/Hello");
            if (!source)
            {
                throw new InvalidOperationException("请同时导入 Hello URUI 样例中的 HTML 资源。");
            }
            var files = new List<TextResource>(document.TextResources);
            AddFile("/urui/base.css", "URUI/base");
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    AddFile("/urui/Fonts/" + name + ".bytes", "URUI/Fonts/" + name);
                }
            document.TextResources = files.ToArray();
            document.Load(source, document.Source ? document.SourceUrl : "samples/hello/Hello.html");

            void AddFile(string uri, string resource)
            {
                if (files.Exists(file => file.Source == uri)) return;
                files.Add(new TextResource(uri, UnityResources.Load<TextAsset>(resource)
                    ?? throw new InvalidOperationException("样例缺少作者依赖：" + resource)));
            }
        }

        void OnLoaded(Document loadedDocument)
        {
            // 重载会替换节点树；每次 Loaded 都重新查询，并释放上一轮订阅。
            clickSubscription?.Dispose();
            loadedDocument.Q("label").Text = "Hello URUI · " + count;
            clickSubscription = loadedDocument.Q("action").RegisterCallback<ClickEvent>(OnClicked);
        }

        void OnClicked(ClickEvent notification)
        {
            count++;
            document.Q("label").Text = "Clicks: " + count;
            var color = count % 2 == 0 ? new Color(.12f, .25f, .5f) : new Color(.12f, .4f, .3f);
            // 原生事件返回后再写动态颜色，沿用文档提供的安全调度入口。
            document.RunAfterEvent(() => document.Q("panel").Style.SetBackgroundColor(color));
        }

        void OnDisable()
        {
            StopAllCoroutines();
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
