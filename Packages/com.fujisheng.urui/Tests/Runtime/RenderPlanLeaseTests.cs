using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;
using URUI.Resources;
using URUI.Rendering.Effects;

namespace URUI.Tests
{
    public sealed class RenderPlanLeaseTests
    {
        GameObject root;
        GameObject camera;
        Document document;
        sealed class Passthrough : IEffectFactory, IElementEffect
        {
            public IElementEffect Create(EffectContext context) => new Passthrough();
            public void Prepare(EffectFrame frame)
            {
            }
            public void Record(EffectRenderContext context) => context.DrawOriginal();
            public void Dispose()
            {
            }
        }
        [SetUp]
        public void SetUp()
        {
            camera = new GameObject("Lease camera", typeof(Camera));
            camera.tag = "MainCamera";
            root = new GameObject("Lease surface");
            var surface = root.AddComponent<Surface>();
            surface.Input.UseUnityInput = false;
            document = root.AddComponent<Document>();
            document.Surface = surface;
            document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
            document.Effects.Register(new EffectDefinition("lease-image", EffectKind.Image, new Passthrough(), "lease-image", "premultiplied"));
            document.Effects.Register(new EffectDefinition("lease-element", EffectKind.Element, new Passthrough(), "lease-element", "premultiplied"));
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:red;}</style></head><body><div></div></body></html>", "/lease.html");
            document.SynchronizeLayout();
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(camera);
        }
        [Test]
        public void LayerOwnersWaitForRecordingAndSubmissionReferences()
        {
            using var targets = new EffectTargetPool();
            var first = LayerContentLease.Rent(targets.Rent(4, 4, RenderTextureFormat.ARGB32), 101, true);
            first.Retain();
            first.Retain();
            Assert.That(first.Available, Is.False);
            first.Release(true);
            Assert.That(first.Available, Is.True, "完成的真实提交才可发布缓存。");
            first.Dispose();
            var second = LayerContentLease.Rent(targets.Rent(4, 4, RenderTextureFormat.ARGB32), 102, true);
            Assert.That(second, Is.Not.SameAs(first), "旧录制仍引用的内部所有者不能重绑新版本。");
            Assert.That(first.Version, Is.EqualTo(101));
            Assert.Throws<ObjectDisposedException>(() => first.Retain());
            first.Release(false);
            var third = LayerContentLease.Rent(targets.Rent(4, 4, RenderTextureFormat.ARGB32), 103, true);
            try
            {
                Assert.That(third, Is.SameAs(first));
                Assert.That(third.Available, Is.False);
                Assert.That(second.Version, Is.EqualTo(102));
            }
            finally
            {
                second.Dispose();
                third.Dispose();
            }
        }

        [Test]
        public void OnlyUnreferencedUnpublishedCacheContentCanChangeVersionInPlace()
        {
            using var targets = new EffectTargetPool();
            using var content = LayerContentLease.Rent(targets.Rent(4, 4, RenderTextureFormat.ARGB32), 1, true);
            var target = content.Target;
            Assert.That(content.TryUpdateUnpublished(2, true), Is.True);
            Assert.That(content.Target, Is.SameAs(target));
            Assert.That(content.Version, Is.EqualTo(2));
            content.Retain();
            Assert.That(content.TryUpdateUnpublished(3, true), Is.False);
            content.Release(false);
            Assert.That(content.TryUpdateUnpublished(3, true), Is.True);
            content.Retain();
            content.Release(true);
            Assert.That(content.Available, Is.True);
            Assert.That(content.TryUpdateUnpublished(4, true), Is.False, "Completed cache readers must keep their immutable source even without a write reference.");
            using var dynamic = LayerContentLease.Rent(targets.Rent(4, 4, RenderTextureFormat.ARGB32), 5, false);
            Assert.That(dynamic.TryUpdateUnpublished(6, true), Is.False);
            Assert.That(dynamic.TryUpdateUnpublished(6, false), Is.False);
        }

        [Test]
        public void RegionReuseWaitsForConflictingQueuedWritesAndACompletedSeed()
        {
            using var targets = new EffectTargetPool();
            using var content = LayerContentLease.Rent(targets.Rent(4, 4, RenderTextureFormat.ARGB32), 1, false);
            var serial = 0UL;
            (ulong generation, bool publishes) Submit(ulong version, bool partial = false)
            {
                content.RecordWrite(++serial, version, partial);
                Assert.That(content.PinWrite(serial, out var generation, out var publishes), Is.True);
                return (generation, publishes);
            }
            var first = Submit(11);
            Assert.That(first.publishes, Is.True);
            Assert.That(content.CanRedrawRegion(11), Is.False);
            content.FinishWrite(first.generation, true);
            Assert.That(content.CanRedrawRegion(11), Is.True);

            var older = Submit(11, partial: true);
            var conflict = Submit(12);
            Assert.That(older.publishes || conflict.publishes, Is.False);
            Assert.That(content.CanRedrawRegion(11), Is.False);
            content.FinishWrite(conflict.generation, false);
            content.FinishWrite(older.generation, false);

            var cancelled = Submit(12);
            Assert.That(cancelled.publishes, Is.True);
            content.FinishWrite(cancelled.generation, false);
            Assert.That(content.CanRedrawRegion(12), Is.False);
            var latest = Submit(12);
            Assert.That(latest.publishes, Is.True);
            content.FinishWrite(latest.generation, true);
            Assert.That(content.CanRedrawRegion(12), Is.True);
        }

