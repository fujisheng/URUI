using NUnit.Framework;
using UnityEngine;

namespace URUI.Effect.Tests
{
    public sealed class TextureGlowTests
    {
        [Test]
        public void AdditiveGlowKeepsHdrColorAndReducesCoverage()
        {
            var material = new Material(Shader.Find("URUI.Effect/TextureFilter"));
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                Assert.IsTrue(material.HasProperty("_AddColorGlow"), "颜色发光需要独立于 RGB 的覆盖率参数。");
                target.Create();
                material.SetFloat("_FilterMode", 2);
                material.SetVector("_BandColor", Vector4.zero);
                material.SetVector("_AddColor", new Vector4(2, 1, 0, 1));
                material.SetFloat("_AddColorIntensity", .515f);
                foreach (var intensity in new[] { .515f, 1f })
                {
                    foreach (var glow in new[] { 0f, 1f })
                    {
                        foreach (var alpha in new[] { 0f, .5f, 1f })
                        {
                            material.SetFloat("_AddColorIntensity", intensity);
                            material.SetFloat("_AddColorGlow", glow);
                            source.SetPixel(0, 0, new Color(.25f, .5f, .75f, alpha));
                            source.Apply();
                            Graphics.Blit(source, target, material);
                            RenderTexture.active = target;
                            read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                            read.Apply();
                            var actual = read.GetPixel(2, 2);
                            var coverage = Mathf.Max(1f / 4096, 1 - glow * intensity);
                            var weight = Mathf.Lerp(alpha, 1, glow);
                            Assert.That(actual.r, Is.EqualTo((.25f + 2 * weight * intensity) / coverage).Within(.003f));
                            Assert.That(actual.g, Is.EqualTo((.5f + weight * intensity) / coverage).Within(.003f));
                            Assert.That(actual.a, Is.EqualTo(alpha * coverage).Within(.003f));
                        }
                    }
                }
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(read);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(material);
            }
        }
    }
}
