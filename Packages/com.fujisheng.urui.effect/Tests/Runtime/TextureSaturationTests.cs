using NUnit.Framework;
using UnityEngine;

namespace URUI.Effect.Tests
{
    public sealed class TextureSaturationTests
    {
        [TestCase(1f)]
        [TestCase(.5f)]
        public void SaturationPreservesOpacityAndInterpolatesOriginalLuminance(float brightness)
        {
            var material = new Material(Shader.Find("URUI.Effect/TextureFilter"));
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var output = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                output.Create();
                source.SetPixel(0, 0, new Color(1, .2f, .1f, .5f));
                source.Apply();
                material.SetFloat("_FilterMode", 2);
                material.SetVector("_BandColor", Vector4.zero);
                material.SetFloat("_Brightness", brightness);
                foreach (var sample in new[] { (0f, new Color(.429f, .429f, .429f, .5f)), (.5f, new Color(.7145f, .3145f, .2645f, .5f)), (1f, new Color(1, .2f, .1f, .5f)) })
                {
                    material.SetFloat("_Saturation", sample.Item1);
                    Graphics.Blit(source, output, material);
                    RenderTexture.active = output;
                    read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                    read.Apply();
                    var actual = read.GetPixel(2, 2);
                    var color = sample.Item2;
                    var expected = new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
                    Assert.That(Vector4.Distance(actual, expected), Is.LessThan(.003f), "饱和度=" + sample.Item1);
                }
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
