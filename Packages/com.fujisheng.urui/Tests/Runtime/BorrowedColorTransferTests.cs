using NUnit.Framework;
using UnityEngine;

namespace URUI.Tests
{
    /// <summary>
    /// 颜色纹理按项目颜色空间解码一次。
    /// </summary>
    public sealed class BorrowedColorTransferTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OpaqueColorSourceMatchesProjectColorSpace(bool borrowed)
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
                    var color = (Color)colors[i];
                    var expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
                    var error = Mathf.Max(Mathf.Abs(actual[i].r - expected.r),
                        Mathf.Abs(actual[i].g - expected.g), Mathf.Abs(actual[i].b - expected.b));
                    maximumError = Mathf.Max(maximumError, error);
                    if (error > .003f)
                    {
                        mismatches++;
                    }
                }
                TestContext.WriteLine($"borrowed={borrowed} mismatches={mismatches} maxColorError={maximumError} code1={actual[1].r}");
                Assert.That(maximumError, Is.LessThanOrEqualTo(.003f),
                    "读取不透明颜色纹理时使用项目颜色空间，避免重复转换。");
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
