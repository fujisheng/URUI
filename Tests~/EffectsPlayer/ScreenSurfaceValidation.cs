using System;
using System.Collections;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Effect.Rendering;

namespace URUI.Effect.PlayerValidation
{
    /// <summary>
    /// 实际屏幕回归：在可见 Editor 或 Player 运行，SRP 必须安装真实提交 feature。
    /// 离屏相机和 Frame Debugger 重放不能证明 CameraTarget 边界正确。
    /// </summary>
    public sealed class ScreenSurfaceValidation : MonoBehaviour
    {
        Document document;
        Texture2D red, staticGreen, halfRed;
        Material sceneMaterial;
        Mesh sceneMesh;
        RenderTexture green;
        GameObject cameraObject, uiObject;
        TextureEffectSurface processed;
        EffectModule module;
        IDisposable binding;
        Material effect;
        bool released;
        static object allocationProbe;

        [Serializable]
        sealed class Report
        {
            public bool passed;
            public bool ordinaryRegionPassed, groupBackdropPassed, fullRootBackdropPassed;
            public Color[] regionScene, regionOrdinary, regionGrouped, regionFullRoot;
            public bool splashFinished;
            public int width, height, frames;
            public long processingPasses, positiveAllocationEvents, layoutAllocationEvents;
            public long rootTransformAllocationEvents, rootTextAllocationEvents;
            public bool[] loweredLayerCasesPassed;
            public Color[] loweredLayerActual, loweredLayerExpected, loweredLayerScene;
            public string unity, graphics, colorSpace, pipeline;
            public string error;
            public Color first, middle, last, background;
            public Color grayWithUi, grayReference, hdrWithUi, hdrReference;
            public Color viewportFirst, viewportMiddle, viewportLast;
            public bool backgroundReferencePassed, viewportPassed, zeroAlphaReferencePassed;
            public Color zeroWithUi, zeroReference, halfOverlay, halfOverlayExpected;
        }

        IEnumerator Start()
        {
            var start = Time.frameCount;
            var processingBefore = TextureEffectSurface.GetStatistics().ProcessingPasses;
            cameraObject = new GameObject("Screen validation camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            start = Time.frameCount;

            red = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            red.SetPixel(0, 0, Color.red);
            red.Apply();
            staticGreen = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            staticGreen.SetPixel(0, 0, Color.green); staticGreen.Apply();
            halfRed = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            halfRed.SetPixel(0, 0, new Color(1, 0, 0, .5f)); halfRed.Apply();
            green = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
            green.Create();
            var old = RenderTexture.active;
            try { RenderTexture.active = green; GL.Clear(false, true, Color.green); }
            finally { RenderTexture.active = old; }

            uiObject = new GameObject("Screen validation document");
            var surface = uiObject.AddComponent<Surface>();
            surface.Viewport = new Vector2Int(Screen.width, Screen.height);
            surface.Input.UseUnityInput = false;
            processed = new TextureEffectSurface(16, 16);
            effect = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            effect.SetVector("_BandColor", Vector4.one);
            document = uiObject.AddComponent<Document>();
            document.Surface = surface;
            module = new EffectModule(document);
            document.ImageResolver = path => path.EndsWith("green-static.png", StringComparison.Ordinal) ? new ImageLease(staticGreen)
                : path.EndsWith("half-red.png", StringComparison.Ordinal) ? new ImageLease(halfRed)
                : path.EndsWith("green.png", StringComparison.Ordinal) ? processed.Output.Acquire() : new ImageLease(red);
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}" +
                "img{position:absolute;top:0;width:16px;height:16px;}" +
                "#intermediate{--urui-image-effect:surface-output;}" +
                "#layout-probe{position:absolute;left:80px;top:80px;width:10px;height:10px;}" +
                "</style></head><body><img src='red.png' style='left:0;'>" +
                "<img id='intermediate' src='green.png' style='left:8px;'>" +
                "<img src='red.png' style='left:16px;'><img id='half-overlay' src='red.png' style='left:40px;opacity:.5;'>" +
                "<div id='layout-probe'></div></body></html>",
                "/screen-surface-validation.html");
            binding = module.BindSurface(document.Q("intermediate"),
                new SurfaceEffectInput(processed, green, effect, () => (ulong)Time.frameCount));

