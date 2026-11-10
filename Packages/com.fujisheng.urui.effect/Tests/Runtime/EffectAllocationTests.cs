using System;
using System.Collections;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
using URUI.Documents;
using URUI.Hosting;
using URUI.Effect.Rendering;

namespace URUI.Effect.Tests
{
    public sealed class EffectAllocationTests
    {
        sealed class Clock : IUiClock { public double Now { get; set; } = 1; public float Delta => .01f; }
        static object probe;
        static long Allocations(Action action)
        {
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            action(); recorder.Stop();
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            long count = 0;
            for (var i = 0; i < recorder.Count; ++i) { count += recorder.GetSample(i).Count; }
            return count;
        }
        static void ValidateChannel()
        {
            Assert.That(Allocations(() => { }), Is.Zero);
            Assert.That(Allocations(() => { probe = new byte[4096]; GC.KeepAlive(probe); }), Is.GreaterThan(0));
            probe = null;
        }

        [Test]
        public void ChangingSchedulerInputsDoNotAllocateUpdateObjects()
        {
            var scheduler = new EffectScheduler();
            var source = new object();
            scheduler.TryPrepare(new EffectInput(source, 100, Vector2.one, 0, "allocation", RenderTextureFormat.ARGBHalf), out var warm);
            warm.Submitted();
            ValidateChannel();
            Action refresh = () =>
            {
                for (ulong i = 0; i < 10; ++i)
                {
                    scheduler.TryPrepare(new EffectInput(source, i, Vector2.one, 0, "allocation", RenderTextureFormat.ARGBHalf), out var update);
                    update.Submitted();
                }
            };
            refresh();
            Assert.That(Allocations(refresh), Is.Zero);
        }

        [UnityTest]
        public IEnumerator TimeDrivenSurfaceUpdatesReuseMaterialAndSubmissionCallbacks() => VerifySurface(false);

        [UnityTest]
        public IEnumerator ChangingMaterialPropertiesReuseGpuRetirementRecords() => VerifySurface(true);

        static IEnumerator VerifySurface(bool changeMaterial)
        {
            var cameraObject = new GameObject("Effect allocation camera", typeof(Camera)); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            var root = new GameObject("Effect allocation document");
            var surface = root.AddComponent<Surface>(); surface.Input.UseUnityInput = false; surface.Viewport = new Vector2Int(128,128);
            var clock = new Clock(); surface.UiClock = clock;
            var document = root.AddComponent<Document>(); document.Surface = surface;
            using var output = new TextureEffectSurface(16,16);
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            EffectModule module = null;
            IDisposable binding = null;
            try
            {
                module = new EffectModule(document);
                document.ImageResolver = uri => output.Output.Acquire();
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{width:16px;height:16px;--urui-image-effect:surface-output;}</style></head><body><img id='effect' src='surface.png'></body></html>", "/surface-allocations.html");
                binding = module.BindSurface(document.Q("effect"), new SurfaceEffectInput(output, Texture2D.whiteTexture, material, () => 1, usesTime:true));
                var frameIndex = 0;
                Action refresh = () => { clock.Now += .01; if (changeMaterial) { material.SetFloat("_Brightness", frameIndex % 2 == 0 ? .5f : .75f); } document.SynchronizeLayout(); };
                for (frameIndex = 0; frameIndex < 24; ++frameIndex) { refresh(); camera.Render(); document.SynchronizeLayout(); yield return null; }
                ValidateChannel();
                long allocations = 0;
                var samples = new long[10];
                var noise = new long[10];
                Action empty = () => { };
                var passes = TextureEffectSurface.GetStatistics().ProcessingPasses;
                for (frameIndex = 0; frameIndex < 10; ++frameIndex)
                {
                    noise[frameIndex] = Allocations(empty);
                    samples[frameIndex] = Allocations(refresh);
                    allocations += samples[frameIndex];
                    camera.Render(); document.SynchronizeLayout(); yield return null;
                }
                Assert.That(allocations, Is.Zero, "samples=" + string.Join(",",samples) + ";empty=" + string.Join(",",noise));
                Assert.That(noise, Is.All.Zero, "空采样存在噪声，不能用于零分配验收。");
                Assert.That(TextureEffectSurface.GetStatistics().ProcessingPasses, Is.EqualTo(passes + 10));
            }
            finally
            {
                binding?.Dispose(); module?.Dispose();
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
                URUI.Resources.GpuRetirement.Retire(() => URUI.Resources.GpuRetirement.DestroyOwnedObject(material));
            }
        }
    }
}
