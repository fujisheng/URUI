using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;

namespace URUI.Tests
{
    public sealed class RenderPlanResourcesTests
    {
        static object probe;
        [Test]
        public void UnreferencedOrdinaryBackingIsReusedAcrossDocumentGenerations()
        {
            var camera = new GameObject("Generation upload camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Generation upload surface");
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                const string markup = "<!doctype html><html><head><style>body{margin:0;}div{width:16px;height:16px;background-color:red;}</style></head><body><div></div></body></html>";
                using var resources = new RenderPlanResources(value => value.Dispose());
                Mesh backing = null;
                ulong previous = 0;
                for (var i = 0; i < 21; i++)
                {
                    resources.EndRecording();
                    document.LoadHtml(markup, "/generation-upload.html");
                    document.SynchronizeLayout();
                    using var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _);
                    using var lease = input.Compile(default, out var packet);
                    resources.Apply(lease, packet);
                    var operation = Marshal.PtrToStructure<RenderPlanOperation>(packet.Operations);
                    var mesh = resources.Mesh(operation.Geometry);
                    if (backing)
                    {
                        Assert.That(operation.Geometry, Is.Not.EqualTo(previous));
                        Assert.That(mesh, Is.SameAs(backing), "无录制或 GPU 引用的旧代 Mesh 应复用物理缓冲。");
                    }
                    else
                    {
                        backing = mesh;
                    }
                    previous = operation.Geometry;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }
        [Test]
        public void TranslationReusesAdvancedMeshUploadAndKeepsInitialLayoutVertices()
        {
            var camera = new GameObject("Upload camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Upload surface");
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:8px;height:8px;}</style></head><body><img id='a' src='white.png'><img src='white.png' style='left:16px;'><img src='white.png' style='left:32px;'></body></html>", "/native-upload.html");
                document.SynchronizeLayout();
                using var resources = new RenderPlanResources(resource => resource.Dispose());
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var lease = input.Compile(default, out var packet))
                    {
                        resources.Apply(lease, packet);
                        var operation = Marshal.PtrToStructure<RenderPlanOperation>(packet.Operations);
                        var mesh = resources.Mesh(operation.Geometry);
                        Assert.That(mesh.vertexCount, Is.EqualTo(12));
                        Assert.That(mesh.vertices[4].x, Is.EqualTo(16), "编译批次保留首次布局位置，后续位置变化由 GPU 差量参数处理。");
                        Assert.That(mesh.uv2[4].y, Is.EqualTo(1));
                    }
                }
                var before = resources.GeometryUploadBytes;
                document.Q("a").Style.Set("left", "3px");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var lease = input.Compile(default, out var packet))
                    {
                        resources.Apply(lease, packet);
                    }
                }
                Assert.That(before, Is.GreaterThan(0));
                Assert.That(resources.GeometryUploadBytes, Is.EqualTo(before));
                for (var i = 0; i < 300; i++)
                {
                    using var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _);
                    using var lease = input.Compile(default, out var packet);
                    resources.Apply(lease, packet);
                }
                using (var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    probe = new byte[4096];
                    GC.KeepAlive(probe);
                    positive.Stop();
                    Assert.That(positive.Valid && positive.Count > 0, Is.True);
                    probe = null;
                }
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    for (var i = 0; i < 32; i++)
                    {
                        using var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _);
                        using var lease = input.Compile(default, out var packet);
                        resources.Apply(lease, packet);
                    }
                    recorder.Stop();
                    Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
                    long events = 0;
                    for (var i = 0; i < recorder.Count; i++)
                    {
                        events += recorder.GetSample(i).Count;
                    }
                    Assert.That(events, Is.Zero, "原生视图的托管上传适配不应分配临时对象。");
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
