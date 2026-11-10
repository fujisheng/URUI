using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Tests
{
    public sealed class DocumentResourceTests
    {
        GameObject root, camera;
        Document a, b;
        Surface surface;
        readonly List<UnityEngine.Object> assets = new();
        const string Html = "<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='box'>page</div></body></html>";
        const string ImageHtml = "<!doctype html><html><head><style>body{margin:0;}img{width:4px;height:4px;}</style></head><body><img id='image' src='icons/shared.png'></body></html>";

        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("Resource camera", typeof(Camera)); camera.tag = "MainCamera";
            root = new GameObject("Resource Surface"); surface = root.AddComponent<Surface>(); surface.Input.UseUnityInput = false;
            a = Create("A"); b = Create("B");
        }
        Document Create(string name)
        {
            var node = new GameObject(name); node.transform.SetParent(root.transform, false);
            var document = node.AddComponent<Document>(); document.Surface = surface; return document;
        }
        TextAsset Css(string value) { var asset = new TextAsset(value); assets.Add(asset); return asset; }
        Texture2D Image()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16]; Array.Fill(pixels, new Color32(255, 0, 0, 255));
            texture.SetPixels32(pixels); texture.Apply(); assets.Add(texture); return texture;
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(camera);
            foreach (var asset in assets) if (asset) UnityEngine.Object.DestroyImmediate(asset);
            assets.Clear();
        }

        [Test]
        public void ImageAssetsExposeDocumentSpriteRegions()
        {
            var type=typeof(ImageAsset).Assembly.GetType("URUI.Resources.SpriteRegion");
            Assert.IsNotNull(type,"图集区域必须通过文档图片资源声明。");
            Assert.IsNotNull(typeof(ImageAsset).GetConstructor(new[] {typeof(string),typeof(Texture2D),type.MakeArrayType()}));
        }

        [Test]
        public void SpriteRegionsKeepDocumentScopeSnapshotsAndFailedReloadState()
        {
            var texture=Image();
            var regions=new[] { new SpriteRegion("icon",new Rect(0,0,2,3)), new SpriteRegion("second",new Rect(2,0,1,2)) };
            a.Images=new[] { new ImageAsset("atlas.png",texture,regions) };
            regions[0]=new SpriteRegion("icon",new Rect(0,0,4,4));
            b.Images=new[] { new ImageAsset("atlas.png",texture,new[] {new SpriteRegion("icon",new Rect(2,2,2,2),2)}) };
            const string page="<!doctype html><html><head></head><body><img id='image' data-urui-sprite='icon'><img id='ordinary' src='atlas.png' sprite='icon' rect='0 0 1 1'></body></html>";
            a.LoadHtml(page,"ui/a.html"); b.LoadHtml(page,"ui/b.html");
            a.SynchronizeLayout(); b.SynchronizeLayout();
            Assert.AreEqual(new Vector2(2,3),a.Q("image").ContentRect.size);
            Assert.AreEqual(new Vector2(4,4),b.Q("image").ContentRect.size);
            Assert.AreEqual(new Vector2(4,4),a.Q("ordinary").ContentRect.size,"旧 sprite/rect 属性不再改变普通 img。");
            var image=a.Q<global::URUI.Controls.Image>("image"); image.SpriteName="second"; a.SynchronizeLayout();
            Assert.AreEqual(new Vector2(1,2),image.ContentRect.size);
            a.Images=new[] { new ImageAsset("atlas.png",texture,new[] {new SpriteRegion("bad",new Rect(3,0,2,2))}) };
            Assert.Throws<ArgumentException>(()=>a.LoadHtml(page,"ui/invalid.html")); Assert.IsTrue(image.IsValid);
            Assert.AreEqual("second",image.SpriteName); a.Unload(); Assert.IsTrue(b.IsLoaded);
            Assert.AreEqual(new Vector2(4,4),b.Q("image").ContentRect.size);
        }

        [Test]
        public void SpriteRegionNamesAndScaleValidateBeforeCandidateCreation()
        {
            var texture=Image();
            a.Images=new[] {new ImageAsset("a.png",texture,new[] {new SpriteRegion("icon",new Rect(0,0,2,2))}),
                new ImageAsset("b.png",texture,new[] {new SpriteRegion("icon",new Rect(0,0,2,2))})};
            const string page="<!doctype html><html><head></head><body></body></html>";
            Assert.Throws<ArgumentException>(()=>a.LoadHtml(page,"ui/invalid.html"));Assert.IsFalse(a.IsLoaded);
            a.Images=new[] {new ImageAsset("atlas.png",texture,new[] {new SpriteRegion("icon",new Rect(0,0,2,2),float.NaN)})};
            Assert.Throws<ArgumentException>(()=>a.LoadHtml(page,"ui/invalid.html"));Assert.IsFalse(a.IsLoaded);
        }

        [Test]
        public void SharedControlsStyleUsesStandardBordersAndUruiControlTags()
        {
            var sheet = global::UnityEngine.Resources.Load<TextAsset>("URUI/controls");
            Assert.IsNotNull(sheet);
            a.TextResources = new[] { new TextResource("ui/controls.css", sheet) };
            a.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='controls.css'></head><body>" +
                "<button id='button'>OK</button><urui-scroll-view id='scroll'></urui-scroll-view></body></html>", "ui/skin.html");
            a.SynchronizeLayout();
            Assert.That(a.Q("button").ContentRect.x, Is.EqualTo(13).Within(.01f), "12px padding + 1px solid border");
            Assert.That(a.Q("scroll").ResolvedStyle.Width, Is.EqualTo(240).Within(.01f));
            Assert.That(a.Q("scroll").ResolvedStyle.Height, Is.EqualTo(200).Within(.01f));
        }

        [Test]
        public void SharedBaseStyleLoadsPackagedFacesThroughExplicitDependencies()
        {
            var sheet = global::UnityEngine.Resources.Load<TextAsset>("URUI/base");
            Assert.IsNotNull(sheet, "浏览器和 URUI 必须能显式引用同一份基础 CSS。");
            var resources = new List<TextResource> { new TextResource("/urui/base.css", sheet) };
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    var font = global::UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name);
                    Assert.IsNotNull(font, name);
                    resources.Add(new TextResource("/urui/Fonts/" + name + ".bytes", font));
                }
            a.TextResources = resources.ToArray();
            a.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'>" +
                "<style>span,code{display:inline-block;}</style></head><body>" +
                "<span id='narrow'>iiiiii</span><span id='wide'>WWWWWW</span>" +
                "<code id='mono-narrow'>iiiiii</code><code id='mono-wide'>WWWWWW</code></body></html>", "ui/base.html");
            a.SynchronizeLayout();
            Assert.That(a.Styles.GetComputed(a.Q("narrow")).GetPixels("font-size"), Is.EqualTo(16));
            Assert.That(a.Q("wide").ResolvedStyle.Width, Is.GreaterThan(a.Q("narrow").ResolvedStyle.Width));
            Assert.That(a.Q("mono-wide").ResolvedStyle.Width, Is.EqualTo(a.Q("mono-narrow").ResolvedStyle.Width).Within(.01f));
        }

        [Test]
        public void MissingGlyphUsesEachDocumentsOrderedFamilyList()
        {
            var resources = new List<TextResource>();
            foreach (var family in new[] { "Mono", "Sans", "Serif" })
            {
                var name = "Liberation" + family + "-Regular.ttf";
                resources.Add(new TextResource("/fonts/" + family + ".ttf", global::UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name)));
            }
            a.TextResources = resources.ToArray(); b.TextResources = resources.ToArray();
            string Page(string family) => "<!doctype html><html><head><style>" +
                "@font-face{font-family:Primary;src:url('/fonts/Mono.ttf');}" +
                "@font-face{font-family:Fallback;src:url('/fonts/" + family + ".ttf');}" +
                "span{display:inline-block;font:30px Primary,Fallback;}#reference{font-family:Fallback;}" +
                "</style></head><body><span id='fallback'>\u0237\u0237\u0237\u0237</span>" +
                "<span id='reference'>\u0237\u0237\u0237\u0237</span></body></html>";
            a.LoadHtml(Page("Sans"), "ui/a.html"); a.SynchronizeLayout();
            var firstWidth = a.Q("reference").Layout.width;
            Assert.That(a.Q("fallback").Layout.width, Is.EqualTo(firstWidth).Within(.01f));
            b.LoadHtml(Page("Serif"), "ui/b.html"); b.SynchronizeLayout(); a.SynchronizeLayout();
            var secondWidth = b.Q("reference").Layout.width;
            Assert.That(secondWidth, Is.Not.EqualTo(firstWidth).Within(.01f));
            Assert.That(b.Q("fallback").Layout.width, Is.EqualTo(secondWidth).Within(.01f));
            Assert.That(a.Q("fallback").Layout.width, Is.EqualTo(firstWidth).Within(.01f));
            b.Unload(); a.SynchronizeLayout();
            Assert.That(a.Q("fallback").Layout.width, Is.EqualTo(firstWidth).Within(.01f));
        }

        [Test]
        public void HeadDependenciesCanBeConfiguredBeforeAnyDocumentIsLoaded()
        {
            a.TextResources = new[] { new TextResource("ui/theme.css", Css("#box{width:37px;}")) };
            a.LoadHtml(Html, "ui/a.html"); a.SynchronizeLayout();
            Assert.AreEqual(37, a.Styles.GetComputed(a.Q("box")).GetPixels("width"));
        }

        [Test]
        public void ResourceConfigurationStagesUntilSuccessfulReloadAndInvalidCssKeepsOldPage()
        {
            a.TextResources = new[] { new TextResource("ui/theme.css", Css("#box{width:37px;}")) };
            a.LoadHtml(Html, "ui/a.html"); a.SynchronizeLayout(); var original = a.Q("box");
            a.TextResources = new[] { new TextResource("ui/theme.css", Css("@property --bad{syntax:\"<angle>\";inherits:false;initial-value:0deg;}#box{width:99px;}")) };
            Assert.AreEqual(37, a.Styles.GetComputed(original).GetPixels("width"));
            Assert.Throws<InvalidOperationException>(() => a.LoadHtml(Html, "ui/failed.html"));
            Assert.IsTrue(original.IsValid); Assert.AreSame(original, a.Q("box"));
            Assert.AreEqual("ui/a.html", a.SourceUrl);
            a.TextResources = new[] { new TextResource("ui/theme.css", Css("#box{width:51px;}")) };
            a.LoadHtml(Html, "ui/reloaded.html"); a.SynchronizeLayout();
            Assert.IsFalse(original.IsValid);
            Assert.AreEqual(51, a.Styles.GetComputed(a.Q("box")).GetPixels("width"));
        }

        [Test]
        public void SameUriWithDifferentBytesCannotReplaceAnotherDocumentsFile()
        {
            a.TextResources = new[] { new TextResource("ui/theme.css", Css("#box{width:37px;}")) };
            b.TextResources = new[] { new TextResource("ui/theme.css", Css("#box{width:91px;}")) };
            a.LoadHtml(Html, "ui/a.html"); a.SynchronizeLayout(); var original = a.Q("box");
            Assert.Throws<InvalidOperationException>(() => b.LoadHtml(Html, "ui/b.html"));
            Assert.IsTrue(a.IsLoaded && original.IsValid); Assert.IsFalse(b.IsLoaded);
            Assert.AreEqual(37, a.Styles.GetComputed(original).GetPixels("width"));
        }

        [Test]
        public void InvalidFontCandidateKeepsCurrentStylesAndElementIdentities()
        {
            a.TextResources = new[] { new TextResource("ui/theme.css", Css("#box{width:37px;}")) };
            a.LoadHtml(Html, "ui/a.html"); a.SynchronizeLayout(); var original = a.Q("box");
            a.TextResources = new[] {
                new TextResource("ui/theme.css", Css("@font-face{font-family:broken;src:url(bad.ttf);}#box{font-family:broken;width:91px;}")),
                new TextResource("ui/bad.ttf", Css("invalid font bytes"))
            };
            Assert.Throws<InvalidOperationException>(() => a.LoadHtml(Html, "ui/failed-font.html"));
            Assert.AreSame(original, a.Q("box")); Assert.IsTrue(original.IsValid);
            Assert.AreEqual(37, a.Styles.GetComputed(original).GetPixels("width"));
        }

        [Test]
        public void SharedStaticImageRemainsAvailableAfterOtherDocumentCloses()
        {
            var texture = Image();
            a.Images = b.Images = new[] { new ImageAsset("icons/shared.png", texture) };
            a.LoadHtml(ImageHtml, "ui/a.html"); b.LoadHtml(ImageHtml, "ui/b.html"); a.SynchronizeLayout();
            Assert.IsTrue(a.TryLoadImage("icons/shared.png", out var first)); Assert.AreEqual(new Vector2Int(4, 4), first);
            a.Unload(); b.SynchronizeLayout();
            Assert.IsTrue(b.IsLoaded && b.Q("image").IsValid);
            Assert.IsTrue(b.TryLoadImage("icons/shared.png", out var second)); Assert.AreEqual(first, second);
        }

        [Test]
        public void ImageCallbackCanQueueOtherPageCloseAndUnusedLeaseReleasesOnce()
        {
            b.LoadHtml("<!doctype html><html><head></head><body></body></html>", "ui/b.html");
            var texture = Image(); var released = 0; var resolved = 0;
            a.ImageResolver = source => {
                Assert.AreEqual("ui/icons/shared.png", source); resolved++; b.Unload();
                return new ImageLease(texture, () => released++);
            };
            a.LoadHtml(ImageHtml, "ui/a.html");
            Assert.IsTrue(a.TryLoadImage("icons/shared.png", out _));
            Assert.IsTrue(a.IsLoaded); Assert.IsFalse(b.IsLoaded); Assert.AreEqual(1, resolved);
            Assert.AreEqual(0, released);
            a.Unload();
            Assert.AreEqual(1, released, "No frame was submitted; retirement must release this unused lease exactly once.");
        }

        [Test]
        public void StagedResolverDoesNotChangeActiveGeneration()
        {
            var texture = Image(); var oldCalls = 0; var nextCalls = 0; var releases = 0;
            a.ImageResolver = _ => { oldCalls++; return new ImageLease(texture, () => releases++); };
            a.LoadHtml(ImageHtml, "ui/a.html");
            a.ImageResolver = _ => { nextCalls++; return null; };
            Assert.IsTrue(a.TryLoadImage("icons/second.png", out _));
            Assert.AreEqual(2, oldCalls); Assert.AreEqual(0, nextCalls);
            a.Unload(); Assert.AreEqual(2, releases);
        }

        [Test]
        public void MissingImageCandidatePreservesCurrentPageAndResources()
        {
            a.TextResources = new[] { new TextResource("ui/theme.css", Css("#box{width:37px;}")) };
            a.LoadHtml(Html, "ui/a.html"); var original = a.Q("box");
            Assert.Throws<InvalidOperationException>(() => a.LoadHtml(ImageHtml, "ui/failed.html"));
            Assert.IsTrue(original.IsValid); Assert.AreSame(original, a.Q("box"));
            Assert.AreEqual("ui/a.html", a.SourceUrl);
        }

        [Test]
        public void DisablingInImageCallbackAbortsPublicationAndReleasesCandidate()
        {
            var texture = Image(); var releases = 0; var loaded = 0;
            a.Loaded += _ => loaded++;
            a.ImageResolver = _ => { a.enabled = false; return new ImageLease(texture, () => releases++); };
            Assert.Throws<InvalidOperationException>(() => a.LoadHtml(ImageHtml, "ui/a.html"));
            Assert.IsFalse(a.IsLoaded); Assert.AreEqual(0, loaded); Assert.AreEqual(1, releases);
        }

        [Test]
        public void DifferentTexturesAtOneCanonicalUriCannotReplaceOtherPage()
        {
            a.Images = new[] { new ImageAsset("icons/shared.png", Image()) };
            b.Images = new[] { new ImageAsset("icons/shared.png", Image()) };
            a.LoadHtml(ImageHtml, "ui/a.html"); var original = a.Q("image");
            Assert.Throws<InvalidOperationException>(() => b.LoadHtml(ImageHtml, "ui/b.html"));
            Assert.IsTrue(original.IsValid && a.IsLoaded); Assert.IsFalse(b.IsLoaded);
            Assert.IsTrue(a.TryLoadImage("icons/shared.png", out _));
        }

        [TestCase("/icons/shared.png", "ui/a.html")]
        [TestCase("icons/my%20icon.png", "ui/a.html")]
        [TestCase("icons/图标.png", "ui/a.html")]
        [TestCase("icons/图%20标.png", "ui/a.html")]
        [TestCase("../icons/shared.png", "ui/a.html")]
        [TestCase("/icons/shared.png", "https://example.test/ui/a.html")]
        public void StaticMappingUsesSameCanonicalUrlAsHtml(string source, string documentUrl)
        {
            a.Images = new[] { new ImageAsset(source, Image()) };
            a.LoadHtml("<!doctype html><html><head></head><body><img src='" + source + "'></body></html>", documentUrl);
            Assert.IsTrue(a.TryLoadImage(source, out var dimensions));
            Assert.AreEqual(new Vector2Int(4, 4), dimensions);
        }
    }
}
