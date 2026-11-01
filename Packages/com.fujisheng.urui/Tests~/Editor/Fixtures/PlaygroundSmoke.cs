#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using URUI.Samples;

namespace URUI.Tests
{
    /// <summary>操作实际 Playground 场景的按钮，验证序列化 RML 与导入的控制器使用当前包。</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class PlaygroundSmoke : MonoBehaviour
    {
        [DllImport("URUIBridge")]
        static extern int urui_get_element_box(string id, [Out] float[] box);

        [DllImport("URUIBridge")]
        static extern void urui_mouse_move(int x, int y);

        [DllImport("URUIBridge")]
        static extern void urui_mouse_button(int button, int pressed);

        Document document;
        Element first;
        Element second;
        int frames;
        bool finished;
        string runtimeError;

        void Awake()
        {
            Application.logMessageReceived += CaptureError;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= CaptureError;
        }

        void CaptureError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                runtimeError = message;
            }
        }

        static void Need(bool value, string reason)
        {
            if (!value)
            {
                throw new Exception(reason);
            }
        }

        static void Click(string name)
        {
            var box = new float[6];
            Need(urui_get_element_box(name, box) == 1 && box[2] > 0 && box[3] > 0, "button layout: " + name);
            urui_mouse_move((int)(box[0] + box[2] / 2), (int)(box[1] + box[3] / 2));
            urui_mouse_button(0, 1);
            urui_mouse_button(0, 0);
        }

        static void Capture()
        {
            var camera = Camera.main;
            var prior = RenderTexture.active;
            var target = new RenderTexture(640, 480, 24);
            var image = new Texture2D(640, 480);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                image.Apply();
                File.WriteAllBytes("Evidence/playground.png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = prior;
                Destroy(target);
                Destroy(image);
            }
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
                    document = FindFirstObjectByType<Document>();
                    Need(document && document.IsLoaded && document.Source && document.GetComponent<ElementPlayground>(), "real serialized playground scene");
                    Click("add");
                }

                if (frames == 6)
                {
                    first = document.Root.Q("item-1");
                    Need(first != null && first.IsAttached, "create by real button");
                    Click("add");
                }

                if (frames == 9)
                {
                    second = document.Root.Q("item-2");
                    Need(second != null && second.ClassListContains("selected") && document.Root.Q("items").ChildCount == 2, "selection class and DOM count");
                    Capture();
                    Click("detach");
                }

                if (frames == 12)
                {
                    Need(second.IsValid && !second.IsAttached && document.Root.Q("items").ChildCount == 1, "detach retains identity");
                    Click("attach");
                }

                if (frames == 15)
                {
                    Need(document.Root.Q("items")[0] == second, "attach at index zero");
                    Click("destroy");
                }

                if (frames == 18)
                {
                    Need(!second.IsValid && first.IsValid, "destroy selected only");
                    Click("clear");
                }

                if (frames == 21)
                {
                    Need(document.Root.Q("items").ChildCount == 0 && first.IsValid && !first.IsAttached, "clear retains detached nodes");
                    document.Unload();
                    Need(!first.IsValid, "unload releases detached nodes");
                }

                if (frames == 24)
                {
                    Finish(true, "");
                }
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        void Finish(bool passed, string error)
        {
            finished = true;
            if (runtimeError != null)
            {
                passed = false;
                error = runtimeError;
            }

            File.WriteAllText("Evidence/playground.json", JsonUtility.ToJson(new Report { passed = passed, frames = frames, error = error }, true));
            Debug.Log(passed ? "URUI_PLAYGROUND_PASS" : "URUI_PLAYGROUND_FAIL " + error);
            UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
        }

        [Serializable]
        class Report
        {
            public bool passed;
            public int frames;
            public string error;
        }
    }
}
#endif
