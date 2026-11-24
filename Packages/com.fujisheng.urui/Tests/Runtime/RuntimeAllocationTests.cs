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
    /// <summary>
    /// 使用真实GC.Alloc事件验证稳定更新，不依赖可能无效的线程分配字节计数。
    /// </summary>
    public sealed class RuntimeAllocationTests
    {
        sealed class Clock : IUIClock
        {
            public double Now => 1;
            public float Delta => 0;
        }
        sealed class MovingClock : IUIClock
        {
            public double Now
            {
                get;
                set;
            } = 1;
            public float Delta => .01f;
        }
        GameObject root;
        GameObject camera;
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
            surface.UIClock = new Clock();
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
            for (var i = 0; i < recorder.Count; ++i)
            {
                count += recorder.GetSample(i).Count;
            }
            Assert.That(valid, Is.True, "GC.Alloc采样器不可用，不能把零值当作无分配。");
            Assert.That(wrapped, Is.False, "采样容量不足。");
            return count;
        }

        static void ValidateRecorder()
        {
            Assert.That(Allocations(() => { }), Is.Zero);
            Assert.That(Allocations(() =>
            {
                allocationProbe = new byte[4096];
                GC.KeepAlive(allocationProbe);
            }), Is.GreaterThan(0),
                "阳性探针未被GC.Alloc捕获。");
        }

        void Load(bool draw = false)
        {
            var html = new StringBuilder("<!doctype html><html><head><style>body{margin:0;}div{width:10px;height:10px;");
            if (draw)
            {
                html.Append("background:red;");
            }
            html.Append("}</style></head><body>");
            for (var i = 0; i < 100; ++i)
            {
                html.Append("<div id='n" + i + "'></div>");
            }
            html.Append("</body></html>");
            document.LoadHtml(html.ToString(), "/allocations.html");
            for (var i = 0; i < 100; ++i)
            {
                _ = document.Q("n" + i);
            }
            for (var i = 0; i < 5; ++i)
            {
                document.SynchronizeLayout();
            }
        }

        [Test]
        public void IdlePointerCancellationDoesNotAllocateDeferredActions()
        {
            Load();
            var owner = document.Q("n0");
            var input = document.Surface.Input;
            Action cycle = () =>
            {
                for (var i = 0; i < 32; i++)
                {
                    input.CancelPointers(owner);
                }
            };
            for (var i = 0; i < 10; i++)
            {
                cycle();
            }
            ValidateRecorder();
            Assert.That(Allocations(cycle), Is.Zero, "非回调中的取消不应反复创建延迟委托。");
        }

        [Test]
        public void AlternatingStyleReadsReuseTwoValuesAndObserveAuthorWrites()
        {
            Load();
            var node = document.Q("n0");
            Action cycle = () =>
            {
                for (var i = 0; i < 32; i++)
                {
                    node.Style.Set("display", "flex");
                    _ = node.Style.Get("display");
                    node.Style.Set("display", "none");
                    _ = node.Style.Get("display");
                }
            };
            for (var i = 0; i < 10; i++)
            {
                cycle();
            }
            ValidateRecorder();
            var allocations = Allocations(cycle);
            node.Style.Set("display", "block");
            Assert.That(node.Style.Get("display"), Is.EqualTo("block"));
            node.Style.Remove("display");
            Assert.That(node.Style.Get("display"), Is.Null);
            Assert.That(allocations, Is.Zero, "有限的双态样式不应在每次切换时创建相同字符串。");
        }

        [Test]
        public void DeferredPointerCancellationKeepsSubtreeAndReentrantSemantics()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#owner{position:absolute;width:100px;height:100px;}</style></head><body><div id='owner'></div></body></html>", "/cancel-reentrancy.html");
            document.SynchronizeLayout();
            var owner = document.Q("owner");
            var input = document.Surface.Input;
            var cancelled = 0;
            using var cancel = owner.RegisterCallback<PointerCancelEvent>(_ =>
            {
                cancelled++;
                input.CancelPointers(owner);
            });
            using var press = owner.RegisterCallback<PointerDownEvent>(_ =>
            {
                owner.CapturePointer(0);
                input.CancelPointers(owner);
            });
            input.SendPointer(PointerPhase.Down, new Vector2(20, 20), pressedButtons: 1);
            Assert.That(cancelled, Is.EqualTo(1));
            Assert.That(owner.HasPointerCapture(0), Is.False);
            input.SendPointer(PointerPhase.Up, new Vector2(20, 20));
            Assert.That(cancelled, Is.EqualTo(1));
        }

        [Test]
        public void NestedEventDispatchReusesPathsAndSubscriptionSnapshots()
        {
            Load();
            var target = document.Q("n0");
            var parent = target.Parent;
            var outer = new PointerMoveEvent();
            var inner = new PointerMoveEvent();
            var delivered = 0;
            using var first = target.RegisterCallback<PointerMoveEvent>(value =>
            {
                delivered++;
                if (ReferenceEquals(value, outer))
                {
                    target.SendEvent(inner);
                }
            });
            using var second = parent.RegisterCallback<PointerMoveEvent>(value => delivered++);
            Action cycle = () => target.SendEvent(outer);
            for (var i = 0; i < 300; i++)
            {
                cycle();
            }
            ValidateRecorder();
            var before = delivered;
            var allocations = Allocations(() =>
            {
                for (var i = 0; i < 32; i++)
                {
                    cycle();
                }
            });
            Assert.That(delivered - before, Is.EqualTo(128));
            Assert.That(allocations, Is.Zero, "嵌套分发应复用独立传播路径，订阅快照仅在订阅改变时创建。");
        }

        [Test]
        public void NestedSubscriptionChangesKeepTheOuterPropagationSnapshot()
        {
            Load();
            var target = document.Q("n0");
            var parent = target.Parent;
            var outer = new PointerMoveEvent();
            var inner = new PointerMoveEvent();
            var calls = new System.Collections.Generic.List<string>();
            bool changed = false;
            IDisposable added = null;
            IDisposable previous = null;
            using var first = target.RegisterCallback<PointerMoveEvent>(value =>
            {
                calls.Add(ReferenceEquals(value, outer) ? "target-outer" : "target-inner");
                if (ReferenceEquals(value, outer) && !changed)
                {
                    changed = true;
                    added = parent.RegisterCallback<PointerMoveEvent>(nested =>
                                        {
                                            calls.Add(ReferenceEquals(nested, outer) ? "new-outer" : "new-inner");
                                            previous.Dispose();
                                        });
                    target.SendEvent(inner);
                }
            });
            previous = parent.RegisterCallback<PointerMoveEvent>(value => calls.Add("old-inner"));
            try
            {
                target.SendEvent(outer);
                Assert.That(calls, Is.EqualTo(new[] { "target-outer", "target-inner", "old-inner", "new-inner" }));
                calls.Clear();
                target.SendEvent(outer);
                Assert.That(calls, Is.EqualTo(new[] { "target-outer", "new-outer" }));
                Assert.That(outer.Target, Is.SameAs(target));
                Assert.That(inner.Target, Is.SameAs(target));
            }
            finally
            {
                added?.Dispose();
                previous.Dispose();
            }
        }

        [Test]
        public void AutomaticPointerPollingSkipsStableInputButTracksLayoutAndExplicitEvents()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;width:128px;height:128px;}div{position:absolute;left:0;top:0;width:100px;height:100px;}#under{z-index:0;}#over{z-index:1;}</style></head><body><div id='under'></div><div id='over'></div></body></html>", "/pointer-poll.html");
            document.SynchronizeLayout();
            var input = document.Surface.Input;
            var moves = 0;
            var enters = 0;
            var leaves = 0;
            using var movement = document.Body.RegisterCallback<PointerMoveEvent>(_ => moves++);
            using var enter = document.Q("under").RegisterCallback<PointerEnterEvent>(_ => enters++);
            using var leave = document.Q("over").RegisterCallback<PointerLeaveEvent>(_ => leaves++);
            void Poll() => input.PollUnityPointer(new Vector2(40, 40), 0, false, 0, InputModifiers.None);
            Poll();
            var initial = moves;
            Assert.That(initial, Is.EqualTo(1));
            for (var i = 0; i < 8; i++)
            {
                Poll();
            }
            Assert.That(moves, Is.EqualTo(initial));
            document.Q("over").Style.Set("left", "150px");
            document.SynchronizeLayout();
            Poll();
            Assert.That(moves, Is.EqualTo(initial + 1));
            Assert.That(enters, Is.EqualTo(1));
            Assert.That(leaves, Is.EqualTo(1));
            input.SendPointer(PointerPhase.Move, new Vector2(40, 40));
            input.SendPointer(PointerPhase.Move, new Vector2(40, 40));
            Assert.That(moves, Is.EqualTo(initial + 3), "显式 SendPointer 每次调用仍是一个真实事件。");
            Poll();
            var afterExplicit = moves;
            Poll();
            Assert.That(moves, Is.EqualTo(afterExplicit));
            document.Q("under").Style.Set("pointer-events", "none");
            document.SynchronizeLayout();
            Poll();
            Assert.That(moves, Is.EqualTo(afterExplicit + 1), "静止坐标也必须观察命中策略变化。");
        }
        [Test]
        public void UnityClipboardSynchronizesAtFocusAndEditingBoundaries()
        {
            var system = GUIUtility.systemCopyBuffer;
            string native = null;
            ulong surface = 0;
            try
            {
                document.LoadHtml("<!doctype html><html><head></head><body><input type='text' id='value' value='abc'></body></html>", "/clipboard-boundaries.html");
                document.SynchronizeLayout();
                surface = document.Surface.SurfaceHandle;
                native = NativeMethods.ReadClipboard(surface);
                var input = document.Surface.Input;
                var field = document.Q<TextField>("value");
                GUIUtility.systemCopyBuffer = "external α";
                NativeMethods.urui_clipboard_write(surface, "old");
                field.Focus();
                input.SyncUnityClipboardFocus();
                Assert.That(NativeMethods.ReadClipboard(surface), Is.EqualTo("external α"));
                GUIUtility.systemCopyBuffer = "later";
                input.SyncUnityClipboardFocus();
                Assert.That(NativeMethods.ReadClipboard(surface), Is.EqualTo("external α"));
                field.SelectRange(0, 3);
                input.SendUnityKey(KeyCode.V, true, InputModifiers.Control);
                Assert.That(field.Value, Is.EqualTo("later"));
                field.SelectRange(0, 5);
                GUIUtility.systemCopyBuffer = "outsider";
                input.SendUnityKey(KeyCode.C, true, InputModifiers.Control);
                Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo("later"));
                Action idle = () =>
                {
                    input.SyncUnityClipboardFocus();
                    input.FlushUnityClipboard();
                };
                for (var i = 0; i < 300; i++)
                {
                    idle();
                }
                ValidateRecorder();
                Assert.That(Allocations(() =>
                {
                    for (var i = 0; i < 32; i++)
                    {
                        idle();
                    }
                }), Is.Zero);
            }
            finally
            {
                GUIUtility.systemCopyBuffer = system;
                if (surface != 0)
                {
                    NativeMethods.urui_clipboard_write(surface, native ?? "");
                }
            }
        }
        [Test]
        public void UnobservedNativePointerMovesDoNotAllocateManagedEvents()
        {
            document.LoadHtml("<!doctype html><html><head><style>#value{position:absolute;left:0;top:0;width:100px;height:100px;}</style></head><body><div id='value'></div></body></html>", "/unobserved-events.html");
            document.SynchronizeLayout();
            var input = document.Surface.Input;
            var frame = 0;
            Action cycle = () => input.SendPointer(PointerPhase.Move, new Vector2(40 + (++frame % 2), 40));
            for (var i = 0; i < 300; i++)
            {
                cycle();
            }
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 32; i++)
                {
                    cycle();
                }
            }), Is.Zero, "没有托管兴趣的原生事件不应构造公开 EventBase。");
            var delivered = 0;
            using (var registration = document.Body.RegisterCallback<PointerMoveEvent>(_ => delivered++))
            {
                cycle();
                Assert.That(delivered, Is.EqualTo(1), "祖先订阅必须接收到子节点事件。");
            }
            cycle();
            Assert.That(delivered, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnobservedManagedLayoutAndAttachmentChangesDoNotAllocate(bool hierarchy)
        {
            Load();
            var element = document.Q("n0");
            var frame = 0;
            Action cycle = () =>
            {
                if (hierarchy)
                {
                    if (element.IsAttached)
                    {
                        element.RemoveFromHierarchy();
                    }
                    else
                    {
                        document.Body.Add(element);
                    }
                }
                else
                {
                    element.Style.Set("width", ++frame % 2 == 0 ? "12px" : "13px");
                }
                document.SynchronizeLayout();
            };
            for (var i = 0; i < 300; i++)
            {
                cycle();
            }
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 32; i++)
                {
                    cycle();
                }
            }), Is.Zero,
                "目标独占的布局/挂载通知没有回调和控件钩子时，不应构造 EventBase。");
        }

        sealed class NotificationHookElement : URUI.Elements.Element
        {
            internal int Geometry;
            internal int Attached;
            internal int Detached;
            internal NotificationHookElement(URUI.Elements.ElementContext context) : base(context) { }
            protected override void OnEvent(EventBase value)
            {
                if (value is GeometryChangedEvent)
                {
                    Geometry++;
                }
                if (value is AttachToDocumentEvent)
                {
                    Attached++;
                }
                if (value is DetachFromDocumentEvent)
                {
                    Detached++;
                }
            }
        }

        [Test]
        public void ManagedNotificationsKeepHooksDynamicSubscriptionsAndRetainedEvents()
        {
            document.RegisterElementType("urui-notification-probe", context => new NotificationHookElement(context));
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div,urui-notification-probe{display:block;width:10px;height:10px;}</style></head><body><div id='plain'></div><urui-notification-probe id='hook'></urui-notification-probe></body></html>", "/managed-notifications.html");
            document.SynchronizeLayout();
            var hook = document.Q<NotificationHookElement>("hook");
            var before = hook.Geometry;
            hook.Style.Set("width", "20px");
            document.SynchronizeLayout();
            Assert.That(hook.Geometry, Is.GreaterThan(before), "OnEvent 是消费者，不依赖公开委托注册。");
            var detached = hook.Detached;
            hook.RemoveFromHierarchy();
            document.SynchronizeLayout();
            Assert.That(hook.Detached, Is.EqualTo(detached + 1));
            var attached = hook.Attached;
            document.Body.Add(hook);
            document.SynchronizeLayout();
            Assert.That(hook.Attached, Is.EqualTo(attached + 1));
            var plain = document.Q("plain");
            plain.Style.Set("width", "15px");
            document.SynchronizeLayout();
            GeometryChangedEvent first = null;
            GeometryChangedEvent second = null;
            var calls = 0;
            var registration = plain.RegisterCallback<GeometryChangedEvent>(value =>
            {
                calls++;
                if (first == null)
                {
                    first = value;
                }
                else
                {
                    second = value;
                }
            });
            plain.Style.Set("width", "16px");
            document.SynchronizeLayout();
            plain.Style.Set("width", "17px");
            document.SynchronizeLayout();
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.OldRect.width, Is.EqualTo(15));
            Assert.That(first.NewRect.width, Is.EqualTo(16));
            Assert.That(second.OldRect.width, Is.EqualTo(16));
            Assert.That(second.NewRect.width, Is.EqualTo(17));
            registration.Dispose();
            plain.Style.Set("width", "18px");
            document.SynchronizeLayout();
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(first.NewRect.width, Is.EqualTo(16), "保留的公开事件不能被后续布局复用或改写。");
        }
        [Test]
        public void PreparedStyleVersionsDoNotAllocateManagedOwners()
        {
            document.LoadHtml("<!doctype html><html><head><style>@property --phase{syntax:'<number>';inherits:false;initial-value:0;}</style></head><body><div id='value'></div></body></html>", "/prepared-style-allocation.html");
            document.SynchronizeLayout();
            var element = document.Q("value");
            var prepared = new URUI.Internal.Rendering.NativePreparedStyle();
            var frame = 0;
            Action cycle = () =>
            {
                element.Style.Set("--phase", ++frame % 2 == 0 ? ".25" : ".75");
                document.SynchronizeLayout();
                prepared.Replace(element);
            };
            try
            {
                for (var i = 0; i < 300; i++)
                {
                    cycle();
                }
                ValidateRecorder();
                Assert.That(Allocations(() =>
                {
                    for (var i = 0; i < 32; i++)
                    {
                        cycle();
                    }
                }), Is.Zero, "准备样式仅在观察公开值时物化，私有版本替换不能每帧创建托管所有者。");
            }
            finally
            {
                prepared.Dispose();
            }
        }

        [Test]
        public void AllocationChannelDetectsPositiveProbeAndHasNoEmptyActionNoise()
        {
            ValidateRecorder();
        }

        [TestCase(true, 0.0000000213, false)]
        [TestCase(true, 0.001, true)]
        [TestCase(false, 0.0000000213, true)]
        public void NativeClockOnlyToleratesBoundedUnitySampleRoundoff(bool unityClock, double backwards, bool rejected)
        {
            if (unityClock)
            {
                document.Surface.UIClock = UnityUIClock.Unscaled;
            }
            Load();
            var host = root.GetComponentInChildren<URUI.Internal.Hosting.RenderHost>(true);
            var last = typeof(URUI.Internal.Hosting.RenderHost).GetField("lastUITime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var previous = document.Surface.UIClock.Now + backwards;
            last.SetValue(host, previous);
            if (rejected)
            {
                Assert.Throws<InvalidOperationException>(() => document.SynchronizeLayout());
            }
            else
            {
                Assert.DoesNotThrow(() => document.SynchronizeLayout());
                Assert.That((double)last.GetValue(host), Is.GreaterThanOrEqualTo(previous));
            }
        }

        struct EarlyUpdateClockProbe { }

        sealed class FixedUpdateClockProbe : MonoBehaviour
        {
            internal Action OnFixed;
            void FixedUpdate() => OnFixed?.Invoke();
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator UnityUIClockStaysMonotonicAcrossEarlyUpdateFixedUpdateAndUpdate()
        {
            var previousScale = Time.timeScale;
            var previousLoop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var probe = root.AddComponent<FixedUpdateClockProbe>();
            Action onEarly = null;
            try
            {
                // Mirror InputSystem / EarlyUpdate callbacks running before FixedUpdate on the same frame.
                var loop = previousLoop;
                var systems = loop.subSystemList;
                for (var i = 0; i < systems.Length; ++i)
                {
                    if (systems[i].type == typeof(UnityEngine.PlayerLoop.EarlyUpdate))
                    {
                        var list = new System.Collections.Generic.List<UnityEngine.LowLevel.PlayerLoopSystem>(systems[i].subSystemList);
                        list.Add(new UnityEngine.LowLevel.PlayerLoopSystem
                        {
                            type = typeof(EarlyUpdateClockProbe),
                            updateDelegate = () => onEarly?.Invoke()
                        });
                        systems[i].subSystemList = list.ToArray();
                        break;
                    }
                }
                loop.subSystemList = systems;
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);

                foreach (var clock in new[] { UnityUIClock.Unscaled, UnityUIClock.Scaled })
                {
                    document.Unload();
                    document.Surface.UIClock = clock;
                    Load();
                    var observed = clock.Now;
                    Exception loopFailure = null;
                    var earlySamples = 0;
                    var fixedSamples = 0;

                    void Sample(string phase)
                    {
                        try
                        {
                            var now = clock.Now;
                            if (now < observed)
                            {
                                throw new InvalidOperationException(
                                    $"{phase} clock regressed: now={now:R} < observed={observed:R} (frame={Time.frameCount}, inFixed={Time.inFixedTimeStep})");
                            }
                            observed = now;
                            document.SynchronizeGeometry();
                        }
                        catch (Exception ex)
                        {
                            loopFailure ??= ex;
                        }
                    }

                    onEarly = () =>
                    {
                        earlySamples++;
                        Sample("EarlyUpdate");
                    };
                    probe.OnFixed = () =>
                    {
                        fixedSamples++;
                        Sample("FixedUpdate");
                    };

                    for (var step = 0; step < 6; ++step)
                    {
                        Time.timeScale = step % 2 == 0 ? 1f : 2f;
                        System.Threading.Thread.Sleep(25);
                        yield return null;
                        if (loopFailure != null)
                        {
                            throw loopFailure;
                        }
                        Sample("Update");
                        if (loopFailure != null)
                        {
                            throw loopFailure;
                        }
                    }
                    onEarly = null;
                    probe.OnFixed = null;
                    Assert.That(earlySamples, Is.GreaterThan(0));
                    Assert.That(fixedSamples, Is.GreaterThan(0));
                }
            }
            finally
            {
                onEarly = null;
                probe.OnFixed = null;
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(previousLoop);
                Time.timeScale = previousScale;
            }
        }

        [Test]
        public void UnchangedElementRefreshDoesNotAllocateMemberSnapshots()
        {
            Load();
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 20; ++i)
                {
                    document.RefreshElements(true);
                }
            }), Is.Zero);
        }

        sealed class RefreshHookElement : URUI.Elements.Element
        {
            internal Action Tick;
            internal RefreshHookElement(URUI.Elements.ElementContext context) : base(context) { }
            protected override void OnUpdate() => Tick?.Invoke();
        }

        [Test]
        public void StablePassiveRefreshDoesNotScaleWithWrappedNodes()
        {
            double Measure(int count)
            {
                var html = new StringBuilder("<!doctype html><html><head><style>div{position:absolute;width:4px;height:4px;}</style></head><body>");
                for (var i = 0; i < count; i++)
                {
                    html.Append("<div></div>");
                }
                document.LoadHtml(html.Append("</body></html>").ToString(), "/passive-refresh.html");
                document.SynchronizeGeometry();
                Assert.That(document.Query<URUI.Elements.Element>().ToList().Count, Is.GreaterThanOrEqualTo(count));
                for (var i = 0; i < 32; i++)
                {
                    document.RefreshElements(true);
                }
                var samples = new double[5];
                for (var sample = 0; sample < samples.Length; sample++)
                {
                    var start = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (var i = 0; i < 128; i++)
                    {
                        document.RefreshElements(true);
                    }
                    samples[sample] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000000d /
                        System.Diagnostics.Stopwatch.Frequency / 128;
                }
                Array.Sort(samples);
                return samples[2];
            }
            var small = Measure(500);
            var large = Measure(4000);
            TestContext.WriteLine("500/4000节点稳定刷新：" + small + "/" + large + "us，比例=" + large / small);
            Assert.That(large / small, Is.LessThan(1.8), "已完整观察且没有更新钩子的静态节点不应在每次刷新重新线性遍历。");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StableMixedRefreshDoesNotScaleWithPassiveNodes(bool hookFirst)
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            double Measure(int count)
            {
                const string markup = "<urui-refresh-probe id='hook'></urui-refresh-probe>";
                var html = new StringBuilder("<!doctype html><html><head><style>div{position:absolute;width:4px;height:4px;}</style></head><body>");
                if (hookFirst)
                {
                    html.Append(markup);
                }
                for (var i = 0; i < count; i++)
                {
                    html.Append("<div></div>");
                }
                if (!hookFirst)
                {
                    html.Append(markup);
                }
                document.LoadHtml(html.Append("</body></html>").ToString(), "/mixed-refresh.html");
                document.SynchronizeGeometry();
                var hook = document.Q<RefreshHookElement>("hook");
                var calls = 0;
                hook.Tick = () => calls++;
                for (var i = 0; i < 32; i++)
                {
                    document.RefreshElements(true);
                }
                var before = calls;
                var samples = new double[5];
                for (var sample = 0; sample < samples.Length; sample++)
                {
                    var start = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (var i = 0; i < 128; i++)
                    {
                        document.RefreshElements(true);
                    }
                    samples[sample] = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000000d /
                        System.Diagnostics.Stopwatch.Frequency / 128;
                }
                Assert.That(calls - before, Is.EqualTo(640), "普通节点不变时也必须逐次执行真实控件更新。");
                Array.Sort(samples);
                return samples[2];
            }
            var small = Measure(500);
            var large = Measure(4000);
            TestContext.WriteLine("混合文档500/4000节点：" + small + "/" + large + "us，比例=" + large / small);
            Assert.That(large / small, Is.LessThan(1.8), "一个必要更新钩子不能让全部被动包装器继续线性刷新。");
        }

        sealed class InitializingRefreshHook : URUI.Elements.Element
        {
            internal int Updates;
            internal InitializingRefreshHook(URUI.Elements.ElementContext context) : base(context) { }
            protected override void OnInitialize() => Document.RefreshElements(true);
            protected override void OnUpdate() => Updates++;
        }

        ulong AddNativeRefreshHook()
        {
            var surface = document.Surface.SurfaceHandle;
            var token = NativeMethods.urui_element_create(surface, document.NativeHandle, "urui-initialize-refresh");
            Assert.That(token, Is.Not.Zero);
            Assert.That(NativeMethods.urui_element_write(surface, document.NativeHandle, token, ElementWriteField.Attribute, "id", "late-hook"), Is.EqualTo(1));
            Assert.That(NativeMethods.urui_element_insert(surface, document.NativeHandle, document.Body.Token, token, -1), Is.EqualTo(1));
            NativeMethods.urui_surface_update(surface);
            for (var i = 0; i < 4; i++)
            {
                document.RefreshElements(true);
            }
            return token;
        }

        [Test]
        public void LateWrapperInitializationCannotReusePassiveDocumentProof()
        {
            document.RegisterElementType("urui-initialize-refresh", context => new InitializingRefreshHook(context));
            Load();
            AddNativeRefreshHook();
            var hook = document.Q<InitializingRefreshHook>("late-hook");
            Assert.That(hook.Updates, Is.GreaterThan(0), "新包装器初始化重入时也必须执行已挂接的更新钩子。");
            var before = hook.Updates;
            for (var i = 0; i < 31; i++)
            {
                document.RefreshElements(true);
            }
            Assert.That(hook.Updates - before, Is.EqualTo(31));
        }

        [Test]
        public void GeometryCallbackWrappingControlCannotPublishPassiveProof()
        {
            document.RegisterElementType("urui-initialize-refresh", context => new InitializingRefreshHook(context));
            Load();
            AddNativeRefreshHook();
            var target = document.Q("n0");
            InitializingRefreshHook hook = null;
            target.RegisterCallback<GeometryChangedEvent>(e => hook = document.Q<InitializingRefreshHook>("late-hook"));
            target.Style.Set("width", "30px");
            NativeMethods.urui_surface_update(document.Surface.SurfaceHandle);
            document.RefreshElements(true);
            Assert.That(hook, Is.Not.Null);
            var before = hook.Updates;
            for (var i = 0; i < 31; i++)
            {
                document.RefreshElements(true);
            }
            Assert.That(hook.Updates - before, Is.EqualTo(31));
        }

        [Test]
        public void ThrowingUpdateHookIsRetriedAfterPassiveDocumentBecomesDynamic()
        {
            Load();
            for (var i = 0; i < 4; i++)
            {
                document.RefreshElements(true);
            }
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            var hook = document.Create<RefreshHookElement>();
            document.Body.Add(hook);
            document.RefreshElements(true);
            document.RefreshElements(true);
            hook.Tick = () => throw new InvalidOperationException("refresh retry probe");
            Assert.Throws<InvalidOperationException>(() => document.RefreshElements(true));
            var calls = 0;
            hook.Tick = () => calls++;
            document.RefreshElements(true);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void SparseUpdateStopsReadingTailWhenCallbackWrapsNativeNodes()
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            document.LoadHtml("<!doctype html><html><head></head><body><urui-refresh-probe id='first'></urui-refresh-probe><urui-refresh-probe id='second'></urui-refresh-probe></body></html>", "/refresh-tail.html");
            var first = document.Q<RefreshHookElement>("first");
            var second = document.Q<RefreshHookElement>("second");
            var surface = document.Surface.SurfaceHandle;
            for (var i = 0; i < 2; i++)
            {
                var token = NativeMethods.urui_element_create(surface, document.NativeHandle, "urui-refresh-probe");
                Assert.That(NativeMethods.urui_element_write(surface, document.NativeHandle, token, ElementWriteField.Attribute, "id", "late" + i), Is.EqualTo(1));
                Assert.That(NativeMethods.urui_element_insert(surface, document.NativeHandle, document.Body.Token, token, -1), Is.EqualTo(1));
            }
            NativeMethods.urui_surface_update(surface);
            for (var i = 0; i < 4; i++)
            {
                document.RefreshElements(true);
            }
            var firstCalls = 0;
            var secondCalls = 0;
            var lateCalls = 0;
            first.Tick = () =>
            {
                firstCalls++;
                first.Tick = () => firstCalls++;
                for (var i = 0; i < 2; i++)
                {
                    document.Q<RefreshHookElement>("late" + i).Tick = () => lateCalls++;
                }
            };
            second.Tick = () => secondCalls++;
            document.RefreshElements(true);
            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1));
            Assert.That(lateCalls, Is.Zero, "回调新包装的节点不能替换旧快照中尚未执行的钩子。");
            document.RefreshElements(true);
            Assert.That(firstCalls, Is.EqualTo(2));
            Assert.That(secondCalls, Is.EqualTo(2));
            Assert.That(lateCalls, Is.EqualTo(2));
        }

        [Test]
        public void DenseUpdateHooksKeepAllCallsWithoutSpareSnapshotCapacity()
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            var html = new StringBuilder("<!doctype html><html><head></head><body>");
            for (var i = 0; i < 100; i++)
            {
                html.Append("<urui-refresh-probe></urui-refresh-probe>");
            }
            document.LoadHtml(html.Append("</body></html>").ToString(), "/refresh-dense.html");
            document.SynchronizeGeometry();
            var hooks = document.Query<RefreshHookElement>().ToList();
            Assert.That(hooks.Count, Is.EqualTo(100));
            var calls = 0;
            foreach (var hook in hooks)
            {
                hook.Tick = () => calls++;
            }
            for (var i = 0; i < 32; i++)
            {
                document.RefreshElements(true);
            }
            Assert.That(calls, Is.EqualTo(3200));
        }

        [Test]
        public void SparseUpdateHooksDoNotAllocateNewSnapshots()
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            Load();
            var hook = document.Create<RefreshHookElement>();
            document.Body.Add(hook);
            var calls = 0;
            hook.Tick = () => calls++;
            Action refresh = () =>
            {
                for (var i = 0; i < 32; i++)
                {
                    document.RefreshElements(true);
                }
            };
            refresh();
            ValidateRecorder();
            var before = calls;
            Assert.That(Allocations(refresh), Is.Zero);
            Assert.That(calls - before, Is.EqualTo(32));
        }

        [Test]
        public void SparseUpdateStopsWhenFirstHookUnloadsDocument()
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            document.LoadHtml("<!doctype html><html><head></head><body><urui-refresh-probe id='first'></urui-refresh-probe><urui-refresh-probe id='second'></urui-refresh-probe></body></html>", "/refresh-unload.html");
            var first = document.Q<RefreshHookElement>("first");
            var second = document.Q<RefreshHookElement>("second");
            document.SynchronizeGeometry();
            for (var i = 0; i < 4; i++)
            {
                document.RefreshElements(true);
            }
            var laterCalls = 0;
            first.Tick = () => document.Unload();
            second.Tick = () => laterCalls++;
            Assert.DoesNotThrow(() => document.RefreshElements(true));
            Assert.That(document.IsLoaded, Is.False);
            Assert.That(laterCalls, Is.Zero);
        }

        [Test]
        public void ReentrantFullRefreshCannotPublishTailOverwrittenByOuterPass()
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            document.LoadHtml("<!doctype html><html><head></head><body><urui-refresh-probe id='a'></urui-refresh-probe><urui-refresh-probe id='b'></urui-refresh-probe><urui-refresh-probe id='c'></urui-refresh-probe></body></html>", "/refresh-reentrant-tail.html");
            var a = document.Q<RefreshHookElement>("a");
            var b = document.Q<RefreshHookElement>("b");
            var c = document.Q<RefreshHookElement>("c");
            document.SynchronizeGeometry();
            for (var i = 0; i < 4; i++)
            {
                document.RefreshElements(true);
            }
            var calls = new System.Collections.Generic.List<string>();
            a.Tick = () =>
            {
                a.Tick = null;
                a.RemoveFromHierarchy();
                document.RefreshElements(true);
            };
            b.Tick = () => calls.Add("b");
            c.Tick = () => calls.Add("c");
            // 原生变化让外层先走完整遍历，再在钩子内发生重入。
            document.Body.Style.Set("width", "120px");
            NativeMethods.urui_surface_update(document.Surface.SurfaceHandle);
            document.RefreshElements(true);
            calls.Clear();
            document.RefreshElements(true);
            Assert.That(calls, Is.EqualTo(new[] { "b", "c" }), "外层不能覆盖内层发布的更新集合，导致重复或漏调。");
        }

        [Test]
        public void AttachmentRefreshCannotSkipFirstGeometryOfLatePassiveWrapper()
        {
            Load();
            var sentinel = document.CreateElement();
            var surface = document.Surface.SurfaceHandle;
            var token = NativeMethods.urui_element_create(surface, document.NativeHandle, "div");
            Assert.That(NativeMethods.urui_element_write(surface, document.NativeHandle, token, ElementWriteField.Attribute, "id", "late-passive"), Is.EqualTo(1));
            Assert.That(NativeMethods.urui_element_insert(surface, document.NativeHandle, document.Body.Token, token, -1), Is.EqualTo(1));
            NativeMethods.urui_surface_update(surface);
            for (var i = 0; i < 4; i++)
            {
                document.RefreshElements(true);
            }
            var target = document.Q("n0");
            URUI.Elements.Element late = null;
            var notifications = 0;
            target.RegisterCallback<GeometryChangedEvent>(e =>
            {
                late = document.Q("late-passive");
                late.RegisterCallback<GeometryChangedEvent>(geometry => notifications++);
            });
            target.Style.Set("width", "30px");
            NativeMethods.urui_surface_update(document.Surface.SurfaceHandle);
            document.RefreshElements(true);
            Assert.That(late, Is.Not.Null);
            Assert.That(notifications, Is.Zero);
            // 对已脱离的节点重复移除仍会走真实的 attachment-only 刷新入口。
            sentinel.RemoveFromHierarchy();
            document.RefreshElements(true);
            document.RefreshElements(true);
            Assert.That(notifications, Is.EqualTo(1), "仅挂接刷新不能掩盖新包装器尚未观察的首次几何通知。");
        }

        [Test]
        public void EmptyHierarchySnapshotsDoNotAllocate()
        {
            Load();
            var leaf = document.Q("n0");
            Assert.That(leaf.Hierarchy.Children(), Is.Empty);
            Action read = () =>
            {
                for (var i = 0; i < 32; ++i)
                {
                    GC.KeepAlive(leaf.Hierarchy.Children());
                }
            };
            read();
            ValidateRecorder();
            Assert.That(Allocations(read), Is.Zero);
        }

        sealed class SweepHookElement : URUI.Elements.Element
        {
            internal Action Invalidating;
            internal int Invalidations;
            internal SweepHookElement(URUI.Elements.ElementContext context) : base(context) { }
            protected override void OnInvalidated()
            {
                Invalidations++;
                var callback = Invalidating;
                Invalidating = null;
                callback?.Invoke();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlainSubtreeInvalidationsPreserveLaterControlCallbacks(bool releaseOnly)
        {
            document.RegisterElementType("urui-sweep-probe", context => new SweepHookElement(context));
            var html = new StringBuilder("<!doctype html><html><head></head><body>");
            for (var i = 0; i < 128; i++)
            {
                html.Append("<div id='plain").Append(i).Append("'></div>");
            }
            document.LoadHtml(html.Append("<urui-sweep-probe id='trigger'></urui-sweep-probe><div id='later'></div></body></html>").ToString(), "/sweep-plain.html");
            var trigger = document.Q<SweepHookElement>("trigger");
            var later = document.Q("later");
            var surface = document.Surface.SurfaceHandle;
            for (var i = 0; i < 128; i++)
            {
                Assert.That(NativeMethods.urui_element_destroy(surface, document.NativeHandle, document.Q("plain" + i).Token), Is.EqualTo(1));
            }
            trigger.Invalidating = () => Assert.That(releaseOnly
                ? NativeMethods.urui_element_ref_release(surface, document.NativeHandle, later.Token)
                : NativeMethods.urui_element_destroy(surface, document.NativeHandle, later.Token), Is.EqualTo(1));
            Assert.That(NativeMethods.urui_element_destroy(surface, document.NativeHandle, trigger.Token), Is.EqualTo(1));
            document.SweepInvalidElements();
            Assert.That(trigger.Invalidations, Is.EqualTo(1));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Assert.That(typeof(URUI.Elements.Element).GetField("invalid", flags).GetValue(later), Is.True,
                "普通节点清理后，控件失效回调仍必须使后续预读失效。");
            var state = typeof(Document).GetField("content", flags).GetValue(document);
            var elements = (System.Collections.IDictionary)state.GetType().GetField("Elements", flags).GetValue(state);
            Assert.That(elements.Contains(later.Token), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SweepDiscardsPrefetchedValidityAfterInvalidationCallbacks(int mutation)
        {
            document.RegisterElementType("urui-sweep-probe", context => new SweepHookElement(context));
            var html = new StringBuilder("<!doctype html><html><head></head><body>");
            for (var i = 0; i < 150; i++)
            {
                html.Append("<urui-sweep-probe id='n").Append(i).Append("'></urui-sweep-probe>");
            }
            document.LoadHtml(html.Append("</body></html>").ToString(), "/sweep-callback.html");
            var trigger = document.Q<SweepHookElement>("n30");
            var later = document.Q<SweepHookElement>("n31");
            var nextWindow = document.Q<SweepHookElement>("n100");
            var earlier = document.Q<SweepHookElement>("n0");
            var surface = document.Surface.SurfaceHandle;
            trigger.Invalidating = () =>
            {
                if (mutation == 3)
                {
                    document.Unload();
                    return;
                }
                Assert.That(NativeMethods.urui_element_destroy(surface, document.NativeHandle, nextWindow.Token), Is.EqualTo(1));
                Assert.That(mutation == 1 ? NativeMethods.urui_element_ref_release(surface, document.NativeHandle, later.Token)
                    : NativeMethods.urui_element_destroy(surface, document.NativeHandle, later.Token), Is.EqualTo(1));
                if (mutation == 2)
                {
                    Assert.That(NativeMethods.urui_element_destroy(surface, document.NativeHandle, earlier.Token), Is.EqualTo(1));
                    document.SweepInvalidElements();
                    var created = document.CreateElement();
                    created.Name = "created-during-sweep";
                    document.Body.Hierarchy.Add(created);
                }
            };
            Assert.That(NativeMethods.urui_element_destroy(surface, document.NativeHandle, trigger.Token), Is.EqualTo(1));
            document.SweepInvalidElements();
            Assert.That(trigger.Invalidations, Is.EqualTo(1));
            Assert.That(later.Invalidations, Is.EqualTo(1), "不能采用失效回调之前预读的有效标记。");
            Assert.That(nextWindow.Invalidations, Is.EqualTo(1));
            if (mutation == 2)
            {
                Assert.That(earlier.Invalidations, Is.EqualTo(1));
                Assert.That(document.Q("created-during-sweep"), Is.Not.Null);
            }
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var state = typeof(Document).GetField("content", flags).GetValue(document);
            if (state != null)
            {
                var elements = (System.Collections.IDictionary)state.GetType().GetField("Elements", flags).GetValue(state);
                Assert.That(elements.Contains(later.Token) || elements.Contains(nextWindow.Token), Is.False);
            }
        }

        [Test]
        public void ReadOnlyUpdateHooksDoNotRepeatFullElementValidation()
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            document.LoadHtml("<!doctype html><html><head></head><body><urui-refresh-probe id='hook'></urui-refresh-probe></body></html>", "/refresh-hook.html");
            document.SynchronizeGeometry();
            var hook = document.Q<RefreshHookElement>("hook");
            var calls = 0;
            hook.Tick = () => calls++;
            long sweeps = 0;
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Elements.Sweep", 128, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                for (var i = 0; i < 32; i++)
                {
                    document.RefreshElements(true);
                }
                recorder.Stop();
                Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
                for (var i = 0; i < recorder.Count; i++)
                {
                    sweeps += recorder.GetSample(i).Count;
                }
            }
            Assert.That(calls, Is.EqualTo(32));
            Assert.That(sweeps, Is.Zero, "只读更新回调不能使已经批量验证过的整份节点列表重新逐项验证。");
        }

        [Test]
        public void StableRefreshReusesObservedStateWithoutSkippingUpdateHooks()
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            document.LoadHtml("<!doctype html><html><head></head><body><urui-refresh-probe id='hook'></urui-refresh-probe><div id='tail'></div></body></html>", "/refresh-stable.html");
            var hook = document.Q<RefreshHookElement>("hook");
            document.Q("tail");
            document.SynchronizeGeometry();
            var calls = 0;
            hook.Tick = () => calls++;
            document.RefreshElements(true);
            calls = 0;
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Elements.ReadStates", 128,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (var i = 0; i < 32; i++)
            {
                document.RefreshElements(true);
            }
            recorder.Stop();
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            long reads = 0;
            for (var i = 0; i < recorder.Count; i++)
            {
                reads += recorder.GetSample(i).Count;
            }
            Assert.That(calls, Is.EqualTo(32), "状态未变化也必须运行每次控件更新。");
            Assert.That(reads, Is.Zero, "只读控件更新不能使未变化节点重复跨层查询状态。");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UpdateHookGeometryMutationRefreshesEarlierAndLaterElements(bool earlier)
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            document.LoadHtml("<!doctype html><html><head><style>div{width:10px;height:10px;}</style></head><body><div id='before'></div><urui-refresh-probe id='hook'></urui-refresh-probe><div id='after'></div></body></html>", "/refresh-mutation.html");
            var before = document.Q("before");
            var hook = document.Q<RefreshHookElement>("hook");
            var after = document.Q("after");
            var target = earlier ? before : after;
            document.SynchronizeGeometry();
            document.RefreshElements(true);
            var widths = new System.Collections.Generic.List<float>();
            target.RegisterCallback<GeometryChangedEvent>(e => widths.Add(e.NewRect.width));
            hook.Tick = () =>
            {
                hook.Tick = null;
                target.Style.Set("width", "30px");
                NativeMethods.urui_surface_update(document.Surface.SurfaceHandle);
            };
            document.RefreshElements(true);
            document.RefreshElements(true);
            Assert.That(widths, Is.EqualTo(new[] { 30f }), "回调修改更早节点后，下一轮仍须观察最新布局；较晚节点应在本轮观察到。");
            Assert.That(target.Layout.width, Is.EqualTo(30));
        }

        [Test]
        public void AttachmentOnlyRefreshCannotPublishUnobservedGeometry()
        {
            document.LoadHtml("<!doctype html><html><head><style>div{width:10px;height:10px;}</style></head><body><div id='target'></div></body></html>", "/refresh-attachment.html");
            var target = document.Q("target");
            document.SynchronizeGeometry();
            document.RefreshElements(true);
            var widths = new System.Collections.Generic.List<float>();
            target.RegisterCallback<GeometryChangedEvent>(e => widths.Add(e.NewRect.width));
            target.Style.Set("width", "30px");
            NativeMethods.urui_surface_update(document.Surface.SurfaceHandle);
            document.RefreshElements(false);
            Assert.That(widths, Is.Empty);
            document.RefreshElements(true);
            Assert.That(widths, Is.EqualTo(new[] { 30f }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UpdateHookDestructionReclaimsPreviouslyVisitedWrappers(bool detached)
        {
            document.RegisterElementType("urui-refresh-probe", context => new RefreshHookElement(context));
            document.LoadHtml("<!doctype html><html><head></head><body><urui-refresh-probe id='victim'></urui-refresh-probe><urui-refresh-probe id='killer'></urui-refresh-probe></body></html>", "/refresh-destroy.html");
            document.SynchronizeGeometry();
            var victim = document.Q<RefreshHookElement>("victim");
            var killer = document.Q<RefreshHookElement>("killer");
            if (detached)
            {
                victim.RemoveFromHierarchy();
            }
            var token = victim.Token;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var state = typeof(Document).GetField("content", flags).GetValue(document);
            var elements = (System.Collections.IDictionary)state.GetType().GetField("Elements", flags).GetValue(state);
            Assert.That(elements.Contains(token), Is.True);
            var visited = 0;
            victim.Tick = () => visited++;
            killer.Tick = () =>
            {
                if (!detached)
                {
                    Assert.That(visited, Is.EqualTo(1));
                }
                Assert.That(NativeMethods.urui_element_destroy(document.Surface.SurfaceHandle, document.NativeHandle, token), Is.EqualTo(1));
                killer.Tick = null;
            };
            document.RefreshElements(true);
            Assert.That(victim.IsValid, Is.False);
            Assert.That(elements.Contains(token), Is.False, "回调销毁更早访问的节点后，不能把失效包装器留在文档中。");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeInvalidationBetweenRefreshesReclaimsWrappers(bool releaseOnly)
        {
            document.LoadHtml("<!doctype html><html><head></head><body><div id='victim'></div></body></html>", "/refresh-between.html");
            document.SynchronizeGeometry();
            var victim = document.Q("victim");
            var token = victim.Token;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var state = typeof(Document).GetField("content", flags).GetValue(document);
            var elements = (System.Collections.IDictionary)state.GetType().GetField("Elements", flags).GetValue(state);
            document.RefreshElements(true);
            var surface = document.Surface.SurfaceHandle;
            var result = releaseOnly ? NativeMethods.urui_element_ref_release(surface, document.NativeHandle, token)
                : NativeMethods.urui_element_destroy(surface, document.NativeHandle, token);
            Assert.That(result, Is.EqualTo(1));
            document.RefreshElements(true);
            Assert.That(elements.Contains(token), Is.False, "刷新开始前已经失效的节点也必须从缓存中移除。");
            document.RefreshElements(true);
            Assert.That(victim.IsValid, Is.False);
            if (releaseOnly)
            {
                var fresh = document.Q("victim");
                Assert.That(fresh, Is.Not.SameAs(victim));
                Assert.That(fresh.IsValid, Is.True);
            }
            else
            {
                Assert.That(document.Q("victim"), Is.Null);
            }
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
                for (var i = 0; i < 20; ++i)
                {
                    document.NotifyLayoutUpdated();
                }
            }), Is.Zero);
        }

        static void EmptyLayout(Document owner)
        {
        }

        static void QueueStableAction(Document owner)
        {
            owner.RunAfterEvent(StableAction);
        }
        static readonly Action StableAction = () => { };
        [Test]
        public void StableDeferredLayoutActionsDoNotAllocateDispatchClosures()
        {
            Load();
            document.LayoutUpdated += QueueStableAction;
            Action action = () =>
            {
                for (var i = 0; i < 20; ++i)
                {
                    document.NotifyLayoutUpdated();
                }
            };
            action();
            ValidateRecorder();
            Assert.That(Allocations(action), Is.Zero);
        }

        [Test]
        public void DeferredActionsKeepOrderAndExceptionHandling()
        {
            Load();
            var order = new System.Collections.Generic.List<int>();
            Action first = () =>
            {
                order.Add(1);
                document.RunAfterEvent(() => order.Add(3));
                throw new InvalidOperationException("expected deferred failure");
            };
            document.LayoutUpdated += owner =>
            {
                owner.RunAfterEvent(first);
                owner.RunAfterEvent(() => order.Add(2));
                Assert.That(order, Is.Empty, "Actions wait for the current dispatch to unwind.");
            };
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: expected deferred failure"));
            document.NotifyLayoutUpdated();
            CollectionAssert.AreEqual(new[] { 1, 3, 2 }, order);
            Assert.Throws<InvalidOperationException>(() => document.RunAfterEvent(() => throw new InvalidOperationException("immediate")));
        }

        sealed class PassthroughEffect : IEffectFactory, IElementEffect
        {
            public IElementEffect Create(EffectContext context)
            {
                return this;
            }
            public void Prepare(EffectFrame frame)
            {
            }
            public void Record(EffectRenderContext context)
            {
                context.DrawOriginal();
            }
            public void Dispose()
            {
            }
        }

        [Test]
        public void StablePainterCallbacksDoNotAllocateContextObjects()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:10px;height:10px;--urui-decorator:shader(allocation-painter);}</style></head><body><div id='p'></div></body></html>", "/painter-allocations.html");
            var calls = 0;
            using var painter = document.AttachPainter(document.Q("p"), "allocation-painter", context => ++calls);
            for (var i = 0; i < 8; ++i)
            {
                document.SynchronizeLayout();
            }
            Assert.That(calls, Is.GreaterThan(0));
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 10; ++i)
                {
                    document.SynchronizeLayout();
                }
            }), Is.Zero);
        }

        [Test]
        public void MovingEffectCallbacksDoNotAllocateFrameOrContextObjects()
        {
            var clock = new MovingClock();
            document.Surface.UIClock = clock;
            document.Images = new[] { new URUI.Resources.ImageAsset("probe.png", Texture2D.whiteTexture) };
            document.Effects.Register(new EffectDefinition("allocation-effect", EffectKind.Image, new PassthroughEffect(), "allocation-program", "premultiplied"));
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{width:10px;height:10px;--urui-image-effect:allocation-effect;}</style></head><body><img src='probe.png'></body></html>", "/effect-allocations.html");
            for (var i = 0; i < 8; ++i)
            {
                clock.Now += .01;
                document.SynchronizeLayout();
            }
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 10; ++i)
                {
                    clock.Now += .01;
                    document.SynchronizeLayout();
                }
            }), Is.Zero);
        }

        [Test]
        public void UnchangedClipboardReadReusesItsUtf8Snapshot()
        {
            Load();
            var surface = document.Surface.SurfaceHandle;
            var previous = NativeMethods.ReadClipboard(surface);
            try
            {
                NativeMethods.urui_clipboard_write(surface, "中文剪贴板😀");
                var first = NativeMethods.ReadClipboard(surface);
                Assert.That(first, Is.EqualTo("中文剪贴板😀"));
                for (var i = 0; i < 5; ++i)
                {
                    _ = NativeMethods.ReadClipboard(surface);
                }
                ValidateRecorder();
                Assert.That(Allocations(() =>
                {
                    for (var i = 0; i < 10; ++i)
                    {
                        _ = NativeMethods.ReadClipboard(surface);
                    }
                }), Is.Zero);
                Assert.That(NativeMethods.ReadClipboard(surface), Is.SameAs(first));
                NativeMethods.urui_clipboard_write(surface, "变化🧪");
                Assert.That(NativeMethods.ReadClipboard(surface), Is.EqualTo("变化🧪"));
                NativeMethods.urui_clipboard_write(surface, "");
                Assert.That(NativeMethods.ReadClipboard(surface), Is.Empty);
            }
            finally
            {
                NativeMethods.urui_clipboard_write(surface, previous ?? "");
            }
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
            for (var i = 0; i < 5; ++i)
            {
                _ = node.ResolvedStyle.Get("opacity");
                _ = node.Style.Get("opacity");
            }
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
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 10; ++i)
                {
                    document.NotifyLayoutUpdated();
                }
            }), Is.Zero);
        }

        [Test]
        public void StableTextAndAttributeReadsReuseUtf8Strings()
        {
            Load();
            var node = document.Q("n0");
            node.Text = "中文😀";
            node.SetAttribute("title", "原始🧪");
            Action read = () =>
            {
                for (var i = 0; i < 10; ++i)
                {
                    _ = node.TagName;
                    _ = node.Text;
                    _ = node.GetAttribute("title");
                }
            };
            read();
            ValidateRecorder();
            Assert.That(Allocations(read), Is.Zero);
            var old = node.Text;
            node.Text = "替换后的内容";
            Assert.That(node.Text, Is.EqualTo("替换后的内容"));
            Assert.That(node.Text, Is.Not.SameAs(old));
            node.RemoveAttribute("title");
            Assert.That(node.GetAttribute("title"), Is.Null);
            node.SetAttribute("title", "");
            Assert.That(node.GetAttribute("title"), Is.Empty);
            var largeText = new string('界', 90_000) + "🧪";
            node.Text = largeText;
            Assert.That(node.Text, Is.EqualTo(largeText));
        }

        [Test]
        public void StableTextFitMeasurementDoesNotAllocateTreeOrNumberStrings()
        {
            var resources = new System.Collections.Generic.List<URUI.Resources.TextResource>
            { new("/urui/base.css", UnityEngine.Resources.Load<TextAsset>("URUI/base")) };
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf";
                    resources.Add(new URUI.Resources.TextResource("/urui/Fonts/" + name + ".bytes", UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name)));
                }
            }
            document.TextResources = resources.ToArray();
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'><style>#host{width:100px;height:40px;--urui-text-fit-min:8px;--urui-text-fit-max:16px;}</style></head><body><div id='host'><div id='content'>Text <span>fit</span></div></div></body></html>", "/fit-allocations.html");
            document.SynchronizeLayout();
            using var fit = new TextFit(document.Q("host"), document.Q("content"));
            for (var i = 0; i < 24; ++i)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            Action measure = () =>
            {
                for (var i = 0; i < 10; ++i)
                {
                    document.NotifyLayoutUpdated();
                }
            };
            measure();
            ValidateRecorder();
            Assert.That(Allocations(measure), Is.Zero);
            document.Q("content").Text = "A much longer replacement text that needs fitting";
            for (var i = 0; i < 24; ++i)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            Assert.That(fit.Content.Style.Get("font-size"), Is.Not.Null);
            Assert.That(Allocations(measure), Is.Zero);
        }

        [Test]
        public void LayoutSubscriptionChangesTakeEffectOnNextNotification()
        {
            Load();
            var calls = new System.Collections.Generic.List<int>();
            Action<Document> second = owner => calls.Add(2);
            Action<Document> third = owner => calls.Add(3);
            var changed = false;
            document.LayoutUpdated += owner =>
            {
                calls.Add(1);
                if (changed)
                {
                    return;
                }
                changed = true;
                document.LayoutUpdated -= second;
                document.LayoutUpdated += third;
            };
            document.LayoutUpdated += second;
            document.NotifyLayoutUpdated();
            document.NotifyLayoutUpdated();
            Assert.That(calls, Is.EqualTo(new[] { 1, 2, 1, 3 }));
        }

        [Test]
        public void RowMeasurementUsesStableTypedStylesAndObservesStyleChanges()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#row{display:flex;}" +
                "#child{width:40px;height:10px;flex-shrink:0;margin-right:10px;}</style></head>" +
                "<body><div id='row'><div id='child'></div></div></body></html>", "/row-allocations.html");
            var row = document.Q("row");
            var child = document.Q("child");
            using var measurement = new RowContentSize(row);
            for (var i = 0; i < 5; i++)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            Assert.That(row.Layout.width, Is.EqualTo(50).Within(.02f));
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 20; i++)
                {
                    document.NotifyLayoutUpdated();
                }
            }), Is.Zero);
            child.Style.Set("margin-right", "20px");
            for (var i = 0; i < 3; i++)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            Assert.That(row.Layout.width, Is.EqualTo(60).Within(.02f));
            child.Style.Set("display", "none");
            for (var i = 0; i < 3; i++)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            Assert.That(row.Layout.width, Is.EqualTo(0).Within(.02f));
            child.Style.Set("display", "block");
            child.Style.Set("margin-right", "10%");
            for (var i = 0; i < 12; i++)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            Assert.That(row.Layout.width, Is.EqualTo(40f / .9f).Within(.02f));
            child.Style.Set("margin-right", "auto");
            for (var i = 0; i < 3; i++)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            Assert.That(row.Layout.width, Is.EqualTo(40).Within(.02f));
        }

        [Test]
        public void StableOrdinaryRenderingDoesNotAllocatePerGraphic()
        {
            Load(true);
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 10; ++i)
                {
                    document.SynchronizeLayout();
                }
            }), Is.Zero);
        }

        [Test]
        public void AnimatedThousandGraphicsReuseCpuGeometryAndBatchStaging()
        {
            var clock = new MovingClock();
            document.Surface.UIClock = clock;
            document.Surface.Viewport = new Vector2Int(128, 128);
            var html = new StringBuilder("<!doctype html><html><head><style>body{margin:0;}" +
                "@keyframes tint{from{background-color:red;}to{background-color:blue;}}" +
                "div{position:absolute;width:3px;height:3px;animation:tint 1s infinite linear;}" +
                "</style></head><body>");
            for (var i = 0; i < 1000; i++)
            {
                html.Append("<div style='left:").Append(i % 32 * 4).Append("px;top:")
                                .Append(i / 32 * 4).Append("px;'></div>");
            }
            document.LoadHtml(html.Append("</body></html>").ToString(), "/dynamic-allocations.html");
            for (var i = 0; i < 20; i++)
            {
                clock.Now += .01;
                document.SynchronizeLayout();
            }
            ValidateRecorder();
            // 纯底色动画只更新 GPU 参数，不生成新的原生拓扑或 Unity Mesh 包装。
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 10; i++)
                {
                    clock.Now += .01;
                    document.SynchronizeLayout();
                }
            }), Is.Zero);
        }

        [TestCase(.25f, .75f)]
        [TestCase(17.25f, 3.75f)]
        public void FractionalIdleScrollDoesNotRewriteNumericStylesAndKeepsCompensation(float x, float y)
        {
            document.Surface.UIClock = new MovingClock();
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/behaviors.css", UnityEngine.Resources.Load<TextAsset>("URUI/behaviors")) };
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/behaviors.css'>" +
                "<style>body{margin:0;}#viewport{width:80px;height:30px;overflow:auto;scrollbar-width:none;}" +
                "#content{width:200px;height:80px;}</style></head><body>" +
                "<div id='viewport' class='scroll-motion'><div id='content' class='scroll-motion-content'></div></div>" +
                "</body></html>", "/fractional-idle-scroll.html");
            var viewport = document.Q("viewport");
            var content = document.Q("content");
            using var scroll = new ScrollMotion(viewport, content);
            document.SynchronizeGeometry();
            var original = content.WorldBound;
            scroll.ScrollTo(new Vector2(x, y));
            for (var i = 0; i < 3; i++)
            {
                document.NotifyLayoutUpdated();
                document.SynchronizeGeometry();
            }
            Assert.That(viewport.ScrollOffset.x, Is.EqualTo(Mathf.Round(x)));
            Assert.That(viewport.ScrollOffset.y, Is.EqualTo(Mathf.Round(y)));
            var before = content.WorldBound;
            Assert.That(before.x, Is.EqualTo(original.x - x).Within(.02f), "原生整数偏移之外的小数补偿必须保留。");
            Assert.That(before.y, Is.EqualTo(original.y - y).Within(.02f));
            static long Calls(ProfilerRecorder probe)
            {
                Assert.That(probe.Valid && !probe.WrappedAround, Is.True);
                long calls = 0;
                for (var i = 0; i < probe.Count; i++)
                {
                    calls += probe.GetSample(i).Count;
                }
                return calls;
            }
            using (var positive = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Style.WriteNumeric", 128, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                content.Style.SetPixels("--urui-scroll-motion-x", viewport.ScrollOffset.x - x);
                positive.Stop();
                Assert.That(Calls(positive), Is.EqualTo(1), "阳性探针必须观察到实际原生样式写入。");
            }
            document.SynchronizeGeometry();
            using (var writes = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Style.WriteNumeric", 128, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                using (document.BeginUpdate())
                {
                    for (var i = 0; i < 20; i++)
                    {
                        document.NotifyLayoutUpdated();
                    }
                }
                writes.Stop();
                Assert.That(Calls(writes), Is.Zero, "静止的小数滚动不能持续重写样式。");
            }
            Assert.That(content.WorldBound, Is.EqualTo(before));
        }

        [Test]
        public void ScrollMotionAdoptsExternalOffsetAndReappliesRequestWhenRangeGrows()
        {
            document.Surface.UIClock = new MovingClock();
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/behaviors.css", UnityEngine.Resources.Load<TextAsset>("URUI/behaviors")) };
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/behaviors.css'>" +
                "<style>body{margin:0;}#viewport{width:80px;height:30px;overflow:auto;scrollbar-width:none;}" +
                "#content{width:200px;height:80px;}</style></head><body>" +
                "<div id='viewport' class='scroll-motion'><div id='content' class='scroll-motion-content'></div></div>" +
                "</body></html>", "/changing-scroll-range.html");
            var viewport = document.Q("viewport");
            var content = document.Q("content");
            using var scroll = new ScrollMotion(viewport, content);
            document.SynchronizeGeometry();
            var original = content.WorldBound;
            scroll.ScrollTo(new Vector2(17.25f, 3.75f));
            document.SynchronizeGeometry();
            viewport.ScrollOffset = new Vector2(30, 10);
            document.NotifyLayoutUpdated();
            document.SynchronizeGeometry();
            Assert.That(content.WorldBound.x, Is.EqualTo(original.x - 30).Within(.02f), "外部原生滚动必须替换旧的小数补偿。");
            viewport.Style.Set("--urui-scroll-boundary", "unrestricted");
            document.SynchronizeGeometry();
            scroll.ScrollTo(new Vector2(400.25f, 30.75f));
            document.SynchronizeGeometry();
            Assert.That(viewport.ScrollOffset.x, Is.LessThan(400));
            var before = content.WorldBound;
            content.Style.SetWidth(600);
            document.SynchronizeGeometry();
            document.NotifyLayoutUpdated();
            document.SynchronizeGeometry();
            Assert.That(viewport.ScrollOffset.x, Is.EqualTo(400), "范围增长后，仍须将原请求重新提交给原生滚动条。");
            Assert.That(content.WorldBound.x, Is.EqualTo(before.x).Within(.02f), "更换原生偏移不能改变请求的逻辑位置。");
        }

        [Test]
        public void IdleScrollMotionDoesNotScheduleUnchangedPositionWrites()
        {
            document.Surface.UIClock = new MovingClock();
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/behaviors.css", UnityEngine.Resources.Load<TextAsset>("URUI/behaviors")) };
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/behaviors.css'>" +
                "<style>body{margin:0;}#viewport{width:80px;height:30px;overflow:auto;}" +
                "#content{width:200px;height:10px;}</style></head><body>" +
                "<div id='viewport' class='scroll-motion'><div id='content' class='scroll-motion-content'></div></div>" +
                "</body></html>", "/idle-scroll.html");
            using var scroll = new ScrollMotion(document.Q("viewport"), document.Q("content"));
            for (var i = 0; i < 5; i++)
            {
                document.SynchronizeLayout();
                document.NotifyLayoutUpdated();
            }
            ValidateRecorder();
            Assert.That(Allocations(() =>
            {
                for (var i = 0; i < 20; i++)
                {
                    document.NotifyLayoutUpdated();
                }
            }), Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ScrollPublicationPreservesCallbackChangesAndDisposal(int change)
        {
            document.Surface.UIClock = new MovingClock();
            document.TextResources = new[] { new URUI.Resources.TextResource("/urui/behaviors.css", UnityEngine.Resources.Load<TextAsset>("URUI/behaviors")) };
            document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/behaviors.css'>" +
                "<style>body{margin:0;}#viewport{width:80px;height:30px;overflow:auto;scrollbar-width:none;}" +
                "#content{width:200px;height:80px;}</style></head><body>" +
                "<div id='viewport' class='scroll-motion'><div id='content' class='scroll-motion-content'></div></div>" +
                "</body></html>", "/reentrant-scroll.html");
            var viewport = document.Q("viewport");
            var content = document.Q("content");
            if (change == 2)
            {
                content.Style.SetPixels("--urui-scroll-motion-x", 3);
                content.Style.SetPixels("--urui-scroll-motion-y", -2);
            }
            using var scroll = new ScrollMotion(viewport, content);
            document.SynchronizeGeometry();
            var callbackRan = false;
            using var subscription = viewport.RegisterCallback<ScrollEvent>(_ =>
            {
                if (callbackRan)
                {
                    return;
                }
                callbackRan = true;
                document.RunAfterEvent(() =>
                {
                    if (change == 0)
                    {
                        viewport.ScrollOffset = new Vector2(5, 0);
                    }
                    else if (change == 1)
                    {
                        scroll.ScrollTo(new Vector2(5.75f, 0));
                    }
                    else
                    {
                        scroll.Dispose();
                    }
                });
            });
            scroll.ScrollTo(new Vector2(17.25f, 0));
            document.SynchronizeGeometry();
            Assert.That(callbackRan, Is.True, "必须真正触发 setter 内的同步滚动回调。");
            if (change == 0)
            {
                // 回读不是本次取整结果时保留旧版的下一次更新重试策略。
                document.NotifyLayoutUpdated();
                document.SynchronizeGeometry();
                Assert.That(viewport.ScrollOffset.x, Is.EqualTo(17));
                Assert.That(content.WorldBound.x, Is.EqualTo(-17.25f).Within(.02f));
            }
            else if (change == 1)
            {
                Assert.That(viewport.ScrollOffset.x, Is.EqualTo(6));
                Assert.That(content.WorldBound.x, Is.EqualTo(-5.75f).Within(.02f));
                using var writes = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "URUI.Style.WriteNumeric", 128, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                for (var i = 0; i < 10; i++)
                {
                    document.NotifyLayoutUpdated();
                }
                writes.Stop();
                Assert.That(writes.Valid && !writes.WrappedAround, Is.True);
                long calls = 0;
                for (var i = 0; i < writes.Count; i++)
                {
                    calls += writes.GetSample(i).Count;
                }
                Assert.That(calls, Is.Zero, "外层请求不能覆盖已经完成的新请求缓存。");
            }
            else
            {
                Assert.That(content.Style.Get("--urui-scroll-motion-x"), Is.EqualTo("3px"));
                Assert.That(content.Style.Get("--urui-scroll-motion-y"), Is.EqualTo("-2px"));
            }
        }

        [Test]
        public void StableThousandAlternatingImagesDoNotAllocateTextureBindingState()
        {
            var a = new Texture2D(1, 1);
            var b = new Texture2D(1, 1);
            try
            {
                a.SetPixel(0, 0, Color.red);
                a.Apply();
                b.SetPixel(0, 0, Color.blue);
                b.Apply();
                document.Surface.Viewport = new Vector2Int(128, 128);
                document.Images = new[] { new URUI.Resources.ImageAsset("a.png", a), new URUI.Resources.ImageAsset("b.png", b) };
                var html = new StringBuilder("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:3px;height:3px;}</style></head><body>");
                for (var i = 0; i < 1000; i++)
                {
                    html.Append("<img src='").Append(i % 2 == 0 ? "a.png" : "b.png").Append("' style='left:")
                                        .Append(i % 32 * 4).Append("px;top:").Append(i / 32 * 4).Append("px;'>");
                }
                document.LoadHtml(html.Append("</body></html>").ToString(), "/binding-allocations.html");
                for (var i = 0; i < 8; i++)
                {
                    document.SynchronizeLayout();
                }
                ValidateRecorder();
                Assert.That(Allocations(() =>
                {
                    for (var i = 0; i < 10; i++)
                    {
                        document.SynchronizeLayout();
                    }
                }), Is.Zero);
            }
            finally
            {
                document.Unload();
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
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
                if (added != null)
                {
                    return;
                }
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
