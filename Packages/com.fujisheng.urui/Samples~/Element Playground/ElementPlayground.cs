using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;

namespace URUI.Samples
{
    /// <summary>显式调用节点 API 的小型控制器，不使用数据绑定或外部 UI 框架。</summary>
    [RequireComponent(typeof(Document))]
    public sealed class ElementPlayground : MonoBehaviour
    {
        Document document;
        Element items;
        Element status;
        Element selected;
        int sequence;
        readonly List<IDisposable> subscriptions = new List<IDisposable>();

        void OnEnable()
        {
            document = GetComponent<Document>();
            document.Loaded += Bind;
            if (document.IsLoaded)
            {
                Bind(document);
            }
            else
            {
                StartCoroutine(LoadWhenEnabled());
            }
        }

        IEnumerator LoadWhenEnabled()
        {
            // 同一 GameObject 的组件启用顺序不固定，不能提前绕开 Document 的生命周期检查。
            while (document && !document.isActiveAndEnabled)
            {
                yield return null;
            }

            if (!document || !isActiveAndEnabled || document.IsLoaded)
            {
                yield break;
            }

            if (document.Source)
            {
                document.Load(document.Source);
            }
        }

        void Bind(Document loaded)
        {
            ReleaseSubscriptions();
            selected = null;
            var root = loaded.Root;
            items = root.Q("items");
            status = root.Q("status");
            subscriptions.Add(root.Q("add").RegisterCallback<ClickEvent>(_ => AddItem()));
            subscriptions.Add(root.Q("detach").RegisterCallback<ClickEvent>(_ => {
                if (selected != null && selected.IsValid)
                {
                    selected.RemoveFromHierarchy();
                }

                RefreshStatus();
            }));
            subscriptions.Add(root.Q("attach").RegisterCallback<ClickEvent>(_ => {
                if (selected != null && selected.IsValid)
                {
                    items.Insert(0, selected);
                }

                RefreshStatus();
            }));
            subscriptions.Add(root.Q("destroy").RegisterCallback<ClickEvent>(_ => {
                if (selected != null && selected.IsValid)
                {
                    selected.Destroy();
                }

                selected = null;
                RefreshStatus();
            }));
            subscriptions.Add(root.Q("clear").RegisterCallback<ClickEvent>(_ => {
                items.Clear();
                RefreshStatus();
            }));
            RefreshStatus();
        }

        void AddItem()
        {
            var item = document.CreateElement("div");
            item.Name = "item-" + ++sequence;
            item.Text = "Item " + sequence + " - click to select";
            item.AddToClassList("item");
            subscriptions.Add(item.RegisterCallback<ClickEvent>(evt => Select(evt.CurrentTarget)));
            items.Add(item);
            Select(item);
        }

        void Select(Element item)
        {
            if (selected != null && selected.IsValid)
            {
                selected.EnableInClassList("selected", false);
            }

            selected = item;
            selected.EnableInClassList("selected", true);
            RefreshStatus();
        }

        void RefreshStatus()
        {
            var selection = selected != null && selected.IsValid ? selected.Name + (selected.IsAttached ? " / attached" : " / detached") : "none";
            status.Text = "Items: " + items.ChildCount + " | Selected: " + selection;
        }

        void ReleaseSubscriptions()
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }

            subscriptions.Clear();
        }

        void OnDisable()
        {
            StopAllCoroutines();
            ReleaseSubscriptions();
            selected = items = status = null;
            if (!document)
            {
                return;
            }

            document.Loaded -= Bind;
            document.Unload();
        }
    }
}
