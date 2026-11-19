using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Hosting;
using URUI.Resources;
using URUI.Rendering.Effects;

namespace URUI.Tests
{
    public sealed class RenderPlanRegressionTests
    {
        static object allocationProbe;
        GameObject host;
        GameObject cameraObject;
        Document document;
        Camera camera;
        RenderTexture target;
        Texture2D mask;
        RenderHost Renderer => host.GetComponentInChildren<RenderHost>(true);

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Plan camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            host = new GameObject("Plan document");
            var surface = host.AddComponent<Surface>();
            surface.Viewport = new Vector2Int(128, 128);
            surface.Input.UseUnityInput = false;
            document = host.AddComponent<Document>();
            document.Surface = surface;
            mask = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            mask.SetPixel(0, 0, new Color(1, 1, 1, .5f));
            mask.Apply();
            document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture), new ImageAsset("mask.png", mask) };
        }

        void Load(string body)
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:8px;height:8px;}</style></head><body>" + body + "</body></html>", "/render-plan.html");
            for (var i = 0; i < 8; i++)
            {
                document.SynchronizeLayout();
            }
        }

        [Test]
        public void DynamicSegmentCountDoesNotAllocateAfterWarmup()
        {
            Load("<img src='white.png'><div id='external' style='position:absolute;left:8px;width:8px;height:8px;'></div><img src='white.png' style='left:16px;'>");
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            var draw = false;
            var calls = 0;
            try
            {
                using var registration = document.AttachPainter(document.Q("external"), "plan-paint", context =>
                {
                    calls++;
                    if (draw)
                    {
                        context.DrawMesh(mesh, material, Matrix4x4.identity);
                    }
                });
                document.Q("external").Style.Set("--urui-decorator", "shader(plan-paint)");
                Action cycle = () =>
                {
                    draw = !draw;
                    document.SynchronizeLayout();
                };
                for (var i = 0; i < 300; i++)
                {
                    cycle();
                }
                Assert.That(calls, Is.GreaterThanOrEqualTo(300), "必须真正执行自定义绘制，避免空尺寸节点让回归虚假通过。");
                var joined = Renderer.RecordedGeometryDraws;
                cycle();
                Assert.That(Renderer.RecordedGeometryDraws, Is.GreaterThan(joined), "夹具必须使实际绘制分组数量变化。");
                cycle();
                using (var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    allocationProbe = new byte[4096];
                    GC.KeepAlive(allocationProbe);
                    positive.Stop();
                    Assert.That(positive.Valid && positive.Count > 0, Is.True, "真实GC.Alloc阳性探针必须被记录。");
                    allocationProbe = null;
                }
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                for (var i = 0; i < 32; i++)
                {
                    cycle();
                }
                recorder.Stop();
                Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
                long allocations = 0;
                for (var i = 0; i < recorder.Count; i++)
                {
                    allocations += recorder.GetSample(i).Count;
                }
                Assert.That(allocations, Is.Zero, "稳定容量的分组数量变化不能反复创建排序计划及暂存数组。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransformOnlyChangesDoNotUploadGeometry(bool cssTransform)
        {
            Load("<img id='moving' src='white.png'><img src='white.png' style='left:16px;'><img src='white.png' style='left:32px;'>");
            var counter = Renderer.GetType().GetProperty("GeometryUploadBytes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(counter, Is.Not.Null, "需要实际上传字节渠道，不能把缺测当作0。");
            long Read() => Convert.ToInt64(counter.GetValue(Renderer));
            Assert.That(Read(), Is.GreaterThan(0), "首次真实几何上传必须被统计。");
            var moving = document.Q("moving");
            moving.Style.Set(cssTransform ? "transform" : "left", cssTransform ? "translate(1px, 0px)" : "1px");
            document.SynchronizeLayout();
            var before = Read();
            moving.Style.Set(cssTransform ? "transform" : "left", cssTransform ? "translate(2px, 0px)" : "2px");
            document.SynchronizeLayout();
            Assert.That(Read() - before, Is.Zero, "只改变变换应更新参数，而不上传顶点或索引。");
            Assert.That(Renderer.RecordedGeometryDraws, Is.EqualTo(1), "兼容几何不因不同变换被拆成多次绘制。");
        }

        [Test]
        public void ImageTintAndAtlasUvUseParametersWithoutGeometryUpload()
        {
            var atlas = new Texture2D(4, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                atlas.SetPixels(new[] { Color.red, Color.red, Color.green, Color.green });
                atlas.Apply();
                document.Images = new[] { new ImageAsset("atlas.png", atlas) };
                Load("<img id='image' src='atlas.png' data-urui-rect='0 0 2 1'>");
                Color Sample()
                {
                    camera.Render();
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(2, 125, 1, 1), 0, 0);
                        read.Apply();
                        return read.GetPixel(0, 0);
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                    }
                }
                Assert.That(Sample().r, Is.GreaterThan(.99f));
                var image = document.Q("image");
                var uploaded = Renderer.GeometryUploadBytes;
                image.SetAttribute("data-urui-rect", "2 0 2 1");
                image.Style.Set("--urui-graphic-tint", "rgba(255,255,255,0.5)");
                document.SynchronizeLayout();
                Assert.That(Renderer.GeometryUploadBytes, Is.EqualTo(uploaded));
                var color = Sample();
                var expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(0, .5f, .5f).linear : new Color(0, .5f, .5f);
                Assert.That(color.r, Is.EqualTo(expected.r).Within(3f / 255));
                Assert.That(color.g, Is.EqualTo(expected.g).Within(3f / 255));
                Assert.That(color.b, Is.EqualTo(expected.b).Within(3f / 255));
            }
            finally
            {
                document.Unload();
                UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void FilterProgramsStayBatchedAndUpdateOnlyTheirParameterRow()
        {
            var body = new System.Text.StringBuilder();
            for (var i = 0; i < 100; i++)
            {
                body.Append("<img id='filter").Append(i).Append("' src='white.png' style='left:").Append(i % 16 * 8)
                                .Append("px;top:").Append(i / 16 * 8).Append("px;filter:brightness(.5);'>");
            }
            Load(body.ToString());
            var image = document.Q("filter0");
            image.Style.Set("filter", "brightness(.25)");
            document.SynchronizeLayout();
            image.Style.Set("filter", "brightness(.5)");
            document.SynchronizeLayout();
            var uploaded = Renderer.GeometryUploadBytes;
            var parameters = Renderer.ParameterUploadBytes;
            image.Style.Set("filter", "brightness(.25)");
            document.SynchronizeLayout();
            Assert.That(Renderer.RecordedGeometryDraws, Is.EqualTo(1), "点运算滤镜与普通图像使用同一有序批次。");
            Assert.That(Renderer.GeometryUploadBytes, Is.EqualTo(uploaded));
            if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0)
            {
                Assert.That(Renderer.ParameterUploadBytes - parameters, Is.EqualTo(4096), "只更新扩展参数所在行。");
            }
            camera.Render();
            var previous = RenderTexture.active;
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                read.ReadPixels(new Rect(2, 125, 1, 1), 0, 0);
                read.Apply();
                var expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(.25f, .25f, .25f).linear : new Color(.25f, .25f, .25f);
                var color = read.GetPixel(0, 0);
                Assert.That(color.r, Is.EqualTo(expected.r).Within(3f / 255));
                Assert.That(color.g, Is.EqualTo(expected.g).Within(3f / 255));
                Assert.That(color.b, Is.EqualTo(expected.b).Within(3f / 255));
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        [UnityTest]
        public IEnumerator StaticFilteredSubtreeDoesNotAllocateDuringRecording()
        {
            Load("<div style='width:32px;height:32px;background:white;filter:blur(1px) opacity(.5);'></div>");
            for (var i = 0; i < 12; i++)
            {
                camera.Render();
                yield return null;
            }
            for (var i = 0; i < 300; i++)
            {
                document.SynchronizeLayout();
            }
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (var i = 0; i < 32; i++)
            {
                document.SynchronizeLayout();
            }
            recorder.Stop();
            long allocations = 0;
            for (var i = 0; i < recorder.Count; i++)
            {
                allocations += recorder.GetSample(i).Count;
            }
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            Assert.That(allocations, Is.Zero, "静态层录制不能逐帧创建不可变滤镜描述或滤镜上下文。");
        }

        [UnityTest]
        public IEnumerator DynamicChildKeepsUnrelatedFilteredSiblingCached()
        {
            Load("<div style='width:80px;height:32px;filter:opacity(.5);'>" +
                "<div id='dynamic' style='position:absolute;left:0;top:0;width:24px;height:24px;background:white;filter:blur(1px);'></div>" +
                "<div style='position:absolute;left:48px;top:0;width:24px;height:24px;background:white;filter:blur(1px);'></div></div>");
            using var painter = document.AttachPainter(document.Q("dynamic"), "unknown-content", context => { });
            document.Q("dynamic").Style.Set("--urui-decorator", "shader(unknown-content)");
            for (var i = 0; i < 12; i++)
            {
                document.SynchronizeLayout();
                camera.Render();
                yield return null;
            }
            var before = EffectStatistics.FilterProcessingPasses;
            document.SynchronizeLayout();
            camera.Render();
            Assert.That(EffectStatistics.FilterProcessingPasses - before, Is.EqualTo(3), "只处理动态子层的两遍blur和父层opacity，无关兄弟不重复blur。");
            Assert.That(Renderer.RecordedGeometryDraws, Is.EqualTo(1));
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                read.ReadPixels(new Rect(60, 115, 1, 1), 0, 0);
                read.Apply();
                var expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(.5f, .5f, 1).linear : new Color(.5f, .5f, 1);
                Assert.That(read.GetPixel(0, 0).r, Is.EqualTo(expected.r).Within(3f / 255), "缓存兄弟仍按当前父层透明度与背景合成。");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        [UnityTest]
        public IEnumerator SingleLeafMaskDoesNotCreateOffscreenLayer()
        {
            Load("<img src='white.png'>");
            for (var i = 0; i < 8; i++)
            {
                camera.Render();
                yield return null;
            }
            var plain = EffectStatistics.GetTargets().LeasedTargets;
            Load("<div style='width:8px;height:8px;mask-image:url(mask.png);'><img src='white.png'></div>");
            for (var i = 0; i < 8; i++)
            {
                camera.Render();
                yield return null;
            }
            var previous = RenderTexture.active;
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                pixel.ReadPixels(new Rect(2, 125, 1, 1), 0, 0);
                pixel.Apply();
                var color = pixel.GetPixel(0, 0);
                var expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(.5f, .5f, 1).linear : new Color(.5f, .5f, 1);
                Assert.That(color.r, Is.EqualTo(expected.r).Within(.025f));
                Assert.That(color.g, Is.EqualTo(expected.g).Within(.025f));
                Assert.That(color.b, Is.EqualTo(expected.b).Within(.025f));
                Assert.That(EffectStatistics.GetTargets().LeasedTargets, Is.LessThanOrEqualTo(plain + 1), "单叶mask应直接采样，不创建子树源/遮罩/合成目标；允许一份短暂在途租约。");
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixel);
            }
        }

        [UnityTest]
        public IEnumerator FramebufferFetchPreservesLowAlphaAndKeepsLinearLdrAccumulation()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Metal)
            {
                Assert.Ignore("本用例验证 Metal 颜色附件读取；其他后端继续由既有像素门禁覆盖。");
            }
            var fetch = UnityEngine.Resources.Load<Shader>("URUI/PremultipliedFetch");
            if (fetch && !fetch.isSupported)
            {
                Assert.Ignore("当前 Metal 设备不支持颜色附件读取。");
            }
            camera.allowHDR = true;
            camera.backgroundColor = new Color(.02f, .1f, .3f, 1);
            var body = new System.Text.StringBuilder("<div style='position:absolute;left:0;top:0;width:32px;height:32px;opacity:.75;'>");
            for (var n = 0; n < 128; n++)
            {
                body.Append("<div style='position:absolute;left:0;top:0;width:32px;height:32px;background-color:rgba(255,0,0,.01);'></div>");
            }
            body.Append("</div><div style='position:absolute;left:96px;top:96px;width:8px;height:8px;background-color:rgba(255,255,255,.5);'></div>");
            for (var mode = 0; mode < 3; mode++)
            {
                var format = mode == 0 ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                document.Unload();
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                target = new RenderTexture(128, 128, 24, format, mode == 1 ? RenderTextureReadWrite.Default : RenderTextureReadWrite.Linear);
                target.Create();
                camera.targetTexture = target;
                Load(body.ToString());
                yield return null;
                yield return null;
                camera.Render();
                if (mode == 0)
                {
                    Assert.Zero(Renderer.RecordedRasterResolves, "可读取的高精度目标不应再往返编码辅助目标。");
                }
                else
                {
                    Assert.IsFalse((bool)typeof(RenderHost).GetMethod("CanFetchOrdinary", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Renderer, new object[] { 0UL }));
                    Assert.Greater(Renderer.RecordedRasterResolves, 0, "8位目标必须保留高精度编码累积，避免低 alpha 被逐图元量化丢失。");
                }
                Color ReadPixel()
                {
                    var previous = RenderTexture.active;
                    Texture2D pixel = null;
                    try
                    {
                        RenderTexture.active = target;
                        pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                        pixel.ReadPixels(new Rect(12, 115, 1, 1), 0, 0);
                        pixel.Apply();
                        return pixel.GetPixel(0, 0);
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        if (pixel)
                        {
                            UnityEngine.Object.DestroyImmediate(pixel);
                        }
                    }
                }
                var actual = ReadPixel();
                // 以同一资源、目标和旧累积程序作参考，包含引擎实际的颜色量化约定。
                var fields = new[] { "framebufferRasterMaterial", "framebufferLayerMaterial" };
                var saved = new object[fields.Length];
                for (var i = 0; i < fields.Length; i++)
                {
                    var field = typeof(RenderHost).GetField(fields[i], BindingFlags.Instance | BindingFlags.NonPublic);
                    saved[i] = field.GetValue(Renderer);
                    field.SetValue(Renderer, null);
                }
                try
                {
                    yield return null;
                    camera.Render();
                    Assert.Greater(Renderer.RecordedRasterResolves, 0);
                    var expected = ReadPixel();
                    TestContext.WriteLine("Target " + format + " sRGB=" + target.sRGB + " fetch=" + actual + " legacy=" + expected + " HDR=" + camera.allowHDR);
                    Assert.That(actual.r, Is.EqualTo(expected.r).Within(.015f));
                    Assert.That(actual.g, Is.EqualTo(expected.g).Within(.015f));
                    Assert.That(actual.b, Is.EqualTo(expected.b).Within(.015f));
                    Assert.Greater(actual.r, .25f, "低 alpha 重叠不能在逐图元量化中丢失。");
                }
                finally
                {
                    for (var i = 0; i < fields.Length; i++)
                    {
                        typeof(RenderHost).GetField(fields[i], BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Renderer, saved[i]);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator FramebufferFetchPreservesPatternedTexturePalettesInOpacityGroups()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Metal)
            {
                Assert.Ignore("验证Metal附件读取中的纹理和多源批次。");
            }
            camera.allowHDR = true;
            var compressed = UnityEngine.Resources.Load<Texture2D>("URUI.Tests/FramebufferAstcPattern");
            Assert.IsNotNull(compressed);
            TestContext.WriteLine("Imported pattern=" + compressed.format + " graphics=" + compressed.graphicsFormat);
            var patterns = new Texture2D[8];
            var images = new ImageAsset[8];
            var body = new System.Text.StringBuilder("<div style='position:absolute;width:128px;height:128px;opacity:.7;'>");
            for (var n = 0; n < patterns.Length; n++)
            {
                var texture = patterns[n] = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
                var pixels = new Color[64];
                for (var y = 0; y < 8; y++)
                {
                    for (var x = 0; x < 8; x++)
                    {
                        pixels[y * 8 + x] = new Color((x + 1) / 9f, (y + 1) / 9f, (n + 1) / 9f, ((x + y) % 3 + 1) / 3f);
                    }
                }
                texture.SetPixels(pixels);
                texture.Apply();
                images[n] = new ImageAsset("pattern" + n + ".png", n == 0 ? compressed : texture);
                body.Append("<img src='pattern").Append(n).Append(".png' style='left:").Append(n % 4 * 30).Append("px;top:").Append(n / 4 * 50).Append("px;width:28px;height:44px;'>");
            }
            body.Append("</div>");
            document.Images = images;
            try
            {
                for (var mode = 0; mode < 3; mode++)
                {
                    document.Unload();
                    camera.targetTexture = null;
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    target = new RenderTexture(128, 128, 24, mode < 2 ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    target.Create();
                    camera.targetTexture = target;
                    Load(mode == 0 ? body.ToString().Replace("opacity:.7;", "") : body.ToString());
                    yield return null;
                    yield return null;
                    camera.Render();
                    Color[] ReadPixels()
                    {
                        var previous = RenderTexture.active;
                        var read = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true);
                        try
                        {
                            RenderTexture.active = target;
                            read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                            read.Apply();
                            return read.GetPixels();
                        }
                        finally
                        {
                            RenderTexture.active = previous;
                            UnityEngine.Object.DestroyImmediate(read);
                        }
                    }
                    var actual = ReadPixels();
                    var fields = new[] { "framebufferRasterMaterial", "framebufferLayerMaterial" };
                    var saved = new object[2];
                    for (var n = 0; n < 2; n++)
                    {
                        var field = typeof(RenderHost).GetField(fields[n], BindingFlags.Instance | BindingFlags.NonPublic);
                        saved[n] = field.GetValue(Renderer);
                        field.SetValue(Renderer, null);
                    }
                    try
                    {
                        yield return null;
                        camera.Render();
                        var expected = ReadPixels();
                        var maxError = 0f;
                        var bad = 0;
                        for (var n = 0; n < actual.Length; n++)
                        {
                            var error = Mathf.Max(Mathf.Abs(actual[n].r - expected[n].r), Mathf.Abs(actual[n].g - expected[n].g), Mathf.Abs(actual[n].b - expected[n].b));
                            maxError = Mathf.Max(maxError, error);
                            if (error > .02f)
                            {
                                bad++;
                            }
                        }
                        TestContext.WriteLine("Pattern mode=" + mode + " target=" + target.format + " maxError=" + maxError + " badPixels=" + bad);
                        Assert.Zero(bad, "多源图像批次和opacity图层必须保持原有纹理采样与参数绑定。");
                    }
                    finally
                    {
                        for (var n = 0; n < 2; n++)
                        {
                            typeof(RenderHost).GetField(fields[n], BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Renderer, saved[n]);
                        }
                    }
                }
            }
            finally
            {
                document.Unload();
                document.Images = Array.Empty<ImageAsset>();
                foreach (var texture in patterns)
                {
                    if (texture)
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator SrgbRootsKeepHalfAccumulationForOneByteAlphaOverlaps()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
            {
                Assert.Ignore("验证Linear项目中的sRGB根累积。");
            }
            document.Unload();
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            target.Create();
            camera.targetTexture = target;
            camera.allowHDR = true;
            camera.backgroundColor = Color.black;
            var body = new System.Text.StringBuilder();
            for (var n = 0; n < 128; n++)
            {
                body.Append("<div style='position:absolute;left:0;top:0;width:32px;height:32px;background-color:rgba(255,0,0,.003921568627);'></div>");
            }
            Load(body.ToString());
            yield return null;
            yield return null;
            camera.Render();
            Assert.Greater(Renderer.RecordedRasterResolves, 0, "8位sRGB根也必须保留Half累积，不能逐图元写回8位目标。");
            var previous = RenderTexture.active;
            Texture2D read = null;
            try
            {
                RenderTexture.active = target;
                read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                read.ReadPixels(new Rect(12, 115, 1, 1), 0, 0);
                read.Apply();
                Assert.Greater(read.GetPixel(0, 0).r, .05f, "低alpha重叠应保留可见颜色。");
            }
            finally
            {
                RenderTexture.active = previous;
                if (read)
                {
                    UnityEngine.Object.DestroyImmediate(read);
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(mask);
        }
    }
}
