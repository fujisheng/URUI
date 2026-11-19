using System.Text;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Hosting;
using URUI.Rendering.Effects;
using Unity.Profiling;

namespace URUI.Tests
{
    public sealed class RuntimeBatchingTests
    {
        sealed class QueuedPipelineAsset : RenderPipelineAsset
        {
            internal System.Action<ScriptableRenderContext> Draw;
            public override System.Type pipelineType => typeof(QueuedPipeline);
            protected override RenderPipeline CreatePipeline() => new QueuedPipeline(this);
        }
        sealed class QueuedPipeline : RenderPipeline
        {
            readonly QueuedPipelineAsset asset;
            internal QueuedPipeline(QueuedPipelineAsset asset)
            {
                this.asset = asset;
            }
            protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
            {
                asset.Draw?.Invoke(context);
                context.Submit();
            }
        }
        GameObject root;
        GameObject cameraObject;
        Camera camera;
        RenderTexture target;
        Document document;
        Texture2D red;
        Texture2D blue;
        RenderHost Host => root.GetComponentInChildren<RenderHost>(true);

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Batch camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            root = new GameObject("Batch document");
            var surface = root.AddComponent<Surface>();
            surface.Viewport = new Vector2Int(128, 128);
            surface.Input.UseUnityInput = false;
            document = root.AddComponent<Document>();
            document.Surface = surface;
            red = Texture(new Color(1, 0, 0, .5f));
            blue = Texture(new Color(0, 0, 1, .5f));
            document.Images = new[] { new URUI.Resources.ImageAsset("red.png", red), new URUI.Resources.ImageAsset("blue.png", blue) };
        }

