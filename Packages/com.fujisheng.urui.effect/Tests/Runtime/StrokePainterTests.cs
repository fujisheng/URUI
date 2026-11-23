using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class StrokePainterTests
    {
        [UnityTest]
        public IEnumerator StrokesRespectAuthoredWidthClipOpacityAndDisposal()
        {
            var type = typeof(ParticlePainter).Assembly.GetType("URUI.Effect.StrokePainter");
            Assert.NotNull(type, "缺少不依赖项目节点或DOM线条样式的通用连线绘制器。");
            var root = new GameObject("Stroke test");
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            var output = new RenderTexture(Screen.width, Screen.height, 0);
            output.Create();
            camera.targetTexture = output;
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            IDisposable painter = null;
            try
            {
                yield return null;
                var document = EffectHtmlFixtureAssets.Create(root);
                document.TextResources = new[] { new TextResource("/URUI.Effect.Tests/Strokes.styles.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Strokes.styles")) };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/Strokes");
                var node = document.Q("strokes");
                painter = (IDisposable)Activator.CreateInstance(type, node);
                type.GetMethod("SetSegments").Invoke(painter, new object[] { new[] { new Vector2(0, 40), new Vector2(80, 40) }, new[] { Color.green } });
                yield return null;
                yield return null;
                camera.Render();
                Color Sample(int x, int y)
                {
                    var prior = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = output;
                        pixel.ReadPixels(new Rect(x, output.height - y - 1, 1, 1), 0, 0);
                        pixel.Apply();
                        return pixel.GetPixel(0, 0);
                    }
                    finally
                    {
                        RenderTexture.active = prior;
                    }
                }
                var inside = Sample(50, 60);
                if (output.sRGB)
                {
                    inside = inside.linear;
                }
                Assert.That(inside.g, Is.EqualTo(.5f).Within(.025f));
                Assert.That(inside.b, Is.EqualTo(.5f).Within(.025f));
                Assert.Less(inside.r, .05f);
                Assert.Less(Sample(85, 60).g, .05f, "必须继承父节点裁剪。");
                Assert.Less(Sample(50, 66).g, .05f, "必须使用外置样式的8px线宽。");
                node.Destroy();
                painter.Dispose();
                painter = null;
                yield return null;
                yield return null;
                camera.Render();
                Assert.Less(Sample(50, 60).g, .05f, "释放绘制器后不能残留连线。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                painter?.Dispose();
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(pixel);
            }
        }
    }
}