        [Test]
        public void PointerLayoutsMatchNativeProtocol()
        {
            Assert.That(Marshal.SizeOf<RenderPlanTargetRequest>(), Is.EqualTo(32));
            Assert.That(Marshal.SizeOf<RenderPlanVertex>(), Is.EqualTo(32));
            Assert.That(Marshal.SizeOf<RenderPlanParameter>(), Is.EqualTo(128));
            Assert.That(Marshal.SizeOf<RenderPlanOperation>(), Is.EqualTo(136));
            Assert.That(Marshal.OffsetOf<RenderPlanOperation>("BaseParameter").ToInt64(), Is.EqualTo(128));
            Assert.That(Marshal.SizeOf<RenderPlanInputPacket>(), Is.EqualTo(IntPtr.Size == 8 ? 80 : 56));
            Assert.That(Marshal.SizeOf<RenderPlanPacket>(), Is.EqualTo(IntPtr.Size == 8 ? 144 : 128));
            Assert.That(Marshal.OffsetOf<RenderPlanPacket>("ExtraParameters").ToInt64(), Is.EqualTo(IntPtr.Size == 8 ? 120 : 108));
            Assert.That(Marshal.SizeOf<RenderPlanPrepareRequest>(), Is.EqualTo(128));
            Assert.That(Marshal.SizeOf<RenderPlanFeedback>(), Is.EqualTo(104));
            Assert.That(Marshal.OffsetOf<RenderPlanFeedback>("Transforms").ToInt64(), Is.EqualTo(96));
            Assert.That(Marshal.SizeOf<RenderPlanPreparePacket>(), Is.EqualTo(IntPtr.Size == 8 ? 56 : 48));
            Assert.That(Marshal.SizeOf<RenderPlanLayerRegion>(), Is.EqualTo(48));
            Assert.That(Marshal.OffsetOf<RenderPlanLayerRegion>("AliasLayer").ToInt64(), Is.EqualTo(40));
            Assert.That(Marshal.SizeOf<RenderPlanBindings>(), Is.EqualTo(IntPtr.Size == 8 ? 80 : 60));
            Assert.That(Marshal.SizeOf<RenderPlanExternalBinding>(), Is.EqualTo(8));
            Assert.That(Marshal.SizeOf<RenderPlanVertexStream>(), Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<RenderPlanAttribute>(), Is.EqualTo(16));
            Assert.That(Marshal.SizeOf<RenderPlanExternalGeometry>(), Is.EqualTo(200));
            Assert.That(Marshal.SizeOf<RenderPlanExternalDraw>(), Is.EqualTo(160));
        }
        [Test]
        public void RejectsExpiredCrossThreadAndCrossDocumentInputs()
        {
            var surface = document.Surface.SurfaceHandle;
            var lease = NativeRenderPlanLease.Begin(surface, out var input);
            try
            {
                int status = 0;
                RenderPlanPacket output = default;
                var other = new Thread(() =>
                {
                    status = NativeMethods.urui_plan_compile(surface, input.Lease, default, out output);
                });
                other.Start();
                other.Join();
                Assert.That(status, Is.EqualTo(NativeStatus.WrongThread));
                Assert.That(output.Lease, Is.Zero);
                var otherRoot = new GameObject("Other lease document");
                var otherDocument = otherRoot.AddComponent<Document>();
                otherDocument.Surface = document.Surface;
                try
                {
                    otherDocument.LoadHtml("<!doctype html><html><head></head><body></body></html>", "/other.html");
                    Assert.That(NativeMethods.urui_plan_compile(surface, input.Lease, default, out output), Is.EqualTo(NativeStatus.NotReady));
                    Assert.That(output.Lease, Is.Zero);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(otherRoot);
                }
            }
            finally
            {
                lease.Dispose();
            }
            Assert.That(NativeMethods.urui_plan_compile(surface, input.Lease, default, out var expired), Is.EqualTo(NativeStatus.NotReady));
            Assert.That(expired.Operations, Is.EqualTo(IntPtr.Zero));
        }
        [Test]
        public void FourLeasesAndBudgetAreEnforced()
        {
            var surface = document.Surface.SurfaceHandle;
            var leases = new NativeRenderPlanLease[4];
            try
            {
                for (var i = 0; i < leases.Length; i++)
                {
                    leases[i] = NativeRenderPlanLease.Begin(surface, out _);
                }
                Assert.That(NativeMethods.urui_plan_begin(surface, out var fifth), Is.EqualTo(NativeStatus.Reentrancy));
                Assert.That(fifth.Lease, Is.Zero);
            }
            finally
            {
                for (var i = 0; i < leases.Length; i++)
                {
                    leases[i].Dispose();
                }
            }
        }
        [Test]
        public void GeometryIsBorrowedOnlyInsideItsPlanLease()
        {
            var surface = document.Surface.SurfaceHandle;
            using var input = NativeRenderPlanLease.Begin(surface, out _);
            var plan = input.Compile(default, out var packet);
            var operation = Marshal.PtrToStructure<RenderPlanOperation>(packet.Operations);
            Assert.That(operation.Kind, Is.EqualTo(1));
            Assert.That(plan.Geometry(operation.Geometry).VertexCount, Is.GreaterThan(0));
            var token = plan.Token;
            plan.Dispose();
            Assert.That(NativeMethods.urui_plan_geometry(surface, token, operation.Geometry, out var expired), Is.EqualTo(NativeStatus.NotReady));
            Assert.That(expired.Vertices, Is.EqualTo(IntPtr.Zero));
        }
        sealed class Captures : IRenderPlanPreparationBindings, IDisposable
        {
            readonly Dictionary<ulong, RenderTexture> targets = new();
            readonly HashSet<uint> prepared = new();
            internal int Images;
            internal int Elements;
            internal readonly List<EffectFrame> Frames = new();
            public RenderTexture ResolveCapture(ulong resource) => targets.TryGetValue(resource, out var texture) ? texture : null;
            public void BeginFeedbackBatch(int requestCount) { }
            static Rect Rect(Vector4 value) => new(value.x, value.y, value.z, value.w);
            public RenderPlanFeedback Prepare(in RenderPlanPrepareRequest request)
            {
                var result = new RenderPlanFeedback();
                if (request.Kind == 1)
                {
                    Images++;
                    result.Ink = new Vector4(-4, -4, 16, 16);
                }
                else if (request.Kind == 3)
                {
                    // Real pooled captures may have a larger allocation and domain.
                    var bounds = request.Capture;
                    bounds.x -= 8;
                    bounds.y -= 8;
                    bounds.z += 16;
                    bounds.w += 16;
                    var source = new RenderTexture((int)bounds.z, (int)bounds.w, 0);
                    source.Create();
                    var resource = (ulong)targets.Count + 1;
                    targets.Add(resource, source);
                    result.Resource = resource;
                    result.SourceVersion = 9;
                    result.PixelWidth = (uint)source.width;
                    result.PixelHeight = (uint)source.height;
                    result.Ink = bounds;
                }
                else if (request.Kind == 4)
                {
                    Assert.That(prepared.Add(request.Slot), Is.True, "同一slot不能重复Prepare。");
                    var source = ResolveCapture(request.Resource);
                    var layout = new EffectLayout(null, new Rect(0, 0, 8, 8), Matrix4x4.identity, request.Element, new Rect(0, 0, 8, 8), Matrix4x4.identity);
                    var frame = new EffectFrame(request.Element, layout, source, false, 0, (uint)request.SourceVersion, EffectGeometry.CapturedQuad, Rect(request.Capture), Rect(request.SourceInk));
                    Assert.That(frame.Source, Is.SameAs(source));
                    Assert.That(frame.SourcePixelSize, Is.EqualTo(new Vector2Int(source.width, source.height)));
                    Assert.That(frame.SourceUvBounds.x, Is.EqualTo(request.Uv.x).Within(.00001f));
                    Assert.That(frame.SourceUvBounds.width, Is.EqualTo(request.Uv.z).Within(.00001f));
                    Frames.Add(frame);
                    Elements++;
                    result.Ink = request.SourceInk + new Vector4(-4, -4, 8, 8);
                }
                return result;
            }
            public void Dispose()
            {
                foreach (var target in targets.Values)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }
        [Test]
        public void NestedElementPrepareReceivesFinalRealCaptureOnce()
        {
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;--urui-effect:lease-element;}img{width:8px;height:8px;--urui-image-effect:lease-image;}</style></head><body><div><div><img src='white.png'></div></div></body></html>", "/nested-lease.html");
            document.SynchronizeLayout();
            using var captures = new Captures();
            using var input = NativeRenderPlanLease.Begin(document.Surface.SurfaceHandle, out var packet);
            RenderPlanPreparation.Resolve(input, packet, captures);
            Assert.That(captures.Images, Is.EqualTo(1));
            Assert.That(captures.Elements, Is.EqualTo(2));
            Assert.That(captures.Frames[1].SourceCaptureBounds.width, Is.GreaterThan(captures.Frames[0].SourceCaptureBounds.width));
            using var compiled = input.Compile(default, out _);
        }
    }
}
