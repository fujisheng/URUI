using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Effect.Rendering;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class EffectRuntimeMemoryLeakRegressionTests
    {
        [UnityTest]
        public IEnumerator SurfaceEffectScalarAnimationReusesMaterialWithoutRetiringPerFrame()
        {
            var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter");
            Assert.That(shader, Is.Not.Null);
            var sourceMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var surfaceOutput = new TextureEffectSurface(16, 16);
            var cameraObj = new GameObject("SurfaceEffect leak camera", typeof(Camera));
            cameraObj.tag = "MainCamera";
            var camera = cameraObj.GetComponent<Camera>();
            var root = new GameObject("SurfaceEffect leak host");
            EffectModule module = null;
            IDisposable binding = null;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                surface.Viewport = new Vector2Int(64, 64);
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                module = new EffectModule(document);
                document.ImageResolver = _ => surfaceOutput.Output.Acquire();
                document.LoadHtml(
                    "<!doctype html><html><head><style>body{margin:0;}img{width:16px;height:16px;--urui-image-effect:surface-output;}</style></head><body><img id='fx' src='surface.png'></body></html>",
                    "/surface-scalar-leak.html");
                binding = module.BindSurface(document.Q("fx"), new SurfaceEffectInput(surfaceOutput, Texture2D.whiteTexture, sourceMat, () => 1));

                document.SynchronizeLayout();
                camera.Render();
                yield return null;

                int CountShaderMaterials()
                {
                    int n = 0;
                    foreach (var mat in UnityEngine.Resources.FindObjectsOfTypeAll<Material>())
                    {
                        if (mat && mat.shader == shader)
                        {
                            n++;
                        }
                    }
                    return n;
                }

                int initialCount = CountShaderMaterials();
                long passesBefore = TextureEffectSurface.GetStatistics().ProcessingPasses;
                // Animate a scalar property for 40 frames; must update via MaterialPropertyBlock without cloning/retiring Materials per frame.
                for (int frame = 0; frame < 40; frame++)
                {
                    sourceMat.SetFloat("_Brightness", 0.2f + (frame % 10) * 0.05f);
                    document.SynchronizeLayout();
                    camera.Render();
                    yield return null;
                }
                int afterCount = CountShaderMaterials();
                long passesAfter = TextureEffectSurface.GetStatistics().ProcessingPasses;
                Assert.That(passesAfter, Is.GreaterThanOrEqualTo(passesBefore + 35),
                    "Positive control: scalar animation must still trigger SurfaceEffect processing passes.");
                Assert.That(afterCount, Is.LessThanOrEqualTo(initialCount),
                    "Animating scalar properties on SurfaceEffect must not allocate or retire cloned Materials per frame.");
                document.Unload();
            }
            finally
            {
                binding?.Dispose();
                module?.Dispose();
                surfaceOutput.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObj);
                GpuRetirement.Retire(() => GpuRetirement.DestroyOwnedObject(sourceMat));
                GpuRetirement.Collect();
            }
        }
    }
}
