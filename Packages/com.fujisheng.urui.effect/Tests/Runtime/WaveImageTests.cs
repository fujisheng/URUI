using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;
using URUI.Animation;
using URUI.Effect.Rendering;

namespace URUI.Effect.Tests
{
    public sealed class WaveImageTests
    {
        [UnityTest]
        public IEnumerator AuthoredWaveMovesEdgesKeepsCenterAndHonorsClipOpacityAndLease() => Verify(false, false);
        [UnityTest]
        public IEnumerator WaveRetainsCoverageOnTransparentTarget() => Verify(true, false);
        [UnityTest]
        public IEnumerator MaskedWaveCompositesCorrectCoverage() => Verify(false, true);
        sealed class ManualClock : IUIClock
        {
            public double Now
            {
                get;
                set;
            }
            public float Delta => 0;
        }
        static IEnumerator Verify(bool transparentTarget, bool maskLayer)
        {
            var root = new GameObject("Wave document");
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = transparentTarget ? Color.clear : Color.blue;
            var target = new RenderTexture(Screen.width, Screen.height, 0);
            target.Create();
            camera.targetTexture = target;
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.red);
            texture.Apply();
            var mask = new Texture2D(1, 1);
            mask.SetPixel(0, 0, new Color(1, 1, 1, .5f));
            mask.Apply();
            var read = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            Document document = null;
            FeatherMaskPainter layerMask = null;
            var releases = 0;
            var clock = new ManualClock();
            try
            {
                document = EffectHtmlFixtureAssets.Create(root);
                document.Surface.UIClock = clock;
                document.Effects.Register(WaveEffect.Define());
                document.ImageResolver = uri => uri == "/URUI.Effect.Tests/wave.png" ? new ImageLease(texture, () => releases++) : null;
                document.Images = new[] { new ImageAsset("wave-mask.png", mask) };
                document.TextResources = new[] { new TextResource("/URUI.Effect.Tests/Wave.styles.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Wave.styles")) };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/Wave");
                if (maskLayer)
                {
                    layerMask = new FeatherMaskPainter(document.Q("clip"));
                    document.Q("clip").AddToClassList("layered");
                }
                for (var frame = 0; frame < 4; frame++)
                {
                    yield return null;
                }
                Color Pixel(int x, int y)
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                        read.Apply();
                        return read.GetPixel(x, target.height - y - 1);
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                    }
                }
                camera.Render();
                var center = Pixel(70, 70);
                var first = read.GetPixels();
                var linear = target.sRGB ? center.linear : center;
                Assert.That(linear.r, Is.EqualTo(.5f).Within(.025f));
                Assert.That(linear.b, Is.EqualTo(transparentTarget ? 0 : .5f).Within(.025f));
                Assert.That(center.a, Is.EqualTo(transparentTarget ? .5f : 1).Within(.02f), "透明覆盖率必须只应用一次，并正确参与后续图层合成。");
                clock.Now = 1;
                yield return null;
                yield return null;
                camera.Render();
                var secondCenter = Pixel(70, 70);
                var second = read.GetPixels();
                Assert.That(secondCenter.r, Is.EqualTo(center.r).Within(.02f));
                var changes = 0;
                for (var index = 0; index < first.Length; index++)
                {
                    if (Mathf.Abs(first[index].r - second[index].r) > .1f)
                    {
                        changes++;
                    }
                }
                Assert.Greater(changes, 20, "波动必须实际改变绘制边缘，不能只绘制静态四边形。");
                Assert.Less(Pixel(125, 70).r, .02f, "作者父节点的 overflow 裁剪必须生效。");
                document.Q("wave").Destroy();
                document.ReleaseImage("wave.png");
                for (var frame = 0; frame < 20 && releases == 0; frame++)
                {
                    yield return null;
                }
                Assert.AreEqual(1, releases, "GPU 绘制退役后且仅释放一次输入租约。");
            }
            finally
            {
                if (document && document.IsLoaded)
                {
                    document.Unload();
                }
                layerMask?.Dispose();
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(read);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(mask);
            }
        }
    }
}
