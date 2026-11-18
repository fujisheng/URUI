using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Hosting;
using URUI.Resources;

namespace URUI.Tests
{
    public sealed class RenderPlanScaleTests
    {
        [Serializable] sealed class Report
        {
            public int nodes, textures, seed = 1234, warmup = 300, rounds = 3, framesPerRound = 1000, geometryDraws;
            public long gcEvents, geometryUploadBytes;
            public double[] p50 = new double[3], p95 = new double[3];
            public double gpuMilliseconds = -1;
            public string gpuStatus = "missing: synchronous layout timing does not measure GPU";
        }
        static object probe;
        static readonly int[] counts = { 10, 100, 1000, 5000 };
        static readonly int[] textures = { 1, 8, 9 };
        [Test, Combinatorial]
        public void StableRandomizedImages([ValueSource(nameof(counts))] int count, [ValueSource(nameof(textures))] int textureCount)
        {
            var camera = new GameObject("Scale camera", typeof(Camera)); camera.tag = "MainCamera";
            var root = new GameObject("Scale surface");
            var sources = new Texture2D[textureCount];
            var report = new Report { nodes = count, textures = textureCount };
            var output = Environment.GetEnvironmentVariable("URUI_RENDER_SCALE_REPORT");
            try
            {
                var surface = root.AddComponent<Surface>(); surface.Viewport = new Vector2Int(1024, 1024); surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>(); document.Surface = surface;
                var assets = new ImageAsset[textureCount]; var random = new System.Random(report.seed);
                for (var i = 0; i < textureCount; i++)
                {
                    sources[i] = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    sources[i].SetPixel(0, 0, new Color((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1)); sources[i].Apply();
                    assets[i] = new ImageAsset("source" + i + ".png", sources[i]);
                }
                document.Images = assets;
                var html = new StringBuilder("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:3px;height:3px;}</style></head><body>");
                for (var i = 0; i < count; i++)
                {
                    html.Append("<img id='n_").Append(random.Next()).Append('_').Append(i).Append("' src='source").Append(i % textureCount)
                        .Append(".png' style='left:").Append(i % 100 * 8).Append("px;top:").Append(i / 100 * 8).Append("px;'>");
                }
                document.LoadHtml(html.Append("</body></html>").ToString(), "/randomized-scale.html");
                for (var i = 0; i < report.warmup; i++) { document.SynchronizeLayout(); }
                var host = root.GetComponentInChildren<RenderHost>(true);
                var bytes = host.GetType().GetProperty("GeometryUploadBytes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var before = Convert.ToInt64(bytes.GetValue(host));
                using (var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    probe = new byte[4096]; GC.KeepAlive(probe); positive.Stop();
                    Assert.That(positive.Valid && positive.Count > 0, Is.True); probe = null;
                }
                using (var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    for (var i = 0; i < 32; i++) { document.SynchronizeLayout(); }
                    allocations.Stop(); Assert.That(allocations.Valid && !allocations.WrappedAround, Is.True);
                    for (var i = 0; i < allocations.Count; i++) { report.gcEvents += allocations.GetSample(i).Count; }
                }
                var samples = new double[report.framesPerRound];
                for (var round = 0; round < report.rounds; round++)
                {
                    for (var i = 0; i < samples.Length; i++)
                    {
                        var started = Stopwatch.GetTimestamp(); document.SynchronizeLayout();
                        samples[i] = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
                    }
                    Array.Sort(samples); report.p50[round] = samples[samples.Length / 2]; report.p95[round] = samples[(int)(samples.Length * .95)];
                }
                report.geometryUploadBytes = Convert.ToInt64(bytes.GetValue(host)) - before;
                report.geometryDraws = host.RecordedGeometryDraws;
                Assert.That(report.gcEvents, Is.Zero);
                Assert.That(report.geometryUploadBytes, Is.Zero);
                Assert.That(report.geometryDraws, Is.EqualTo((textureCount + 7) / 8));
            }
            finally
            {
                if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, count + "-" + textureCount + ".json"), JsonUtility.ToJson(report, true)); }
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(camera);
                foreach (var source in sources) { if (source) { UnityEngine.Object.DestroyImmediate(source); } }
            }
        }
    }
}
