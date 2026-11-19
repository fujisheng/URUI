using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Effect.Elements;
using URUI.Resources;
namespace URUI.Effect.Tests
{
    public sealed partial class ParticlesElementTests
    {
        static GameObject Instance(GameObject source) => UnityEngine.Resources.FindObjectsOfTypeAll<ParticleSystem>().Select(x => x.gameObject).SingleOrDefault(x => x.name == "URUI particles: " + source.name);
        static ParticlesElement Load(EffectRenderFixture fixture, string attributes = "", string css = "")
        {
            fixture.Load("--urui-effect:none;" + css, "<urui-particles id='subject' src='spark' " + attributes + "></urui-particles>");
            fixture.Render();
            return fixture.Document.Q<ParticlesElement>("subject");
        }
        [UnityTest]
        public IEnumerator FailedSourceSwapKeepsOldInstanceAndPlaybackState()
        {
            using var fixture = new EffectRenderFixture();
            var source = Source(out var material);
            var second = Source(out var material2);
            var bad = new GameObject("Invalid particle source", typeof(MeshRenderer));
            fixture.ParticleSources.Add("spark", source);
            fixture.ParticleSources.Add("second", second);
            fixture.ParticleSources.Add("bad", bad);
            try
            {
                var node = Load(fixture);
                yield return null;
                yield return null;
                fixture.Render();
                node.Pause();
                var old = Instance(source);
                Assert.Throws<InvalidOperationException>(() => node.Source = "missing");
                Assert.That(node.CurrentSource, Is.EqualTo("spark"));
                Assert.That(Instance(source), Is.SameAs(old));
                Assert.That(node.PlaybackState, Is.EqualTo(ParticlePlaybackState.Paused));
                Assert.Throws<ArgumentException>(() => node.Source = "bad");
                Assert.That(node.CurrentSource, Is.EqualTo("spark"));
                Assert.That(Instance(source), Is.SameAs(old));
                node.Source = "second";
                Assert.That(node.CurrentSource, Is.EqualTo("second"));
                Assert.That(node.PlaybackState, Is.EqualTo(ParticlePlaybackState.Paused));
                Assert.That(Instance(second), Is.Not.Null);
                node.Play();
                yield return null;
                yield return null;
                fixture.Render();
                Assert.That(node.ParticleCount, Is.GreaterThan(0));
                node.Stop();
                fixture.Render();
                Assert.That(node.ParticleCount, Is.Zero);
                node.Restart();
                yield return null;
                fixture.Render();
                Assert.That(node.PlaybackState, Is.EqualTo(ParticlePlaybackState.Playing));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(bad);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(material2);
            }
        }
        [UnityTest]
        public IEnumerator FragmentReplacementCreatesAndReleasesAndFailedFragmentRollsBack()
        {
            using var fixture = new EffectRenderFixture();
            var source = Source(out var material);
            fixture.ParticleSources.Add("spark", source);
            try
            {
                var node = Load(fixture);
                yield return null;
                fixture.Render();
                var old = Instance(source);
                Assert.Throws<InvalidOperationException>(() => fixture.Document.Body.SetInnerHtml("<urui-particles id='bad' src='missing'></urui-particles>", "/failed-fragment.html"));
                Assert.That(node.IsValid, Is.True);
                Assert.That(node.CurrentSource, Is.EqualTo("spark"));
                fixture.Render();
                fixture.Document.Body.SetInnerHtml("<urui-particles id='next' src='spark' style='width:64px;height:64px;'></urui-particles>", "/particle-fragment.html");
                Assert.That(node.IsValid, Is.False);
                var next = fixture.Document.Q<ParticlesElement>("next");
                Assert.That(next.CurrentSource, Is.EqualTo("spark"));
                fixture.Render();
                for (var i = 0; i < 60 && old; i++)
                {
                    GpuRetirement.Collect();
                    yield return null;
                }
                Assert.That(old == null, Is.True);
                next.Source = "";
                fixture.Render();
                Assert.That(next.CurrentSource, Is.Empty);
                Assert.That(next.ClassListContains("has-particles"), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
        [UnityTest]
        public IEnumerator HiddenAndClippedPoliciesAreSeparate()
        {
            using var fixture = new EffectRenderFixture();
            var source = Source(out var material);
            fixture.ParticleSources.Add("spark", source);
            try
            {
                fixture.Load("--urui-effect:none;", "<div id='clip' style='position:relative;width:256px;height:256px;overflow:hidden;'><urui-particles id='subject' src='spark'></urui-particles></div>");
                fixture.Render();
                var node = fixture.Document.Q<ParticlesElement>("subject");
                yield return null;
                fixture.Render();
                var system = Instance(source).GetComponent<ParticleSystem>();
                var originalClock = system.main.useUnscaledTime;
                node.Style.Set("visibility", "hidden");
                fixture.Render();
                Assert.That(system.isPaused, Is.True);
                node.SetAttribute("data-urui-pause-when-hidden", "false");
                Assert.That(system.isPlaying, Is.True);
                node.SetAttribute("data-urui-restart-on-show", "false");
                yield return null;
                var time = system.time;
                node.Style.Set("visibility", "visible");
                fixture.Render();
                Assert.That(system.time, Is.GreaterThanOrEqualTo(time));
                node.Pause();
                node.Style.Set("display", "none");
                fixture.Render();
                node.Style.Set("display", "block");
                fixture.Render();
                Assert.That(node.PlaybackState, Is.EqualTo(ParticlePlaybackState.Paused));
                Assert.That(system.isPaused, Is.True);
                node.Play();
                var clip = fixture.Document.Q("clip");
                clip.Style.SetPixels("width", 0);
                clip.Style.SetPixels("height", 0);
                fixture.Render();
                var bakes = node.GetStatistics().BodyBakes;
                var before = system.time;
                yield return null;
                yield return null;
                fixture.Render();
                Assert.That(system.isPlaying, Is.True);
                Assert.That(system.time, Is.GreaterThan(before));
                Assert.That(node.GetStatistics().BodyBakes, Is.EqualTo(bakes), "完全裁剪只跳过烘焙，不改变模拟策略");
                Assert.That(system.main.useUnscaledTime, Is.EqualTo(originalClock));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
        [UnityTest]
        public IEnumerator OneBodyAndTrailBakePerEmitterPerFrame()
        {
            using var fixture = new EffectRenderFixture();
            var source = Source(out var material, true);
            fixture.ParticleSources.Add("spark", source);
            try
            {
                var node = Load(fixture);
                yield return null;
                yield return null;
                fixture.Render();
                var before = node.GetStatistics();
                // 验证同帧重复准备不重复烘焙；GPU 提交积压另有八份准入测试。
                for (var i = 0; i < 12; i++)
                {
                    fixture.Document.SynchronizeLayout();
                }
                fixture.Render();
                var after = node.GetStatistics();
                Assert.That(after.BodyBakes, Is.EqualTo(before.BodyBakes));
                Assert.That(after.TrailBakes, Is.EqualTo(before.TrailBakes));
                Assert.That(after.ParticleReads, Is.EqualTo(before.ParticleReads));
                yield return null;
                fixture.Render();
                var next = node.GetStatistics();
                Assert.That(next.BodyBakes - after.BodyBakes, Is.EqualTo(1));
                Assert.That(next.TrailBakes - after.TrailBakes, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
        [UnityTest]
        public IEnumerator TrailsAndBlendPreserved()
        {
            using var fixture = new EffectRenderFixture();
            fixture.Camera.backgroundColor = Color.blue;
            var source = Source(out var material, true);
            var trailMaterial = new Material(material);
            trailMaterial.SetFloat("_SrcBlend", 1);
            trailMaterial.SetFloat("_DstBlend", 10);
            var system = source.GetComponent<ParticleSystem>();
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = 4;
            var trails = system.trails;
            trails.minVertexDistance = .001f;
            source.GetComponent<ParticleSystemRenderer>().sharedMaterials = new[] { material, trailMaterial };
            fixture.ParticleSources.Add("spark", source);
            try
            {
                var node = Load(fixture);
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }
                var pixels = fixture.Render("Particles.Blend");
                var pixel = pixels[(256 - 65) * 256 + 64];
                Assert.That(pixel.r, Is.GreaterThan(.9));
                Assert.That(pixel.b, Is.GreaterThan(.9));
                var renderer = Instance(source).GetComponent<ParticleSystemRenderer>();
                Assert.That(renderer.sharedMaterials[0], Is.SameAs(material));
                Assert.That(renderer.sharedMaterials[1], Is.SameAs(trailMaterial));
                Assert.That(renderer.sharedMaterials[0].GetFloat("_DstBlend"), Is.EqualTo(1));
                Assert.That(renderer.sharedMaterials[1].GetFloat("_DstBlend"), Is.EqualTo(10));
                Assert.That(node.GetStatistics().TrailBakes, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(trailMaterial);
            }
        }
        [UnityTest]
        public IEnumerator ReleaseWaitsForGpuRetirement()
        {
            using var fixture = new EffectRenderFixture();
            var source = Source(out var material);
            var second = Source(out var material2);
            fixture.ParticleSources.Add("spark", source);
            fixture.ParticleSources.Add("second", second);
            try
            {
                var node = Load(fixture);
                yield return null;
                fixture.Render();
                var old = Instance(source);
                node.Source = "second";
                Assert.That(old != null, Is.True, "换源不得同步销毁可能被已录制命令引用的对象");
                fixture.Render();
                for (var i = 0; i < 60 && old; i++)
                {
                    GpuRetirement.Collect();
                    yield return null;
                }
                Assert.That(old == null, Is.True);
                var current = Instance(second);
                fixture.Document.Unload();
                Assert.That(current != null, Is.True);
                for (var i = 0; i < 60 && current; i++)
                {
                    GpuRetirement.Collect();
                    yield return null;
                }
                Assert.That(current == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(material2);
            }
        }
    }
}
