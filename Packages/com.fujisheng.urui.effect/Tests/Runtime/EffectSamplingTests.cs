using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Rendering.Effects;
using URUI.Resources;
using URUI.Styles;

namespace URUI.Effect.Tests
{
    public sealed class EffectSamplingTests
    {
        [Serializable]
        sealed class SamplingCapture
        {
            public string id;
            public int nominalKernelSamples;
            public int leasedTargets;
            public long processingPasses;
        }
        [Serializable]
        sealed class CaptureSet
        {
            public string colorSpace;
            public List<SamplingCapture> cases = new();
        }
        sealed class Probe : IEffectFactory, IElementEffect
        {
            internal EffectFrame Frame;
            public IElementEffect Create(EffectContext context)
            {
                return this;
            }
            public void Prepare(EffectFrame frame)
            {
                Frame = frame;
            }
            public void Record(EffectRenderContext context)
            {
                context.DrawOriginal();
            }
            public void Dispose()
            {
            }
        }

        [Test]
        public void SamplingScaleClampsForRenderingAndRetainsTheAuthoredValue()
        {
            using var fixture = new EffectRenderFixture();
            fixture.Load("--urui-fx-sampling-filter:blur-fast;");
            fixture.Document.SynchronizeLayout();
            var element = fixture.Document.Q("subject");
            var controller = fixture.Module.GetController(element);
            foreach (var value in new[] { -1f, .005f, 1f, 200f })
            {
                element.Style.SetNumber("--urui-fx-sampling-scale", value);
                fixture.Document.SynchronizeLayout();
                Assert.That(controller.CreateParameters().SamplingScale, Is.EqualTo(Mathf.Clamp(value, .01f, 100)));
                Assert.That(fixture.Document.Styles.GetComputed(element).GetNumber("--urui-fx-sampling-scale"), Is.EqualTo(value));
            }
        }

