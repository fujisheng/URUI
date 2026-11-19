using global::URUI.Animation;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class StaggeredRevealTests
    {
        [UnityTest]
        public IEnumerator AuthoredDelayStartsAfterActivationAndBeforeFirstItem()
        {
            var host = new GameObject("Delayed stagger");
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            StaggeredReveal reveal = null;
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.TextResources = new[] { new TextResource("/URUI.Effect.Tests/StaggeredViewport.styles.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/StaggeredViewport.styles")) };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/StaggeredViewport");
                var root = document.Q("reveal");
                root.AddToClassList("delayed");
                reveal = new StaggeredReveal(root, document.Q("entries"), "active", "prepared", "revealed");
                yield return null;
                yield return new WaitForSecondsRealtime(.4f);
                root.RemoveFromClassList("prepared");
                root.AddToClassList("active");
                yield return new WaitForSecondsRealtime(.15f);
                Assert.IsFalse(document.Q("first").ClassListContains("revealed"), "准备时间不能消耗作者声明的启动延迟。");
                yield return new WaitForSecondsRealtime(.25f);
                Assert.IsTrue(document.Q("first").ClassListContains("revealed"));
            }
            finally
            {
                reveal?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator PreparedWaitDoesNotConsumeStaggerAndEntryHasIntermediateMotion()
        {
            var host = new GameObject("Prepared stagger timeline");
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            StaggeredReveal reveal = null;
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.TextResources = new[] { new TextResource("/URUI.Effect.Tests/StaggeredViewport.styles.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/StaggeredViewport.styles")) };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/StaggeredViewport");
                var root = document.Q("reveal");
                var entries = document.Q("entries");
                var first = document.Q("first");
                var second = document.Q("second");
                reveal = new StaggeredReveal(root, entries, "active", "prepared", "revealed");
                yield return null;
                yield return new WaitForSecondsRealtime(.25f);
                var initial = first.WorldBound.x;
                root.RemoveFromClassList("prepared");
                root.AddToClassList("active");
                yield return null;
                Assert.IsFalse(second.ClassListContains("revealed"), "布局准备等待不能消耗实际逐项启动间隔。");
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(first.WorldBound.x, Is.GreaterThan(60));
                Assert.That(first.WorldBound.x, Is.LessThan(initial - 10), "实际关键帧必须有中间位移，不能逐项瞬移到终态。");
                yield return new WaitForSecondsRealtime(.6f);
                Assert.That(first.WorldBound.x, Is.EqualTo(50).Within(.1f));
            }
            finally
            {
                reveal?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator ParentViewportEdgeStaysFixedWhenScrollableContentGrows()
        {
            var host = new GameObject("Viewport-bound staggered effect");
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            StaggeredReveal reveal = null;
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.TextResources = new[] { new TextResource("/URUI.Effect.Tests/StaggeredViewport.styles.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/StaggeredViewport.styles")) };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/StaggeredViewport");
                var root = document.Q("reveal");
                var entries = document.Q("entries");
                var viewport = document.Q("viewport");
                var first = document.Q("first");
                reveal = new StaggeredReveal(root, entries, "active", "prepared", "revealed");
                yield return null;
                yield return null;
                Assert.That(first.WorldBound.x, Is.EqualTo(viewport.WorldBound.xMax).Within(.02f), "条目应从视口边缘进入，不能从更宽的滚动内容边缘进入。");
                root.RemoveFromClassList("prepared");
                yield return null;
                yield return null;
                entries.Style.SetWidth(1000);
                root.AddToClassList("prepared");
                yield return null;
                yield return null;
                Assert.That(first.WorldBound.x, Is.EqualTo(viewport.WorldBound.xMax).Within(.02f), "内容增长不应改变视口边缘。");
                reveal.Dispose();
                reveal = null;
                document.Unload();
            }
            finally
            {
                reveal?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator AuthoredSequenceUsesMeasuredEdgesAndReleasesDynamicOverrides()
        {
            var host = new GameObject("Independent staggered effect");
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            StaggeredReveal reveal = null;
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.TextResources = new[] { new TextResource("/URUI.Effect.Tests/Staggered.styles.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Staggered.styles")) };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/Staggered");
                var root = document.Q("reveal");
                var entries = document.Q("entries");
                reveal = new StaggeredReveal(root, entries, "active", "prepared", "revealed");
                yield return null;
                yield return null;
                var first = document.Q("first");
                var second = document.Q("second");
                var third = document.Q("third");
                Assert.AreEqual("-50px", first.Style.Get("--urui-stagger-distance"));
                Assert.AreEqual("-120px", second.Style.Get("--urui-stagger-distance"));
                Assert.AreEqual("-180px", third.Style.Get("--urui-stagger-distance"));
                root.RemoveFromClassList("prepared");
                root.AddToClassList("active");
                var start = Time.unscaledTime;
                var firstSeen = -1f;
                var thirdSeen = -1f;
                while (Time.unscaledTime - start < 1.5f && thirdSeen < 0)
                {
                    yield return null;
                    if (first.ClassListContains("revealed") && firstSeen < 0)
                    {
                        firstSeen = Time.unscaledTime;
                    }
                    if (second.ClassListContains("revealed"))
                    {
                        Assert.IsTrue(first.ClassListContains("revealed"));
                    }
                    if (third.ClassListContains("revealed"))
                    {
                        Assert.IsTrue(second.ClassListContains("revealed"));
                        thirdSeen = Time.unscaledTime;
                    }
                }
                Assert.That(thirdSeen - firstSeen, Is.GreaterThan(.25f));
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(first.WorldBound.x, Is.EqualTo(10).Within(.02f));
                Assert.That(third.WorldBound.x, Is.EqualTo(140).Within(.02f));
                reveal.Dispose();
                reveal = null;
                Assert.IsNull(first.Style.Get("--urui-stagger-distance"));
                Assert.IsFalse(first.ClassListContains("revealed"));
                document.Unload();
            }
            finally
            {
                reveal?.Dispose();
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