            while (Time.frameCount - start < 300) yield return null;
            yield return new WaitForEndOfFrame();
            var result = new Report
            {
                frames = Time.frameCount - start,
                splashFinished = UnityEngine.Rendering.SplashScreen.isFinished,
                unity = Application.unityVersion,
                graphics = SystemInfo.graphicsDeviceType.ToString(),
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                    ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().FullName : "Built-in"
            };
            var output = Output();
            Directory.CreateDirectory(output);
            var read = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                result.width = read.width;
                result.height = read.height;
                File.WriteAllBytes(Path.Combine(output, "screen.png"), read.EncodeToPNG());
                result.first = read.GetPixel(4, read.height - 8);
                result.middle = read.GetPixel(12, read.height - 8);
                result.last = read.GetPixel(28, read.height - 8);
                result.background = read.GetPixel(80, read.height - 80);
                result.processingPasses = TextureEffectSurface.GetStatistics().ProcessingPasses - processingBefore;
            }
            finally { Destroy(read); }

            // 同时验证 IL2CPP 的钩子元数据与无消费者通知。原生布局确实变化，
            // 隐形空节点不引入绘制、图片拓扑或表面尺寸变化等其他分配来源。
            var probe = document.Q("layout-probe");
            var iteration = 0;
            Action layout = () =>
            {
                probe.Style.Set("width", ++iteration % 2 == 0 ? "10px" : "11px");
                document.SynchronizeLayout();
            };
            for (var i = 0; i < 300; i++) layout();
            result.positiveAllocationEvents = Allocations(() =>
            {
                allocationProbe = new byte[4096];
                GC.KeepAlive(allocationProbe);
            });
            allocationProbe = null;
            result.layoutAllocationEvents = Allocations(() => { for (var i = 0; i < 32; i++) layout(); });
            // 比较同一真实相机启用/隐藏 UI 的屏幕背景，避免把色彩空间、HDR
            // 或后处理输出猜成固定数值。此诊断不参与任何性能计时。
            camera.backgroundColor = new Color(.18f, .42f, .73f, 1);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            result.grayWithUi = ReadScreenBackground();
            document.SetPresentation(0, 0, false, false);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            result.grayReference = ReadScreenBackground();
            camera.backgroundColor = new Color(1.8f, .23f, .55f, .6f);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            result.hdrReference = ReadScreenBackground();
            document.SetPresentation(0, 0, true, false);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            result.hdrWithUi = ReadScreenBackground();
            result.backgroundReferencePassed = Near(result.grayWithUi, result.grayReference) &&
                Near(result.hdrWithUi, result.hdrReference) &&
                Mathf.Abs(result.grayWithUi.a - result.grayReference.a) < 3f / 255 &&
                Mathf.Abs(result.hdrWithUi.a - result.hdrReference.a) < 3f / 255 && result.grayReference.b > .1f &&
                Mathf.Abs(result.grayReference.r - result.hdrReference.r) > .1f;