        [UnityTest]
        // 首次冷编译三档源采样核；不改变运行时画质或像素预算。
        [Timeout(600000)]
        public IEnumerator AllSamplingModesMatchReference()
        {
            var host = new GameObject("Sampling reference");
            var cameraObject = new GameObject("Sampling reference camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .06f, .08f, 0);
            var target = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var source = EffectColorTests.InputTexture();
            var read = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            var space = QualitySettings.activeColorSpace.ToString();
            var captureRoot = Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../candidate"));
            var output = Path.Combine(captureRoot, space);
            Directory.CreateDirectory(output);
            var report = new CaptureSet { colorSpace = space };
            var keywords = new[] { "none", "blur-fast", "blur-medium", "blur-detail", "pixelation", "rgb-shift", "edge-luminance", "edge-alpha" };
            try
            {
                var surface = host.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(256, 256);
                surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                using var module = new EffectModule(document);
                document.Images = new[] { new ImageAsset("input.png", source) };
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                foreach (var mode in (SamplingFilter[])Enum.GetValues(typeof(SamplingFilter)))
                {
                    foreach (var phase in new[] { 0f, .5f, 1f })
                    {
                        var id = "SamplingFilter." + mode + "." + Mathf.RoundToInt(phase * 100).ToString("000");
                        document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}" +
                            "img{position:absolute;left:64px;top:64px;width:128px;height:128px;--urui-effect:ui;--urui-fx-sampling-filter:" +
                            keywords[(int)mode] + ";--urui-fx-sampling-intensity:" + phase.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            ";}</style></head><body><img src='input.png'></body></html>", "/sampling-reference.html");
                        document.SynchronizeLayout();
                        var passes = EffectStatistics.FilterProcessingPasses;
                        camera.Render();
                        var previous = RenderTexture.active;
                        try
                        {
                            RenderTexture.active = target;
                            read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                            read.Apply();
                        }
                        finally
                        {
                            RenderTexture.active = previous;
                        }
                        File.WriteAllBytes(Path.Combine(output, id + ".png"), read.EncodeToPNG());
                        var data = UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Reference/" + space + "/" + id);
                        Assert.That(data, Is.Not.Null, "缺少采样参考：" + space + "/" + id);
                        var expected = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                        try
                        {
                            Assert.That(expected.LoadImage(data.bytes), Is.True);
                            var actual = read.GetPixels();
                            var original = expected.GetPixels();
                            var maximum = 0f;
                            var sum = 0d;
                            var finite = true;
                            for (var index = 0; index < actual.Length; index++)
                            {
                                for (var channel = 0; channel < 4; channel++)
                                {
                                    finite &= float.IsFinite(actual[index][channel]);
                                    var error = Mathf.Abs(Mathf.Clamp01(actual[index][channel]) - original[index][channel]);
                                    maximum = Mathf.Max(maximum, error);
                                    sum += error;
                                }
                            }
                            Assert.That(finite, Is.True, id);
                            Assert.That(maximum, Is.LessThanOrEqualTo(4f / 255), space + "/" + id);
                            Assert.That(sum / (actual.Length * 4), Is.LessThanOrEqualTo(.5 / 255), space + "/" + id);
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(expected);
                        }
                        report.cases.Add(new SamplingCapture
                        {
                            id = id,
                            nominalKernelSamples = mode == SamplingFilter.BlurFast ? 25 : mode == SamplingFilter.BlurMedium ? 81 : mode == SamplingFilter.BlurDetail ? 169 : 0,
                            leasedTargets = EffectStatistics.GetTargets().LeasedTargets,
                            processingPasses = EffectStatistics.FilterProcessingPasses - passes
                        });
                        yield return null;
                    }
                }
                File.WriteAllText(Path.Combine(output, "sampling-captures.json"), JsonUtility.ToJson(report, true));
                yield return null;
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(source));
            }
        }

        [UnityTest]
        public IEnumerator OriginalGeometryFreezesUvAndPixels()
        {
            var host = new GameObject("Sampling snapshot");
            var cameraObject = new GameObject("Sampling snapshot camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var atlas = new Texture2D(8, 4, TextureFormat.RGBA32, false, true);
            try
            {
                var surface = host.AddComponent<Surface>();
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                var probe = new Probe();
                document.Images = new[] { new ImageAsset("atlas.png", atlas, new[] { new SpriteRegion("sample", new Rect(2, 0, 4, 4)) }) };
                document.Effects.Register(new EffectDefinition("probe", EffectKind.Element, probe, "probe:v1", "premultiplied"));
                document.LoadHtml("<!doctype html><html><head><style>img{width:40px;height:20px;--urui-effect:probe;}</style></head>" +
                    "<body><img id='sample' src='atlas.png' data-urui-sprite='sample'></body></html>", "/sampling.html");
                document.SynchronizeLayout();
                var geometryProperty = typeof(EffectFrame).GetProperty("SourceGeometry");
                Assert.That(geometryProperty, Is.Not.Null, "采样必须取得冻结的原始几何和 UV");
                var first = geometryProperty.GetValue(probe.Frame);
                Assert.That(first, Is.Not.Null);
                var type = first.GetType();
                Assert.That(type.GetProperty("UvBounds").GetValue(first), Is.EqualTo(new Rect(.25f, 0, .5f, 1)));
                Assert.That(type.GetProperty("Bounds").GetValue(first), Is.EqualTo(new Rect(0, 0, 40, 20)));
                Assert.That(typeof(EffectFrame).GetProperty("SourcePixelSize").GetValue(probe.Frame), Is.EqualTo(new Vector2Int(8, 4)));
                document.Q("sample").Style.SetWidth(80);
                document.SynchronizeLayout();
                var second = geometryProperty.GetValue(probe.Frame);
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(type.GetProperty("Bounds").GetValue(first), Is.EqualTo(new Rect(0, 0, 40, 20)), "旧快照不能随原网格改变");
                Assert.That(type.GetProperty("Bounds").GetValue(second), Is.EqualTo(new Rect(0, 0, 80, 20)));
                document.Q("sample").Style.SetNumber("--urui-fx-sampling-width", 4);
                document.SynchronizeLayout();
                Assert.That(geometryProperty.GetValue(probe.Frame), Is.SameAs(second), "采样参数不能重建原始几何");
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(atlas));
            }
        }

        [UnityTest]
        public IEnumerator AtlasNeighborCannotBleed() => VerifyAtlas(false);
        [UnityTest]
        public IEnumerator NineSliceAndFilledImageStayWithinSource() => VerifyAtlas(true);
        static IEnumerator VerifyAtlas(bool shapes)
        {
            var host = new GameObject("Atlas sampling");
            var cameraObject = new GameObject("Atlas camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            var atlas = new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[256];
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    pixels[y * 16 + x] = x >= 4 && x < 12 && y >= 4 && y < 12 ? Color.green : Color.red;
                }
            }
            atlas.SetPixels(pixels);
            atlas.Apply();
            var read = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true);
            var variants = shapes ? new[] {
                "--urui-image-slice-left:2px;--urui-image-slice-right:2px;--urui-image-slice-top:2px;--urui-image-slice-bottom:2px;",
                "--urui-image-fill-mode:horizontal;--urui-image-fill-amount:.5;",
                "--urui-image-fill-mode:vertical;--urui-image-fill-amount:.5;",
                "--urui-image-fill-mode:radial90;--urui-image-fill-amount:.7;",
                "--urui-image-fill-mode:radial180;--urui-image-fill-amount:.7;",
                "--urui-image-fill-mode:radial360;--urui-image-fill-amount:.7;", "object-fit:cover;width:80px;height:40px;",
                "width:1px;height:1px;", "width:1px;height:7px;", "width:7px;height:1px;", "width:3px;height:9px;" } : new[] { "" };
            var modes = new[] { "none", "blur-fast", "blur-medium", "blur-detail", "pixelation", "rgb-shift", "edge-alpha" };
            try
            {
                var surface = host.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(128, 128);
                surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                using var module = new EffectModule(document);
                document.Images = new[] { new ImageAsset("atlas.png", atlas, new[] { new SpriteRegion("target", new Rect(4, 4, 8, 8)) }) };
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                foreach (var variant in variants)
                {
                    foreach (var mode in modes)
                    {
                        document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}" +
                            "img{position:absolute;left:32px;top:32px;width:64px;height:64px;--urui-effect:ui;--urui-fx-sampling-filter:" + mode +
                            ";--urui-fx-sampling-intensity:1;" + variant + "}</style></head><body><img src='atlas.png' data-urui-sprite='target'></body></html>", "/atlas.html");
                        document.SynchronizeLayout();
                        camera.Render();
                        var previous = RenderTexture.active;
                        try
                        {
                            RenderTexture.active = target;
                            read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                            read.Apply();
                        }
                        finally
                        {
                            RenderTexture.active = previous;
                        }
                        var maximumRed = 0f;
                        var maximumGreen = 0f;
                        foreach (var pixel in read.GetPixels())
                        {
                            Assert.That(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b) && float.IsFinite(pixel.a), Is.True);
                            maximumRed = Mathf.Max(maximumRed, pixel.r);
                            maximumGreen = Mathf.Max(maximumGreen, pixel.g);
                        }
                        Assert.That(maximumRed, Is.LessThan(.002f), "不得采样相邻红色图块：" + mode + "/" + variant);
                        if (!variant.Contains("width:1px") && !variant.Contains("height:1px"))
                        {
                            Assert.That(maximumGreen, Is.GreaterThan(.05f), "输入不能被错误地清空：" + mode + "/" + variant);
                        }
                        if (mode == "blur-fast" && variant.Contains("width:80px"))
                        {
                            Save(read, "AtlasNonUniform.png");
                        }
                        if (mode == "blur-fast" && variant == "width:1px;height:1px;")
                        {
                            Save(read, "AtlasTiny.png");
                        }
                        yield return null;
                    }
                }
                yield return null;
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(atlas));
            }
        }

        [UnityTest]
        public IEnumerator SamplingExpansionHonorsClip()
        {
            var host = new GameObject("Sampling clipping");
            var cameraObject = new GameObject("Sampling clipping camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            var source = new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
            source.SetPixels(Enumerable.Repeat(Color.white, 256).ToArray());
            source.Apply();
            var read = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true);
            try
            {
                var surface = host.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(128, 128);
                surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                using var module = new EffectModule(document);
                document.Images = new[] { new ImageAsset("white.png", source) };
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}" +
                    ".clip{position:absolute;left:16px;top:16px;width:32px;height:32px;overflow:hidden;}" +
                    "img,.group{position:absolute;left:16px;top:8px;width:16px;height:16px;--urui-effect:ui;--urui-fx-sampling-filter:blur-fast;--urui-fx-sampling-intensity:1;}" +
                    "#free{left:80px;top:24px;}#lower{top:64px;}.fill{width:16px;height:16px;background-color:white;}" +
                    "</style></head><body><div class='clip'><img src='white.png'></div><img id='free' src='white.png'>" +
                    "<div id='lower' class='clip'><div class='group'><div class='fill'></div></div></div></body></html>", "/sampling-clip.html");
                document.SynchronizeLayout();
                camera.Render();
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                    read.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                Color Pixel(int x, int y) => read.GetPixel(x, 127 - y);
                Assert.That(Pixel(29, 32).a, Is.GreaterThan(.01f), "图片原几何必须包含采样扩边");
                Assert.That(Pixel(49, 32).a, Is.LessThan(.002f), "扩边仍受祖先裁剪约束");
                Assert.That(Pixel(77, 32).a, Is.GreaterThan(.01f), "无裁剪时采样可超出原图片边框");
                Assert.That(Pixel(29, 80).a, Is.GreaterThan(.01f), "子树覆盖图必须预留可见 halo");
                Assert.That(Pixel(49, 80).a, Is.LessThan(.002f));
                Save(read, "SamplingClip.png");
                yield return null;
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(source));
            }
        }

        [UnityTest]
        public IEnumerator TextAtlasDoesNotSampleAnotherGlyph()
        {
            var host = new GameObject("Text sampling");
            var cameraObject = new GameObject("Text sampling camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            Texture2D read = null;
            try
            {
                var surface = host.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(128, 128);
                surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                using var module = new EffectModule(document);
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}" +
                    "div{position:absolute;font-size:24px;}#label{left:24px;top:24px;width:24px;height:32px;color:white;" +
                    "--urui-effect:ui;--urui-fx-sampling-filter:blur-detail;--urui-fx-sampling-intensity:1;}#neighbor{left:80px;top:24px;color:red;}" +
                    "</style></head><body><div id='label'>I</div><div id='neighbor'>WWXX</div></body></html>", "/text-sampling.html");
                document.SynchronizeLayout();
                camera.Render();
                var method = typeof(Document).GetMethod("TryGetEffectFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var args = new object[] { document.Q("label"), null };
                Assert.That((bool)method.Invoke(document, args), Is.True);
                var frame = (EffectFrame)args[1];
                Assert.That(frame.SourceIsCaptured, Is.True);
                Assert.That(frame.Source, Is.InstanceOf<RenderTexture>());
                Assert.That(frame.Source.width, Is.LessThanOrEqualTo(64));
                Assert.That(frame.Source.height, Is.LessThanOrEqualTo(64));
                read = new Texture2D(frame.Source.width, frame.Source.height, TextureFormat.RGBAFloat, false, true);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = (RenderTexture)frame.Source;
                    read.ReadPixels(new Rect(0, 0, read.width, read.height), 0, 0);
                    read.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                var maximum = 0f;
                foreach (var pixel in read.GetPixels())
                {
                    Assert.That(pixel.r, Is.EqualTo(pixel.g).Within(.002f));
                    Assert.That(pixel.g, Is.EqualTo(pixel.b).Within(.002f));
                    maximum = Mathf.Max(maximum, pixel.a);
                }
                Assert.That(maximum, Is.GreaterThan(.1f), "文字覆盖图应含真实字形");
                Save(read, "TextSamplingInput.png");
                yield return null;
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                if (read)
                {
                    UnityEngine.Object.DestroyImmediate(read);
                }
            }
        }

        [UnityTest]
        public IEnumerator SamplingZeroIsIdentity()
        {
            var host = new GameObject("Zero sampling");
            var cameraObject = new GameObject("Zero sampling camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            var source = EffectColorTests.InputTexture();
            var read = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true);
            Color[] baseline = null;
            try
            {
                var surface = host.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(128, 128);
                surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                using var module = new EffectModule(document);
                document.Images = new[] { new ImageAsset("input.png", source) };
                document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
                foreach (var mode in new[] { "none", "blur-fast", "blur-medium", "blur-detail" })
                {
                    document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}" +
                        "img{position:absolute;left:32px;top:32px;width:64px;height:64px;--urui-effect:ui;--urui-fx-sampling-filter:" + mode +
                        ";--urui-fx-sampling-intensity:0;}</style></head><body><img src='input.png'></body></html>", "/sampling-zero.html");
                    document.SynchronizeLayout();
                    camera.Render();
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                        read.Apply();
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                    }
                    var pixels = read.GetPixels();
                    if (baseline == null)
                    {
                        baseline = pixels;
                    }
                    else
                    {
                        var maximum = 0f;
                        for (var index = 0; index < pixels.Length; index++)
                        {
                            for (var channel = 0; channel < 4; channel++)
                            {
                                maximum = Mathf.Max(maximum, Mathf.Abs(pixels[index][channel] - baseline[index][channel]));
                            }
                        }
                        Assert.That(maximum, Is.LessThan(.002f), mode);
                    }
                    Save(read, "SamplingZero-" + mode + ".png");
                }
                yield return null;
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(source));
            }
        }

        static void Save(Texture2D texture, string name)
        {
            var root = Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../candidate"));
            var output = Path.Combine(root, QualitySettings.activeColorSpace.ToString());
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator SamplingShapeFlagKeepsShadowAndOriginalGeometry()
        {
            using var fixture = new EffectRenderFixture();
            fixture.Source.SetPixels(Enumerable.Repeat(Color.white, 4096).ToArray());
            fixture.Source.Apply();
            fixture.Load("--urui-fx-sampling-filter:blur-fast;--urui-fx-sampling-intensity:1;--urui-fx-allow-to-modify-mesh-shape:false;--urui-fx-shadow-distance-x:20;--urui-fx-shadow-distance-y:-20;");
            var clipped = fixture.Render();
            fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"), out var first);
            Assert.That(clipped[(255 - 128) * 256 + 63].a, Is.LessThan(.002f), "关闭形状修改时采样不扩边");
            fixture.Document.Q("subject").Style.Set("--urui-fx-shadow-mode", "shadow");
            var shadow = fixture.Render("SamplingShapeFlagShadow");
            fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"), out var second);
            Assert.That(shadow[(255 - 128) * 256 + 200].a, Is.GreaterThan(.1f), "源选项不关闭独立阴影几何");
            Assert.That(second.SourceGeometry, Is.SameAs(first.SourceGeometry));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SubtreeExpansionIsResolvedBeforeAllocatingInput()
        {
            var host = new GameObject("Sampling halo");
            var cameraObject = new GameObject("Sampling halo camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            try
            {
                var surface = host.AddComponent<Surface>();
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                var probe = new Probe();
                var guarded = 0;
                Func<EffectLayout, Vector4> resolver = layout =>
                {
                    Assert.Throws<InvalidOperationException>(() => document.CreateElement());
                    guarded++;
                    return new Vector4(8, 4, 8, 4);
                };
                var definition = new EffectDefinition("probe", EffectKind.Element, probe, "probe:v1", "premultiplied", expansionResolver: resolver);
                document.Effects.Register(definition);
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:80px;height:40px;background-color:red;--urui-effect:probe;}" +
                    "</style></head><body><div></div></body></html>", "/halo.html");
                document.SynchronizeLayout();
                cameraObject.GetComponent<Camera>().Render();
                Assert.That(guarded, Is.GreaterThan(0));
                Assert.That(probe.Frame.Source.width, Is.EqualTo(96));
                Assert.That(probe.Frame.Source.height, Is.EqualTo(48));
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
