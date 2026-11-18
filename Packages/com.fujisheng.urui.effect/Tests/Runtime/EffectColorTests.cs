using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class EffectColorTests
    {
        [Serializable] sealed class CaptureSet { public string colorSpace; public string[] cases; }
        internal static Texture2D InputTexture()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[4096];
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var u = x / 63f; var v = y / 63f;
                    var alpha = Mathf.Clamp01((.47f - Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .5f))) * 40);
                    pixels[y * 64 + x] = new Color(.15f + .7f * u, .1f + .4f * v, .8f - .5f * u, alpha * .8f);
                }
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        [UnityTest]
        public IEnumerator EveryToneAndColorModeMatchesReference()
        {
            var host = new GameObject("UI color reference");
            var cameraObject = new GameObject("Reference camera", typeof(Camera)); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .06f, .08f, 0);
            var target = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf); target.Create(); camera.targetTexture = target;
            var read = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            var source = InputTexture();
            var space = QualitySettings.activeColorSpace.ToString();
            var captureRoot = Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../../../candidate"));
            var output = Path.Combine(captureRoot, space);
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "Input.png"), source.EncodeToPNG());
            try
            {
                var surface = host.AddComponent<Surface>(); surface.Viewport = new Vector2Int(256, 256);
                var document = host.AddComponent<Document>(); document.Surface = surface;
                using var module = new EffectModule(document);
                document.Images = new[] { new ImageAsset("input.png", source) };
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                var cases = new List<(string id, string declarations)> { ("Neutral", "") };
                foreach (var mode in (ToneFilter[])Enum.GetValues(typeof(ToneFilter)))
                { cases.Add(("ToneFilter." + mode + ".050", "--urui-fx-tone-filter:" + Kebab(mode.ToString()) + ";")); }
                foreach (var mode in (ColorFilter[])Enum.GetValues(typeof(ColorFilter)))
                { cases.Add(("ColorFilter." + mode + ".050", "--urui-fx-color-filter:" + Kebab(mode.ToString()) + ";")); }
                foreach (var mode in (TargetMode[])Enum.GetValues(typeof(TargetMode)))
                { cases.Add(("TargetMode." + mode + ".050", "--urui-fx-color-filter:replace;--urui-fx-target-mode:" + Kebab(mode.ToString()) + ";--urui-fx-target-range:.5;")); }
                foreach (var item in cases)
                {
                    document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>" +
                        "body{margin:0;}img{position:absolute;left:64px;top:64px;width:128px;height:128px;--urui-effect:ui;" +
                        "--urui-fx-tone-intensity:.5;--urui-fx-color-intensity:.5;--urui-fx-color:#b33366cc;" +
                        item.declarations + "}</style></head><body><img src='input.png'></body></html>", "/colors.html");
                    document.SynchronizeLayout(); camera.Render();
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); read.Apply(); }
                    finally { RenderTexture.active = previous; }
                    File.WriteAllBytes(Path.Combine(output, item.id + ".png"), read.EncodeToPNG());
                    var data = UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Reference/" + space + "/" + item.id);
                    Assert.That(data, Is.Not.Null, "缺少固定原插件参考：" + space + "/" + item.id);
                    var expected = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    try
                    {
                        Assert.That(expected.LoadImage(data.bytes), Is.True);
                        var actual = read.GetPixels(); var original = expected.GetPixels();
                        var maximum = 0f; var sum = 0d;
                        for (var index = 0; index < actual.Length; index++)
                        {
                            for (var channel = 0; channel < 4; channel++)
                            {
                                Assert.That(float.IsFinite(actual[index][channel]), Is.True, item.id);
                                // 原插件 PNG 是有界的 8 位参考；负值/HDR 另由原始浮点用例约束。
                                var error = Mathf.Abs(Mathf.Clamp01(actual[index][channel]) - original[index][channel]);
                                maximum = Mathf.Max(maximum, error); sum += error;
                            }
                        }
                        Assert.That(maximum, Is.LessThanOrEqualTo(4f / 255), space + "/" + item.id + " 最大误差");
                        Assert.That(sum / (actual.Length * 4), Is.LessThanOrEqualTo(.5 / 255), space + "/" + item.id + " 平均误差");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(expected); }
                    yield return null; // Each case is a real submission, allowing its GPU fence to retire.
                }
                File.WriteAllText(Path.Combine(output, "color-captures.json"), JsonUtility.ToJson(new CaptureSet {
                    colorSpace = space, cases = cases.ConvertAll(item => item.id).ToArray() }, true));
                document.Unload(); yield return null;
            }
            finally
            {
                camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(source));
            }
        }

        static string Kebab(string value)
        {
            var result = new System.Text.StringBuilder();
            for (var index = 0; index < value.Length; index++)
            { if (index > 0 && char.IsUpper(value[index])) { result.Append('-'); } result.Append(char.ToLowerInvariant(value[index])); }
            return result.ToString();
        }

        [UnityTest]
        public IEnumerator NegativeHsvAndContrastParametersUseScalarOverrides()
        {
            var host = new GameObject("Signed color parameters");
            var cameraObject = new GameObject("Signed color camera", typeof(Camera)); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create(); camera.targetTexture = target;
            var source = new Texture2D(1, 1, TextureFormat.RGBA32, false, true); source.SetPixel(0, 0, Color.red); source.Apply();
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                var surface = host.AddComponent<Surface>(); surface.Viewport = new Vector2Int(16, 16); surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>(); document.Surface = surface;
                using var module = new EffectModule(document);
                document.Images = new[] { new ImageAsset("red.png", source) };
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                var cases = new[] {
                    ("--urui-fx-color-filter:hsv-modifier;--urui-fx-color-hue-shift:-.25;--urui-fx-color-saturation-shift:0;--urui-fx-color-value-shift:0;--urui-fx-color-alpha:1;", new Color(.5f, 0, 1, 1)),
                    ("--urui-fx-color-filter:hsv-modifier;--urui-fx-color-hue-shift:0;--urui-fx-color-saturation-shift:-.5;--urui-fx-color-value-shift:0;--urui-fx-color-alpha:.5;", new Color(.5f, .25f, .25f, .5f)),
                    ("--urui-fx-color-filter:contrast;--urui-fx-color-contrast-shift:-.5;--urui-fx-color-brightness-shift:-.25;--urui-fx-color-alpha:1;", new Color(.375f, -.125f, -.125f, 1))
                };
                foreach (var item in cases)
                {
                    document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}" +
                        "img{width:16px;height:16px;--urui-effect:ui;--urui-fx-color-intensity:1;" + item.Item1 +
                        "}</style></head><body><img src='red.png'></body></html>", "/signed.html");
                    document.SynchronizeLayout(); camera.Render();
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = target; read.ReadPixels(new Rect(8, 8, 1, 1), 0, 0); read.Apply(); }
                    finally { RenderTexture.active = previous; }
                    var actual = read.GetPixel(0, 0);
                    for (var channel = 0; channel < 4; channel++)
                    { Assert.That(actual[channel], Is.EqualTo(item.Item2[channel]).Within(.004f), item.Item1 + " channel=" + channel); }
                }
                yield return null;
            }
            finally
            {
                camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(source));
            }
        }

        [Test]
        public void TargetHueWrapsAtZero()
        {
            var color = Render(new Color(1, 0, .06f, 1), parameters =>
            {
                parameters.ColorFilter=ColorFilter.Replace; parameters.Color=Color.green; parameters.ColorIntensity=1;
                parameters.TargetMode=TargetMode.Hue; parameters.TargetColor=new Color(1,.06f,0,1);
                parameters.TargetRange=.1f; parameters.TargetSoftness=.5f;
            });
            Assert.That(color.g, Is.GreaterThan(.98f)); Assert.That(color.r, Is.LessThan(.02f));
        }

        [Test]
        public void PremultipliedTransparentHdrRemainsFinite()
        {
            foreach (var alpha in new[] { 0f, .5f, 1f })
            {
                var color = Render(new Color(2, -.1f, .5f, alpha), parameters =>
                { parameters.ColorFilter=ColorFilter.Additive; parameters.Color=new Color(2,1,0,1); parameters.ColorIntensity=.5f; });
                for (var channel = 0; channel < 4; channel++) { Assert.That(float.IsFinite(color[channel]), Is.True); }
                Assert.That(color.r, Is.EqualTo(3 * alpha).Within(.004f));
                Assert.That(color.a, Is.EqualTo(alpha).Within(.002f));
            }
        }

        [Test]
        public void GammaAndLinearAreExplicit()
        {
            var color = Render(new Color(.25f, .5f, .75f, .5f), parameters => { });
            Assert.That(color.r, Is.EqualTo(.125f).Within(.002f), QualitySettings.activeColorSpace.ToString());
            Assert.That(color.g, Is.EqualTo(.25f).Within(.002f)); Assert.That(color.b, Is.EqualTo(.375f).Within(.002f));
        }

        [Test]
        public void GlowPreservesHdrRgbWhileReducingAlpha()
        {
            var color = Render(new Color(2, .1f, .5f, .5f), parameters =>
            {
                parameters.ColorFilter=ColorFilter.Additive; parameters.Color=new Color(2,1,0,1);
                parameters.ColorIntensity=1; parameters.ColorGlow=true;
            });
            Assert.That(color.r, Is.EqualTo(2).Within(.004f)); Assert.That(color.g, Is.EqualTo(.55f).Within(.002f));
            Assert.That(color.b, Is.EqualTo(.25f).Within(.002f)); Assert.That(color.a, Is.EqualTo(0).Within(.002f));
        }

        [TestCase(0, .2f, .3f, .4f, .5f)]
        [TestCase(1, .13f, .1725f, .29f, .4f)]
        [TestCase(2, .275f, .3375f, .5125f, .5f)]
        [TestCase(3, .125f, .2625f, .2875f, .5f)]
        [TestCase(4, .175f, .1875f, .3125f, .4f)]
        [TestCase(5, .14816875f, .174084375f, .272253125f, .5f)]
        [TestCase(6, .23f, .3225f, .49f, .5f)]
        public void EveryGradationColorModePreservesItsAlphaContract(int mode, float r, float g, float b, float a)
        {
            // 源算法调用 Unity 的 Legacy Luminance；6000.3.22f1 在 Linear 下采用另一组常量。
            if (mode == 5 && QualitySettings.activeColorSpace == ColorSpace.Linear)
            { r = .14304181f; g = .17152090f; b = .26456271f; }
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect.Tests/ColorOpsHarness"));
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true); var previous = RenderTexture.active;
            try
            {
                source.SetPixel(0, 0, new Color(.2f, .3f, .4f, .5f)); source.Apply(); target.Create();
                material.SetVector("_Factor", new Vector4(.5f, .25f, .75f, .6f)); material.SetFloat("_Intensity", .5f); material.SetInt("_Mode", mode);
                Graphics.Blit(source, target, material); RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 2, 2), 0, 0); read.Apply(); var actual = read.GetPixel(1, 1);
                Assert.That(actual.r, Is.EqualTo(r).Within(.002f)); Assert.That(actual.g, Is.EqualTo(g).Within(.002f));
                Assert.That(actual.b, Is.EqualTo(b).Within(.002f)); Assert.That(actual.a, Is.EqualTo(a).Within(.002f));
            }
            finally
            {
                RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static Color Render(Color input, Action<EffectParameters> configure)
        {
            var source = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var target = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true); var previous = RenderTexture.active;
            try
            {
                source.SetPixel(0, 0, input); source.Apply(); target.Create(); RenderTexture.active = target; GL.Clear(false, true, Color.clear);
                var parameters=new EffectParameters();configure(parameters);
                using var preview=new EffectPreview(parameters);preview.Render(source,new Rect(0,0,2,2),0,target,Color.clear);
                RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 2, 2), 0, 0); read.Apply(); return read.GetPixel(1, 1);
            }
            finally
            {
                RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(read);
            }
        }
    }
}
