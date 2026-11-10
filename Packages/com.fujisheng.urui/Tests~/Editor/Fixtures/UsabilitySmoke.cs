#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using URUI.Samples;
using URUI.Documents;
using URUI.Events;

namespace URUI.Tests
{
    /// <summary>
    /// 验证菜单生成文档和样例点击、禁用恢复，并保存实际渲染截图。
    /// </summary>
    [DefaultExecutionOrder(-20000)]
    public sealed class UsabilitySmoke : MonoBehaviour
    {
        public Document generatedDocument;
        public TextAsset generatedSource;
        HelloUrui sample;
        int frames;
        int clicks;
        bool finished;
        bool failed;
        string error;

        static void Need(bool value, string message)
        {
            if (!value)
            {
                throw new Exception(message);
            }
        }

        void OnEnable()
        {
            Application.logMessageReceived += Log;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= Log;
        }

        void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                failed = true;
                error = message;
            }
        }

        void Click()
        {
            var document = sample.GetComponent<Document>();
            var element = document.Q("action");
            Need(element != null && element.HasLayout, "sample button has layout bounds");
            var box = element.WorldBound;
            Need(Mathf.Abs(box.width - 220) < .1f && Mathf.Abs(box.height - 50) < .1f, "sample button uses documented block size " + box);
            document.Surface.Input.UseUnityInput = false;
            document.Surface.Input.SendPointer(PointerPhase.Down, box.center);
            document.Surface.Input.SendPointer(PointerPhase.Up, box.center);
        }

        void Update()
        {
            if (finished)
            {
                return;
            }

            frames++;
            try
            {
                if (frames == 3)
                {
                    Need(generatedDocument.IsLoaded && generatedDocument.Q("label").IsValid, "generated serialized Document loaded");
                    Need(generatedDocument.Source == generatedSource, "TextAsset source visible after Loaded");
                    generatedDocument.enabled = false;
                    generatedDocument.enabled = true;
                    Need(generatedDocument.IsLoaded && generatedDocument.Source == generatedSource, "serialized source survives enable cycle");
                    generatedDocument.Unload();
                    var surface = generatedDocument.Surface;
                    surface.Viewport = new Vector2Int(640, 480);
                    Destroy(generatedDocument.gameObject);
                    var sampleObject = new GameObject("Hello URUI");
                    sampleObject.AddComponent<Document>().Surface = surface;
                    sample = sampleObject.AddComponent<HelloUrui>();
                }

                if (frames == 6)
                {
                    var document = sample.GetComponent<Document>();
                    Need(document.IsLoaded && document.Q("action").IsValid, "sample loaded via new API");
                    document.Q("action").RegisterCallback<ClickEvent>(value => clicks++);
                    Click();
                }

                if (frames == 9)
                {
                    Need(clicks == 1, "sample native click");
                    var document = sample.GetComponent<Document>();
                    Need(document.Q("label").Text == "Clicks: 1", "sample controller handled one click: " + document.Q("label").Text);
                    var target = Camera.main;
                    var previous = RenderTexture.active;
                    var rt = new RenderTexture(640, 480, 24);
                    var pixels = new Texture2D(640, 480);
                    try
                    {
                        target.targetTexture = rt;
                        target.Render();
                        RenderTexture.active = rt;
                        pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                        pixels.Apply();
                        var panel = sample.GetComponent<Document>().Q("panel");
                        Need(panel.HasLayout, "sample panel bounds");
                        var box = panel.WorldBound;
                        var color = pixels.GetPixel((int)box.x + 5, 479 - (int)box.y - 5);
                        Need(color.g > .35f && color.b < .4f, "sample click changed green background " + color);
                        File.WriteAllBytes("Evidence/hello-urui.png", pixels.EncodeToPNG());
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        target.targetTexture = null;
                        Destroy(rt);
                        Destroy(pixels);
                    }

                    sample.gameObject.SetActive(false);
                    sample.gameObject.SetActive(true);
                }

                if (frames == 12)
                {
                    sample.GetComponent<Document>().Q("action").RegisterCallback<ClickEvent>(value => clicks++);
                    Click();
                }

                if (frames == 15)
                {
                    Need(clicks == 2, "sample reenable has one current subscription");
                    Destroy(sample.gameObject);
                }

                if (frames == 18)
                {
                    Need(!sample && generatedSource, "sample cleanup preserves caller asset");
                    Finish(!failed, error);
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        void Finish(bool success, string message)
        {
            finished = true;
            File.WriteAllText("Evidence/usability-smoke.json", "{\"passed\":" + (success ? "true" : "false") + ",\"frames\":" + frames + ",\"clicks\":" + clicks + ",\"error\":\"" + (message ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"}");
            Debug.Log(success ? "URUI_USABILITY_SMOKE_PASS" : "URUI_USABILITY_SMOKE_FAIL " + message);
            UnityEditor.EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
