using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;
namespace URUI.Effect.Tests
{
    public sealed class ParticlePainterTests
    {
        [UnityTest]
        public IEnumerator BakedParticlePreservesAdditiveBlendThroughPublicPaintApi() => RenderParticle(0);

        [UnityTest]
        public IEnumerator ParticleDepthProjectsOntoTheUIPlane() => RenderParticle(8);

        [UnityTest]
        public IEnumerator LargeParticleKeepsItsAuthoredSize() => RenderParticle(0, 20, 200);

        static IEnumerator RenderParticle(float depth, float size = 2, int sampleX = 80)
        {
            var root = new GameObject("Particle effect test");
            var source = new GameObject("Authored test emitter", typeof(ParticleSystem));
            var system = source.GetComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.startColor = Color.red;
            main.startSize = size;
            main.startSpeed = 0;
            main.startLifetime = 100;
            main.loop = true;
            var emission = system.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
            var shape = system.shape;
            shape.enabled = false;
            if (depth != 0)
            {
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.position = new Vector3(0, 0, depth);
                shape.scale = Vector3.one * .0001f;
            }
            var material = new Material(Shader.Find("URUI.Effect/Particle"));
            material.SetFloat("_SrcBlend", 5);
            material.SetFloat("_DstBlend", 1);
            var renderer = source.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false;
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            var output = new RenderTexture(Screen.width, Screen.height, 0);
            output.Create();
            camera.targetTexture = output;
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            ParticlePainter painter = null;
            try
            {
                yield return null;
                var document = EffectHtmlFixtureAssets.Create(root);
                document.TextResources = new[] {
                    new TextResource("/URUI.Effect.Tests/Particles.styles.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Particles.styles")), };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/Particles");
                var node = document.Q("particles");
                node.Style.Display = global::URUI.Elements.DisplayStyle.None;
                painter = new ParticlePainter(node, source);
                yield return null;
                yield return null;
                Assert.AreEqual(0, painter.ParticleCount, "首次布局前隐藏的粒子不能访问缺失的布局或绘制。");
                node.Style.Display = global::URUI.Elements.DisplayStyle.Block;
                for (var index = 0; index < 5; index++)
                {
                    yield return null;
                }
                Assert.Greater(painter.ParticleCount, 0);
                camera.Render();
                var prior = RenderTexture.active;
                try
                {
                    RenderTexture.active = output;
                    pixel.ReadPixels(new Rect(sampleX, output.height - 81, 1, 1), 0, 0);
                    pixel.Apply();
                }
                finally
                {
                    RenderTexture.active = prior;
                }
                var color = pixel.GetPixel(0, 0);
                Assert.Greater(color.r, .9f);
                Assert.Greater(color.b, .9f);
                Assert.Less(color.g, .1f);
                node.Style.Display = global::URUI.Elements.DisplayStyle.None;
                yield return null;
                yield return null;
                node.Style.Display = global::URUI.Elements.DisplayStyle.Flex;
                yield return null;
                yield return null;
                Assert.Greater(painter.ParticleCount, 0);
                painter.Dispose();
                painter = null;
                yield return null;
                document.Unload();
            }
            finally
            {
                Object.DestroyImmediate(root);
                painter?.Dispose();
                Object.DestroyImmediate(source);
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                output.Release();
                Object.DestroyImmediate(output);
                Object.DestroyImmediate(pixel);
                Object.DestroyImmediate(material);
            }
        }
    }
}
