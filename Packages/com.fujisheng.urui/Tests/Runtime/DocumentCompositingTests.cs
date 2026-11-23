using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Events;

namespace URUI.Tests
{
    public sealed class DocumentCompositingTests
    {
        GameObject root;
        GameObject cameraObject;
        Camera camera;
        RenderTexture target;
        Surface surface;
        Document a;
        Document b;
        [SetUp]
        public void SetUp()
        {
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType,
                "This gate requires actual GPU rendering; run the isolated project without -nographics.");
            cameraObject = new GameObject("Compositing camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = true;
            camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            root = new GameObject("Compositing Surface");
            surface = root.AddComponent<Surface>();
            surface.Viewport = new Vector2Int(128, 128);
            surface.Input.UseUnityInput = false;
            a = Create("A");
            b = Create("B");
        }
        Document Create(string name)
        {
            var node = new GameObject(name);
            node.transform.SetParent(root.transform, false);
            var document = node.AddComponent<Document>();
            document.Surface = surface;
            return document;
        }
        static string Html(string css, string body) => "<!doctype html><html><head><style>body{margin:0;}" + css + "</style></head><body>" + body + "</body></html>";
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
        static void Rgb(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(2f / 255));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(2f / 255));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(2f / 255));
        }
        static Color Css(Color encoded) => QualitySettings.activeColorSpace == ColorSpace.Linear ? encoded.linear : encoded;
        [UnityTest]
        public IEnumerator LoadedDocumentsKeepRenderingAfterMainCameraIsReplaced()
        {
            a.LoadHtml(Html("#panel{width:100px;height:100px;background-color:red;opacity:.5;filter:blur(1px);}",
                "<div id='panel'></div>"), "ui/camera-replacement.html");
            yield return null;
            yield return null;
            var panel = a.Q("panel");
            Rgb(Pixel(20, 20), new Color(.5f, 0, 0));
            Object.DestroyImmediate(cameraObject);
            camera = null;
            Assert.DoesNotThrow(() => a.SynchronizeLayout());
            Assert.IsFalse(surface.SubmitRendering(default, target, new Rect(0, 0, 128, 128)));
            // A Surface can outlive a scene camera; the gap must not issue invalid draws.
            yield return null;
            cameraObject = new GameObject("Replacement camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = target;
            a.SynchronizeLayout();
            Rgb(Pixel(20, 20), new Color(.5f, 0, 0));
            yield return null;
            yield return null;
            Assert.IsTrue(panel.IsValid);
            Rgb(Pixel(20, 20), new Color(.5f, 0, 0));
            b.LoadHtml(Html("#top{width:40px;height:40px;background-color:blue;}", "<div id='top'></div>"),
                "ui/camera-replacement-top.html");
            yield return null;
            Rgb(Pixel(20, 20), Color.blue);
            b.Unload();
            yield return null;
            Rgb(Pixel(20, 20), new Color(.5f, 0, 0));
        }
        [TearDown]
        public void TearDown()
        {
            if (root)
            {
                Object.DestroyImmediate(root);
            }
            if (cameraObject)
            {
                Object.DestroyImmediate(cameraObject);
            }
            if (target)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
        [UnityTest]
        public IEnumerator GroupOpacityBlendsNonPrimaryColorsInProjectColorSpace()
        {
            a.LoadHtml(Html("#base{width:128px;height:128px;background-color:#335577;}" +
                "#group{position:absolute;left:20px;top:20px;width:100px;height:100px;opacity:.5;background-color:#aa6622;}" +
                "#child{position:absolute;left:20px;top:20px;width:60px;height:60px;background-color:#779933;}",
                "<div id='base'><div id='group'><div id='child'></div></div></div>"), "ui/project-color-opacity.html");
            yield return null;
            yield return null;
            Color SceneColor(byte r, byte g, byte b)
            {
                var encoded = new Color(r / 255f, g / 255f, b / 255f);
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? encoded.linear : encoded;
            }
            var background = SceneColor(51, 85, 119);
            Rgb(Pixel(5, 5), background);
            Rgb(Pixel(25, 25), Color.Lerp(background, SceneColor(170, 102, 34), .5f));
            Rgb(Pixel(45, 45), Color.Lerp(background, SceneColor(119, 153, 51), .5f));
        }

        [UnityTest]
        public IEnumerator HiddenParentAllowsVisibleChildrenToPaintAndReceiveInput()
        {
            a.LoadHtml(Html(
                "#parent{visibility:hidden;width:100px;height:100px;background-color:red;}" +
                "#inherited{display:block;width:10px;height:10px;background-color:red;}" +
                "#shown{position:absolute;left:20px;top:20px;width:40px;height:40px;visibility:visible;background-color:lime;border:0;padding:0;}" +
                "#layer{position:absolute;left:80px;top:0;width:40px;height:40px;visibility:hidden;transform:translate(5px,0);opacity:.5;background-color:red;}" +
                "#child{display:block;width:30px;height:30px;visibility:visible;background-color:blue;}",
                "<div id='parent'><span id='inherited'></span><button id='shown'></button></div><div id='layer'><div id='child'></div></div>"),
                "ui/visibility.html");
            yield return null;
            yield return null;
            Assert.IsFalse(a.Q("parent").IsVisibleInHierarchy);
            Assert.IsFalse(a.Q("inherited").IsVisibleInHierarchy);
            Assert.IsTrue(a.Q("shown").IsVisibleInHierarchy);
            Rgb(Pixel(5, 5), Color.black);
            Rgb(Pixel(30, 30), Color.green);
            Rgb(Pixel(90, 15), new Color(0, 0, .5f));
            var clicks = 0;
            using var subscription = a.Q("shown").RegisterCallback<ClickEvent>(_ => clicks++);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(30, 30), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(30, 30));
            Assert.AreEqual(1, clicks);
            a.Q("parent").Style.Set("display", "none");
            yield return null;
            yield return null;
            Assert.IsFalse(a.Q("shown").IsVisibleInHierarchy);
            Rgb(Pixel(30, 30), Color.black);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(30, 30), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(30, 30));
            Assert.AreEqual(1, clicks);
            a.Q("parent").Style.Set("display", "block");
            yield return null;
            yield return null;
            Rgb(Pixel(30, 30), Color.green);
        }

        [UnityTest]
        public IEnumerator SharedAuthoredSliderSkinPaintsAndTracksPointerInput()
        {
            var clock = new SkinClock();
            surface.UIClock = clock;
            a.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            a.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SliderSkin"), "/urui/tests/slider.html");
            var control = a.Q<URUI.Controls.Slider>("input");
            yield return null;
            yield return null;
            Assert.That(a.Q("slider-host").Layout.height, Is.EqualTo(44).Within(.01f));
            Rgb(Pixel(20, 30), Color.red);
            Rgb(Pixel(108, 30), Color.blue);
            Rgb(Pixel(64, 30), Color.white);
            var changes = 0;
            using var callback = control.RegisterValueChangedCallback(_ => changes++);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(89, 30), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(89, 30));
            // 先提交状态变更，再推进到100ms过渡结束；像素断言不依赖测试帧率。
            a.SynchronizeGeometry();
            clock.Now += .2;
            yield return null;
            yield return null;
            Assert.That(control.Value, Is.EqualTo(75).Within(.01f));
            Assert.AreEqual(1, changes);
            Assert.That(a.Q("fill").Layout.width, Is.EqualTo(75).Within(.01f));
            Rgb(Pixel(64, 30), Color.red);
            Rgb(Pixel(89, 30), Css(new Color(245f / 255, 245f / 255, 245f / 255)));
            control.SetEnabled(false);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(34, 30), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(34, 30));
            Assert.That(control.Value, Is.EqualTo(75).Within(.01f));
            Assert.AreEqual(1, changes);
        }

        sealed class SkinClock : URUI.Animation.IUIClock
        {
            public double Now
            {
                get;
                set;
            } = 1;
            public float Delta => 0;
        }

        [UnityTest]
        public IEnumerator SharedAuthoredSelectSkinPaintsOpenOptionsAndDisabledRows()
        {
            a.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            a.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/SelectSkin"), "/urui/tests/select.html");
            var control = a.Q<URUI.Controls.Dropdown>("input");
            yield return null;
            yield return null;
            Rgb(Pixel(100, 16), Color.blue);
            Rgb(Pixel(100, 52), Color.black);
            control.SetValueWithoutNotify("c");
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(60, 26), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(60, 26));
            yield return null;
            yield return null;
            Rgb(Pixel(100, 52), Color.red);
            Rgb(Pixel(100, 72), Color.blue);
            Rgb(Pixel(100, 92), Color.green);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(60, 72), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(60, 72));
            Assert.AreEqual("c", control.Value);
            Assert.IsFalse(a.Q("popup").HasAttribute("hidden"));
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(60, 52), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(60, 52));
            yield return null;
            yield return null;
            Assert.AreEqual("a", control.Value);
            Assert.IsTrue(a.Q("popup").HasAttribute("hidden"));
            Rgb(Pixel(100, 92), Color.black);
        }

        [UnityTest]
        public IEnumerator SharedAuthoredScrollbarPaintsThumbAndScrolledContent()
        {
            a.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            a.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/ScrollSkin"), "/urui/tests/scroll.html");
            var viewport = a.Q<URUI.Controls.ScrollView>("viewport");
            yield return null;
            yield return null;
            Rgb(Pixel(115, 15), Color.white);
            Rgb(Pixel(115, 60), Color.green);
            Rgb(Pixel(20, 20), Color.red);
            viewport.ScrollOffset = new Vector2(0, 160);
            yield return null;
            yield return null;
            Rgb(Pixel(115, 45), Color.white);
            Rgb(Pixel(20, 20), Color.red);
            Rgb(Pixel(20, 80), Color.blue);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(115, 50), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Move, new Vector2(115, 81), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(115, 81));
            yield return null;
            yield return null;
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(320).Within(.01f));
            Rgb(Pixel(115, 80), Color.white);
            Rgb(Pixel(20, 20), Color.blue);
        }

        [UnityTest]
        public IEnumerator SharedAuthorProgressSlotsPaintInitialAndChangedValues()
        {
            a.TextResources = new[] { new URUI.Resources.TextResource("/urui/controls.css",
                UnityEngine.Resources.Load<TextAsset>("URUI/controls")) };
            a.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/OtherControls"), "/urui/tests/other.html");
            var progress = a.Q<URUI.Controls.ProgressBar>("progress");
            yield return null;
            yield return null;
            Rgb(Pixel(20, 91), Color.green);
            Rgb(Pixel(95, 91), Color.blue);
            progress.SetValueWithoutNotify(25);
            yield return null;
            yield return null;
            Rgb(Pixel(80, 91), Color.green);
        }

        [UnityTest]
        public IEnumerator NativeAutoScrollbarFallbackStillPaintsPrivateThumb()
        {
            a.LoadHtml(Html("#viewport{width:40px;height:80px;overflow:auto;background-color:blue;}#content{width:20px;height:400px;}",
                "<div id='viewport'><div id='content'></div></div>"), "ui/native-scrollbar.html");
            yield return null;
            yield return null;
            var thumb = Pixel(35, 5);
            Rgb(thumb, Color.white);
            Rgb(Pixel(35, 60), Css(new Color(217f / 255, 217f / 255, 217f / 255)));
        }

        [UnityTest]
        public IEnumerator DragPreviewKeepsPageResourcesAndTypedOpacity()
        {
            var image = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            image.SetPixel(0, 0, Color.green);
            image.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("image.png", image) };
                a.LoadHtml(Html("@property --drag-opacity{syntax:\"<number>\";inherits:false;initial-value:.5;}" +
                    "#drag{--urui-drag:clone;width:40px;height:40px;background-color:red;opacity:var(--drag-opacity);}" +
                    "img{width:10px;height:10px;}", "<div id='drag'><img src='image.png'></div>"), "ui/a/page.html");
                b.LoadHtml(Html("@property --drag-opacity{syntax:\"<number>\";inherits:false;initial-value:.8;}body{display:none;}", ""), "ui/b/page.html");
                yield return null;
                yield return null;
                surface.Input.SendPointer(PointerPhase.Down, new Vector2(5, 5), pressedButtons: 1);
                surface.Input.SendPointer(PointerPhase.Move, new Vector2(20, 20), pressedButtons: 1);
                surface.Input.SendPointer(PointerPhase.Move, new Vector2(70, 70), pressedButtons: 1);
                yield return null;
                yield return null;
                var originalPixel = Pixel(20, 20);
                var previewPixel = Pixel(70, 70);
                var imagePixel = Pixel(55, 55);
                Rgb(originalPixel, new Color(.5f, 0, 0));
                Rgb(previewPixel, new Color(.5f, 0, 0));
                Rgb(imagePixel, new Color(0, .5f, 0));
                surface.Input.SendPointer(PointerPhase.Up, new Vector2(70, 70));
                yield return null;
                Rgb(Pixel(70, 70), Color.black);
            }
            finally
            {
                a.Unload();
                b.Unload();
                Object.DestroyImmediate(image);
            }
        }

        [UnityTest]
        public IEnumerator ParentAndChildOpacityComposeAsGroups()
        {
            a.LoadHtml(Html("#parent{width:100px;height:100px;background-color:red;opacity:.5;}#child{width:40px;height:40px;background-color:blue;opacity:.5;}", "<div id='parent'><div id='child'></div></div>"), "ui/opacity.html");
            yield return null;
            yield return null;
            Rgb(Pixel(80, 20), new Color(.5f, 0, 0));
            Rgb(Pixel(20, 20), new Color(.25f, 0, .25f));
        }
        [UnityTest]
        public IEnumerator DefaultBackdropCannotSampleAnotherDocument()
        {
            a.LoadHtml(Html("body{background-color:red;width:128px;height:128px;}", ""), "ui/a.html");
            b.LoadHtml(Html("#panel{width:80px;height:80px;backdrop-filter:brightness(.5);}", "<div id='panel'></div>"), "ui/b.html");
            yield return null;
            yield return null;
            Rgb(Pixel(20, 20), Color.red);
            Rgb(Pixel(100, 20), Color.red);
        }
        [UnityTest]
        public IEnumerator ExplicitSurfaceBackdropSamplesOnlyIntoItsPanel()
        {
            a.LoadHtml(Html("body{background-color:red;width:128px;height:128px;}", ""), "ui/a.html");
            b.LoadHtml(Html("#panel{width:80px;height:80px;backdrop-filter:brightness(.5);--urui-backdrop-scope:surface;}", "<div id='panel'></div>"), "ui/b.html");
            yield return null;
            yield return null;
            Rgb(Pixel(20, 20), Css(new Color(.5f, 0, 0)));
            Rgb(Pixel(100, 20), Color.red);
        }
        [UnityTest]
        public IEnumerator TransformedChildKeepsAncestorOverflowClipAndOtherPageState()
        {
            a.LoadHtml(Html("#clip{width:50px;height:50px;overflow:hidden;}#child{width:50px;height:50px;background-color:blue;transform:translateX(30px);}", "<div id='clip'><div id='child'></div></div>"), "ui/a.html");
            b.LoadHtml(Html("#green{position:absolute;left:90px;top:10px;width:20px;height:20px;background-color:lime;}", "<div id='green'></div>"), "ui/b.html");
            yield return null;
            yield return null;
            Rgb(Pixel(40, 20), Color.blue);
            Rgb(Pixel(70, 20), Color.black);
            Rgb(Pixel(100, 20), Color.green);
        }

        [UnityTest]
        public IEnumerator SubmittedImageLeaseWaitsForGpuRetirementAndReleasesOnce()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red, Color.red, Color.red, Color.red, Color.red,
                Color.red, Color.red, Color.red, Color.red, Color.red, Color.red, Color.red, Color.red });
            texture.Apply();
            var released = 0;
            try
            {
                a.ImageResolver = _ => new URUI.Resources.ImageLease(texture, () => released++);
                a.LoadHtml(Html("img{width:40px;height:40px;}", "<img src='image.png'>"), "ui/image.html");
                yield return null;
                yield return null;
                // Actual rendering submission, without a synchronous pixel readback.
                camera.Render();
                a.Unload();
                Assert.AreEqual(0, released, "Submitted image pins must pass through GPU retirement after the native document reference is gone.");
                for (var frame = 0; frame < 30 && released == 0; ++frame)
                {
                    yield return null;
                }
                Assert.AreEqual(1, released);
                for (var frame = 0; frame < 3; ++frame)
                {
                    yield return null;
                }
                Assert.AreEqual(1, released);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator MixedBackdropScopesIncludeThisDocumentsPriorPixels()
        {
            a.LoadHtml(Html("body{background-color:blue;width:128px;height:128px;}", ""), "ui/a.html");
            b.LoadHtml(Html("body{background-color:red;width:128px;height:128px;}#local{position:absolute;left:80px;top:0;width:48px;height:80px;backdrop-filter:brightness(.5);}#surface{width:60px;height:80px;backdrop-filter:brightness(.5);--urui-backdrop-scope:surface;}", "<div id='local'></div><div id='surface'></div>"), "ui/b.html");
            yield return null;
            yield return null;
            Rgb(Pixel(20, 20), Css(new Color(.5f, 0, 0)));
            Rgb(Pixel(100, 20), Css(new Color(.5f, 0, 0)));
        }

        [UnityTest]
        public IEnumerator FilterAndMaskStateDoesNotLeakIntoNextDocument()
        {
            var mask = new Texture2D(80, 80, TextureFormat.RGBA32, false);
            var pixels = new Color[80 * 80];
            for (var index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = index % 80 < 40 ? Color.white : Color.clear;
            }
            mask.SetPixels(pixels);
            mask.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("mask.png", mask) };
                a.LoadHtml(Html("#masked{width:80px;height:80px;background-color:blue;transform:translateX(10px);overflow:hidden;mask-image:url('mask.png');filter:brightness(.5);}", "<div id='masked'></div>"), "ui/a.html");
                b.LoadHtml(Html("#green{position:absolute;left:90px;top:10px;width:20px;height:20px;background-color:lime;}", "<div id='green'></div>"), "ui/b.html");
                yield return null;
                yield return null;
                var left = Pixel(20, 20);
                var right = Pixel(70, 20);
                var next = Pixel(100, 20);
                Debug.Log($"T09 mask pixels left={left} right={right} next={next}");
                Rgb(left, Css(new Color(0, 0, .5f)));
                Rgb(right, Color.black);
                Rgb(next, Color.green);
            }
            finally
            {
                Object.DestroyImmediate(mask);
            }
        }
        [UnityTest]
        public IEnumerator StandardMaskUrlRepeatsAtNaturalSizeWithoutChangingSourceTexture()
        {
            var mask = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color[16];
            for (var index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = index % 4 < 2 ? Color.white : Color.clear;
            }
            mask.SetPixels(pixels);
            mask.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("mask.png", mask) };
                a.LoadHtml(Html("#masked{width:80px;height:80px;background-color:blue;mask-image:url(mask.png);}", "<div id='masked'></div>"), "ui/mask.html");
                yield return null;
                yield return null;
                Rgb(Pixel(20, 20), Color.blue);
                Rgb(Pixel(22, 20), Color.black);
                Rgb(Pixel(24, 20), Color.blue);
                Assert.AreEqual(TextureWrapMode.Clamp, mask.wrapMode);
                Assert.AreEqual(FilterMode.Point, mask.filterMode);
            }
            finally
            {
                Object.DestroyImmediate(mask);
            }
        }
        [UnityTest]
        public IEnumerator StandardBackgroundImagesPaintBelowBorderAndInsetShadow()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color[16];
            for (var index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = Color.green;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("pattern.png", texture) };
                a.LoadHtml(Html("#box{position:absolute;left:12px;top:12px;width:60px;height:60px;border:10px solid rgba(255,0,0,.5);background-image:url(pattern.png);box-shadow:inset 10px 0 0 blue, -5px -5px 0 red;}", "<div id='box'></div>"), "ui/background.html");
                yield return null;
                yield return null;
                foreach (var shadow in new[] { "none", "-5px -5px 0 red", "inset 10px 0 0 blue", "inset 10px 0 0 blue, -5px -5px 0 red" })
                {
                    a.Q("box").Style.Set("box-shadow", shadow);
                    yield return null;
                    yield return null;
                    Rgb(Pixel(20, 20), new Color(.5f, .5f, 0));
                }

                // 内阴影覆盖图像，不能预先烘焙在底色中。
                Rgb(Pixel(25, 40), Color.blue);
                Rgb(Pixel(40, 40), Color.green);
                // 外阴影在元素后方。
                Rgb(Pixel(9, 40), Color.red);
                a.Q("box").Style.Set("background-size", "20px 20px");
                a.Q("box").Style.Set("background-repeat", "no-repeat");
                a.Q("box").Style.Set("background-position", "right bottom");
                yield return null;
                yield return null;
                Rgb(Pixel(40, 40), Color.black);
                Rgb(Pixel(70, 70), Color.green);
                Assert.AreEqual(TextureWrapMode.Clamp, texture.wrapMode);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator ProgressGameDirectionsUseUruiAttributes()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.green);
            texture.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("fill.png", texture) };
                a.LoadHtml(Html("progress{position:absolute;left:0;top:0;width:40px;height:40px;--urui-fill-image:fill.png;}#obsolete{left:50px;}#quarter{top:50px;}",
                    "<progress id='bottom' value='.5' data-urui-progress-direction='bottom'></progress><progress id='obsolete' value='.5' direction='bottom'></progress><progress id='quarter' value='.25' data-urui-progress-direction='clockwise' data-urui-progress-start-edge='left'></progress>"), "ui/progress.html");
                yield return null;
                yield return null;
                Rgb(Pixel(30, 10), Color.green);
                Rgb(Pixel(30, 30), Color.black);
                Rgb(Pixel(60, 30), Color.green);
                Rgb(Pixel(80, 10), Color.black);
                Rgb(Pixel(10, 60), Color.green);
                Rgb(Pixel(30, 60), Color.black);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator AtlasRegionsAndNinePatchUseDocumentImageResources()
        {
            var texture = new Texture2D(10, 10, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color[100];
            for (var y = 0; y < 10; ++y)
            {
                for (var x = 0; x < 10; ++x)
                {
                    var edgeX = x < 2 || x >= 8;
                    var edgeY = y < 2 || y >= 8;
                    pixels[y * 10 + x] = edgeX && edgeY ? Color.red : edgeX || edgeY ? Color.green : Color.blue;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            try
            {
                a.Images = new[] {new URUI.Resources.ImageAsset("atlas.png",texture,new[] {
                    new URUI.Resources.SpriteRegion("outer",new Rect(0,0,10,10)),new URUI.Resources.SpriteRegion("inner",new Rect(2,2,6,6)),
                    new URUI.Resources.SpriteRegion("corner",new Rect(0,0,2,2)),new URUI.Resources.SpriteRegion("center",new Rect(2,2,6,6))})};
                a.LoadHtml(Html("img{position:absolute;width:20px;height:20px;}#center{left:30px;}#patch{position:absolute;top:30px;width:40px;height:40px;--urui-decorator:ninepatch(outer,inner);}",
                    "<img id='corner' data-urui-sprite='corner'><img id='center' data-urui-sprite='center'><div id='patch'></div>"), "ui/atlas.html");
                yield return null;
                yield return null;
                Rgb(Pixel(10, 10), Color.red);
                Rgb(Pixel(40, 10), Color.blue);
                Rgb(Pixel(1, 31), Color.red);
                Rgb(Pixel(20, 50), Color.blue);
                Rgb(Pixel(20, 31), Color.green);
                Assert.AreEqual(TextureWrapMode.Clamp, texture.wrapMode);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator NoneImageLayersPreserveColorClipAndDoNotMaskTheElement()
        {
            a.LoadHtml(Html("div{position:absolute;width:20px;height:20px;background-color:blue;}#single{left:0;mask-image:none;}#multi{left:40px;mask-image:none,none;}#background{left:80px;border:4px solid transparent;background-image:none,none;background-clip:border-box,padding-box;}", "<div id='single'></div><div id='multi'></div><div id='background'></div>"), "ui/none-layers.html");
            yield return null;
            yield return null;
            Rgb(Pixel(5, 5), Color.blue);
            Rgb(Pixel(45, 5), Color.blue);
            Rgb(Pixel(82, 2), Color.black);
            Rgb(Pixel(88, 8), Color.blue);
        }

        [UnityTest]
        public IEnumerator StandardGradientImagesSupportShapesSizingRepeatingAndAlphaMasks()
        {
            a.LoadHtml(Html("#box{width:80px;height:80px;background-image:linear-gradient(to right, red 0%, red 50%, blue 50%, blue 100%);}", "<div id='box'></div>"), "ui/gradient.html");
            yield return null;
            yield return null;
            Rgb(Pixel(20, 20), Color.red);
            Rgb(Pixel(60, 20), Color.blue);
            var style = a.Q("box").Style;
            style.Set("background-size", "20px 10px");
            style.Set("background-position", "right bottom");
            style.Set("background-repeat", "no-repeat");
            yield return null;
            yield return null;
            Rgb(Pixel(10, 10), Color.black);
            Rgb(Pixel(65, 75), Color.red);
            Rgb(Pixel(75, 75), Color.blue);
            style.Set("background-size", "auto");
            style.Set("background-position", "left top");
            style.Set("background-image", "radial-gradient(circle at 40px 40px, lime 0px, lime 10px, blue 10px, blue 40px)");
            yield return null;
            yield return null;
            Rgb(Pixel(40, 40), Color.green);
            Rgb(Pixel(60, 40), Color.blue);
            style.Set("background-image", "conic-gradient(from 0deg at center, red 0deg, red 180deg, blue 180deg, blue 360deg)");
            yield return null;
            yield return null;
            Rgb(Pixel(60, 40), Color.red);
            Rgb(Pixel(20, 40), Color.blue);
            style.Set("background-image", "repeating-linear-gradient(to right, red 0px, red 4px, blue 4px, blue 8px)");
            yield return null;
            yield return null;
            Rgb(Pixel(1, 20), Color.red);
            Rgb(Pixel(5, 20), Color.blue);
            Rgb(Pixel(9, 20), Color.red);
            Rgb(Pixel(13, 20), Color.blue);
            style.Set("background-image", "none");
            style.Set("background-color", "lime");
            style.Set("mask-image", "linear-gradient(to right, white 0%, white 50%, transparent 50%, transparent 100%)");
            yield return null;
            yield return null;
            Rgb(Pixel(20, 20), Color.green);
            Rgb(Pixel(60, 20), Color.black);
            style.Set("mask-image", "none");
            style.Set("background-color", "blue");
            style.Set("background-image", "linear-gradient(rgba(255,0,0,.5), rgba(255,0,0,.5))");
            yield return null;
            yield return null;
            Rgb(Pixel(20, 20), new Color(.5f, 0, .5f));
        }

        [UnityTest]
        public IEnumerator StandardBorderImageSlicesFillAndOutsetUseStandardPaintOrder()
        {
            var texture = new Texture2D(10, 10, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color[100];
            for (var y = 0; y < 10; ++y)
            {
                for (var x = 0; x < 10; ++x)
                {
                    var edgeX = x < 2 || x >= 8;
                    var edgeY = y < 2 || y >= 8;
                    pixels[y * 10 + x] = edgeX && edgeY ? Color.red : edgeX || edgeY ? Color.green : Color.blue;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("border.png", texture) };
                a.LoadHtml(Html("#box{position:absolute;left:12px;top:12px;width:40px;height:40px;border:4px solid lime;border-radius:12px;background-color:white;border-image:url(border.png) 2 fill / 6px / 2px stretch;}", "<div id='box'></div>"), "ui/border-image.html");
                yield return null;
                yield return null;
                Rgb(Pixel(11, 11), Color.red);
                Rgb(Pixel(30, 11), Color.green);
                Rgb(Pixel(61, 30), Color.green);
                Rgb(Pixel(30, 30), Color.blue);
                a.Q("box").Style.SetOpacity(.5f);
                yield return null;
                yield return null;
                Rgb(Pixel(11, 11), new Color(.5f, 0, 0));
                Rgb(Pixel(30, 30), new Color(0, 0, .5f));
                a.Q("box").Style.SetOpacity(1f);
                a.Q("box").Style.Set("border-image-slice", "20%");
                yield return null;
                yield return null;
                Rgb(Pixel(30, 30), Color.white);
                Rgb(Pixel(11, 11), Color.red);
                a.Q("box").Style.Set("border", "4px solid lime");
                yield return null;
                yield return null;
                Rgb(Pixel(30, 14), Color.green);
                Rgb(Pixel(30, 30), Color.white);
                Rgb(Pixel(11, 11), Color.black);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator SameUrlUsesIndependentBackgroundAndMaskGeometry()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color[16];
            for (var index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = index % 4 < 2 ? Color.green : Color.clear;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("pattern.png", texture) };
                a.LoadHtml(Html("#box{width:80px;height:80px;background-color:blue;background-image:url(pattern.png);background-size:40px 40px;mask-image:none,url(pattern.png);mask-repeat:no-repeat,repeat;mask-size:1px 1px,8px 8px;}", "<div id='box'></div>"), "ui/image-layers.html");
                yield return null;
                yield return null;
                Rgb(Pixel(9, 9), Color.green);
                Rgb(Pixel(13, 9), Color.black);
                Rgb(Pixel(25, 9), Color.blue);
                Rgb(Pixel(29, 9), Color.black);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator PlainHtmlRendersTextWithPackagedDefaultFont()
        {
            a.LoadHtml(Html("body{width:128px;height:128px;background-color:white;}", "<div id='text'>MMMM</div>"), "ui/default-text.html");
            yield return null;
            yield return null;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(64, 32, TextureFormat.RGBA32, false, true);
            try
            {
                read.ReadPixels(new Rect(0, 96, 64, 32), 0, 0);
                read.Apply();
                var dark = 0;
                foreach (var color in read.GetPixels())
                {
                    if (color.r < .5f && color.g < .5f && color.b < .5f)
                    {
                        dark++;
                    }
                }
                Assert.Greater(dark, 8, "Normal HTML must render glyphs without an author font-family or an OS font path.");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(read);
            }
        }
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator RepeatingAndClampedDrawsShareTextureWithAutomaticBatching()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color[16];
            for (var index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = index % 4 < 2 ? new Color(1, 0, 0, .5f) : new Color(0, 1, 0, .5f);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            try
            {
                a.Images = new[] { new URUI.Resources.ImageAsset("pattern.png", texture) };
                a.LoadHtml(Html("#pattern{width:80px;height:80px;--urui-decorator:image('pattern.png' repeat left top);}#image{position:absolute;left:0;top:90px;width:80px;height:20px;}#gradient{position:absolute;left:0;top:112px;width:80px;height:8px;background-image:linear-gradient(to right, rgba(255,0,0,.5) 0%, rgba(255,0,0,.5) 50%, rgba(0,255,0,.5) 50%, rgba(0,255,0,.5) 100%);}", "<div id='pattern'></div><img id='image' src='pattern.png'><div id='gradient'></div>"), "ui/repeat.html");
                var host = root.GetComponentInChildren<URUI.Internal.Hosting.RenderHost>(true);
                yield return null;
                yield return null;
                Rgb(Pixel(20, 20), new Color(.5f, 0, 0));
                Rgb(Pixel(22, 20), new Color(0, .5f, 0));
                Rgb(Pixel(20, 100), new Color(.5f, 0, 0));
                Rgb(Pixel(60, 100), new Color(0, .5f, 0));
                Rgb(Pixel(20, 116), new Color(.5f, 0, 0));
                Rgb(Pixel(60, 116), new Color(0, .5f, 0));
                Assert.IsTrue(host.ShaderContractActive);
                Assert.AreEqual(TextureWrapMode.Clamp, texture.wrapMode);
                Assert.AreEqual(FilterMode.Point, texture.filterMode);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
#endif
    }
}