            // Unity 相机允许 RGB 非零、Alpha 为零；屏幕 RGB 仍是有效场景颜色。
            // 同时验证半透明 CSS 组与这个实际背景按编码颜色合成。
            camera.backgroundColor = new Color(.18f, .42f, .73f, 0);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            var zeroRead = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                result.zeroWithUi = zeroRead.GetPixel(80, zeroRead.height - 80);
                result.halfOverlay = zeroRead.GetPixel(44, zeroRead.height - 8);
            }
            finally { Destroy(zeroRead); }
            document.SetPresentation(0, 0, false, false);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            result.zeroReference = ReadScreenBackground();
            result.halfOverlayExpected = Color.Lerp(result.zeroReference, Color.red, .5f);
            result.zeroAlphaReferencePassed = Near(result.zeroWithUi, result.zeroReference) &&
                Mathf.Abs(result.zeroWithUi.a - result.zeroReference.a) < 3f / 255 &&
                Near(result.halfOverlay, result.halfOverlayExpected) &&
                Mathf.Abs(result.halfOverlay.a - result.halfOverlayExpected.a) < 3f / 255;
            document.SetPresentation(0, 0, true, false);

            // 移除末尾透明组，使最后一段普通几何可以直接解析到屏幕；
            // 偏移视口像素同时验证这个合并输出的实际朝向和落点。
            document.Q("half-overlay").Style.Set("display", "none");
            // 非零视口原点和缩放仍以完整输出目标保留场景内容。
            camera.rect = new Rect(.25f, .25f, .5f, .5f);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            var viewportRead = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var viewport = camera.pixelRect;
                result.viewportFirst = viewportRead.GetPixel((int)viewport.x + 2, (int)viewport.yMax - 4);
                result.viewportMiddle = viewportRead.GetPixel((int)viewport.x + 6, (int)viewport.yMax - 4);
                result.viewportLast = viewportRead.GetPixel((int)viewport.x + 14, (int)viewport.yMax - 4);
                result.viewportPassed = Near(result.viewportFirst, Color.red) && Near(result.viewportMiddle, Color.green) &&
                    Near(result.viewportLast, Color.red);
                File.WriteAllBytes(Path.Combine(output, "viewport.png"), viewportRead.EncodeToPNG());
            }
            finally { Destroy(viewportRead); }
            // 非对称场景颜色能揭示屏幕裁剪的 Y 朝向错误；Alpha0仍保留RGB。
            // 第一份文档只有普通图像，第二份使用独立透明组，分别验证根区域
            // 和已有 backdrop 快照。两者均不依赖页面名称或特殊渲染器代码。
            binding?.Dispose(); binding = null;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.orthographic = true; camera.orthographicSize = 1;
            var backdrop = new GameObject("Asymmetric scene backdrop", typeof(MeshFilter), typeof(MeshRenderer));
            backdrop.transform.SetParent(cameraObject.transform, false);
            backdrop.transform.localPosition = new Vector3(0, 0, 10);
            var aspect = (float)Screen.width / Screen.height;
            sceneMesh = new Mesh();
            sceneMesh.vertices = new[] { new Vector3(-aspect,-1), new Vector3(aspect,-1), new Vector3(aspect,1), new Vector3(-aspect,1) };
            sceneMesh.colors = new[] { new Color(.1f,.2f,.8f,0), new Color(.9f,.1f,.2f,0), new Color(.2f,.8f,.1f,0), new Color(.7f,.7f,.7f,0) };
            sceneMesh.triangles = new[] { 0,1,2,0,2,3 }; sceneMesh.RecalculateBounds();
            sceneMaterial = new Material(UnityEngine.Resources.Load<Shader>("URUI.Validation/SceneBackdrop"));
            backdrop.GetComponent<MeshFilter>().sharedMesh = sceneMesh;
            backdrop.GetComponent<MeshRenderer>().sharedMaterial = sceneMaterial;
            document.SetPresentation(0, 0, false, false);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            var sceneRead = ScreenCapture.CaptureScreenshotAsTexture();
            var sampleX = new[] { 68, 76, 92, 108 };
            result.regionScene = new Color[4];
            try
            {
                for (var i = 0; i < sampleX.Length; i++) result.regionScene[i] = sceneRead.GetPixel(sampleX[i], sceneRead.height - 40);
                File.WriteAllBytes(Path.Combine(output, "root-scene-reference.png"), sceneRead.EncodeToPNG());
            }
            finally { Destroy(sceneRead); }
            IDisposable noOpDrawing = null;
            for (var group = 0; group < 3; group++)
            {
                var opacity = group == 1 ? .5f : 1f;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;" + (group == 1 ? "opacity:.5;" : "") +
                    "}img{position:absolute;top:32px;width:16px;height:16px;}</style></head><body>" +
                    "<img src='red.png' style='left:64px;'><img src='green-static.png' style='left:72px;'>" +
                    "<img src='red.png' style='left:80px;'><img src='half-red.png' style='left:104px;'>" +
                    (group == 2 ? "<div id='root-probe' style='width:1px;height:1px;--urui-decorator:shader(root-probe);'></div>" : "") + "</body></html>",
                    "/root-region-validation.html");
                if (group == 2) noOpDrawing = document.AttachPainter(document.Q("root-probe"), "root-probe", context => { });
                document.SetPresentation(0, 0, true, false);
                for (var frame = 0; frame < 16; frame++) yield return null;
                yield return new WaitForEndOfFrame();
                var actual = new Color[4]; var regionRead = ScreenCapture.CaptureScreenshotAsTexture();
                var valid = true;
                try
                {
                    for (var i = 0; i < sampleX.Length; i++)
                    {
                        actual[i] = regionRead.GetPixel(sampleX[i], regionRead.height - 40);
                        var source = i == 1 ? Color.green : Color.red;
                        var alpha = opacity * (i == 3 ? 128f / 255 : 1);
                        var expected = Color.Lerp(result.regionScene[i], source, alpha);
                        valid &= Near(actual[i], expected) && Mathf.Abs(actual[i].a - expected.a) < 3f / 255;
                    }
                    File.WriteAllBytes(Path.Combine(output, group == 0 ? "root-region.png" : group == 1 ? "root-group-backdrop.png" : "root-full-backdrop.png"), regionRead.EncodeToPNG());
                }
                finally { Destroy(regionRead); }
                if (group == 0) { result.ordinaryRegionPassed = valid; result.regionOrdinary = actual; }
                else if (group == 1) { result.groupBackdropPassed = valid; result.regionGrouped = actual; }
                else { result.fullRootBackdropPassed = valid; result.regionFullRoot = actual; }
            }
            noOpDrawing?.Dispose();
            // 直接遮罩会把作者离屏层降为根几何；普通、解析遮罩和保留图层
            // 均须保留同一真实屏幕背景。用独立 source-over 数值而非旧实现作参考。
            var layerSamples = new[] { new Vector2Int(72, 88), new Vector2Int(88, 88), new Vector2Int(156, 88), new Vector2Int(88, 40), new Vector2Int(300, 300) };
            document.SetPresentation(0, 0, false, false);
            for (var frame = 0; frame < 16; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            var layerScene = ScreenCapture.CaptureScreenshotAsTexture();
            result.loweredLayerScene = new Color[layerSamples.Length];
            try { for (var i = 0; i < layerSamples.Length; i++) result.loweredLayerScene[i] = layerScene.GetPixel(layerSamples[i].x, layerScene.height - layerSamples[i].y); }
            finally { Destroy(layerScene); }
            result.loweredLayerCasesPassed = new bool[4];
            result.loweredLayerActual = new Color[4 * layerSamples.Length]; result.loweredLayerExpected = new Color[4 * layerSamples.Length];
            for (var variant = 0; variant < 4; variant++)
            {
                var filter = variant == 3;
                var rule = variant == 0 ? "mask-image:url(green-static.png);mask-size:100% 100%;" : filter ? "filter:blur(1px) brightness(.8);" :
                    "--urui-mask-image:shader(feather-mask);--urui-mask-feather-top:32px;";
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#group{position:absolute;left:64px;top:32px;width:96px;height:96px;" + rule +
                    "}#group div{position:absolute;width:80px;height:96px;background:rgba(255,0,0,.5);}</style></head><body><div id='group'><div></div>" +
                    (variant == 2 ? "<div style='left:16px;background:rgba(0,255,0,.5);'></div>" : "") + "</div></body></html>", "/lowered-layer-validation.html");
                using var feather = variant == 1 || variant == 2 ? new FeatherMaskPainter(document.Q("group")) : null;
                document.SetPresentation(0, 0, true, false);
                for (var frame = 0; frame < 16; frame++) yield return null;
                yield return new WaitForEndOfFrame();
                var layerRead = ScreenCapture.CaptureScreenshotAsTexture(); var valid = true;
                try
                {
                    for (var i = 0; i < layerSamples.Length; i++)
                    {
                        var alpha = 128f / 255;
                        // 屏幕 GetPixel(h-y) 的像素中心在文档 y-.5；羽化线性距离为 7.5px。
                        var featherAlpha = (variant == 1 || variant == 2) && i == 3 ? 7.5f / 32 : 1;
                        var redWeight = i == 0 || i == 1 || i == 3 ? alpha : 0;
                        var greenWeight = variant == 2 && (i == 1 || i == 2 || i == 3) ? alpha : 0;
                        redWeight *= (1 - greenWeight) * featherAlpha; greenWeight *= featherAlpha;
                        var expected = result.loweredLayerScene[i] * (1 - redWeight - greenWeight) +
                            (filter ? new Color(.8f, 0, 0, 1) : Color.red) * redWeight + Color.green * greenWeight;
                        var actual = layerRead.GetPixel(layerSamples[i].x, layerRead.height - layerSamples[i].y);
                        result.loweredLayerActual[variant * layerSamples.Length + i] = actual; result.loweredLayerExpected[variant * layerSamples.Length + i] = expected;
                        valid &= Near(actual, expected) && Mathf.Abs(actual.a - expected.a) < 3f / 255;
                    }
                    File.WriteAllBytes(Path.Combine(output, "lowered-layer-" + variant + ".png"), layerRead.EncodeToPNG());
                }
                finally { Destroy(layerRead); }
                result.loweredLayerCasesPassed[variant] = valid;
            }
            module.Dispose(); module = null;
            // 动态 ink 不能反复租还根目标；使用预先准备的字符串隔离业务格式化分配。
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}" +
                "#moving{position:absolute;left:64px;top:32px;width:16px;height:16px;}" +
                "#label{position:absolute;left:104px;top:32px;}</style></head><body>" +
                "<img id='moving' src='red.png'><span id='label'>A</span></body></html>", "/root-parameter-validation.html");
            var moving = document.Q("moving"); var label = document.Q("label");
            var parameterIteration = 0;
            Action transform = () =>
            {
                moving.Style.Set("transform", ++parameterIteration % 2 == 0 ? "translate(0px,0px)" : "translate(.5px,-.25px)");
                document.SynchronizeLayout();
            };
            Action text = () =>
            {
                label.Text = ++parameterIteration % 2 == 0 ? "A" : "B";
                document.SynchronizeLayout();
            };
            for (var i = 0; i < 300; i++) { transform(); text(); }
            result.rootTransformAllocationEvents = Allocations(() => { for (var i = 0; i < 32; i++) transform(); });
            result.rootTextAllocationEvents = Allocations(() => { for (var i = 0; i < 32; i++) text(); });
            result.passed = result.ordinaryRegionPassed && result.groupBackdropPassed && result.fullRootBackdropPassed &&
                Array.TrueForAll(result.loweredLayerCasesPassed, value => value) &&
                result.backgroundReferencePassed && result.zeroAlphaReferencePassed && result.viewportPassed &&
                result.processingPasses > 250 && result.positiveAllocationEvents > 0 &&
                result.layoutAllocationEvents == 0 && result.rootTransformAllocationEvents == 0 && result.rootTextAllocationEvents == 0 && Near(result.first, Color.red) &&
                Near(result.middle, Color.green) && Near(result.last, Color.red) && Near(result.background, Color.blue);
            if (!result.passed) result.error = "Expected ordered RGB in full and offset viewports, unchanged gray/HDR scene background, positive GC probe and zero unobserved layout allocations.";
            File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(result, true));
            Debug.Log("Screen surface validation " + result.passed);
            Cleanup();
            for (var frame = 0; frame < 16; frame++) yield return null;
            if (!Application.isEditor) Application.Quit(result.passed ? 0 : 2);
        }

        static Color ReadScreenBackground()
        {
            var read = ScreenCapture.CaptureScreenshotAsTexture();
            try { return read.GetPixel(80, read.height - 80); }
            finally { Destroy(read); }
        }

        static long Allocations(Action action)
        {
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            action();
            recorder.Stop();
            if (!recorder.Valid || recorder.WrappedAround) throw new InvalidOperationException("GC.Alloc recorder unavailable or overflowed.");
            long count = 0;
            for (var i = 0; i < recorder.Count; i++) count += recorder.GetSample(i).Count;
            return count;
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 3f / 255 &&
            Mathf.Abs(a.g - b.g) < 3f / 255 && Mathf.Abs(a.b - b.b) < 3f / 255;
        static string Output() => Environment.GetEnvironmentVariable("URUI_SCREEN_SURFACE_OUTPUT") ??
            Path.Combine(Application.persistentDataPath, "urui-screen-surface");
        void OnDestroy() => Cleanup();
        void Cleanup()
        {
            if (released) return;
            released = true;
            binding?.Dispose();
            module?.Dispose();
            if (document && document.IsLoaded) document.Unload();
            processed?.Dispose();
            if (effect) GpuRetirement.Retire(() => { if (effect) Destroy(effect); });
            if (uiObject) Destroy(uiObject);
            if (cameraObject) Destroy(cameraObject);
            var first = red;
            var second = green;
            var third = staticGreen; var fourth = halfRed; var backgroundProgram = sceneMaterial; var backgroundGeometry = sceneMesh;
            GpuRetirement.Retire(() =>
            {
                if (first) Destroy(first);
                if (third) Destroy(third); if (fourth) Destroy(fourth);
                if (backgroundProgram) Destroy(backgroundProgram); if (backgroundGeometry) Destroy(backgroundGeometry);
                if (second) { second.Release(); Destroy(second); }
            });
        }
    }
}
