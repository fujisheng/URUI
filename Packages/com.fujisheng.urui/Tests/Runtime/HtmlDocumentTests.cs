using System;
using System.Threading;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Events;
using URUI.Hosting;
using URUI.Elements;

namespace URUI.Tests
{
    public sealed class HtmlDocumentTests
    {
        GameObject root;
        GameObject camera;
        Surface surface;
        Document a;
        Document b;
        const string Html = "<!doctype html><html><head><style>body{margin:0;}button{position:absolute;left:0;top:0;width:80px;height:40px;}</style></head><body><button id='button'>ready</button></body></html>";

        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("HTML camera", typeof(Camera));
            camera.tag = "MainCamera";
            root = new GameObject("HTML Surface");
            surface = root.AddComponent<Surface>();
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
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera);
        }

        [Test]
        public void InvalidDocumentAndFragmentPreserveLiveElements()
        {
            a.LoadHtml(Html, "ui/a.html");
            var button = a.Q("button");
            var loaded = 0;
            var unloaded = 0;
            a.Loaded += _ => loaded++;
            a.Unloaded += _ => unloaded++;
            Assert.Throws<InvalidOperationException>(() => a.LoadHtml("<!doctype html><html><head></head><body><div/></body></html>", "ui/bad.html"));
            Assert.AreSame(button, a.Q("button"));
            Assert.IsTrue(button.IsValid);
            Assert.AreEqual("ui/a.html", a.SourceUrl);
            Assert.Throws<InvalidOperationException>(() => a.Body.SetInnerHtml("<p>unclosed", "ui/bad-fragment.html"));
            Assert.AreSame(button, a.Q("button"));
            Assert.AreEqual(0, loaded);
            Assert.AreEqual(0, unloaded);
            a.Body.SetInnerHtml("<p id='replacement'>new</p>", "ui/fragment.html");
            Assert.IsFalse(button.IsValid);
            Assert.AreEqual("new", a.Q("replacement").Text);
            var serialized = a.Body.GetInnerHtml();
            a.Body.SetInnerHtml(serialized, "ui/roundtrip.html");
            Assert.AreEqual("new", a.Q("replacement").Text);
        }

        sealed class FailingElement : Element
        {
            internal FailingElement(ElementContext context) : base(context) { }
            protected override void OnInitialize()
            {
                throw new InvalidOperationException("candidate control failure");
            }
        }

        sealed class UpdatingElement : Element
        {
            internal int Updates;
            internal int Invalidations;
            internal Action UpdateAction;
            internal UpdatingElement(ElementContext context) : base(context) { }
            protected override void OnUpdate()
            {
                Updates++;
                UpdateAction?.Invoke();
            }
            protected override void OnInvalidated()
            {
                Invalidations++;
            }
        }

        [Test]
        public void ExplicitSynchronizationRefreshesEachDocumentOnce()
        {
            a.RegisterElementType("urui-update-probe", context => new UpdatingElement(context));
            b.RegisterElementType("urui-update-probe", context => new UpdatingElement(context));
            const string html = "<!doctype html><html><head></head><body><urui-update-probe id='probe'></urui-update-probe></body></html>";
            a.LoadHtml(html, "ui/a.html");
            b.LoadHtml(html, "ui/b.html");
            a.SynchronizeLayout();
            var first = a.Q<UpdatingElement>("probe");
            var second = b.Q<UpdatingElement>("probe");
            var one = first.Updates;
            var two = second.Updates;
            a.SynchronizeLayout();
            Assert.That(first.Updates - one, Is.EqualTo(1), "A shares the host layout refresh with B.");
            Assert.That(second.Updates - two, Is.EqualTo(1));
        }

        sealed class InvalidationOnlyElement : Element
        {
            internal int Invalidations;
            internal InvalidationOnlyElement(ElementContext context) : base(context) { }
            protected override void OnInvalidated()
            {
                Invalidations++;
            }
        }

        [Test]
        public void GeometryCallbackDiscardsPrefetchedStateForDestroyedLaterElement()
        {
            a.RegisterElementType("urui-invalidation-probe", context => new InvalidationOnlyElement(context));
            a.LoadHtml("<!doctype html><html><head><style>div{width:12px;height:9px;}</style></head><body><div id='mutator'></div><urui-invalidation-probe id='victim'></urui-invalidation-probe></body></html>", "ui/refresh-window.html");
            a.SynchronizeLayout();
            var mutator = a.Q("mutator");
            var victim = a.Q<InvalidationOnlyElement>("victim");
            mutator.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                Assert.That(URUI.Internal.Interop.NativeMethods.urui_remove_element(a.NativeHandle, "victim"), Is.EqualTo(1));
            });
            mutator.Style.SetWidth(20);
            a.SynchronizeLayout();
            Assert.That(victim.Invalidations, Is.EqualTo(1));
        }

        [Test]
        public void CustomUpdateInvalidatesAnAlreadyRefreshedElementBeforeReturning()
        {
            a.RegisterElementType("urui-update-probe", context => new UpdatingElement(context));
            a.LoadHtml("<!doctype html><html><head></head><body><urui-update-probe id='victim'></urui-update-probe><urui-update-probe id='mutator'></urui-update-probe></body></html>", "ui/update-invalidation.html");
            a.SynchronizeLayout();
            var victim = a.Q<UpdatingElement>("victim");
            var mutator = a.Q<UpdatingElement>("mutator");
            mutator.UpdateAction = () =>
            {
                mutator.UpdateAction = null;
                Assert.That(URUI.Internal.Interop.NativeMethods.urui_remove_element(a.NativeHandle, "victim"), Is.EqualTo(1));
            };
            a.SynchronizeLayout();
            Assert.That(victim.Invalidations, Is.EqualTo(1), "Read no public validity getter: refresh itself must publish the invalidation.");
            Assert.That(mutator.IsValid, Is.True);
        }

        [Test]
        public void CandidateControlFailureKeepsOriginalPageAndIdentity()
        {
            a.LoadHtml(Html, "ui/a.html");
            var button = a.Q("button");
            var loaded = 0;
            var unloaded = 0;
            a.Loaded += _ => loaded++;
            a.Unloaded += _ => unloaded++;
            a.RegisterElementType("urui-fail-control", context => new FailingElement(context));
            Assert.Throws<InvalidOperationException>(() => a.LoadHtml("<!doctype html><html><head></head><body><urui-fail-control></urui-fail-control></body></html>", "ui/failing-control.html"));
            Assert.IsTrue(button.IsValid);
            Assert.AreSame(button, a.Q("button"));
            Assert.AreEqual("ui/a.html", a.SourceUrl);
            Assert.AreEqual(0, loaded);
            Assert.AreEqual(0, unloaded);
            button.Text = "still active";
            Assert.AreEqual("still active", a.Q("button").Text);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FragmentControlFailureKeepsOriginalSubtreeAndSubscriptions(bool queuesClose)
        {
            a.LoadHtml(Html, "ui/a.html");
            b.LoadHtml(Html, "ui/b.html");
            var button = a.Q("button");
            var other = b.Q("button");
            var clicks = 0;
            using var subscription = button.RegisterCallback<ClickEvent>(_ => clicks++);
            a.RegisterElementType("urui-fail-control", context =>
            {
                if (queuesClose)
                {
                    a.Unload();
                }
                return new FailingElement(context);
            });
            Assert.Throws<InvalidOperationException>(() => a.Body.SetInnerHtml(
                "<div id='candidate'><urui-fail-control></urui-fail-control></div>", "ui/fragment.html"));
            Assert.IsTrue(button.IsValid);
            Assert.AreSame(button, a.Q("button"));
            Assert.IsNull(a.Q("candidate"));
            Assert.IsTrue(other.IsValid);
            Assert.AreSame(other, b.Q("button"));
            button.SendEvent(new ClickEvent());
            Assert.AreEqual(1, clicks);
            a.Body.SetInnerHtml("<button id='success'>OK</button>", "ui/success.html");
            Assert.IsFalse(button.IsValid);
            Assert.IsNotNull(a.Q("success"));
            Assert.IsTrue(other.IsValid);
        }

        [Test]
        public void LoadedCallbackCanQueueReloadAfterCurrentPublication()
        {
            var loaded = 0;
            a.Loaded += owner =>
            {
                loaded++;
                if (loaded == 1)
                {
                    owner.LoadHtml(Html.Replace("ready", "reloaded"), "ui/reloaded.html");
                }
            };
            a.LoadHtml(Html, "ui/a.html");
            Assert.AreEqual(2, loaded);
            Assert.AreEqual("reloaded", a.Q("button").Text);
            Assert.AreEqual("ui/reloaded.html", a.SourceUrl);
        }

        [Test]
        public void InitialAnimationEventsArriveAfterLoadedAndOnlyInTheirDocument()
        {
            var html = Html.Replace("</style>", "@keyframes appear{from{opacity:0;}to{opacity:1;}}button{animation:appear 1s linear;}</style>");
            var createdA = 0;
            var createdB = 0;
            a.Loaded += document => document.Q("button").RegisterCallback<AnimationCreatedEvent>(value =>
            {
                Assert.AreEqual("appear", value.AnimationName);
                createdA++;
            });
            b.Loaded += document => document.Q("button").RegisterCallback<AnimationCreatedEvent>(_ => createdB++);
            a.LoadHtml(html, "ui/animated-a.html");
            Assert.AreEqual(1, createdA);
            Assert.AreEqual(0, createdB);
            b.LoadHtml(html, "ui/animated-b.html");
            Assert.AreEqual(1, createdA);
            Assert.AreEqual(1, createdB);
        }

        [Test]
        public void NativeClickQueuesCloseAndOtherDocumentReloadWithoutDuplicateInput()
        {
            a.LoadHtml(Html, "ui/a.html");
            b.LoadHtml(Html, "ui/b.html");
            a.SetPresentation(1, 0, true, false);
            b.SetPresentation(0, 0, true, false);
            a.SynchronizeLayout();
            var oldB = b.Q("button");
            var clicks = 0;
            using var subscription = a.Q("button").RegisterCallback<ClickEvent>(_ =>
            {
                clicks++;
                a.Unload();
                b.LoadHtml(Html.Replace("ready", "B reloaded"), "ui/b-reloaded.html");
            });
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(10, 10), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(10, 10));
            Assert.AreEqual(1, clicks);
            Assert.IsFalse(a.IsLoaded);
            Assert.IsTrue(b.IsLoaded);
            Assert.IsFalse(oldB.IsValid);
            Assert.AreEqual("B reloaded", b.Q("button").Text);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManagedEventAndTabQueueOtherPageLifecycleUntilDispatchReturns(bool tab)
        {
            a.LoadHtml(Html, "ui/a.html");
            b.LoadHtml(Html, "ui/b.html");
            var order = new List<string>();
            b.Unloaded += _ => order.Add("B unloaded");
            void RequestClose()
            {
                order.Add("A entered");
                b.Unload();
                order.Add(b.IsLoaded ? "B pending" : "B closed too early");
                order.Add("A returned");
            }
            var button = a.Q("button");
            if (tab)
            {
                using var subscription = button.RegisterCallback<KeyDownEvent>(_ => RequestClose());
                Assert.IsTrue(button.Focus());
                surface.Input.SendKey(KeyCode.Tab, true);
            }
            else
            {
                using var subscription = button.RegisterCallback<ClickEvent>(_ => RequestClose());
                button.SendEvent(new ClickEvent());
            }
            CollectionAssert.AreEqual(new[] { "A entered", "B pending", "A returned", "B unloaded" }, order);
            Assert.IsFalse(b.IsLoaded);
        }

        [Test]
        public void HidingOnlyFocusedPageStopsTextAndKeyboardInput()
        {
            a.LoadHtml("<!doctype html><html><head></head><body><input id='editor' value='before'></body></html>", "ui/editor.html");
            var editor = a.Q("editor");
            Assert.IsTrue(editor.Focus());
            a.SetPresentation(0, 0, false, false);
            Assert.IsNull(a.FocusedElement);
            surface.Input.SendText("X");
            Assert.AreEqual("before", editor.Text);
            surface.Input.SendKey(KeyCode.Delete, true);
            Assert.AreEqual("before", editor.Text);
        }

        [Test]
        public void OpeningModalCancelsLowerPageCaptureBeforeFurtherPointerEvents()
        {
            b.SetPresentation(1, 0, false, true);
            a.LoadHtml(Html, "ui/a.html");
            b.LoadHtml(Html, "ui/modal.html");
            a.SynchronizeLayout();
            var button = a.Q("button");
            var cancelled = 0;
            var lost = 0;
            var moved = 0;
            var clicked = 0;
            using var down = button.RegisterCallback<PointerDownEvent>(value => button.CapturePointer(value.PointerId));
            using var cancel = button.RegisterCallback<PointerCancelEvent>(_ => cancelled++);
            using var capture = button.RegisterCallback<PointerCaptureOutEvent>(_ => lost++);
            using var move = button.RegisterCallback<PointerMoveEvent>(_ => moved++);
            using var click = button.RegisterCallback<ClickEvent>(_ => clicked++);
            surface.Input.SendPointer(PointerPhase.Down, new Vector2(10, 10), pressedButtons: 1);
            Assert.IsTrue(button.HasPointerCapture(0));
            b.SetPresentation(1, 0, true, true);
            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(1, lost);
            Assert.IsFalse(button.HasPointerCapture(0));
            surface.Input.SendPointer(PointerPhase.Move, new Vector2(10, 10), pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, new Vector2(10, 10));
            Assert.AreEqual(0, moved);
            Assert.AreEqual(0, clicked);
        }

        [Test]
        public void ForeignThreadCannotReadOrCloseDocument()
        {
            a.LoadHtml(Html, "ui/a.html");
            Exception read = null;
            Exception close = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = a.IsLoaded;
                }
                catch (Exception error)
                {
                    read = error;
                }
                try
                {
                    a.Unload();
                }
                catch (Exception error)
                {
                    close = error;
                }
            });
            thread.Start();
            thread.Join();
            Assert.IsInstanceOf<InvalidOperationException>(read);
            Assert.IsInstanceOf<InvalidOperationException>(close);
            Assert.IsTrue(a.IsLoaded);
        }
    }
}
