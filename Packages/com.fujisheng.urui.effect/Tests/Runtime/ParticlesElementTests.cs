using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace URUI.Effect.Tests
{
    public sealed partial class ParticlesElementTests
    {
        internal static GameObject Source(out Material material, bool trails = false)
        {
            var source = new GameObject("Declarative test emitter " + System.Guid.NewGuid().ToString("N"), typeof(ParticleSystem));
            var system = source.GetComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.startColor = Color.red;
            main.startSize = 12;
            main.startSpeed = 0;
            main.startLifetime = 20;
            main.loop = true;
            var emission = system.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
            var shape = system.shape;
            shape.enabled = false;
            var trail = system.trails;
            trail.enabled = trails;
            material = new Material(Shader.Find("URUI.Effect/Particle"));
            material.SetFloat("_SrcBlend", 5);
            material.SetFloat("_DstBlend", 1);
            var renderer = source.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterials = trails ? new[] { material, material } : new[] { material };
            renderer.enabled = false;
            return source;
        }
        [UnityTest]
        public IEnumerator HtmlCreatesParticlesWithoutGameCode()
        {
            using var fixture = new EffectRenderFixture();
            var source = Source(out var material);
            fixture.ParticleSources.Add("spark", source);
            try
            {
                fixture.Load("--urui-effect:none;", "<urui-particles id='subject' src='spark'></urui-particles>");
                fixture.Render();
                var node = fixture.Document.Q("subject");
                Assert.That(node.GetType().FullName, Is.EqualTo("URUI.Effect.Elements.ParticlesElement"));
                Assert.That(node.ClassListContains("has-particles"), Is.True);
                yield return null;
                yield return null;
                fixture.Render();
                Assert.That((int)node.GetType().GetProperty("ParticleCount").GetValue(node), Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(material);
            }
        }
    }
}
