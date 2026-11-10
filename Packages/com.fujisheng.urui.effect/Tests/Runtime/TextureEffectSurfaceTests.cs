using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace URUI.Effect.Tests
{
    public sealed class TextureEffectSurfaceTests
    {
        [UnityTest]
        public IEnumerator SurfaceMatchesUnityBlitOrientationAndKeepsLeasedOutputAlive()
        {
            var input = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            input.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.yellow }); input.Apply();
            var material = new Material(Shader.Find("Hidden/URUI.Effect.Tests/Copy"));
            var expected = new RenderTexture(8, 8, 0); expected.Create();
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var surface = new TextureEffectSurface(8, 8);
            var lease = surface.Output.Acquire(); var actual = surface.Texture;
            try
            {
                Graphics.Blit(input, expected); surface.Render(input, material, new Vector2(100, 100));
                Color Sample(RenderTexture texture, int x, int y)
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = texture; pixel.ReadPixels(new Rect(x, y, 1, 1), 0, 0); pixel.Apply();
                        var value=pixel.GetPixel(0,0);
                        // 比较最终采样的线性颜色，不把不同输出格式的存储编码当作方向差异。
                        return texture.sRGB?value.linear:value;
                    }
                    finally { RenderTexture.active = previous; }
                }
                foreach (var point in new[] { new Vector2Int(1, 1), new Vector2Int(6, 1), new Vector2Int(1, 6), new Vector2Int(6, 6) })
                {
                    var wanted = Sample(expected, point.x, point.y); var observed = Sample(actual, point.x, point.y);
                    Assert.That(observed.r, Is.EqualTo(wanted.r).Within(.02f), "red at " + point);
                    Assert.That(observed.g, Is.EqualTo(wanted.g).Within(.02f), "green at " + point);
                    Assert.That(observed.b, Is.EqualTo(wanted.b).Within(.02f), "blue at " + point);
                }
                surface.Dispose(); Assert.IsTrue(actual.IsCreated());
                lease.Dispose(); lease = null;
                for (var frame = 0; frame < 20 && actual; frame++) { yield return null; }
                Assert.IsFalse(actual, "生产者和最后租约均释放且 GPU 完成后退役纹理。");
            }
            finally
            {
                surface.Dispose(); lease?.Dispose(); expected.Release();
                Object.DestroyImmediate(expected); Object.DestroyImmediate(input); Object.DestroyImmediate(pixel); Object.DestroyImmediate(material);
            }
        }
    }
}