        static Texture2D Texture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
        static string Html(string body) => "<!doctype html><html><head><style>body{margin:0;}" +
            "img{position:absolute;width:3px;height:3px;}</style></head><body>" + body + "</body></html>";
        void Load(string body)
        {
            document.LoadHtml(Html(body), "/batching.html");
            for (var i = 0; i < 4; i++)
            {
                document.SynchronizeLayout();
            }
        }
        Color Pixel(int x, int y)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                read.ReadPixels(new Rect(x, 127 - y, 1, 1), 0, 0);
                read.Apply();
                return read.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(read);
            }
        }
        static void Rgb(Color actual, Color encoded)
        {
            var expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? encoded.linear : encoded;
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(3f / 255));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(3f / 255));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(3f / 255));
        }

        [Test]
        public void LargeLayoutTranslationPreservesVisibleSourceGeometry()
        {
            Load("<img src='red.png' style='width:16.75px;'>" +
                "<img src='red.png' style='left:16777216px;top:8px;width:16.75px;transform:translate(-16777216px,0);transform-origin:0 0;'>");
            var reference = Pixel(15, 1);
            Assert.That(reference.r, Is.GreaterThan(.1f), "参考图形必须实际覆盖像素中心。");
            Assert.That(Pixel(15, 9).r, Is.EqualTo(reference.r).Within(1f / 255), "大布局坐标变换后仍应绘制原生源网格覆盖的像素。");
        }

        [TestCase(100)]
        [TestCase(1000)]
        public void SameTextureAutomaticallyUsesOneGeometrySubmission(int count)
        {
            var body = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                body.Append("<img src='red.png' style='left:").Append(i % 32 * 4)
                                .Append("px;top:").Append(i / 32 * 4).Append("px;'>");
            }
            Load(body.ToString());
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            Rgb(Pixel(1, 1), new Color(.5f, 0, 0));
        }

        [Test]
        public void DisjointAlternatingTexturesUseOneGeometrySubmission()
        {
            var body = new StringBuilder();
            for (var i = 0; i < 1000; i++)
            {
                body.Append("<img src='").Append(i % 2 == 0 ? "red.png" : "blue.png")
                                .Append("' style='left:").Append(i % 32 * 4).Append("px;top:").Append(i / 32 * 4).Append("px;'>");
            }
            Load(body.ToString());
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            Rgb(Pixel(1, 1), new Color(.5f, 0, 0));
            Rgb(Pixel(5, 1), new Color(0, 0, .5f));
        }

        [Test]
        public void OverlappingAlternatingTexturesKeepSourceOverOrder()
        {
            Load("<img src='red.png'><img src='blue.png'><img src='red.png'>");
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            Rgb(Pixel(1, 1), new Color(.625f, 0, .25f));
        }

        [Test]
        public void OverlappingSameTexturePreservesPrimitiveOrderInOneSubmission()
        {
            Load("<img src='red.png'><img src='red.png'><img src='red.png'>");
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            Rgb(Pixel(1, 1), new Color(.875f, 0, 0));
        }

        [Test]
        public void NineDistinctTexturesSplitAtTheFixedBindingBudget()
        {
            var textures = new Texture2D[9];
            var images = new URUI.Resources.ImageAsset[9];
            try
            {
                for (var i = 0; i < textures.Length; i++)
                {
                    textures[i] = Texture(i % 2 == 0 ? Color.red : Color.blue);
                    images[i] = new URUI.Resources.ImageAsset("source" + i + ".png", textures[i]);
                }
                document.Images = images;
                var body = new StringBuilder();
                for (var i = 0; i < textures.Length; i++)
                {
                    body.Append("<img src='source").Append(i).Append(".png' style='left:").Append(i * 4).Append("px;'>");
                }
                Load(body.ToString());
                Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(2));
                for (var i = 0; i < textures.Length; i++)
                {
                    Rgb(Pixel(i * 4 + 1, 1), i % 2 == 0 ? Color.red : Color.blue);
                }
            }
            finally
            {
                document.Unload();
                foreach (var texture in textures)
                {
                    if (texture)
                    {
                        Object.DestroyImmediate(texture);
                    }
                }
            }
        }

        [Test]
        public void BorrowedTexturesStayLiveAndBindTheirOriginalObjects()
        {
            Load("<img src='red.png'><img src='blue.png' style='left:4px;'>");
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            var properties = (MaterialPropertyBlock)typeof(RenderHost).GetField("reusableProperties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Host);
            Assert.That(properties.GetTexture(Shader.PropertyToID("_MainTex")), Is.SameAs(red));
            Assert.That(properties.GetTexture(Shader.PropertyToID("_UruiTexture1")), Is.SameAs(blue));
            red.SetPixel(0, 0, new Color(0, 1, 0, .5f));
            red.Apply();
            document.SynchronizeLayout();
            Rgb(Pixel(1, 1), new Color(0, .5f, 0));
            Rgb(Pixel(5, 1), new Color(0, 0, .5f));
            Assert.That(red.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(blue.filterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void LiveRenderTargetsKeepTheirColorDomainBoundary()
        {
            var live = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            live.Create();
            try
            {
                var previous = RenderTexture.active;
                RenderTexture.active = live;
                GL.Clear(false, true, Color.green);
                RenderTexture.active = previous;
                document.Images = new[] { new URUI.Resources.ImageAsset("blue.png", blue) };
                document.ImageResolver = uri => uri.EndsWith("live.png", System.StringComparison.Ordinal) ? new URUI.Resources.ImageLease(live) : null;
                Load("<img src='live.png'><img src='blue.png' style='left:4px;'>");
                Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(2));
                Rgb(Pixel(1, 1), Color.green);
                Rgb(Pixel(5, 1), new Color(0, 0, .5f));
                previous = RenderTexture.active;
                RenderTexture.active = live;
                GL.Clear(false, true, Color.red);
                RenderTexture.active = previous;
                document.SynchronizeLayout();
                Rgb(Pixel(1, 1), Color.red);
            }
            finally
            {
                document.Unload();
                live.Release();
                Object.DestroyImmediate(live);
            }
        }

        [Test]
        public void ChangedGeometryAndClipRemainVisibleAfterBatching()
        {
            Load("<div style='position:relative;overflow:hidden;width:6px;height:3px;'>" +
                "<img id='a' src='red.png'><img id='b' src='red.png' style='left:4px;'></div>");
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            Rgb(Pixel(5, 1), new Color(.5f, 0, 0));
            Rgb(Pixel(6, 1), Color.black);
            document.Q("b").Style.Set("left", "8px");
            document.SynchronizeLayout();
            Rgb(Pixel(5, 1), Color.black);
            Rgb(Pixel(9, 1), Color.black);
        }

        [Test]
        public void OffscreenFilterInputStillCastsVisibleShadow()
        {
            Load("<div style='position:absolute;left:-20px;top:20px;width:10px;height:20px;" +
                "background:white;filter:drop-shadow(red 24px 0px 0px);'></div>");
            Rgb(Pixel(7, 25), Color.red);
        }

        [Test]
        public void FailedRecordingRejectsResourceRetirementInTheSameFrame()
        {
            Load("<img src='red.png'>");
            var host = Host;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var attachment = typeof(RenderHost).GetField("buffer", flags).GetValue(host);
            var blocked = typeof(RenderHost).GetField("blockedRecording", flags);
            var frame = typeof(RenderHost).GetField("blockedRecordingFrame", flags);
            blocked.SetValue(host, attachment);
            frame.SetValue(host, Time.frameCount);
            try
            {
                Assert.Throws<System.InvalidOperationException>(() => host.RetirePaintResources(new System.Collections.Generic.List<System.IDisposable>()));
            }
            finally
            {
                blocked.SetValue(host, null);
                frame.SetValue(host, -1);
            }
        }

        [Test]
        public void EmptyPainterDoesNotSplitTheColorRaster()
        {
            Load("<img src='red.png'><div id='empty' style='position:absolute;left:8px;top:0;width:3px;height:3px;'></div>" +
                "<img src='red.png' style='left:4px;'>");
            var callbacks = 0;
            using var registration = document.AttachPainter(document.Q("empty"), "empty-output", context => ++callbacks);
            document.Q("empty").Style.Set("--urui-decorator", "shader(empty-output)");
            document.SynchronizeLayout();
            Assert.That(callbacks, Is.GreaterThan(0));
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            Assert.That(Host.RecordedRasterResolves, Is.EqualTo(QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0));
            Rgb(Pixel(1, 1), new Color(.5f, 0, 0));
            Rgb(Pixel(5, 1), new Color(.5f, 0, 0));
        }

        sealed class OriginalEffect : IEffectFactory, IElementEffect
        {
            public IElementEffect Create(EffectContext context)
            {
                return this;
            }
            public void Prepare(EffectFrame frame)
            {
            }
            public void Record(EffectRenderContext context)
            {
                var copy = context;
                copy.DrawOriginal();
            }
            public void Dispose()
            {
            }
        }

        [Test]
        public void DrawOriginalEffectDoesNotSplitGeometryOrColorRaster()
        {
            document.Effects.Register(new EffectDefinition("original", EffectKind.Image, new OriginalEffect(), "original", "premultiplied"));
            Load("<img src='red.png'><img src='red.png' style='left:4px;--urui-image-effect:original;'><img src='red.png' style='left:8px;'>");
            Assert.That(Host.RecordedGeometryDraws, Is.EqualTo(1));
            Assert.That(Host.RecordedRasterResolves, Is.EqualTo(QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0));
            Rgb(Pixel(1, 1), new Color(.5f, 0, 0));
            Rgb(Pixel(5, 1), new Color(.5f, 0, 0));
            Rgb(Pixel(9, 1), new Color(.5f, 0, 0));
        }

        static long MarkerCalls(ProfilerRecorder recorder)
        {
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            long count = 0;
            for (var i = 0; i < recorder.Count; i++)
            {
                count += recorder.GetSample(i).Count;
            }
            return count;
        }

        [UnityTest]
        public IEnumerator StableCommandsDoNotRepeatUploadsAfterAValidatedParameterRefresh()
        {
            Load("<img id='probe' src='red.png'>");
            var uploaded = Host.ParameterUploadBytes;
            document.Q("probe").Style.SetPixels("left", 4);
            document.SynchronizeLayout();
            Assert.That(Host.ParameterUploadBytes, Is.GreaterThan(uploaded), "阳性验证：实际参数变化必须触发上传。");
            for (var i = 0; i < 5; i++)
            {
                document.SynchronizeLayout();
            }
            uploaded = Host.ParameterUploadBytes;
            var geometry = Host.GeometryUploadBytes;
            for (var i = 0; i < 5; i++)
            {
                document.SynchronizeLayout();
            }
            Assert.That(Host.ParameterUploadBytes, Is.EqualTo(uploaded));
            Assert.That(Host.GeometryUploadBytes, Is.EqualTo(geometry));
            yield return null;
            Rgb(Pixel(5, 1), new Color(.5f, 0, 0));
        }

        [Test]
        public void UnchangedCommandsRestoreTheBufferAfterExternalClear()
        {
            Load("<img src='red.png'>");
            // Linear 禁止向调用者借出有合成资源的缓冲；此处也覆盖同等外部清空故障。
            var commands = (CommandBuffer)typeof(RenderHost).GetField("buffer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Host);
            commands.Clear();
            Assert.That(commands.sizeInBytes, Is.Zero);
            document.SynchronizeLayout();
            Assert.That(commands.sizeInBytes, Is.GreaterThan(0));
            Rgb(Pixel(1, 1), new Color(.5f, 0, 0));
        }

        [Test]
        public void DestroyingHiddenGeometryDoesNotRetainItBehindTheStaticCache()
        {
            Load("<img id='visible' src='red.png'><img id='hidden' src='red.png' style='left:4px;'>");
            var commands = new URUI.Internal.Interop.CommandNative[2];
            Assert.That(URUI.Internal.Interop.NativeMethods.urui_get_commands(commands, commands.Length), Is.EqualTo(2));
            var hidden = commands[1].geometry;
            Assert.That(URUI.Internal.Interop.NativeMethods.urui_geometry_exists(hidden), Is.EqualTo(1));
            document.Q("hidden").Style.Set("display", "none");
            document.SynchronizeLayout();
            document.Q("hidden").Destroy();
            document.SynchronizeLayout();
            Assert.That(URUI.Internal.Interop.NativeMethods.urui_geometry_exists(hidden), Is.Zero);
            Rgb(Pixel(1, 1), new Color(.5f, 0, 0));
        }

        [UnityTest]
        public IEnumerator TwoQueuedSrpSubmissionsKeepTheirOwnGeometryVersions() => VerifyQueuedSubmissions(false);

        [UnityTest]
        public IEnumerator TwoQueuedSrpSubmissionsKeepTheirTextureBindings() => VerifyQueuedSubmissions(true);

        IEnumerator VerifyQueuedSubmissions(bool textures)
        {
            Texture2D green = null;
            if (textures)
            {
                green = Texture(new Color(0, 1, 0, .5f));
                document.Images = new[] { new URUI.Resources.ImageAsset("red.png", red), new URUI.Resources.ImageAsset("blue.png", blue), new URUI.Resources.ImageAsset("green.png", green) };
                Load("<img src='red.png'><img id='changed' src='blue.png' style='left:4px;'>");
            }
            else
            {
                Load("<div id='changed' style='position:absolute;left:0;top:0;width:3px;height:3px;background:red;'></div>" +
                                "<div style='position:absolute;left:4px;top:0;width:3px;height:3px;background:red;'></div>");
            }
            var first = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            first.Create();
            var second = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            second.Create();
            var previous = GraphicsSettings.defaultRenderPipeline;
            var previousQuality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<QueuedPipelineAsset>();
            var submitted = false;
            try
            {
                pipeline.Draw = context =>
                {
                    if (submitted)
                    {
                        return;
                    }
                    submitted = true;
                    context.SetupCameraProperties(camera);
                    using var clear = new CommandBuffer();
                    clear.SetRenderTarget(first);
                    clear.ClearRenderTarget(false, true, Color.black);
                    context.ExecuteCommandBuffer(clear);
                    Assert.That(document.Surface.SubmitRendering(context, first, new Rect(0, 0, 128, 128)), Is.True);
                    if (textures)
                    {
                        document.Q("changed").SetAttribute("src", "green.png");
                    }
                    else
                    {
                        document.Q("changed").Style.Set("background-color", "blue");
                    }
                    document.SynchronizeLayout();
                    clear.Clear();
                    clear.SetRenderTarget(second);
                    clear.ClearRenderTarget(false, true, Color.black);
                    context.ExecuteCommandBuffer(clear);
                    Assert.That(document.Surface.SubmitRendering(context, second, new Rect(0, 0, 128, 128)), Is.True);
                    // Pipeline在回调结束后才Submit，两份命令必须继续引用各自的不可变Mesh版本。
                };
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                Assert.That(submitted, Is.True);
                Color Sample(RenderTexture texture, int x)
                {
                    var active = RenderTexture.active;
                    RenderTexture.active = texture;
                    var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                    try
                    {
                        read.ReadPixels(new Rect(x, 126, 1, 1), 0, 0);
                        read.Apply();
                        return read.GetPixel(0, 0);
                    }
                    finally
                    {
                        RenderTexture.active = active;
                        Object.DestroyImmediate(read);
                    }
                }
                if (textures)
                {
                    Rgb(Sample(first, 1), new Color(.5f, 0, 0));
                    Rgb(Sample(first, 5), new Color(0, 0, .5f));
                    Rgb(Sample(second, 1), new Color(.5f, 0, 0));
                    Rgb(Sample(second, 5), new Color(0, .5f, 0));
                }
                else
                {
                    Rgb(Sample(first, 1), Color.red);
                    Rgb(Sample(first, 5), Color.red);
                    Rgb(Sample(second, 1), Color.blue);
                    Rgb(Sample(second, 5), Color.red);
                }
            }
            finally
            {
                pipeline.Draw = null;
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = previousQuality;
                Object.DestroyImmediate(pipeline);
                first.Release();
                second.Release();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                if (green)
                {
                    document.Unload();
                    Object.DestroyImmediate(green);
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(cameraObject);
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(red);
            Object.DestroyImmediate(blue);
        }
    }
}
