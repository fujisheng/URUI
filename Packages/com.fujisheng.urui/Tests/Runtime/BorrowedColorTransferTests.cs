using NUnit.Framework;
using UnityEngine;

namespace URUI.Tests
{
    /// <summary>
    /// Opaque texel-center sampling must preserve the image's encoded sRGB values.
    /// </summary>
    public sealed class BorrowedColorTransferTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void EncodedOpaqueSourceKeepsItsSrgbCodes(bool borrowed)
        {
            var shader = UnityEngine.Resources.Load<Shader>("URUI.Tests/BorrowedColorTransfer");
            Assert.That(shader && shader.isSupported, Is.True);
            const int width = 256;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false, !borrowed)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var colors = new Color32[width];
            for (var i = 0; i < width; i++)
            {
                colors[i] = new Color32((byte)i, (byte)((i * 37) % 256), (byte)(255 - i), 255);
            }
            texture.SetPixels32(colors);
            texture.Apply();
            Assert.That(texture.isDataSRGB, Is.EqualTo(borrowed));
            var target = new RenderTexture(width, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Assert.That(target.Create(), Is.True);
            var material = new Material(shader);
            var id = borrowed ? URUI.Internal.Resources.BorrowedTextureRegistry.ExternalBit : 0;
            var source = URUI.Internal.Rendering.Shaders.TextureShaderState.SourceInfo(texture, id);
            material.SetFloat("_UruiBorrowedTexture", source.z);
            material.SetFloat("_UruiEncodedTarget", 1);
            material.SetFloat("_UruiSourceSrgb", borrowed ? 1 : 0);
            material.SetFloat("_UruiTextureSampling", 0);
            material.SetVector("_UruiSourceSize", new Vector4(width, 1, 1f / width, 1));
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                Graphics.Blit(texture, target, material);
                RenderTexture.active = target;
                pixels = new Texture2D(width, 1, TextureFormat.RGBAFloat, false, true);
                pixels.ReadPixels(new Rect(0, 0, width, 1), 0, 0);
                pixels.Apply();
                var actual = pixels.GetPixels();
                var mismatches = 0;
                var maximumError = 0f;
                for (var i = 0; i < width; i++)
                {
                    var expected = (Color)colors[i];
                    var error = Mathf.Max(Mathf.Abs(actual[i].r - expected.r),
                        Mathf.Abs(actual[i].g - expected.g), Mathf.Abs(actual[i].b - expected.b));
                    maximumError = Mathf.Max(maximumError, error);
                    if (error > 1e-6f)
                    {
                        mismatches++;
                    }
                }
                TestContext.WriteLine($"borrowed={borrowed} mismatches={mismatches} maxEncodedError={maximumError} code1={actual[1].r}");
                Assert.That(maximumError, Is.LessThanOrEqualTo(1e-6f),
                    "An opaque unscaled image in the encoded-premultiplied domain must preserve its source sRGB values.");
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixels)
                {
                    Object.DestroyImmediate(pixels);
                }
                target.Release();
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
            }
        }
    }
}
