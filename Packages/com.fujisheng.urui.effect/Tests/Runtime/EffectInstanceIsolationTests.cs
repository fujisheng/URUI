using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;
using URUI.Effect.Rendering;
namespace URUI.Effect.Tests
{
    public sealed class EffectInstanceIsolationTests
    {
        [UnityTest]
        public IEnumerator ExplicitParameterBindingsFeedTheFrozenProgramRows()
        {
            using var fixture = new EffectRenderFixture();
            var authored = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            authored.SetFloat("_FilterMode", 2);
            authored.SetTexture("_PatternTex", fixture.Pattern);
            authored.SetFloat("_ColorBlend", 1);
            authored.SetVector("_BandColor", new Vector4(1, 0, 0, 1));
            authored.SetFloat("_Width", .5f);
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.Define("generic-bound", authored, "--test-rate"));
                fixture.Load("--urui-effect:none;--urui-image-effect:generic-bound;} @property --test-rate {syntax:'<number>';inherits:false;initial-value:0;} #subject{");
                var subject = fixture.Document.Q("subject");
                subject.Style.SetNumber("--test-rate", .2f);
                var expected = fixture.Render();
                subject.Style.SetNumber("--test-rate", .8f);
                var other = fixture.Render();
                float difference = 0;
                for (var i = 0; i < expected.Length; i++)
                {
                    difference = Mathf.Max(difference, Mathf.Abs(expected[i].g - other[i].g));
                }
                Assert.That(difference, Is.GreaterThan(.02f));
                var values = new System.Collections.Generic.Dictionary<string, global::URUI.Styles.StyleOverride> { { "--test-rate", global::URUI.Styles.StyleOverride.Number(.2f) } };
                using var binding = fixture.Document.Effects.BindParameters(subject, layout => layout.WithStyle(layout.Style.WithOverrides(values, global::URUI.Styles.StyleValueOrigin.Author)));
                var actual = fixture.Render();
                float max = 0;
                for (var i = 0; i < actual.Length; i++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        max = Mathf.Max(max, Mathf.Abs(actual[i][c] - expected[i][c]));
                    }
                }
                Assert.That(max, Is.LessThan(.001f), "显式参数绑定必须覆盖原生 CSS 字段，而不是静默丢失。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(authored);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator ImageParametersShareAnOrderedBatchAndDoNotUploadGeometry()
        {
            using var fixture = new EffectRenderFixture();
            var authored = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.Define("generic-program", authored, "--test-rate"));
                fixture.Load("--urui-effect:none;--urui-image-effect:generic-program;} @property --test-rate {syntax:'<number>';inherits:false;initial-value:0;} img{",
                    "<img id='subject' src='input.png' style='left:0;width:32px;'>" +
                    "<img src='input.png' style='left:40px;width:32px;--test-rate:.2;'>" +
                    "<img src='input.png' style='left:80px;width:32px;--test-rate:.4;'>" +
                    "<img src='input.png' style='left:120px;width:32px;--test-rate:.6;'>", "img");
                fixture.Render();
                object renderer = null;
                foreach (var component in fixture.Surface.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component.GetType().FullName == "URUI.Internal.Hosting.RenderHost")
                    {
                        renderer = component;
                    }
                }
                Assert.That(renderer, Is.Not.Null);
                object Read(string name) => renderer.GetType().GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(renderer);
                Assert.That(Read("RecordedGeometryDraws"), Is.EqualTo(1));
                var uploaded = Read("GeometryUploadBytes");
                fixture.Document.Q("subject").Style.SetNumber("--test-rate", .75f);
                fixture.Render();
                Assert.That(Read("GeometryUploadBytes"), Is.EqualTo(uploaded));
                Assert.That(Read("RecordedGeometryDraws"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(authored);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator SamePresetDifferentSizesAndRatesKeepIndependentPixelsAndOpacity()
        {
            var host = new GameObject("Independent image effects");
            var cameraObject = new GameObject("Image effect camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var authored = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            var pattern = new Texture2D(64, 2, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[128];
            for (int i = 0; i < 128; i++)
            {
                colors[i] = new Color(1, 1, 1, (i % 64) / 63f);
            }
            pattern.SetPixels(colors);
            pattern.Apply();
            authored.SetTexture("_PatternTex", pattern);
            authored.SetFloat("_FilterMode", 2);
            authored.SetFloat("_ColorBlend", 1);
            authored.SetVector("_BandColor", new Vector4(1, 0, 0, 1));
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                document.Effects.Register(TextureColorEffect.Define("probe-texture", authored, "--image-rate"));
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/TextureInstances");
                for (int i = 0; i < 4; i++)
                {
                    yield return null;
                }
                camera.Render();
                Color Sample(int x, int y)
                {
                    var old = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(x, target.height - y - 1, 1, 1), 0, 0);
                        read.Apply();
                        return read.GetPixel(0, 0);
                    }
                    finally
                    {
                        RenderTexture.active = old;
                    }
                }
                var a = Sample(44, 50);
                var b = Sample(228, 80);
                Assert.That(a.r, Is.EqualTo(.5f).Within(.025f));
                Assert.That(b.r, Is.EqualTo(.5f).Within(.025f));
                Assert.That(a.g, Is.LessThan(.4f));
                Assert.That(b.g, Is.GreaterThan(.47f), "共享程序不能共用节点的 Rate 或尺寸参数");
                Assert.That(a.a, Is.EqualTo(.5f).Within(.025f));
                Assert.That(b.a, Is.EqualTo(.5f).Within(.025f));
                document.Unload();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() =>
                {
                    UnityEngine.Object.Destroy(authored);
                    UnityEngine.Object.Destroy(pattern);
                });
            }
        }
    }
}
