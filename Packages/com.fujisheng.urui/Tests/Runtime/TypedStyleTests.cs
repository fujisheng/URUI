using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Styles;
using URUI.Internal.Rendering;
using Unity.Profiling;
namespace URUI.Tests
{
    public sealed class TypedStyleTests
    {
        GameObject root, camera;
        Document document;
        [SetUp] public void SetUp()
        {
            camera = new GameObject("Typed styles camera", typeof(Camera)); camera.tag = "MainCamera";
            root = new GameObject("Typed styles"); document = HtmlFixtureAssets.Create(root);
            document.Load(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/TypedStyle"), "URUI/Tests/TypedStyle.html"); document.SynchronizeLayout();
        }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(camera); }
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void EquivalentElementSnapshotsDoNotReallocateAllRegisteredPropertyNames(bool frozen, bool differentKeywords)
        {
            var html = new System.Text.StringBuilder("<!doctype html><html><head><style>");
            for (var i = 0; i < 200; i++)
            {
                html.Append("@property --shared-").Append(i).Append("{syntax:'<number>';inherits:false;initial-value:0;}");
            }
            html.Append("@property --shared-key{syntax:'a | b';inherits:false;initial-value:a;}div{width:10px;height:10px;}</style></head><body>");
            for (var i = 0; i < 17; i++)
            {
                html.Append("<div id='n").Append(i).Append("' style='--shared-key:").Append(differentKeywords && i % 2 == 1 ? "b" : "a").Append(";'></div>");
            }
            html.Append("</body></html>");
            document.LoadHtml(html.ToString(), "ui/shared-styles.html");
            document.SynchronizeLayout();
            var nodes = new URUI.Elements.Element[17];
            var prepared = new NativePreparedStyle[17];
            var snapshots = new ComputedStyleSnapshot[17];
            try
            {
                for (var i = 0; i < nodes.Length; i++)
                {
                    nodes[i] = document.Q("n" + i);
                    if (frozen) { prepared[i].Replace(nodes[i]); }
                }
                snapshots[0] = frozen ? prepared[0].Materialize(nodes[0]) : document.Styles.GetComputed(nodes[0]);
                using (var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    var probe = new byte[4096]; GC.KeepAlive(probe); positive.Stop();
                    Assert.IsTrue(positive.Valid && positive.Count > 0, "必须记录到真实 GC.Alloc 阳性探针。");
                }
                long allocations = 0;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    for (var i = 1; i < nodes.Length; i++)
                    {
                        snapshots[i] = frozen ? prepared[i].Materialize(nodes[i]) : document.Styles.GetComputed(nodes[i]);
                    }
                    recorder.Stop();
                    Assert.IsTrue(recorder.Valid && !recorder.WrappedAround);
                    for (var i = 0; i < recorder.Count; i++) { allocations += recorder.GetSample(i).Count; }
                }
                Assert.LessOrEqual(allocations, differentKeywords ? 384 : 96, "相同样式的 16 个新元素不能各自重新分配 200 个属性名和索引。");
                nodes[1].Style.SetNumber("--shared-30", 11); document.SynchronizeLayout();
                Assert.AreEqual(11, document.Styles.GetComputed(nodes[1]).GetNumber("--shared-30"));
                Assert.AreEqual(0, snapshots[1].GetNumber("--shared-30"));
                Assert.AreEqual(0, snapshots[2].GetNumber("--shared-30"));
                Assert.AreEqual(differentKeywords ? "b" : "a", snapshots[1].GetKeyword("--shared-key"));
                Assert.AreEqual("a", snapshots[2].GetKeyword("--shared-key"));
                var overridden = snapshots[2].WithOverrides(new System.Collections.Generic.Dictionary<string, StyleOverride> { { "--shared-key", StyleOverride.FromKeyword("b") } }, StyleValueOrigin.Transition);
                Assert.AreEqual("b", overridden.GetKeyword("--shared-key"));
                Assert.AreEqual("a", snapshots[2].GetKeyword("--shared-key"));
            }
            finally { for (var i = 0; i < prepared.Length; i++) { prepared[i].Dispose(); } }
        }
        [Test] public void ComputedDisplayReflectsPositionAndFloatBlockification()
        {
            document.LoadHtml("<!doctype html><html><head><style>" +
                "#normal{display:inline-block;}#absolute{position:absolute;display:inline-block;}" +
                "#fixed{position:fixed;display:inline-flex;}#floated{float:left;display:inline-table;}" +
                "#hidden{visibility:hidden;}</style></head><body><span id='normal'></span>" +
                "<span id='absolute'></span><span id='fixed'></span><span id='floated'></span>" +
                "<div id='hidden'><span id='inherited'></span></div></body></html>", "ui/display.html");
            document.SynchronizeLayout();
            Assert.AreEqual("inline-block", document.Styles.GetComputed(document.Q("normal")).GetKeyword("display"));
            var element = document.Q("absolute"); var before = document.Styles.GetComputed(element);
            Assert.AreEqual("block", before.GetKeyword("display"));
            Assert.AreEqual("flex", document.Styles.GetComputed(document.Q("fixed")).GetKeyword("display"));
            Assert.AreEqual("table", document.Styles.GetComputed(document.Q("floated")).GetKeyword("display"));
            Assert.AreEqual("hidden", document.Styles.GetComputed(document.Q("inherited")).GetKeyword("visibility"));
            element.Style.Set("position", "static"); document.SynchronizeLayout();
            Assert.AreEqual("inline-block", document.Styles.GetComputed(element).GetKeyword("display"));
            Assert.AreEqual("block", before.GetKeyword("display"));
        }
        [Test] public void PreparedStyleReadsTheFrozenVersionAfterLiveStyleChanges()
        {
            var child=document.Q("child");var prepared=new NativePreparedStyle();
            try
            {
                prepared.Replace(child);
                child.Style.SetNumber("--fx-progress",.75f);document.SynchronizeLayout();
                Assert.That(document.Styles.GetComputed(child).GetNumber("--fx-progress"),Is.EqualTo(.75f));
                var old=prepared.Materialize(child);Assert.That(old.GetNumber("--fx-progress"),Is.Zero);
                prepared.Replace(child);var current=prepared.Materialize(child);
                Assert.That(current.GetNumber("--fx-progress"),Is.EqualTo(.75f));Assert.That(old.GetNumber("--fx-progress"),Is.Zero);
                document.Unload();Assert.Throws<InvalidOperationException>(()=>old.GetNumber("--fx-progress"));
            }
            finally{prepared.Dispose();}
        }
        [Test] public void RegisteredNumbersRespectInitialInheritanceAndVar()
        {
            var child = document.Q("child"); var snapshot = document.Styles.GetComputed(child);
            Assert.AreEqual("top",snapshot.GetKeyword("--test-edge"));
            Assert.AreEqual(0, snapshot.GetNumber("--fx-progress"));
            Assert.That(snapshot.GetNumber("--inherited"), Is.EqualTo(.6f).Within(.00001f));
            Assert.AreEqual(0, snapshot.GetNumber("opacity"));
            Assert.AreEqual(120, snapshot.GetPixels("width")); Assert.AreEqual(4, snapshot.GetPixels("--padding"));
            Assert.AreEqual(0, snapshot.GetPixels("--zero"));
            Assert.That(snapshot.GetColor("--tint").r, Is.EqualTo(18f / 255).Within(.00001f));
            Assert.Throws<InvalidOperationException>(() => snapshot.GetColor("--fx-progress"));
        }
        [Test] public void SnapshotsRemainImmutableAndEqualWritesRetainRevision()
        {
            var child = document.Q("child"); var first = document.Styles.GetComputed(child);
            child.Style.SetNumber("--fx-progress", .123456789f); document.SynchronizeLayout();
            var changed = document.Styles.GetComputed(child);
            Assert.AreNotEqual(first.Revision, changed.Revision);
            Assert.AreEqual(0, first.GetNumber("--fx-progress")); Assert.AreEqual(.123456789f, changed.GetNumber("--fx-progress"));
            child.Style.SetNumber("--fx-progress", .123456789f); document.SynchronizeLayout();
            Assert.AreEqual(changed.Revision, document.Styles.GetComputed(child).Revision);
            document.Unload(); Assert.Throws<InvalidOperationException>(() => changed.GetNumber("--fx-progress"));
        }
        [Test] public void RetainedSnapshotsAndOverridesSurviveNumericAndTextChangesOnOtherElements()
        {
            var css = new System.Text.StringBuilder();
            for (var i = 0; i < 70; i++)
            {
                css.Append("@property --retained-").Append(i).Append(" { syntax: \"<number>\"; inherits:false; initial-value:0; }");
            }
            document.LoadHtml("<!doctype html><html><head><style>" + css +
                "</style></head><body><div id='a'></div><div id='b'></div></body></html>", "ui/retained.html");
            document.SynchronizeLayout();
            var a = document.Q("a"); var b = document.Q("b");
            var first = document.Styles.GetComputed(a);
            var overrides = new System.Collections.Generic.Dictionary<string, StyleOverride>();
            for (var i = 0; i < 70; i++) { overrides.Add("--retained-" + i, StyleOverride.Number(i + 1)); }
            overrides.Add("display", StyleOverride.FromKeyword("none"));
            var overridden = first.WithOverrides(overrides, StyleValueOrigin.Transition);
            for (var i = 0; i < 70; i++)
            {
                a.Style.SetNumber("--retained-" + i, i + 100);
                b.Style.SetNumber("--retained-" + i, i + 200);
            }
            a.Style.Set("display", "inline-flex"); b.Style.Set("display", "inline-block");
            document.SynchronizeLayout();
            var next = document.Styles.GetComputed(a);
            var other = document.Styles.GetComputed(b);
            for (var i = 0; i < 70; i++)
            {
                Assert.AreEqual(0, first.GetNumber("--retained-" + i));
                Assert.AreEqual(i + 1, overridden.GetNumber("--retained-" + i));
                Assert.AreEqual(i + 100, next.GetNumber("--retained-" + i));
                Assert.AreEqual(i + 200, other.GetNumber("--retained-" + i));
            }
            Assert.AreEqual("block", first.GetKeyword("display"));
            Assert.AreEqual("none", overridden.GetKeyword("display"));
            Assert.AreEqual("inline-flex", next.GetKeyword("display"));
            Assert.AreEqual("inline-block", other.GetKeyword("display"));
            a.Style.SetNumber("--retained-35", 999); document.SynchronizeLayout();
            Assert.AreEqual(999, document.Styles.GetComputed(a).GetNumber("--retained-35"));
            Assert.AreEqual(135, next.GetNumber("--retained-35"));
            Assert.AreEqual(36, overridden.GetNumber("--retained-35"));
        }
        [Test] public void NumericUpdatesRefreshDerivedVariablesAndInheritedValues()
        {
            document.LoadHtml("<!doctype html><html><head><style>" +
                "@property --source{syntax:'<number>';inherits:true;initial-value:.2;}" +
                "@property --derived{syntax:'<number>';inherits:false;initial-value:0;}" +
                "#parent{--derived:var(--source);--unregistered:var(--source);opacity:var(--source);}" +
                "</style></head><body><div id='parent'><div id='child'></div></div></body></html>", "ui/numeric-dependencies.html");
            document.SynchronizeLayout();
            var parent = document.Q("parent"); var child = document.Q("child");
            var before = document.Styles.GetComputed(parent);
            foreach (var value in new[] { .4f, .7f, .9f })
            {
                parent.Style.SetNumber("--source", value); document.SynchronizeLayout();
                var next = document.Styles.GetComputed(parent);
                Assert.AreEqual(value, next.GetNumber("--derived"));
                Assert.AreEqual(value, next.GetNumber("--unregistered"));
                Assert.AreEqual(value, next.GetNumber("opacity"));
                Assert.AreEqual(value, document.Styles.GetComputed(child).GetNumber("--source"));
                Assert.AreEqual(.2f, before.GetNumber("--derived"));
            }
        }
        [Test] public void NumericUpdatesAlsoRefreshRelativeLengthsAfterParentResize()
        {
            document.LoadHtml("<!doctype html><html><head><style>" +
                "@property --source{syntax:'<number>';inherits:false;initial-value:0;}" +
                "#parent{width:100px;}#child{width:10px;height:10px;margin-left:10%;}" +
                "</style></head><body><div id='parent'><div id='child'></div></div></body></html>", "ui/numeric-relative.html");
            document.SynchronizeLayout();
            var parent = document.Q("parent"); var child = document.Q("child");
            var before = document.Styles.GetComputed(child);
            Assert.AreEqual(10, before.GetPixels("margin-left"));
            parent.Style.Set("width", "200px"); child.Style.SetNumber("--source", 1);
            document.SynchronizeLayout();
            Assert.AreEqual(20, document.Styles.GetComputed(child).GetPixels("margin-left"));
            Assert.AreEqual(10, before.GetPixels("margin-left"));
        }
        [Test] public void SameTypeReloadUpdatesDefaultsWithoutMatchingDeclarationsAndInvalidReloadKeepsOldPage()
        {
            var bare = document.Q("bare"); var original = document.Styles.GetComputed(bare);
            Assert.AreEqual(24, original.GetPixels("font-size"));
            Assert.That(original.GetPixels("line-height"), Is.EqualTo(28.8f).Within(.001f));
            document.AddStyleSheet(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/TypedStyleReload"), "URUI/Tests/TypedStyleReload.css"); document.SynchronizeLayout();
            Assert.AreEqual(.4f, document.Styles.GetComputed(bare).GetNumber("--fx-progress"));
            Assert.AreEqual(0, original.GetNumber("--fx-progress"));
            Assert.Throws<InvalidOperationException>(() => document.AddStyleSheet(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/TypedStyleInvalid"), "URUI/Tests/TypedStyleInvalid.css"));
            document.SynchronizeLayout(); Assert.AreEqual(.4f, document.Styles.GetComputed(bare).GetNumber("--fx-progress"));
        }
        [Test] public void DisposedSubscriptionReleasesCapturedOwnerImmediately()
        {
            var subscription = Capture(document, document.Q("child"), out var captured);
            subscription.Dispose();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.IsFalse(captured.IsAlive, "已取消的样式订阅不能继续持有表现控制器或业务对象。");
            GC.KeepAlive(subscription);
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static IDisposable Capture(Document document, URUI.Elements.Element element, out WeakReference captured)
        {
            var owner = new object(); captured = new WeakReference(owner);
            return document.Styles.Subscribe(element, _ => GC.KeepAlive(owner));
        }
        [UnityTest] public IEnumerator NamedNumberAnimationInterpolatesAndFinishesAtAuthoredValue()
        {
            var animated = document.Q("animated");
            yield return new WaitForSecondsRealtime(.07f);
            var progress = document.Styles.GetComputed(animated).GetNumber("--fx-progress");
            Assert.That(progress, Is.GreaterThan(0).And.LessThan(1));
            yield return new WaitForSecondsRealtime(1.05f);
            Assert.That(document.Styles.GetComputed(animated).GetNumber("--fx-progress"), Is.EqualTo(1).Within(.0001f));
        }
        [UnityTest] public IEnumerator StableSubscriptionsDoNotRepeatAndCanUnloadDuringNotification()
        {
            var child = document.Q("child"); var calls = 0;
            using var subscription = document.Styles.Subscribe(child, _ => calls++);
            yield return null; yield return null;
            var initial = calls; Assert.AreEqual(1, initial);
            yield return null; Assert.AreEqual(initial, calls);
            child.Style.SetNumber("--fx-progress", .4f);
            yield return null; yield return null; Assert.AreEqual(initial + 1, calls);
            using var unload = document.Styles.Subscribe(child, _ => document.Unload());
            child.Style.SetNumber("--fx-progress", .5f);
            yield return null; yield return null; Assert.IsFalse(document.IsLoaded);
        }
    }
}
