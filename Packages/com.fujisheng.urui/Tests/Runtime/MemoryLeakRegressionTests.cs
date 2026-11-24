using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;
using URUI.Internal.Resources;

namespace URUI.Tests
{
    public sealed class MemoryLeakRegressionTests
    {
        static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        sealed class ThrowingDisposable : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("Simulated retirement disposal failure");
        }

        sealed class TrackingDisposable : IDisposable
        {
            internal int Disposals;
            public void Dispose() => Disposals++;
        }

        [Test]
        public void RenderPlanResourcesBoundsIdleMeshCountUnderDynamicVertexChurn()
        {
            var camera = new GameObject("Leak regression camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Leak regression surface");
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                surface.Viewport = new Vector2Int(512, 512);
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml(
                    "<!doctype html><html><head><style>body{margin:0;}.box{display:inline-block;width:4px;height:4px;background-color:#4af;}</style></head><body><div id='host'></div></body></html>",
                    "/render-plan-idle-cap.html");
                var host = document.Q("host");
                var meshesField = typeof(RenderPlanResources).GetField("meshes", InstancePrivate);
                Assert.That(meshesField, Is.Not.Null);

                using var resources = new RenderPlanResources(v => v.Dispose());
                var sb = new StringBuilder();
                // Generate 180 frames with monotonically growing vertex counts so older small Mesh buckets cannot be reused.
                for (var frame = 1; frame <= 180; frame++)
                {
                    sb.Clear();
                    for (var n = 0; n < frame; n++)
                    {
                        sb.Append("<div class='box'></div>");
                    }
                    host.SetInnerHtml(sb.ToString(), "/render-plan-idle-cap.html");
                    document.SynchronizeLayout();
                    // Release current recording references so evicted batches become idle (References == 0).
                    resources.EndRecording();
                    using var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _);
                    using var lease = input.Compile(default, out var packet);
                    resources.Apply(lease, packet);
                }

                var meshes = (IDictionary)meshesField.GetValue(resources);
                Assert.That(meshes.Count, Is.LessThanOrEqualTo(136),
                    "RenderPlanResources must cap idle Unity Mesh count (MaxIdleEntries=128) instead of growing unboundedly below 48 MiB.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void ImagePinRetirementExceptionInOneResourceDoesNotLeakRemainingResourcesOrBudget()
        {
            var pendingType = typeof(ImagePinRetirement).GetNestedType("Pending", StaticPrivate);
            var releaseMethod = typeof(ImagePinRetirement).GetMethod("ReleaseResources", StaticPrivate);
            Assert.That(pendingType, Is.Not.Null);
            Assert.That(releaseMethod, Is.Not.Null);

            var item = Activator.CreateInstance(pendingType);
            var resources = (List<IDisposable>)pendingType.GetField("resources", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);
            var submissions = (List<ImageSubmissionFence>)pendingType.GetField("submissions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);

            var firstGood = new TrackingDisposable();
            var throwing = new ThrowingDisposable();
            var secondGood = new TrackingDisposable();
            resources.Add(firstGood);
            resources.Add(throwing);
            resources.Add(secondGood);

            var budget = new ImageSubmissionBudget();
            budget.Reserve();
            var submission = new ImageSubmissionFence { Budget = budget };
            submissions.Add(submission);

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Simulated retirement disposal failure");
            var succeeded = (bool)releaseMethod.Invoke(null, new[] { item });

            Assert.That(succeeded, Is.False, "Faulted batch should still report failure.");
            Assert.That(firstGood.Disposals, Is.EqualTo(1));
            Assert.That(secondGood.Disposals, Is.EqualTo(1), "Resources after a throwing Dispose() must still be disposed.");
            Assert.That(submission.Budget, Is.Null, "ImageSubmissionFence and its budget slot must still be released.");
            Assert.That(submissions.Count, Is.Zero);
            // Verify budget slot is free by reserving up to full capacity (8) without throwing.
            for (var i = 0; i < 8; i++)
            {
                budget.Reserve();
            }
            for (var i = 0; i < 8; i++)
            {
                budget.Release();
            }
        }

        [Test]
        public void VisibilityClassAndBezierMotionAutoDetachOnDocumentUnload()
        {
            var camera = new GameObject("Anim detach camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Anim detach surface");
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                surface.Viewport = new Vector2Int(256, 256);
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml(
                    "<!doctype html><html><head><style>" +
                    "body{margin:0;width:256px;height:256px;}" +
                    "#a,#b,#p,#r{position:absolute;width:20px;height:20px;}" +
                    "#p{--urui-motion-midpoint-from:0.5;--urui-motion-midpoint-to:0.5;--urui-motion-bend-from:0;--urui-motion-bend-to:0;--urui-motion-rotation-from:0;--urui-motion-rotation-to:0;--urui-motion-progress:0;}" +
                    "#r{--urui-motion-rotation-progress:0;}" +
                    "</style></head><body><div id='a'></div><div id='b'></div><div id='p'></div><div id='r'></div></body></html>",
                    "/anim-detach.html");
                document.SynchronizeLayout();

                // Deliberately omit Dispose() to verify Document.Unload() auto-detaches both listeners.
                var visibility = new VisibilityClass(document.Q("a"), document.Q("b"), "visible");
                var motion = new BezierMotion(document.Q("p"), document.Q("r"), document.Q("a"), document.Q("b"));
                motion.Begin();
                GC.KeepAlive(visibility);
                GC.KeepAlive(motion);

                var layoutField = typeof(Document).GetField("layoutUpdated", InstancePrivate);
                var unloadedField = typeof(Document).GetField("Unloaded", InstancePrivate);
                Assert.That(layoutField, Is.Not.Null);
                Assert.That(unloadedField, Is.Not.Null);
                Assert.That(layoutField.GetValue(document), Is.Not.Null);

                document.Unload();

                var layoutDelegate = (Delegate)layoutField.GetValue(document);
                var unloadedDelegate = (Delegate)unloadedField.GetValue(document);
                if (layoutDelegate != null)
                {
                    foreach (var d in layoutDelegate.GetInvocationList())
                    {
                        Assert.That(d.Target, Is.Not.SameAs(visibility));
                        Assert.That(d.Target, Is.Not.SameAs(motion));
                    }
                }
                if (unloadedDelegate != null)
                {
                    foreach (var d in unloadedDelegate.GetInvocationList())
                    {
                        Assert.That(d.Target, Is.Not.SameAs(visibility));
                        Assert.That(d.Target, Is.Not.SameAs(motion));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }
    }
}
