using System;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;

namespace URUI.Tests
{
    public sealed class MultiDocumentLifecycleTests
    {
        GameObject root, camera;
        Surface surface;
        Document a, b;
        const string Html = "<!doctype html><html><head></head><body><div id='same'>page</div></body></html>";

        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("Shared Surface camera", typeof(Camera));
            camera.tag = "MainCamera";
            root = new GameObject("Shared Surface");
            surface = root.AddComponent<Surface>();
            surface.Input.UseUnityInput = false;
            a = Create("A"); b = Create("B");
        }

        Document Create(string name)
        {
            var node = new GameObject(name);
            node.transform.SetParent(root.transform, false);
            var document = node.AddComponent<Document>();
            document.Surface = surface;
            return document;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera);
        }

        [Test]
        public void IndependentDocumentsKeepSameIdsAndCloseOnlyTheirOwner()
        {
            a.LoadHtml(Html, "ui/a.html"); b.LoadHtml(Html, "ui/b.html");
            Assert.IsTrue(a.IsLoaded && b.IsLoaded);
            Assert.AreEqual("ui/a.html", a.SourceUrl);
            Assert.AreEqual("ui/b.html", b.SourceUrl);
            Assert.AreEqual("html", a.DocumentElement.TagName);
            Assert.AreEqual("body", a.Body.TagName);
            var first = a.Q("same"); var second = b.Q("same");
            Assert.AreNotSame(first, second);
            first.Text = "A"; second.Text = "B";
            Assert.AreEqual("A", first.Text); Assert.AreEqual("B", second.Text);
            Assert.Throws<ArgumentException>(() => a.Body.Add(second));
            var unloaded = 0; b.Unloaded += _ => unloaded++;
            a.Unload();
            Assert.IsFalse(first.IsValid);
            Assert.IsNull(a.RenderCommandBuffer);
            Assert.IsTrue(b.IsLoaded && second.IsValid);
            Assert.AreEqual("B", second.Text); Assert.AreEqual(0, unloaded);
            a.LoadHtml(Html, "ui/a-reopened.html");
            Assert.IsTrue(a.IsLoaded && b.IsLoaded);
            Assert.AreNotSame(first, a.Q("same"));
        }

        [Test]
        public void DisablingSurfaceRestoresEachDocumentAndInvalidatesOldElements()
        {
            a.LoadHtml(Html, "ui/a.html"); b.LoadHtml(Html, "ui/b.html");
            var first = a.Q("same"); var second = b.Q("same");
            var loadedA = 0; var loadedB = 0;
            a.Loaded += _ => loadedA++; b.Loaded += _ => loadedB++;
            surface.enabled = false;
            Assert.IsFalse(a.IsLoaded || b.IsLoaded);
            Assert.IsFalse(first.IsValid || second.IsValid);
            surface.enabled = true;
            Assert.IsTrue(a.IsLoaded && b.IsLoaded);
            Assert.AreEqual(1, loadedA); Assert.AreEqual(1, loadedB);
            Assert.AreEqual("ui/a.html", a.SourceUrl);
            Assert.AreEqual("ui/b.html", b.SourceUrl);
        }

        [Test]
        public void UnloadingLastDocumentKeepsSurfaceAvailableForNextPage()
        {
            a.LoadHtml(Html, "ui/a.html"); a.Unload();
            Assert.IsTrue(surface.isActiveAndEnabled);
            b.LoadHtml(Html, "ui/b.html");
            Assert.IsTrue(b.IsLoaded);
            Assert.AreEqual("page", b.Q("same").Text);
        }

        sealed class Clock : URUI.Animation.IUiClock
        {
            internal Clock(double now) { Now = now; }
            public double Now { get; }
            public float Delta => 0;
        }

        [TestCase(float.NaN, 0, 64, 64)]
        [TestCase(0, float.PositiveInfinity, 64, 64)]
        [TestCase(0, 0, float.PositiveInfinity, 64)]
        [TestCase(0, 0, 64, float.NegativeInfinity)]
        [TestCase(0, 0, 0, 64)]
        [TestCase(0, 0, 64, -1)]
        public void SurfaceSubmissionRejectsInvalidViewportBeforeAnyRecording(float x, float y, float width, float height)
        {
            var target = new UnityEngine.Rendering.RenderTargetIdentifier(UnityEngine.Rendering.BuiltinRenderTextureType.CameraTarget);
            Assert.Throws<ArgumentOutOfRangeException>(() => surface.SubmitRendering(default, target, new Rect(x, y, width, height)));
            Assert.IsFalse(a.IsLoaded || b.IsLoaded);
            Assert.IsFalse(surface.SubmitRendering(default, target, new Rect(3.5f, 7.25f, 64, 48)));
        }

        [Test]
        public void SharedClockCannotChangeWhileLoadedAndCanRebaseAfterLastClose()
        {
            var first = new Clock(10); surface.UiClock = first;
            a.LoadHtml(Html, "ui/a.html"); a.SynchronizeLayout();
            Assert.Throws<InvalidOperationException>(() => surface.UiClock = new Clock(0));
            Assert.AreSame(first, surface.UiClock);
            a.Unload(); surface.UiClock = new Clock(0);
            b.LoadHtml(Html, "ui/b.html");
            Assert.DoesNotThrow(() => b.SynchronizeLayout());
        }
    }
}
