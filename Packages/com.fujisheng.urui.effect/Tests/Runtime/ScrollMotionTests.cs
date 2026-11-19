using global::URUI.Controls;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Events;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class ScrollMotionTests
    {
        GameObject host;
        GameObject cameraObject;
        Document document;
        ScrollMotion motion;
        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            host = new GameObject("Independent authored scroll effect");
            document = EffectHtmlFixtureAssets.Create(host);
            document.Surface.Input.UseUnityInput = false;
            document.TextResources = new[]{
                new TextResource("/URUI.Effect.Tests/ScrollMotion.styles.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/ScrollMotion.styles")),};
            EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/ScrollMotion");
            motion = new ScrollMotion(document.Q("viewport"), document.Q("content"));
        }
        [TearDown]
        public void TearDown()
        {
            motion?.Dispose();
            if (document && document.IsLoaded)
            {
                document.Unload();
            }
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(cameraObject);
        }

        [UnityTest]
        public IEnumerator MouseDragUsesLocalCoordinatesWheelUnitsAndNeverClicks()
        {
            yield return null;
            yield return null;
            var viewport = document.Q("viewport");
            var target = document.Q("target");
            var clicks = 0;
            Assert.That(viewport.ScrollSize.y, Is.GreaterThan(600));
            using var observer = target.RegisterCallback<ClickEvent>(_ => clicks++);
            var start = viewport.LocalToWorld(new Vector2(100, 150));
            document.Surface.Input.SendPointer(PointerPhase.Down, start);
            for (var step = 1; step <= 4; step++)
            {
                yield return new WaitForSecondsRealtime(.03f);
                document.Surface.Input.SendPointer(PointerPhase.Move, start - new Vector2(0, 20 * step), pressedButtons: 1);
            }
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(100).Within(1), "80 文档像素在 .8 缩放视口中应为 100 本地像素。");
            document.Surface.Input.SendPointer(PointerPhase.Up, start - new Vector2(0, 80));
            Assert.That(clicks, Is.Zero);
            var released = viewport.ScrollOffset.y;
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(viewport.ScrollOffset.y, Is.GreaterThan(released + 2));
            viewport.ScrollOffset = new Vector2(0, 100);
            yield return null;
            yield return null;
            document.Surface.Input.SendPointer(PointerPhase.Wheel, start, delta: new Vector2(0, 20));
            yield return null;
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(120).Within(1), "滚轮采用作者灵敏度，不乘原生默认行高。");
        }

        [UnityTest]
        public IEnumerator TouchOverscrollSpringsBackAndCancellationAllowsTap()
        {
            yield return null;
            yield return null;
            var viewport = document.Q("viewport");
            var content = document.Q("content");
            var clicks = 0;
            using var observer = document.Q("target").RegisterCallback<ClickEvent>(_ => clicks++);
            var start = viewport.LocalToWorld(new Vector2(100, 70));
            var initial = content.WorldBound.y;
            document.Surface.Input.SendPointer(PointerPhase.Down, start, 7, true);
            yield return new WaitForSecondsRealtime(.03f);
            document.Surface.Input.SendPointer(PointerPhase.Move, start + new Vector2(0, 64), 7, true);
            yield return null;
            Assert.That(viewport.ScrollOffset.y, Is.Zero);
            Assert.That(content.WorldBound.y, Is.GreaterThan(initial + 10), "原生夹取范围之外必须仍有可见弹性位移。");
            document.Surface.Input.SendPointer(PointerPhase.Up, start + new Vector2(0, 64), 7, true);
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(content.WorldBound.y, Is.EqualTo(initial).Within(1));
            Assert.That(clicks, Is.Zero);
            document.Surface.Input.SendPointer(PointerPhase.Down, start, 7, true);
            document.Surface.Input.SendPointer(PointerPhase.Cancel, start, 7, true);
            document.Surface.Input.SendPointer(PointerPhase.Down, start, 7, true);
            document.Surface.Input.SendPointer(PointerPhase.Up, start, 7, true);
            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ProgrammaticScrollUsesLocalOffsetsAndSpringsFromElasticBoundary()
        {
            yield return null;
            yield return null;
            var method = typeof(ScrollMotion).GetMethod("ScrollTo");
            Assert.That(method, Is.Not.Null, "程序定位应复用真实滚动边界和惯性控制器。");
            var viewport = document.Q("viewport");
            var content = document.Q("content");
            method.Invoke(motion, new object[] { new Vector2(0, 180) });
            yield return null;
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(180).Within(1));
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(180).Within(1), "程序定位必须清除旧惯性。");
            method.Invoke(motion, new object[] { new Vector2(0, -70) });
            yield return null;
            Assert.That(viewport.ScrollOffset.y, Is.Zero);
            Assert.That(content.WorldBound.y, Is.GreaterThan(20));
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(content.WorldBound.y, Is.EqualTo(0).Within(1));
            motion.Dispose();
            Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(motion, new object[] { Vector2.zero }));
            motion = null;
        }

        [UnityTest]
        public IEnumerator ProgrammaticScrollCancelsPressedContentAndHonorsClampedBoundary()
        {
            yield return null;
            yield return null;
            var method = typeof(ScrollMotion).GetMethod("ScrollTo");
            Assert.That(method, Is.Not.Null);
            var viewport = document.Q("viewport");
            var content = document.Q("content");
            viewport.AddToClassList("clamped");
            yield return null;
            var target = document.Q("target");
            var clicks = 0;
            using var observer = target.RegisterCallback<ClickEvent>(_ => clicks++);
            var point = target.WorldBound.center;
            document.Surface.Input.SendPointer(PointerPhase.Down, point, 7, true);
            method.Invoke(motion, new object[] { new Vector2(0, -70) });
            document.Surface.Input.SendPointer(PointerPhase.Up, point, 7, true);
            yield return null;
            Assert.That(clicks, Is.Zero);
            Assert.That(viewport.ScrollOffset.y, Is.Zero);
            Assert.That(content.WorldBound.y, Is.EqualTo(0).Within(1));
            var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(motion, new object[] { new Vector2(0, float.NaN) }));
            Assert.That(failure.InnerException, Is.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [UnityTest]
        public IEnumerator ExternalScrollIsAdoptedAndDisposeRestoresAuthorStyle()
        {
            yield return null;
            yield return null;
            var viewport = document.Q("viewport");
            var content = document.Q("content");
            viewport.ScrollOffset = new Vector2(0, 175);
            yield return null;
            yield return null;
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(175));
            Assert.That(content.WorldBound.y, Is.EqualTo(-140).Within(1));
            var start = viewport.LocalToWorld(new Vector2(100, 70));
            document.Surface.Input.SendPointer(PointerPhase.Down, start, 7, true);
            document.Surface.Input.SendPointer(PointerPhase.Move, start - new Vector2(0, 40), 7, true);
            document.Surface.Input.SendPointer(PointerPhase.Cancel, start, 7, true);
            motion.Dispose();
            motion = null;
            Assert.That(content.Style.Get("--urui-scroll-motion-y"), Is.Null);
            document.Unload();
        }
    }
}
