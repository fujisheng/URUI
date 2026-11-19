using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;
using URUI.Resources;
using URUI.Rendering.Effects;
using URUI.Effect;
using URUI.Effect.Rendering;

namespace URUI.Effect.Performance
{
    /// <summary>
    /// 冻结包的实际更新/提交开销。GPU 时间不可用时保持缺测，不能以 CPU 提交耗时替代。
    /// </summary>
    public sealed class EffectPerformanceTests
    {
        [Serializable]
        sealed class Case
        {
            public string name;
            public int round;
            public int elements;
            public int documents;
            public int particles;
            public long leasedBytes;
            public long liveBytes;
            public long peakLeasedBytes;
            public long peakLiveBytes;
            public long peakIdleBytes;
            public long createdTargets;
            public long reusedTargets;
            public long filterPasses;
            public List<long> drawCalls = new List<long>(1000);
            public List<long> setPassCalls = new List<long>(1000);
            public List<double> synchronizeMs = new List<double>(1000);
            public List<double> renderSubmitWallMs = new List<double>(1000);
            public List<long> allocatedBytes = new List<long>(1000);
        }
        [Serializable]
        sealed class Report
        {
            public string unity;
            public string graphics;
            public string cpu;
            public string colorSpace;
            public int width = 1280;
            public int height = 720;
            public int warmup = 300;
            public int samples = 1000;
            public int rounds = 3;
            public string method = "Stopwatch around parameter update + Document.SynchronizeLayout, and Camera.Render; submission wall time includes any driver waits, not GPU duration. Allocation covers these synchronous calls only.";
            public bool gpuTimeAvailable;
            public bool drawCallsAvailable;
            public bool setPassCallsAvailable;
            public string graphicsCounterScope = "Profiler counters for the previous complete Editor frame, including automatic and explicit camera submissions; not per-node draw counts.";
            public bool allocationCounterAvailable;
            public List<Case> cases = new List<Case>();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureCandidate()
        {
            var report = new Report
            {
                unity = Application.unityVersion,
                graphics = SystemInfo.graphicsDeviceType.ToString(),
                cpu = SystemInfo.processorType,
                colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            var output = Environment.GetEnvironmentVariable("URUI_EFFECT_PERFORMANCE_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../performance"));
            Directory.CreateDirectory(output);
            var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            var allocationProbe = new byte[4096];
            var allocationAfter = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(allocationProbe);
            report.allocationCounterAvailable = allocationAfter - allocationBefore >= 4096;
            var reportPath = Path.Combine(output, "effect-costs.json");
            if (Environment.GetEnvironmentVariable("URUI_EFFECT_RESUME") == "1" && File.Exists(reportPath))
            {
                var previous = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
                Assert.That(previous.unity, Is.EqualTo(report.unity));
                Assert.That(previous.graphics, Is.EqualTo(report.graphics));
                Assert.That(previous.cpu, Is.EqualTo(report.cpu));
                Assert.That(previous.colorSpace, Is.EqualTo(report.colorSpace));
                report.cases.AddRange(previous.cases);
            }
            using var drawRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var passRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            report.drawCallsAvailable = drawRecorder.Valid;
            report.setPassCallsAvailable = passRecorder.Valid;
            var oldCapture = Time.captureDeltaTime;
            var oldVsync = QualitySettings.vSyncCount;
            var oldTarget = Application.targetFrameRate;
            Time.captureDeltaTime = 1f / 60;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var cameraObject = new GameObject("Benchmark camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var authored = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            authored.SetVector("_BandColor", Vector4.zero);
            var particleMaterial = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle"));
            var particleSource = new GameObject("Benchmark pure particle source");
            var system = particleSource.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false;
            system.randomSeed = 1234;
            var main = system.main;
            main.startLifetime = 1;
            main.startSpeed = 20;
            main.startSize = 3;
            main.maxParticles = 256;
            main.playOnAwake = false;
            var emission = system.emission;
            emission.rateOverTime = 64;
            var particleRenderer = particleSource.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = particleMaterial;
            particleRenderer.enabled = false;
            var inputs = new[] { ("no-effect", 100, false, 1, false),
                ("filter-static", 100, false, 1, false), ("filter-dynamic", 100, true, 1, false),
                ("images-1", 1, false, 1, false), ("images-100", 100, false, 1, false),
                ("images-500", 500, false, 1, false), ("dynamic-1", 1, true, 1, false),
                ("dynamic-100", 100, true, 1, false), ("dynamic-500", 500, true, 1, false),
                ("multidocument", 100, true, 2, false), ("particles-1", 1, false, 1, true),
                ("particles-10", 10, false, 1, true), ("particles-30", 30, false, 1, true) };
            try
            {
                foreach (var input in inputs)
                {
                    for (var round = 0; round < 3; round++)
                    {
                        if (report.cases.Exists(value => value.name == input.Item1 && value.round == round))
                        {
                            continue;
                        }
                        var sample = new Case
                        {
                            name = input.Item1,
                            elements = input.Item2,
                            documents = input.Item4,
                            round = round
                        };
                        var host = new GameObject("Frozen benchmark surface");
                        var surface = host.AddComponent<Surface>();
                        surface.Viewport = new Vector2Int(1280, 720);
                        var docs = new List<Document>();
                        var elements = new List<Element>();
                        var painters = new List<ParticlePainter>();
                        var beginning = EffectStatistics.GetTargets();
                        var beforePasses = EffectStatistics.FilterProcessingPasses;
                        try
                        {
                            for (var docIndex = 0; docIndex < input.Item4; docIndex++)
                            {
                                var documentObject = new GameObject("Benchmark document " + docIndex);
                                documentObject.transform.SetParent(host.transform, false);
                                var document = documentObject.AddComponent<Document>();
                                document.Surface = surface;
                                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                                document.Effects.Register(TextureColorEffect.Define("benchmark", authored,
                                    brightnessProperty: "--probe-brightness"));
                                var html = new StringBuilder("<!doctype html><html><head><style>" +
                                    "@property --probe-brightness {syntax:'<number>';inherits:false;initial-value:1;}" +
                                    "body{margin:0;}img,div.particle-paint{position:absolute;width:32px;height:32px;}" +
                                    (input.Item1 == "no-effect" ? "" : "img{--urui-image-effect:benchmark;}") +
                                    (input.Item1.StartsWith("filter-") ? "body{filter:blur(3px) brightness(.8);}" : "") +
                                    ".particle-paint.has-particles{--urui-decorator:shader(particles);}" +
                                    "</style></head><body>");
                                for (var index = 0; index < input.Item2; index++)
                                {
                                    var tag = input.Item5 ? "div" : "img";
                                    html.Append('<').Append(tag).Append(" id=\"i").Append(index)
                                        .Append("\" class=\"particle-paint\" style=\"left:").Append(index % 35 * 34)
                                        .Append("px;top:").Append(index / 35 * 34).Append("px;\"");
                                    if (tag == "img")
                                    {
                                        html.Append(" src=\"white.png\">");
                                    }
                                    else
                                    {
                                        html.Append("></div>");
                                    }
                                }
                                html.Append("</body></html>");
                                document.LoadHtml(html.ToString(), "/benchmark.html");
                                document.SynchronizeLayout();
                                docs.Add(document);
                                for (var index = 0; index < input.Item2; index++)
                                {
                                    var node = document.Query("i" + index)[0];
                                    elements.Add(node);
                                    if (input.Item5)
                                    {
                                        painters.Add(new ParticlePainter(node, particleSource));
                                    }
                                }
                            }
                            for (var frame = 0; frame < 1300; frame++)
                            {
                                yield return null;
                                var before = GC.GetAllocatedBytesForCurrentThread();
                                var started = Stopwatch.GetTimestamp();
                                if (input.Item3)
                                {
                                    var value = frame % 2 == 0 ? .7f : 1f;
                                    foreach (var node in elements)
                                    {
                                        node.Style.SetNumber("--probe-brightness", value);
                                    }
                                    if (input.Item1 == "filter-dynamic")
                                    {
                                        foreach (var document in docs)
                                        {
                                            document.Body.Style.Set("filter", frame % 2 == 0 ? "blur(3px) brightness(.8)" : "blur(3px) brightness(1)");
                                        }
                                    }
                                }
                                foreach (var document in docs)
                                {
                                    document.SynchronizeLayout();
                                }
                                var syncMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                                started = Stopwatch.GetTimestamp();
                                camera.Render();
                                var drawMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                                var observedTargets = EffectStatistics.GetTargets();
                                sample.peakLeasedBytes = Math.Max(sample.peakLeasedBytes, observedTargets.LeasedColorBytes);
                                sample.peakLiveBytes = Math.Max(sample.peakLiveBytes, observedTargets.LiveColorBytes);
                                sample.peakIdleBytes = Math.Max(sample.peakIdleBytes, observedTargets.IdleColorBytes);
                                if (frame >= 300)
                                {
                                    sample.drawCalls.Add(drawRecorder.Valid ? drawRecorder.LastValue : -1);
                                    sample.setPassCalls.Add(passRecorder.Valid ? passRecorder.LastValue : -1);
                                    sample.synchronizeMs.Add(syncMs);
                                    sample.renderSubmitWallMs.Add(drawMs);
                                    sample.allocatedBytes.Add(report.allocationCounterAvailable ? bytes : -1);
                                }
                            }
                            foreach (var painter in painters)
                            {
                                sample.particles += painter.ParticleCount;
                            }
                            var targets = EffectStatistics.GetTargets();
                            sample.createdTargets = targets.CreatedTargets - beginning.CreatedTargets;
                            sample.reusedTargets = targets.Reuses - beginning.Reuses;
                            sample.filterPasses = EffectStatistics.FilterProcessingPasses - beforePasses;
                            sample.leasedBytes = targets.LeasedColorBytes;
                            sample.liveBytes = targets.LiveColorBytes;
                            Assert.That(sample.synchronizeMs.Count, Is.EqualTo(1000));
                            if (input.Item5)
                            {
                                Assert.That(sample.particles, Is.GreaterThan(0));
                            }
                            report.cases.Add(sample);
                            File.WriteAllText(Path.Combine(output, "effect-costs.json"), JsonUtility.ToJson(report, true));
                        }
                        finally
                        {
                            foreach (var painter in painters)
                            {
                                painter.Dispose();
                            }
                            UnityEngine.Object.DestroyImmediate(host);
                        }
                        yield return null;
                    }
                }
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(authored);
                UnityEngine.Object.DestroyImmediate(particleSource);
                UnityEngine.Object.DestroyImmediate(particleMaterial);
                Time.captureDeltaTime = oldCapture;
                QualitySettings.vSyncCount = oldVsync;
                Application.targetFrameRate = oldTarget;
            }
        }

        [Test]
        public void AllocationCounterProbe()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var probe = new byte[4096];
            var after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(probe);
            var output = Path.Combine(Environment.GetEnvironmentVariable("URUI_EFFECT_PERFORMANCE_OUTPUT") ?? Application.temporaryCachePath, "allocation-counter.json");
            File.WriteAllText(output, "{\"probeBytes\":4096,\"observedBytes\":" + (after - before) +
                ",\"available\":" + (after - before >= 4096 ? "true" : "false") + "}");
            Assert.That(after, Is.GreaterThanOrEqualTo(before));
        }
    }
}
