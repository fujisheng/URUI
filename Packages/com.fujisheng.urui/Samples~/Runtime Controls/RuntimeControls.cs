using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;
using UnityResources = UnityEngine.Resources;
using URUI.Resources;

namespace URUI.Samples
{
    /// <summary>导入样例并添加到空 GameObject，展示设置、表单与可复用列表。</summary>
    [RequireComponent(typeof(Document))]
    public sealed class RuntimeControls : MonoBehaviour
    {
        Document document;
        Camera ownedCamera;
        readonly List<IDisposable> subscriptions = new List<IDisposable>();
        readonly List<string> items = new List<string>();
        Button apply;
        Texture2D sampleImage;

        void OnEnable()
        {
            if (!Camera.main)
            {
                ownedCamera = new GameObject("URUI Sample Camera").AddComponent<Camera>();
                ownedCamera.tag = "MainCamera";
                ownedCamera.clearFlags = CameraClearFlags.SolidColor;
                ownedCamera.backgroundColor = new Color(.04f, .06f, .1f);
            }
            document = GetComponent<Document>();
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

            if (!sampleImage)
            {
                sampleImage = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var pixels = new Color32[256];
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        pixels[y * 16 + x] = (x / 4 + y / 4) % 2 == 0 ? new Color32(72, 197, 175, 255) : new Color32(49, 111, 202, 255);
                    }
                }

                sampleImage.SetPixels32(pixels);
                sampleImage.Apply();
            }
            var images = new List<ImageAsset>(document.Images);
            if (!images.Exists(image => image.Source == "runtime-controls-icon"))
            {
                images.Add(new ImageAsset("runtime-controls-icon", sampleImage));
                document.Images = images.ToArray();
            }

            var source = document.Source ? document.Source : UnityResources.Load<TextAsset>("URUI/Samples/RuntimeControls.rml");
            if (!source)
            {
                throw new InvalidOperationException("请同时导入 Runtime Controls 样例中的 RML 资源。");
            }

            document.Load(source);
        }

        void OnLoaded(Document loaded)
        {
            ClearSubscriptions();
            var volume = loaded.Q<Slider>("volume");
            var progress = loaded.Q<ProgressBar>("progress");
            void UpdateProgress(float value)
            {
                progress.SetValueWithoutNotify(value);
                progress.Text = "Volume " + Mathf.RoundToInt(value) + "%";
            }
            UpdateProgress(volume.Value);
            subscriptions.Add(volume.RegisterValueChangedCallback(evt => UpdateProgress(evt.NewValue)));
            var name = loaded.Q<TextField>("player-name");
            name.MaxLength = 32;
            var status = loaded.Q<Label>("status");
            var count = loaded.Q<IntegerField>("count");
            count.IsDelayed = true;
            count.Step = 5;
            count.Submitted += value => status.Text = "Quantity committed: " + value;
            var weight = loaded.Q<FloatField>("weight");
            weight.Step = .25f;
            loaded.Q<TextField>("read-only").IsReadOnly = true;
            var integerSlider = loaded.Q<IntegerSlider>("level");
            subscriptions.Add(integerSlider.RegisterValueChangedCallback(evt => loaded.Q<Label>("level-caption").Text = "Level " + evt.NewValue));
            apply = loaded.Q<Button>("apply");
            apply.Clicked += Apply;
            subscriptions.Add(name.RegisterValueChangedCallback(evt => apply.SetEnabled(!string.IsNullOrWhiteSpace(evt.NewValue))));
            var list = loaded.Q<ListView>("items");
            items.Clear();
            for (int i = 0; i < 1000; i++)
            {
                items.Add("Inventory item " + (i + 1));
            }

            list.FixedItemHeight = 28;
            list.MakeItem = () =>
            {
                var label = loaded.Create<Label>();
                label.AddToClassList("inventory-row");
                return label;
            };
            list.BindItem = (element, index) => element.Text = items[index];
            list.ItemsSource = items;
            list.SelectionChanged += index => status.Text = index < 0 ? "Selection cleared" : "Selected item " + (index + 1);
            list.ItemChosen += index => status.Text = "Opened item " + (index + 1);
        }

        void Apply()
        {
            var name = document.Q<TextField>("player-name").Value;
            var quality = document.Q<Dropdown>("quality").Value;
            document.Q<Label>("status").Text = "Saved " + name + " / " + quality;
        }
        void ClearSubscriptions()
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }

            subscriptions.Clear();
            if (apply != null && apply.IsValid)
            {
                apply.Clicked -= Apply;
            }

            apply = null;
        }
        void OnDisable()
        {
            StopAllCoroutines();
            ClearSubscriptions();
            if (document)
            {
                document.Loaded -= OnLoaded;
                document.Unload();
            }
        }
        void OnDestroy()
        {
            if (sampleImage)
            {
                Destroy(sampleImage);
            }

            if (ownedCamera)
            {
                Destroy(ownedCamera.gameObject);
            }
        }
    }
}
