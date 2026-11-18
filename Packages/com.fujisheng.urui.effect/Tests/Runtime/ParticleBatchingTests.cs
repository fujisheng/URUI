using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Profiling;
using URUI.Documents;
using URUI.Hosting;

namespace URUI.Effect.Tests
{
    public sealed class ParticleBatchingTests
    {
        [UnityTest]
        public IEnumerator CompatibleEmittersSubmitByStateGroup() => CompatibleEmitters(4);
        [UnityTest]
        public IEnumerator OneEmitterSubmitsOneStateGroup() => CompatibleEmitters(1);
        [UnityTest]
        public IEnumerator SixteenEmittersSubmitOneStateGroup() => CompatibleEmitters(16);

        [UnityTest]
        public IEnumerator TwoMaterialRunsRetainTheirBoundary() => CompatibleEmitters(4,2);

        [UnityTest] public IEnumerator OneEmitterUpdatesWithoutAllocatingAfterWarmup() => CompatibleEmitters(1,measure:true);
        [UnityTest] public IEnumerator FourEmittersUpdateWithoutAllocatingAfterWarmup() => CompatibleEmitters(4,measure:true);
        [UnityTest] public IEnumerator SixteenEmittersUpdateWithoutAllocatingAfterWarmup() => CompatibleEmitters(16,measure:true);

        IEnumerator CompatibleEmitters(int count,int materials=1,bool measure=false)
        {
            var source = new GameObject("Generic emitters");
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle"));
            var second = materials==2 ? new Material(material) : null;
            var host = new GameObject("Particle batch document");
            var cameraObject = new GameObject("Particle batch camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            ParticlePainter painter = null;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var child = new GameObject("Emitter " + i, typeof(ParticleSystem));
                    child.transform.SetParent(source.transform, false);
                    child.transform.localPosition = new Vector3(i * 3, 0, 0);
                    var system = child.GetComponent<ParticleSystem>();
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = system.main;
                    main.startSize = 1;
                    main.startSpeed = 0;
                    main.startLifetime = 100;
                    main.playOnAwake = false;
                    var emission = system.emission;
                    emission.rateOverTime = 0;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
                    var shape = system.shape;
                    shape.enabled = false;
                    var renderer = child.GetComponent<ParticleSystemRenderer>();
                    renderer.sharedMaterial = second && i>=count/2 ? second : material;
                    renderer.enabled = false;
                }
                var surface = host.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(128, 128);
                surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>#p{position:absolute;left:64px;top:64px;width:20px;height:20px;--urui-decorator:shader(particles);}</style></head><body><div id='p'></div></body></html>", "/particle-batches.html");
                painter = new ParticlePainter(document.Q("p"), source);
                painter.SetManualSimulation(true, 1234);
                painter.Seek(.1f, 1234);
                yield return null;
                document.SynchronizeLayout();
                Assert.That(painter.ParticleCount, Is.EqualTo(count));
                var before = painter.GetStatistics();
                document.SynchronizeLayout();
                var after = painter.GetStatistics();
                Assert.That(after.DrawCalls - before.DrawCalls, Is.EqualTo(materials), "同材质/同裁剪的发射器应作为一组有序几何提交。");
                if(measure)
                {
                    // 显式推进使每个采样都包含实际 BakeMesh、MeshData 获取、原生组装及上传。
                    for(var i=0;i<300;i++){painter.Step(.001f);document.SynchronizeLayout();}
                    using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",8192,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                    recorder.Stop();long empty=0;for(var i=0;i<recorder.Count;i++)empty+=recorder.GetSample(i).Count;
                    Assert.That(empty,Is.Zero);recorder.Reset();recorder.Start();
                    var positive=new byte[4096];System.GC.KeepAlive(positive);recorder.Stop();
                    long calibration=0;for(var i=0;i<recorder.Count;i++)calibration+=recorder.GetSample(i).Count;
                    Assert.That(calibration,Is.GreaterThan(0));recorder.Reset();recorder.Start();
                    for(var i=0;i<32;i++){painter.Step(.001f);document.SynchronizeLayout();}
                    recorder.Stop();long allocations=0;for(var i=0;i<recorder.Count;i++)allocations+=recorder.GetSample(i).Count;
                    Assert.That(recorder.Valid&&!recorder.WrappedAround,Is.True);Assert.That(allocations,Is.Zero,"动态发射器数="+count);
                }
            }
            finally
            {
                painter?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(material);
                if(second)Object.DestroyImmediate(second);
            }
        }
    }
}
