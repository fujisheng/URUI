using System;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Events;
using URUI.Elements;
using Unity.Profiling;

namespace URUI.Tests
{
    public sealed class GeometrySynchronizationTests
    {
        sealed class BufferReadingElement : Element
        {
            internal Action UpdateAction;
            internal BufferReadingElement(ElementContext context) : base(context) { }
            protected override void OnUpdate()
            {
                UpdateAction?.Invoke();
            }
        }
        GameObject root;
        GameObject camera;
        Document document;
        const string Html = "<!doctype html><html><head><style>body{margin:0;}#a{position:absolute;left:10px;top:20px;width:100px;height:80px;transform:translate(30px,40px) scale(2);transform-origin:0px 0px;}#b{position:absolute;left:5px;top:7px;width:20px;height:10px;}</style></head><body><div id='a'><div id='b'></div></div></body></html>";
        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("Geometry Camera", typeof(Camera));
            camera.tag = "MainCamera";
            root = new GameObject("Geometry Surface");
            var surface = root.AddComponent<Surface>();
            surface.Input.UseUnityInput = false;
            document = root.AddComponent<Document>();
            document.Surface = surface;
            document.LoadHtml(Html, "/geometry.html");
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera);
        }
        [Test]
        public void AppendingElementsDoesNotCopyTheWholeDocumentSnapshot()
        {
            var html = new System.Text.StringBuilder("<!doctype html><html><head></head><body>");
            for (var i = 0; i < 2048; i++)
            {
                html.Append("<div></div>");
            }
            html.Append("</body></html>");
            document.LoadHtml(html.ToString(), "/snapshot-append.html");
            document.SynchronizeGeometry();
            var body = document.Body;
            long copies = 0;
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Elements.Snapshot.Copy", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                for (var i = 0; i < 32; i++)
                {
                    body.Add(document.CreateElement());
                }
                recorder.Stop();
                Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
                for (var i = 0; i < recorder.Count; i++)
                {
                    copies += recorder.GetSample(i).Count;
                }
            }
            Assert.That(copies, Is.LessThanOrEqualTo(2), "追加32个节点不能逐个重新复制2048个既有节点的数组。");
            Assert.That(body.ChildCount, Is.EqualTo(2080));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeometryProvidersAreReadyWithoutCompilingGpuCommands(bool geometryProvider)
        {
            var node = document.Q("b");
            node.Style.Set("--urui-decorator", "shader(deferred-geometry)");
            var calls = 0;
            using var registration = geometryProvider
                ? document.AttachGeometryProvider(node, "deferred-geometry", context =>
                {
                    calls++;
                    context.DrawFeatheredRectangle(new Rect(Vector2.zero, context.Size), Vector4.zero);
                })
                : document.AttachPainter(node, "deferred-geometry", _ => calls++);
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Native.Plan.Compile", 64, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                document.SynchronizeGeometry();
                recorder.Stop();
                Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
                long compiled = 0;
                for (var i = 0; i < recorder.Count; i++)
                {
                    compiled += recorder.GetSample(i).Count;
                }
                Assert.That(calls, Is.GreaterThan(0), "返回前仍必须执行自己的准备回调。");
                if (geometryProvider)
                {
                    Assert.That(compiled, Is.Zero, "只读几何准备不能顺带编译 GPU 命令。");
                }
                else
                {
                    Assert.That(compiled, Is.GreaterThan(0), "任意绘制回调仍走完整录制生命周期。");
                }
            }
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Native.Plan.Compile", 64, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                document.SynchronizeLayout();
                recorder.Stop();
                Assert.That(recorder.Valid && recorder.Count > 0, Is.True, "显式完整录制必须完成计划和资源准备。");
            }
        }
        static void Synchronize(Document owner)
        {
            owner.SynchronizeGeometry();
        }
        static void AssertRect(Rect actual, float x, float y, float width, float height)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(.01));
            Assert.That(actual.y, Is.EqualTo(y).Within(.01));
            Assert.That(actual.width, Is.EqualTo(width).Within(.01));
            Assert.That(actual.height, Is.EqualTo(height).Within(.01));
        }
        [Test]
        public void NestedTransformsAreReadyBeforeTheFirstDrawingRecord()
        {
            Synchronize(document);
            var node = document.Q("b");
            Assert.That(node.HasLayout, Is.True);
            AssertRect(node.WorldBound, 50, 74, 40, 20);
            var before = node.WorldBound;
            document.SynchronizeLayout();
            Assert.That(node.WorldBound, Is.EqualTo(before));
        }
        [Test]
        public void ChangedAncestorTransformsAndReattachedChildrenRefreshGeometry()
        {
            Synchronize(document);
            var parent = document.Q("a");
            var node = document.Q("b");
            parent.Style.Set("transform", "translate(4px,8px) scale(3)");
            Synchronize(document);
            AssertRect(node.WorldBound, 29, 49, 60, 30);
            document.Body.Add(node);
            Synchronize(document);
            AssertRect(node.WorldBound, 5, 7, 20, 10);
        }
        [Test]
        public void ScrolledGeometryMatchesTheFullDrawingPath()
        {
            var parent = document.Q("a");
            var node = document.Q("b");
            parent.Style.Set("overflow", "scroll");
            node.Style.Set("position", "relative");
            node.Style.Set("height", "200px");
            Synchronize(document);
            parent.ScrollOffset = new Vector2(0, 20);
            Assert.That(parent.ScrollOffset.y, Is.EqualTo(20), "先确认内容确实建立了滚动范围。");
            Synchronize(document);
            AssertRect(node.WorldBound, 50, 34, 40, 400);
            var before = node.WorldBound;
            document.SynchronizeLayout();
            Assert.That(node.WorldBound, Is.EqualTo(before));
        }

        [Test]
        public void GeometryNotificationReadsCurrentWorldBounds()
        {
            Synchronize(document);
            var parent = document.Q("a");
            var node = document.Q("b");
            var calls = 0;
            var observed = default(Rect);
            node.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                calls++;
                observed = node.WorldBound;
            });
            parent.Style.Set("transform", "translate(4px,8px) scale(3)");
            node.Style.Set("width", "30px");
            Synchronize(document);
            Assert.That(calls, Is.EqualTo(1));
            AssertRect(observed, 29, 49, 90, 30);
            Assert.That(node.WorldBound, Is.EqualTo(observed));
        }

        [Test]
        public void GeometryCallbackScrollingIsReadyBeforeSynchronizeReturns()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#viewport{position:absolute;width:100px;height:80px;overflow:scroll;}#child{width:20px;height:200px;transform:scale(2);transform-origin:0px 0px;}</style></head><body><div id='viewport'><div id='child'></div></div></body></html>", "/callback-scroll.html");
            Synchronize(document);
            var viewport = document.Q("viewport");
            var child = document.Q("child");
            var calls = 0;
            child.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                calls++;
                viewport.ScrollOffset = new Vector2(0, 20);
            });
            child.Style.Set("width", "21px");
            Synchronize(document);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(20));
            AssertRect(child.WorldBound, 0, -20, 42, 400);
            var before = child.WorldBound;
            document.SynchronizeLayout();
            Assert.That(child.WorldBound, Is.EqualTo(before));
        }

        [Test]
        public void DeferredRecordingStopsWhenGeometryCallbackDisablesSurface()
        {
            Synchronize(document);
            var node = document.Q("b");
            var notified = false;
            node.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                notified = true;
                root.GetComponent<Surface>().enabled = false;
            });
            node.Style.Set("width", "21px");
            UnityEngine.Rendering.CommandBuffer commands = null;
            Assert.DoesNotThrow(() => commands = document.RenderCommandBuffer);
            Assert.That(notified, Is.True);
            Assert.That(document.IsLoaded, Is.False);
            Assert.That(commands, Is.Null);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PainterRegisteredByGeometryCallbackIsReadyOnReturn(bool geometryProvider, bool subtree)
        {
            Synchronize(document);
            var node = document.Q("b");
            var paints = 0;
            IDisposable painter = null;
            node.RegisterCallback<GeometryChangedEvent>(_ => document.RunAfterEvent(() =>
            {
                painter = geometryProvider
                    ? document.AttachGeometryProvider(node, "callback-painter", context =>
                    {
                        paints++;
                        Assert.That(context.Size.x, Is.EqualTo(21));
                    })
                    : document.AttachPainter(node, "callback-painter", context =>
                    {
                        paints++;
                        Assert.That(context.Size.x, Is.EqualTo(21));
                    });
            }));
            try
            {
                node.Style.Set("--urui-decorator", "shader(callback-painter)");
                node.Style.Set("width", "21px");
                if (subtree)
                {
                    document.SynchronizeGeometry(document.Q("a"));
                }
                else
                {
                    Synchronize(document);
                }
                Assert.That(painter, Is.Not.Null);
                Assert.That(paints, Is.GreaterThan(0));
            }
            finally
            {
                painter?.Dispose();
            }
        }

        [Test]
        public void GeometrySubtreeRejectsForeignDetachedAndStaleRoots()
        {
            Assert.Throws<ArgumentNullException>(() => document.SynchronizeGeometry(null));
            var detached = document.CreateElement();
            Assert.Throws<ArgumentException>(() => document.SynchronizeGeometry(detached));
            detached.Destroy();
            var stale = document.Q("b");
            stale.Destroy();
            Assert.Throws<ArgumentException>(() => document.SynchronizeGeometry(stale));
            var foreignRoot = new GameObject("Foreign document");
            foreignRoot.transform.SetParent(root.transform);
            var foreign = foreignRoot.AddComponent<Document>();
            foreign.Surface = document.Surface;
            foreign.LoadHtml("<!doctype html><html><head></head><body><div></div></body></html>", "/foreign.html");
            Assert.Throws<ArgumentException>(() => document.SynchronizeGeometry(foreign.Body));
        }

        [Test]
        public void DeferredRecordingRejectsBufferBorrowDuringElementUpdate()
        {
            document.RegisterElementType("urui-buffer-reader", context => new BufferReadingElement(context));
            document.LoadHtml("<!doctype html><html><head><style>urui-buffer-reader{display:block;width:20px;height:20px;}</style></head><body><urui-buffer-reader id='reader'></urui-buffer-reader></body></html>", "/buffer-reader.html");
            var reader = document.Q<BufferReadingElement>("reader");
            var calls = 0;
            var rejected = false;
            reader.UpdateAction = () =>
            {
                calls++;
                // 限制测试本身的递归深度，旧实现必须失败而不是造成栈溢出。
                if (calls > 1)
                {
                    return;
                }
                try
                {
                    _ = document.RenderCommandBuffer;
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
            };
            Synchronize(document);
            Assert.That(calls, Is.EqualTo(1), "借用命令不能在 OnUpdate 内递归刷新文档。");
            Assert.That(rejected, Is.True, "需要同步准备的借用必须在回调外执行。");
            reader.UpdateAction = null;
            Assert.DoesNotThrow(() => document.SynchronizeLayout());
        }

        [Test]
        public void RedisplayedRotatedAndPerspectiveChildrenMatchFullDrawing()
        {
            var parent = document.Q("a");
            var node = document.Q("b");
            parent.Style.Set("display", "none");
            Synchronize(document);
            parent.Style.Set("display", "block");
            parent.Style.Set("perspective", "300px");
            node.Style.Set("transform", "rotateY(25deg) rotate(30deg)");
            Synchronize(document);
            var before = node.WorldBound;
            Assert.That(before.width, Is.GreaterThan(1));
            document.SynchronizeLayout();
            Assert.That(node.WorldBound, Is.EqualTo(before));
        }
    }
}
