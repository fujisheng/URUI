#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using URUI.Internal.Hosting;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Internal.Interop;
using URUI.Internal.Resources;

namespace URUI.Tests
{
    // 当前借用纹理路径的三个布局和合批边界检查；不再装配旧全局文档参考路径。
    public sealed class BorrowedBatchValidation : MonoBehaviour
    {
        [Serializable]
        sealed class Row
        {
            public string registration;
            public string scenario;
            public string shader;
            public string keywords;
            public bool profilerCounterValid;
            public bool profilerCounterUsable;
            public int nativeCommands;
            public int nativeBeforeSync;
            public int nativeAfterSync;
            public int distinctTextureIds;
            public int combinedGroups;
            public int uiDrawSubmissions;
            public int commandBufferBytes;
            public long unityDrawCalls;
            public long emptyCameraDrawCalls;
            public int unityImmediateDrawCalls;
        }
        [Serializable]
        sealed class Report
        {
            public bool passed;
            public int assertions;
            public string error;
            public List<Row> rows = new List<Row>();
            public string scope = "Editor / Metal, three tiny ordered layouts; native commands, actual steady-frame combined caches and recorded DrawMesh submissions/command-buffer bytes. Global Draw Calls Count gives no usable samples for this headless CommandBuffer rendering, so it is NOT used as a GPU draw count. All rows use current borrowed assets and independent HTML documents. No performance matrix or transparent reordering.";
        }
        readonly Report report = new Report();
        Texture2D a;
        Texture2D b;
        ProfilerRecorder counter;
        Camera camera;
        void Check(bool value, string label)
        {
            report.assertions++;
            if (!value)
            {
                throw new Exception(label);
            }
        }
        static async Task Frames(int count = 7)
        {
            int end = Time.frameCount + count;
            float start = Time.realtimeSinceStartup;
            while (Time.frameCount < end)
            {
                if (Time.realtimeSinceStartup - start > 12)
                {
                    throw new Exception("frame timeout");
                }
                await Task.Delay(20);
            }
        }
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Small batch comparison").AddComponent<BorrowedBatchValidation>();
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
        static Texture2D Image(Color32 color)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, false);
            var p = new Color32[16];
            for (int i = 0; i < p.Length; i++)
            {
                p[i] = color;
            }
            t.SetPixels32(p);
            t.Apply();
            return t;
        }
        static string Markup(string scenario)
        {
            string first = "<img style='left:0px' src='a.png'/>";
            string middle = scenario == "image-font-image" ? "<span style='position:absolute;left:48px;top:0px'>AA</span>" : "<img style='left:48px' src='b.png'/>";
            string last = "<img style='left:96px' src='" + (scenario == "image-font-image" ? "b.png" : "a.png") + "'/>";
            return "<!doctype html><html><head><style>@font-face{font-family:Acceptance;src:url(font.ttf);}body{margin:0;font-family:Acceptance;font-size:16px;color:white}img{display:block;position:absolute;top:0px;width:32px;height:32px}</style></head><body>" + first + middle + last + "</body></html>";
        }
        long CounterMaximum()
        {
            long value = 0;
            for (int i = 0; i < counter.Count; i++)
            {
                value = Math.Max(value, counter.GetSample(i).Value);
            }
            return value;
        }
        async Task<Row> Measure(string scenario)
        {
            // Batch mode may not automatically render a Game View. Render exactly
            // once with the camera disabled, then read the completed frame counter.
            camera.enabled = false;
            counter.Reset();
            camera.Render();
            await Frames(3);
            long empty = counter.Valid ? CounterMaximum() : -1;
            camera.enabled = true;
            var owner = new GameObject("batch fixture Surface");
            var surface = owner.AddComponent<Surface>();
            surface.Viewport = new Vector2Int(640, 360);
            surface.Input.UseUnityInput = false;
            var document = owner.AddComponent<Document>();
            document.Surface = surface;
            try
            {
                document.TextResources = new[] { new TextResource("font.ttf", UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/LiberationSans-Regular.ttf")) };
                var second = scenario == "different-images" ? b : a;
                document.Images = new[] { new ImageAsset("a.png", a), new ImageAsset("b.png", second) };
                document.LoadHtml(Markup(scenario), "batch.html");
                var host = RenderHost.ActiveHost;
                Check(host && host.Ready, "Surface host initialization");
                camera.enabled = false;
                await Frames(9);
                var beforeSync = NativeMethods.urui_command_count();
                // Surface 需要显式录制预热；合批缓存只在连续稳定记录后建立。
                for (var warmup = 0; warmup < 3; warmup++)
                {
                    camera.enabled = true;
                    document.SynchronizeLayout();
                    camera.Render();
                    await Frames(1);
                }
                camera.enabled = false;
                var afterSync = NativeMethods.urui_command_count();
                counter.Reset();
                camera.Render();
                int immediate = UnityStats.drawCalls;
                await Frames(3);
                long actual = counter.Valid ? CounterMaximum() : -1;
                var commands = new CommandNative[NativeMethods.urui_command_count()];
                Check(NativeMethods.urui_get_commands(commands, commands.Length) == commands.Length, "actual ordered native commands");
                var ids = new HashSet<ulong>();
                foreach (var c in commands)
                {
                    if (c.texture != 0)
                    {
                        ids.Add(c.texture);
                    }
                }
                var caches = (IList)typeof(RenderHost).GetField("batches", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var draws = (int)typeof(RenderHost).GetProperty("RecordedGeometryDraws", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var material = (Material)typeof(RenderHost).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var buffer = (CommandBuffer)typeof(RenderHost).GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var row = new Row
                {
                    nativeBeforeSync = beforeSync,
                    nativeAfterSync = afterSync,
                    commandBufferBytes = buffer.sizeInBytes,
                    profilerCounterUsable = counter.Valid && actual > empty,
                    registration = "borrowed",
                    scenario = scenario,
                    nativeCommands = commands.Length,
                    distinctTextureIds = ids.Count,
                    combinedGroups = caches.Count,
                    uiDrawSubmissions = draws,
                    unityDrawCalls = actual,
                    unityImmediateDrawCalls = immediate,
                    emptyCameraDrawCalls = empty,
                    profilerCounterValid = counter.Valid,
                    shader = material.shader.name,
                    keywords = string.Join(",", material.shaderKeywords)
                };
                report.rows.Add(row);
                Check(row.nativeCommands == 3, "three real ordered draws before host batching: beforeSync=" + beforeSync + ", afterSync=" + afterSync + ", now=" + row.nativeCommands);
                Check(row.uiDrawSubmissions == (scenario == "same-texture-aliases" ? 1 : 2), "expected batching boundary: " + row.registration + " / " + scenario);
                return row;
            }
            finally
            {
                document.Unload();
                Destroy(owner);
                await Frames(3);
            }
        }
        async void Start()
        {
            try
            {
                camera = new GameObject("Batch camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                a = Image(new Color32(200, 100, 50, 180));
                b = Image(new Color32(30, 100, 200, 180));
                counter = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 16);
                foreach (var scenario in new[] { "same-texture-aliases", "different-images", "image-font-image" })
                {
                    await Measure(scenario);
                }
                report.passed = true;
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                Debug.LogException(error);
            }
            finally
            {
                counter.Dispose();
                if (a)
                {
                    Destroy(a);
                }
                if (b)
                {
                    Destroy(b);
                }
            }
            const string path = "Artifacts/ImageMemoryAudit/Implementation/batching.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("BORROWED_BATCH_" + (report.passed ? "PASS" : "FAIL") + " rows=" + report.rows.Count);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }
    }
}
#endif
