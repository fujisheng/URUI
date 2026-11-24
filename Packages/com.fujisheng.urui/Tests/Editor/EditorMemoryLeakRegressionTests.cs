using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Editor.Inspection;
using URUI.Resources;

namespace URUI.Tests.Editor
{
    public sealed class EditorMemoryLeakRegressionTests
    {
        [Test]
        public void DocumentAuthoringRootClearedOnUnloadAndSourceMappingDisablesCleanly()
        {
            var previousCapture = Document.CaptureAuthoringSources;
            try
            {
                RuntimeInspectionBridge.EnableSourceMapping();
                using var preview = RuntimeInspectionBridge.CreatePreview(256, 256);
                var document = preview.Document;
                const string html = "<!doctype html><html><head></head><body><div id='a'><span>hello</span></div></body></html>";

                document.LoadHtml(html, "/authoring-root-leak.html");
                Assert.That(document.AuthoringRoot, Is.Not.Null);

                document.Unload();
                Assert.That(document.AuthoringRoot, Is.Null, "Document.Unload() must release AuthoringRoot.");

                RuntimeInspectionBridge.DisableSourceMapping();
                document.LoadHtml(html, "/authoring-root-disabled.html");
                Assert.That(document.AuthoringRoot, Is.Null, "Disabling source mapping must prevent allocating AuthorNode trees.");
                document.Unload();
            }
            finally
            {
                Document.CaptureAuthoringSources = previousCapture;
                GpuRetirement.Collect();
            }
        }

        [Test]
        public void OutOfOrderOverrideImageForInspectionRestoresBaseResolverWithoutChaining()
        {
            var texA = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            var texB = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                using var preview = RuntimeInspectionBridge.CreatePreview(256, 256);
                var document = preview.Document;
                Func<string, ImageLease> baseResolver = _ => new ImageLease(Texture2D.whiteTexture);
                document.ImageResolver = baseResolver;
                document.LoadHtml(
                    "<!doctype html><html><head></head><body><img id='a' src='a.png'><img id='b' src='b.png'></body></html>",
                    "/override-order.html");

                var leaseA = document.OverrideImageForInspection(document.Q("a"), texA);
                var leaseB = document.OverrideImageForInspection(document.Q("b"), texB);

                // Dispose out of LIFO order: A first, then B.
                leaseA.Dispose();
                leaseB.Dispose();

                Assert.That(document.ImageResolver, Is.SameAs(baseResolver),
                    "Out-of-order inspection image override disposal must restore the exact base resolver without leaking chained closures.");
                document.Unload();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texA);
                UnityEngine.Object.DestroyImmediate(texB);
                GpuRetirement.Collect();
            }
        }

        [Test]
        public void EffectPreviewRendererMarksOwnedMeshHideAndDontSave()
        {
            var previewType = typeof(URUI.Rendering.Effects.EffectPreviewRenderer);
            var meshField = previewType.GetField("mesh", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(meshField, Is.Not.Null);
            var factory = new DummyFactory();
            var def = new URUI.Rendering.Effects.EffectDefinition("dummy-preview", URUI.Rendering.Effects.EffectKind.Image, factory, "dummy:v1", "test");
            var rt = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            try
            {
                using (var renderer = new URUI.Rendering.Effects.EffectPreviewRenderer(def))
                {
                    renderer.Render(Texture2D.whiteTexture, new Rect(0, 0, 16, 16), 0, rt, Color.clear);
                    var mesh = (Mesh)meshField.GetValue(renderer);
                    Assert.That(mesh, Is.Not.Null);
                    Assert.That(mesh.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
                }
                URUI.Internal.Resources.ImagePinRetirement.TryCompleteImmediateRetirements(() =>
                {
                    var active = RenderTexture.active;
                    var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    try
                    {
                        RenderTexture.active = rt;
                        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        pixel.Apply();
                    }
                    finally
                    {
                        RenderTexture.active = active;
                        UnityEngine.Object.DestroyImmediate(pixel);
                    }
                });
            }
            finally
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                GpuRetirement.Collect();
            }
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator RepeatedGpuRetirementsDrainPendingQueueWithoutStalling()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("Requires real GPU fence.");
            }
            var released = 0;
            var rt = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            var previous = RenderTexture.active;
            rt.Create();
            try
            {
                for (var round = 0; round < 6; ++round)
                {
                    var target = round + 1;
                    GpuRetirement.Retire(() => released++);
                    Graphics.Blit(Texture2D.whiteTexture, rt);
                    UnityEngine.Rendering.AsyncGPUReadback.Request(rt).WaitForCompletion();
                    for (var tick = 0; tick < 60 && released < target; ++tick)
                    {
                        yield return null;
                        GpuRetirement.Collect();
                    }
                    Assert.That(released, Is.EqualTo(target), $"GPU retirement fence stalled on round {round}.");
                }
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                GpuRetirement.Collect();
            }
        }

        sealed class DummyFactory : URUI.Rendering.Effects.IEffectFactory
        {
            public URUI.Rendering.Effects.IElementEffect Create(URUI.Rendering.Effects.EffectContext context) => new DummyEffect();
        }

        sealed class DummyEffect : URUI.Rendering.Effects.IElementEffect
        {
            public void Prepare(URUI.Rendering.Effects.EffectFrame frame) { }
            public void Record(URUI.Rendering.Effects.EffectRenderContext context) { }
            public void Dispose() { }
        }
    }
}
