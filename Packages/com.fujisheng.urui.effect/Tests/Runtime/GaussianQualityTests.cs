using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Effect.Rendering;
namespace URUI.Effect.Tests
{
    public sealed class GaussianQualityTests
    {
        [UnityTest]
        public IEnumerator WideGaussianAndShadowHaveContinuousSymmetricUnitEnergyFootprints()
        {
            var host = new GameObject("Gaussian impulse");
            var cam = new GameObject("Gaussian camera", typeof(Camera));
            cam.tag = "MainCamera";
            var camera = cam.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/GaussianThinLine");
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                }
                camera.Render();
                float Alpha(int x)
                {
                    var old = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(x, target.height - 65, 1, 1), 0, 0);
                        read.Apply();
                        return read.GetPixel(0, 0).a;
                    }
                    finally
                    {
                        RenderTexture.active = old;
                    }
                }
                var center = Alpha(64);
                Assert.That(center, Is.EqualTo(.0499f).Within(.007f));
                for (int i = 1; i <= 12; i++)
                {
                    var a = Alpha(64 + i);
                    Assert.That(a, Is.GreaterThan(center * .25f), "Gaussian足迹不能在采样点之间出现空洞：" + i);
                    Assert.That(a, Is.EqualTo(Alpha(64 - i)).Within(.002f));
                    Assert.That(Alpha(180 + i), Is.GreaterThan(center * .25f), "drop-shadow必须复用连续Gaussian核。");
                }
                float energy = 0;
                for (int i = -32; i <= 32; i++)
                {
                    energy += Alpha(64 + i);
                }
                Assert.That(energy, Is.EqualTo(1).Within(.035f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                camera.targetTexture = null;
                Object.DestroyImmediate(cam);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }
    }
}
