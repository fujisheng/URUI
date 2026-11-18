using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using URUI.Editor.Inspection;
using URUI.Resources;

namespace URUI.Authoring.Tests
{
    /// <summary>隔离工程的真实 GPU 退役与多次预览开关检查；不等同于设备压力验收。</summary>
    public static class ResourceValidation
    {
        static int frames, cycles, phase;
        static bool retired;
        static int[] baseline;
        static string failure;
        public static void Run()
        {
            Application.logMessageReceived += Log;
            Cycle();
            GpuRetirement.Retire(() => retired = true);
            EditorApplication.update += Tick;
        }
        static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                failure = message + "\n" + stack;
        }
        static int[] Counts() => new[]
        {
            UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>().Length,
            UnityEngine.Resources.FindObjectsOfTypeAll<Material>().Length,
            UnityEngine.Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
            UnityEngine.Resources.FindObjectsOfTypeAll<Texture2D>().Length,
            Array.FindAll(UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>(), go => go.name == "URUI Authoring Preview").Length
        };
        static void Cycle()
        {
            using (var preview = RuntimeInspectionBridge.CreatePreview(128, 72))
            {
                preview.Document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:80px;height:40px;background-color:red;}@keyframes a{from{opacity:0;}to{opacity:1;}}</style></head><body><div id='box'></div></body></html>", "ui/retirement.html");
                preview.Render();
                var definition = new URUI.Animation.SequenceDefinition { Duration = 1 };
                definition.Tracks.Add(new URUI.Animation.SequenceTrack { Id = "a", Target = "box", Keyframes = "a", Duration = 1 });
                using (var player = new URUI.Animation.SequencePlayer(preview.Document.Body, definition))
                {
                    player.Sample(.8);
                    preview.Render();
                    player.Sample(.2);
                    preview.Render();
                    player.Sample(1);
                    preview.Render();
                    using (var pixels = preview.Capture())
                        if (pixels.Texture.GetPixel(10, 62).r < .9f)
                            throw new Exception("Preview rendered unexpected pixels.");
                }
            }
        }
        static void Tick()
        {
            try
            {
                if (failure != null)
                    throw new Exception(failure);
                GpuRetirement.Collect();
                frames++;
                if (phase == 0)
                {
                    if (!retired)
                    {
                        if (frames > 240)
                            throw new Exception("Warmup GPU fence did not retire.");
                        return;
                    }
                    if (frames < 30)
                        return;
                    baseline = Counts();
                    phase = 1;
                    frames = 0;
                }
                else if (phase == 1)
                {
                    Cycle();
                    cycles++;
                    if (cycles == 24)
                    {
                        retired = false;
                        GpuRetirement.Retire(() => retired = true);
                        phase = 2;
                        frames = 0;
                    }
                }
                else
                {
                    if (!retired)
                    {
                        if (frames > 240)
                            throw new Exception("Final GPU fence did not retire.");
                        return;
                    }
                    if (frames < 30)
                        return;
                    var after = Counts();
                    for (int i = 0; i < baseline.Length; i++)
                        if (after[i] > baseline[i])
                            throw new Exception("Owned preview resource growth at metric " + i + ": " + baseline[i] + " -> " + after[i]);
                    Directory.CreateDirectory("Evidence");
                    File.WriteAllText("Evidence/resources.json", "{\"passed\":true,\"cycles\":" + cycles + ",\"baseline\":[" + string.Join(",", baseline) + "],\"after\":[" + string.Join(",", after) + "]}");
                    Stop(0);
                }
            }
            catch (Exception error) { Directory.CreateDirectory("Evidence"); File.WriteAllText("Evidence/resources-failure.txt", error.ToString()); Debug.LogException(error); Stop(1); }
        }
        static void Stop(int code)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            EditorApplication.Exit(code);
        }
    }
}
