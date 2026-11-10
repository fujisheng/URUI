using System;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using URUI.Animation;
using URUI.Controls;
using URUI.Documents;
using URUI.Events;
using URUI.Hosting;
using URUI.Rendering;
using URUI.Rendering.Effects;
using URUI.Internal.Interop;

namespace URUI.Tests
{
    /// <summary>使用真实GC.Alloc事件验证稳定更新，不依赖可能无效的线程分配字节计数。</summary>
    public sealed class RuntimeAllocationTests
    {
        sealed class Clock : IUiClock { public double Now => 1; public float Delta => 0; }
        sealed class MovingClock : IUiClock { public double Now { get; set; } = 1; public float Delta => .01f; }
        GameObject root, camera;
        Document document;
        static object allocationProbe;

        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("Allocation camera", typeof(Camera));
            camera.tag = "MainCamera";
            root = new GameObject("Allocation document");
            var surface = root.AddComponent<Surface>();
            surface.Input.UseUnityInput = false;
            surface.UiClock = new Clock();
            document = root.AddComponent<Document>();
            document.Surface = surface;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera);
            allocationProbe = null;
        }

        static long Allocations(Action action)
        {
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            action();
            recorder.Stop();
            var valid = recorder.Valid;
            var wrapped = recorder.WrappedAround;
            long count = 0;
            for (var i = 0; i < recorder.Count; ++i) { count += recorder.GetSample(i).Count; }
            Assert.That(valid, Is.True, "GC.Alloc采样器不可用，不能把零值当作无分配。");
            Assert.That(wrapped, Is.False, "采样容量不足。");
            return count;
        }

        static void ValidateRecorder()
        {
            Assert.That(Allocations(() => { }), Is.Zero);
            Assert.That(Allocations(() => { allocationProbe = new byte[4096]; GC.KeepAlive(allocationProbe); }), Is.GreaterThan(0),
                "阳性探针未被GC.Alloc捕获。");
        }

        void Load(bool draw = false)
        {
            var html = new StringBuilder("<!doctype html><html><head><style>body{margin:0;}div{width:10px;height:10px;");
            if (draw) { html.Append("background:red;"); }
            html.Append("}</style></head><body>");
            for (var i = 0; i < 100; ++i) { html.Append("<div id='n" + i + "'></div>"); }
            html.Append("</body></html>");
            document.LoadHtml(html.ToString(), "/allocations.html");
            for (var i = 0; i < 100; ++i) { _ = document.Q("n" + i); }
            for (var i = 0; i < 5; ++i) { document.SynchronizeLayout(); }
        }

        [Test]
        public void AllocationChannelDetectsPositiveProbeAndHasNoEmptyActionNoise()
        {
            ValidateRecorder();
        }

        [TestCase(true, 0.0000000213, false)]
        [TestCase(true, 0.001, true)]
        [TestCase(false, 0.0000000213, true)]
        public void NativeClockOnlyToleratesBoundedUnitySampleRoundoff(bool unityClock,double backwards,bool rejected)
        {
            if(unityClock)document.Surface.UiClock=UnityUiClock.Unscaled;
            Load();
            var host=root.GetComponentInChildren<URUI.Internal.Hosting.RenderHost>(true);
            var last=typeof(URUI.Internal.Hosting.RenderHost).GetField("lastUiTime",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            var previous=document.Surface.UiClock.Now+backwards;
            last.SetValue(host,previous);
            if(rejected)Assert.Throws<InvalidOperationException>(()=>document.SynchronizeLayout());
            else
            {
                Assert.DoesNotThrow(()=>document.SynchronizeLayout());
                Assert.That((double)last.GetValue(host),Is.GreaterThanOrEqualTo(previous));
            }
        }

        [Test]
        public void UnchangedElementRefreshDoesNotAllocateMemberSnapshots()
        {
            Load();
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 20; ++i) { document.RefreshElements(true); }
            }), Is.Zero);
        }

        [Test]
        public void StableLayoutNotificationDoesNotAllocateDelegateArrays()
        {
            Load();
            document.LayoutUpdated += EmptyLayout;
            document.NotifyLayoutUpdated();
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 20; ++i) { document.NotifyLayoutUpdated(); }
            }), Is.Zero);
        }

        static void EmptyLayout(Document owner) { }

        static void QueueStableAction(Document owner) { owner.RunAfterEvent(StableAction); }
        static readonly Action StableAction = () => { };
        [Test]
        public void StableDeferredLayoutActionsDoNotAllocateDispatchClosures()
        {
            Load();
            document.LayoutUpdated+=QueueStableAction;
            Action action=()=>
            {
                for(var i=0;i<20;++i)document.NotifyLayoutUpdated();
            };
            action();ValidateRecorder();
            Assert.That(Allocations(action),Is.Zero);
        }

        [Test]
        public void DeferredActionsKeepOrderAndExceptionHandling()
        {
            Load();
            var order=new System.Collections.Generic.List<int>();
            Action first=()=>
            {
                order.Add(1);
                document.RunAfterEvent(()=>order.Add(3));
                throw new InvalidOperationException("expected deferred failure");
            };
            document.LayoutUpdated+=owner=>
            {
                owner.RunAfterEvent(first);
                owner.RunAfterEvent(()=>order.Add(2));
                Assert.That(order,Is.Empty,"Actions wait for the current dispatch to unwind.");
            };
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,new System.Text.RegularExpressions.Regex("InvalidOperationException: expected deferred failure"));
            document.NotifyLayoutUpdated();
            CollectionAssert.AreEqual(new[]{1,3,2},order);
            Assert.Throws<InvalidOperationException>(()=>document.RunAfterEvent(()=>throw new InvalidOperationException("immediate")));
        }

        sealed class PassthroughEffect : IEffectFactory, IElementEffect
        {
            public IElementEffect Create(EffectContext context) { return this; }
            public void Prepare(EffectFrame frame) { }
            public void Record(EffectRenderContext context) { context.DrawOriginal(); }
            public void Dispose() { }
        }

        [Test]
        public void StablePainterCallbacksDoNotAllocateContextObjects()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:10px;height:10px;--urui-decorator:shader(allocation-painter);}</style></head><body><div id='p'></div></body></html>", "/painter-allocations.html");
            var calls = 0;
            using var painter = document.AttachPainter(document.Q("p"), "allocation-painter", context => ++calls);
            for (var i = 0; i < 8; ++i) { document.SynchronizeLayout(); }
            Assert.That(calls, Is.GreaterThan(0));
            ValidateRecorder();
            Assert.That(Allocations(() => { for (var i = 0; i < 10; ++i) { document.SynchronizeLayout(); } }), Is.Zero);
        }

        [Test]
        public void MovingEffectCallbacksDoNotAllocateFrameOrContextObjects()
        {
            var clock = new MovingClock();
            document.Surface.UiClock = clock;
            document.Images = new[] { new URUI.Resources.ImageAsset("probe.png", Texture2D.whiteTexture) };
            document.Effects.Register(new EffectDefinition("allocation-effect", EffectKind.Image, new PassthroughEffect(), "allocation-program", "premultiplied"));
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{width:10px;height:10px;--urui-image-effect:allocation-effect;}</style></head><body><img src='probe.png'></body></html>", "/effect-allocations.html");
            for (var i = 0; i < 8; ++i) { clock.Now += .01; document.SynchronizeLayout(); }
            ValidateRecorder();
            Assert.That(Allocations(() => { for (var i = 0; i < 10; ++i) { clock.Now += .01; document.SynchronizeLayout(); } }), Is.Zero);
        }

        [Test]
        public void UnchangedClipboardReadReusesItsUtf8Snapshot()
        {
            Load();
            var previous = NativeMethods.ReadClipboard();
            try
            {
                NativeMethods.urui_clipboard_write("中文剪贴板😀");
                var first = NativeMethods.ReadClipboard();
                Assert.That(first, Is.EqualTo("中文剪贴板😀"));
                for (var i = 0; i < 5; ++i) { _ = NativeMethods.ReadClipboard(); }
                ValidateRecorder();
                Assert.That(Allocations(() => { for (var i = 0; i < 10; ++i) { _ = NativeMethods.ReadClipboard(); } }), Is.Zero);
                Assert.That(NativeMethods.ReadClipboard(), Is.SameAs(first));
                NativeMethods.urui_clipboard_write("变化🧪");
                Assert.That(NativeMethods.ReadClipboard(), Is.EqualTo("变化🧪"));
                NativeMethods.urui_clipboard_write("");
                Assert.That(NativeMethods.ReadClipboard(), Is.Empty);
            }
            finally { NativeMethods.urui_clipboard_write(previous ?? ""); }
        }

        [Test]
        public void UnchangedStyleReadsReuseStringsAndObserveImmediateWrites()
        {
            Load();
            var node = document.Q("n0");
            node.Style.Set("opacity", ".5");
            document.SynchronizeLayout();
            var computed = node.ResolvedStyle.Get("opacity");
            var inline = node.Style.Get("opacity");
            for (var i = 0; i < 5; ++i) { _ = node.ResolvedStyle.Get("opacity"); _ = node.Style.Get("opacity"); }
            ValidateRecorder();
            Action read = () =>
            {
                for (var i = 0; i < 10; ++i)
                {
                    _ = node.ResolvedStyle.Get("opacity");
                    _ = node.Style.Get("opacity");
                }
            };
            read();
            Assert.That(Allocations(read), Is.Zero);
            Assert.That(node.ResolvedStyle.Get("opacity"), Is.SameAs(computed));
            Assert.That(node.Style.Get("opacity"), Is.SameAs(inline));
            node.Style.Set("opacity", ".25");
            Assert.That(node.Style.Get("opacity"), Is.Not.EqualTo(inline), "行内值读取不能等到下一次布局才失效。");
            document.SynchronizeLayout();
            Assert.That(node.ResolvedStyle.Get("opacity"), Is.Not.EqualTo(computed));
        }

        [Test]
        public void IdleAnimationControllerDoesNotAllocatePlaybackLists()
        {
            Load();
            _ = document.Animations;
            document.NotifyLayoutUpdated();
            ValidateRecorder();
            Assert.That(Allocations(() => { for (var i = 0; i < 10; ++i) { document.NotifyLayoutUpdated(); } }), Is.Zero);
        }

        [Test]
        public void StableTextAndAttributeReadsReuseUtf8Strings()
        {
            Load();
            var node = document.Q("n0");
            node.Text = "中文😀"; node.SetAttribute("title", "原始🧪");
            Action read = () => { for (var i = 0; i < 10; ++i) { _ = node.TagName; _ = node.Text; _ = node.GetAttribute("title"); } };
            read();
            ValidateRecorder();
            Assert.That(Allocations(read), Is.Zero);
            var old = node.Text;
            node.Text = "替换后的内容";
            Assert.That(node.Text, Is.EqualTo("替换后的内容"));
            Assert.That(node.Text, Is.Not.SameAs(old));
            node.RemoveAttribute("title");
            Assert.That(node.GetAttribute("title"), Is.Null);
        }

        [Test]
        public void StableTextFitMeasurementDoesNotAllocateTreeOrNumberStrings()
        {
            var resources = new System.Collections.Generic.List<URUI.Resources.TextResource>
            { new("/urui/base.css", UnityEngine.Resources.Load<TextAsset>("URUI/base")) };
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
            {
                var name = "Liberation" + family + "-" + face + ".ttf";
                resources.Add(new URUI.Resources.TextResource("/urui/Fonts/" + name + ".bytes", UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name)));
            }
            document.TextResources = resources.ToArray();
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'><style>#host{width:100px;height:40px;--urui-text-fit-min:8px;--urui-text-fit-max:16px;}</style></head><body><div id='host'><div id='content'>Text <span>fit</span></div></div></body></html>", "/fit-allocations.html");
            document.SynchronizeLayout();
            using var fit = new TextFit(document.Q("host"), document.Q("content"));
            for (var i = 0; i < 24; ++i) { document.SynchronizeLayout(); document.NotifyLayoutUpdated(); }
            Action measure = () => { for (var i = 0; i < 10; ++i) { document.NotifyLayoutUpdated(); } };
            measure(); ValidateRecorder();
            Assert.That(Allocations(measure), Is.Zero);
            document.Q("content").Text = "A much longer replacement text that needs fitting";
            for (var i = 0; i < 24; ++i) { document.SynchronizeLayout(); document.NotifyLayoutUpdated(); }
            Assert.That(fit.Content.Style.Get("font-size"), Is.Not.Null);
            Assert.That(Allocations(measure), Is.Zero);
        }

        [Test]
        public void LayoutSubscriptionChangesTakeEffectOnNextNotification()
        {
            Load();
            var calls=new System.Collections.Generic.List<int>();
            Action<Document> second=owner=>calls.Add(2);
            Action<Document> third=owner=>calls.Add(3);
            var changed=false;
            document.LayoutUpdated+=owner=>
            {
                calls.Add(1);
                if(changed)return;
                changed=true;
                document.LayoutUpdated-=second;
                document.LayoutUpdated+=third;
            };
            document.LayoutUpdated+=second;
            document.NotifyLayoutUpdated();
            document.NotifyLayoutUpdated();
            Assert.That(calls,Is.EqualTo(new[]{1,2,1,3}));
        }

        [Test]
        public void RowMeasurementUsesStableTypedStylesAndObservesStyleChanges()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#row{display:flex;}"+
                "#child{width:40px;height:10px;flex-shrink:0;margin-right:10px;}</style></head>"+
                "<body><div id='row'><div id='child'></div></div></body></html>","/row-allocations.html");
            var row=document.Q("row");var child=document.Q("child");
            using var measurement=new RowContentSize(row);
            for(var i=0;i<5;i++){document.SynchronizeLayout();document.NotifyLayoutUpdated();}
            Assert.That(row.Layout.width,Is.EqualTo(50).Within(.02f));
            ValidateRecorder();
            Assert.That(Allocations(()=>
            {
                for(var i=0;i<20;i++)document.NotifyLayoutUpdated();
            }),Is.Zero);
            child.Style.Set("margin-right","20px");
            for(var i=0;i<3;i++){document.SynchronizeLayout();document.NotifyLayoutUpdated();}
            Assert.That(row.Layout.width,Is.EqualTo(60).Within(.02f));
            child.Style.Set("display","none");
            for(var i=0;i<3;i++){document.SynchronizeLayout();document.NotifyLayoutUpdated();}
            Assert.That(row.Layout.width,Is.EqualTo(0).Within(.02f));
            child.Style.Set("display","block");child.Style.Set("margin-right","10%");
            for(var i=0;i<12;i++){document.SynchronizeLayout();document.NotifyLayoutUpdated();}
            Assert.That(row.Layout.width,Is.EqualTo(40f/.9f).Within(.02f));
            child.Style.Set("margin-right","auto");
            for(var i=0;i<3;i++){document.SynchronizeLayout();document.NotifyLayoutUpdated();}
            Assert.That(row.Layout.width,Is.EqualTo(40).Within(.02f));
        }

        [Test]
        public void StableOrdinaryRenderingDoesNotAllocatePerGraphic()
        {
            Load(true);
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 10; ++i) { document.SynchronizeLayout(); }
            }), Is.Zero);
        }

        [Test]
        public void AnimatedThousandGraphicsReuseCpuGeometryAndBatchStaging()
        {
            var clock=new MovingClock();document.Surface.UiClock=clock;
            document.Surface.Viewport=new Vector2Int(128,128);
            var html=new StringBuilder("<!doctype html><html><head><style>body{margin:0;}"+
                "@keyframes tint{from{background-color:red;}to{background-color:blue;}}"+
                "div{position:absolute;width:3px;height:3px;animation:tint 1s infinite linear;}"+
                "</style></head><body>");
            for(var i=0;i<1000;i++)html.Append("<div style='left:").Append(i%32*4).Append("px;top:")
                .Append(i/32*4).Append("px;'></div>");
            document.LoadHtml(html.Append("</body></html>").ToString(),"/dynamic-allocations.html");
            for(var i=0;i<20;i++){clock.Now+=.01;document.SynchronizeLayout();}
            ValidateRecorder();
            // 每次变化只允许一个不可变GPU批次版本的Unity Mesh包装对象，不再按1000个图形分配数组。
            Assert.That(Allocations(()=>
            {
                for(var i=0;i<10;i++){clock.Now+=.01;document.SynchronizeLayout();}
            }),Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void IdleScrollMotionDoesNotScheduleUnchangedPositionWrites()
        {
            document.Surface.UiClock=new MovingClock();
            document.TextResources=new[]{new URUI.Resources.TextResource("/urui/behaviors.css",UnityEngine.Resources.Load<TextAsset>("URUI/behaviors"))};
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/behaviors.css'>"+
                "<style>body{margin:0;}#viewport{width:80px;height:30px;overflow:auto;}"+
                "#content{width:200px;height:10px;}</style></head><body>"+
                "<div id='viewport' class='scroll-motion'><div id='content' class='scroll-motion-content'></div></div>"+
                "</body></html>","/idle-scroll.html");
            using var scroll=new ScrollMotion(document.Q("viewport"),document.Q("content"));
            for(var i=0;i<5;i++){document.SynchronizeLayout();document.NotifyLayoutUpdated();}
            ValidateRecorder();
            Assert.That(Allocations(()=>
            {
                for(var i=0;i<20;i++)document.NotifyLayoutUpdated();
            }),Is.Zero);
        }

        [Test]
        public void StableThousandAlternatingImagesDoNotAllocateTextureBindingState()
        {
            var a=new Texture2D(1,1);var b=new Texture2D(1,1);
            try
            {
                a.SetPixel(0,0,Color.red);a.Apply();b.SetPixel(0,0,Color.blue);b.Apply();
                document.Surface.Viewport=new Vector2Int(128,128);
                document.Images=new[]{new URUI.Resources.ImageAsset("a.png",a),new URUI.Resources.ImageAsset("b.png",b)};
                var html=new StringBuilder("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:3px;height:3px;}</style></head><body>");
                for(var i=0;i<1000;i++)html.Append("<img src='").Append(i%2==0?"a.png":"b.png").Append("' style='left:")
                    .Append(i%32*4).Append("px;top:").Append(i/32*4).Append("px;'>");
                document.LoadHtml(html.Append("</body></html>").ToString(),"/binding-allocations.html");
                for(var i=0;i<8;i++)document.SynchronizeLayout();
                ValidateRecorder();
                Assert.That(Allocations(()=>{for(var i=0;i<10;i++)document.SynchronizeLayout();}),Is.Zero);
            }
            finally{document.Unload();UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
        }

        [Test]
        public void ElementAddedDuringLayoutNotificationParticipatesInNextRefresh()
        {
            Load();
            var first = document.Q("n0");
            URUI.Elements.Element added = null;
            var notifications = 0;
            first.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (added != null) { return; }
                added = document.CreateElement();
                added.Name = "late";
                added.RegisterCallback<GeometryChangedEvent>(change => ++notifications);
                document.Body.Add(added);
            });
            first.Style.SetWidth(20);
            document.SynchronizeLayout();
            document.SynchronizeLayout();
            Assert.That(added, Is.Not.Null);
            Assert.That(added.IsAttached, Is.True);
            Assert.That(notifications, Is.GreaterThan(0));
            document.SynchronizeLayout();
            var settled = notifications;
            document.SynchronizeLayout();
            Assert.That(notifications, Is.EqualTo(settled));
        }
    }
}
