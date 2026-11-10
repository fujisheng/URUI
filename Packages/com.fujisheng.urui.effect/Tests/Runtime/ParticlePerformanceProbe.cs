using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Effect.Elements;
using URUI.Effect.Tests;
namespace URUI.Effect.Performance
{
    public sealed class ParticlePerformanceProbe
    {
        [Serializable] sealed class Sample {public double simulate,read,bake,trails,record;}
        [Serializable] sealed class Distribution {public double p50,p95,p99;}
        [Serializable] sealed class Report
        {
            public string colorSpace,device,graphicsApi,simulationScope;
            public int warmup=60,samples=300,particles=256;
            public long bodyBakes,trailBakes,particleReads;
            public long managedAllocationBytes=-1;
            public string allocationStatus="not measured in this stage probe; E00 4096-byte calibration returned 0; no zero-GC claim";
            public string gpuTiming="unavailable; CPU record duration is not GPU duration";
            public Distribution simulate,read,bake,trails,record;
            public Sample[] raw;
        }
        static Distribution Summarize(double[] values)
        {Array.Sort(values);double At(double q)=>values[(int)Math.Ceiling(q*(values.Length-1))];return new(){p50=At(.5),p95=At(.95),p99=At(.99)};}
        [UnityTest]
        public IEnumerator RecordParticleStageCostsInAnIsolatedEditor()
        {
            using var fixture=new EffectRenderFixture();var source=ParticlesElementTests.Source(out var material,true);
            var system=source.GetComponent<ParticleSystem>();var main=system.main;main.maxParticles=256;main.startLifetime=100;
            var emission=system.emission;emission.SetBursts(new[]{new ParticleSystem.Burst(0,256)});
            fixture.ParticleSources.Add("spark",source);
            try
            {
                fixture.Load("--urui-effect:none;","<urui-particles id='subject' src='spark'></urui-particles>");fixture.Render();var node=fixture.Document.Q<ParticlesElement>("subject");
                // 独立的同配置参考发射器只用于显式 Simulate 计时；生产节点仍使用 Unity 自动模拟。
                system.Pause(false);system.Emit(256);
                for(var i=0;i<60;i++){yield return null;fixture.Document.SynchronizeLayout();fixture.Camera.Render();}
                var report=new Report{colorSpace=QualitySettings.activeColorSpace.ToString(),device=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),simulationScope="separate matching emitter: paused, explicit 1/60s Simulate; UI emitter remains automatic; each sample spans an automatic update plus explicit verification sync; record cost includes both; stage samples, not an end-to-end performance acceptance",raw=new Sample[300]};
                var initial=node.GetStatistics();var previousSample=initial;
                for(var i=0;i<report.samples;i++)
                {
                    yield return null;
                    var before=previousSample;var start=Stopwatch.GetTimestamp();system.Simulate(1f/60,false,false,false);var simulation=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                    fixture.Document.SynchronizeLayout();fixture.Camera.Render();var after=node.GetStatistics();previousSample=after;
                    report.raw[i]=new(){simulate=simulation,read=after.ReadMilliseconds-before.ReadMilliseconds,bake=after.BodyBakeMilliseconds-before.BodyBakeMilliseconds,trails=after.TrailBakeMilliseconds-before.TrailBakeMilliseconds,record=after.RecordMilliseconds-before.RecordMilliseconds};
                }
                var last=node.GetStatistics();report.bodyBakes=last.BodyBakes-initial.BodyBakes;report.trailBakes=last.TrailBakes-initial.TrailBakes;report.particleReads=last.ParticleReads-initial.ParticleReads;
                Assert.That(node.ParticleCount,Is.EqualTo(256));Assert.That(report.bodyBakes,Is.EqualTo(report.samples));Assert.That(report.trailBakes,Is.EqualTo(report.samples));Assert.That(report.particleReads,Is.EqualTo(report.samples));
                report.simulate=Summarize(report.raw.Select(x=>x.simulate).ToArray());report.read=Summarize(report.raw.Select(x=>x.read).ToArray());report.bake=Summarize(report.raw.Select(x=>x.bake).ToArray());report.trails=Summarize(report.raw.Select(x=>x.trails).ToArray());report.record=Summarize(report.raw.Select(x=>x.record).ToArray());
                Assert.That(report.read.p95,Is.GreaterThan(0));Assert.That(report.bake.p95,Is.GreaterThan(0));Assert.That(report.trails.p95,Is.GreaterThan(0));
                File.WriteAllText(Path.Combine(fixture.Output,"particle-performance.json"),JsonUtility.ToJson(report,true));
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
