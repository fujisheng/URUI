using NUnit.Framework;
using UnityEngine;

namespace URUI.Effect.Tests
{
    public sealed class TextureFilterColorTests
    {
        [Test]
        public void PostAdditiveColorWeightsOpacityAndPreservesHdr()
        {
            var material = new Material(Shader.Find("URUI.Effect/TextureFilter"));
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var output = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                output.Create();
                material.SetFloat("_FilterMode", 2);
                material.SetVector("_BandColor", Vector4.zero);
                material.SetVector("_AddColor", new Vector4(2, 1, 0, 1));
                material.SetFloat("_AddColorIntensity", .5f);
                foreach (var alpha in new[] { 0f, .5f, 1f })
                {
                    source.SetPixel(0, 0, new Color(.25f, .5f, .75f, alpha));
                    source.Apply();
                    Graphics.Blit(source, output, material);
                    RenderTexture.active = output;
                    read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                    read.Apply();
                    var color = read.GetPixel(2, 2);
                    Assert.That(color.r, Is.EqualTo(.25f + alpha).Within(.002f), "附加颜色须按源透明度加权并保留超过1的颜色。");
                    Assert.That(color.g, Is.EqualTo(.5f + .5f * alpha).Within(.002f));
                    Assert.That(color.b, Is.EqualTo(.75f).Within(.002f));
                    Assert.That(color.a, Is.EqualTo(alpha).Within(.002f));
                }
                material.SetFloat("_AddColorIntensity", 0);
                Graphics.Blit(source, output, material);
                RenderTexture.active = output;
                read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                read.Apply();
                Assert.That(read.GetPixel(2, 2).r, Is.EqualTo(.25f).Within(.002f), "默认关闭时必须保持原滤镜行为。");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(read);
                output.Release();
                Object.DestroyImmediate(output);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(material);
            }
        }
    }
}
