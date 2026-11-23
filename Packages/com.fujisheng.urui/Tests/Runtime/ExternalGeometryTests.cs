using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.TestTools;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Hosting;
using URUI.Rendering;

namespace URUI.Tests
{
    public sealed class ExternalGeometryTests
    {
        sealed class QueuedPipelineAsset : RenderPipelineAsset
        {
            internal Action<ScriptableRenderContext> Draw;
            public override Type pipelineType => typeof(QueuedPipeline);
            protected override RenderPipeline CreatePipeline() => new QueuedPipeline(this);
        }
        sealed class QueuedPipeline : RenderPipeline
        {
            readonly QueuedPipelineAsset asset;
            internal QueuedPipeline(QueuedPipelineAsset asset)
            {
                this.asset = asset;
            }
            protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
            {
                asset.Draw?.Invoke(context);
                context.Submit();
            }
        }
        GameObject host;
        GameObject cameraObject;
        Document document;
        Camera camera;
        RenderTexture target;
        Material material;
        Mesh mesh;
        RenderHost Renderer => host.GetComponentInChildren<RenderHost>(true);
        static RenderProgramDescriptor Descriptor => new(RenderTextureEncoding.TextureDeclared, RenderColorSpace.Active,
            RenderColorSpace.Active, RenderAlpha.Premultiplied, RenderBlendDomain.Active, BlendMode.One,
            BlendMode.OneMinusSrcAlpha, RenderTargetCapability.Hdr, RenderSampling.BilinearClamp, RenderGeometry.Local);
        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("External mesh camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            host = new GameObject("External geometry document");
            var surface = host.AddComponent<Surface>();
            surface.Viewport = new Vector2Int(128, 128);
            surface.Input.UseUnityInput = false;
            document = host.AddComponent<Document>();
            document.Surface = surface;
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#p{width:64px;height:16px;}</style></head><body><div id='p'></div></body></html>", "/external-geometry.html");
            material = new Material(UnityEngine.Resources.Load<Shader>("URUI/Tests/ExternalGeometry"));
            mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, new Vector3(8, 0), new Vector3(8, 8), new Vector3(0, 8) };
            mesh.colors32 = new[] { new Color32(255, 0, 0, 255), new Color32(255, 0, 0, 255), new Color32(255, 0, 0, 255), new Color32(255, 0, 0, 255) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        }
        [Test]
        public void CompatibleSourcesBatchAndTransformWithoutUploadingGeometry()
        {
            var program = new ExternalGeometryProgram(material, Descriptor);
            var offset = 0f;
            var recorded = 0;
            using var registration = Attach(context =>
            {
                for (var i = 0; i < 4; i++)
                {
                    context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.Translate(new Vector3(i * 16 + offset, 0)));
                }
            }, onRecorded: count => recorded += count);
            for (var i = 0; i < 8; i++)
            {
                document.SynchronizeLayout();
            }
            Assert.That(Renderer.RecordedExternalGeometryDraws, Is.EqualTo(1));
            Assert.That(recorded, Is.GreaterThan(0));
            var uploaded = Renderer.GeometryUploadBytes;
            offset = 1;
            document.SynchronizeLayout();
            Assert.That(Renderer.GeometryUploadBytes, Is.EqualTo(uploaded));
            camera.Render();
            Assert.That(Pixel(3, 3).r, Is.GreaterThan(.99f));
            Assert.That(Pixel(19, 3).r, Is.GreaterThan(.99f));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void OrdinaryAndExternalGeometryShareTheSameOutput(bool unknown)
        {
            var source = unknown ? new Material(UnityEngine.Resources.Load<Shader>("URUI/Tests/ExternalRawGeometry")) : material;
            if (unknown)
            {
                mesh.SetUVs(2, new List<Vector4> { new(1, 0, 0, 1), new(1, 0, 0, 1), new(1, 0, 0, 1), new(1, 0, 0, 1) });
            }
            var program = new ExternalGeometryProgram(source, unknown ? default : Descriptor);
            try
            {
                (long bytes, Color color) Record(bool externalFirst)
                {
                    const string ordinary = "<div style='position:absolute;width:8px;height:8px;background:rgba(0,255,0,.5);'></div>";
                    const string external = "<div id='p' style='position:absolute;width:8px;height:8px;'></div>";
                    document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}</style></head><body>" +
                        (externalFirst ? external + ordinary : ordinary + external) + "</body></html>", "/mixed-target-state.html");
                    using var registration = Attach(context => context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity));
                    for (var i = 0; i < 8; i++)
                    {
                        document.SynchronizeLayout();
                    }
                    Assert.That(Renderer.RecordedExternalGeometryDraws, Is.EqualTo(1));
                    Assert.That(Renderer.RecordedGeometryDraws, Is.EqualTo(1));
                    long bytes = 0;
                    foreach (var commands in camera.GetCommandBuffers(CameraEvent.AfterEverything))
                    {
                        bytes += commands.sizeInBytes;
                    }
                    camera.Render();
                    return (bytes, Pixel(3, 3));
                }
                var afterOrdinary = Record(false);
                var beforeOrdinary = Record(true);
                Assert.That(afterOrdinary.color.r, Is.GreaterThan(.99f));
                Assert.That(afterOrdinary.color.g + afterOrdinary.color.b, Is.LessThan(.01f));
                var expected = new Color(.5f, .5f, 0);
                Assert.That(beforeOrdinary.color.r, Is.EqualTo(expected.r).Within(3f / 255));
                Assert.That(beforeOrdinary.color.g, Is.EqualTo(expected.g).Within(3f / 255));
                Assert.That(beforeOrdinary.color.b, Is.LessThan(.01f));
                Assert.That(afterOrdinary.bytes, Is.EqualTo(beforeOrdinary.bytes));
            }
            finally
            {
                if (unknown)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }
        }
        [Test]
        public void ProviderParametersAreFrozenAndInvalidFieldsCannotLeakTheirScope()
        {
            var element = document.Q("p");
            element.Style.SetNumber("--provider-value", .25f);
            var layout = new RenderParameterLayout(1, new RenderParameterField[]{
                new(new URUI.Styles.StylePropertyDefinition("--provider-value",URUI.Styles.StyleValueType.Number),0),
                new(new URUI.Styles.StylePropertyDefinition("--provider-length",URUI.Styles.StyleValueType.Pixels),1,optional:true)});
            ExternalGeometryContext retained = default;
            var received = Vector4.zero;
            var calls = 0;
            var program = new ExternalGeometryProgram(material, Descriptor);
            using var registration = document.AttachGeometryProvider(element, "geometry", context =>
            {
                retained = context;
                received = context.Parameters[0];
                calls++;
                context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity);
            }, parameters: layout);
            element.Style.Set("--urui-decorator", "shader(geometry)");
            document.SynchronizeLayout();
            Assert.That(received, Is.EqualTo(new Vector4(.25f, 0, 0, 0)));
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = retained.Parameters.Length;
            });
            element.Style.Set("--provider-length", "red");
            var previous = calls;
            Assert.Throws<InvalidOperationException>(() => document.SynchronizeLayout());
            Assert.That(calls, Is.EqualTo(previous), "无效字段不能进入使用者回调，也不能当作可选缺省零。");
            element.Style.Set("--provider-length", "12px");
            document.SynchronizeLayout();
            Assert.That(received.y, Is.EqualTo(12));
            Assert.That(calls, Is.GreaterThan(previous));
        }
        [Test]
        public void GeometryCallbackScopeCannotEscapeOrModifyTheDocument()
        {
            var program = new ExternalGeometryProgram(material, Descriptor);
            ExternalGeometryContext retained = default;
            using var registration = Attach(context =>
            {
                retained = context;
                Assert.Throws<InvalidOperationException>(() => document.Q("p").Style.SetWidth(40));
                context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity);
            });
            document.SynchronizeLayout();
            Assert.Throws<InvalidOperationException>(() => retained.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity));
        }
        [Test]
        public void StableCapacityVertexUpdatesDoNotAllocateManagedOwners()
        {
            var program = new ExternalGeometryProgram(material, Descriptor);
            ulong version = 1;
            var frame = 0;
            var vertices = new List<Vector3> { Vector3.zero, new(8, 0), new(8, 8), new(0, 8) };
            using var registration = Attach(context => context.Draw(new ExternalGeometryInput(mesh, version), program, Matrix4x4.identity));
            Action cycle = () =>
            {
                vertices[0] = new Vector3(++frame % 2, 0);
                mesh.SetVertices(vertices);
                version++;
                document.SynchronizeLayout();
            };
            for (var i = 0; i < 300; i++)
            {
                cycle();
            }
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (var i = 0; i < 32; i++)
            {
                cycle();
            }
            recorder.Stop();
            long allocations = 0;
            for (var i = 0; i < recorder.Count; i++)
            {
                allocations += recorder.GetSample(i).Count;
            }
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            Assert.That(allocations, Is.Zero, "稳定容量的动态几何不能为每个版本创建新的托管 Mesh 与所有者。");
        }
        [Test]
        public void MaterialParameterChangesReuseManagedOwnersAndGeometry()
        {
            var program = new ExternalGeometryProgram(material, Descriptor);
            var frame = 0;
            using var registration = Attach(context => context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity));
            Action cycle = () =>
            {
                material.SetColor("_TestTint", ++frame % 2 == 0 ? Color.red : Color.green);
                document.SynchronizeLayout();
            };
            for (var i = 0; i < 300; i++)
            {
                cycle();
            }
            var uploaded = Renderer.GeometryUploadBytes;
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (var i = 0; i < 32; i++)
            {
                cycle();
            }
            recorder.Stop();
            long allocations = 0;
            for (var i = 0; i < recorder.Count; i++)
            {
                allocations += recorder.GetSample(i).Count;
            }
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            Assert.That(allocations, Is.Zero);
            Assert.That(Renderer.GeometryUploadBytes, Is.EqualTo(uploaded));
        }
        [Test]
        public void TextureContentChangesCreateANewProgramResourceIdentity()
        {
            var texture = new Texture2D(1, 1);
            var program = new ExternalGeometryProgram(material, Descriptor);
            using var pool = new URUI.Internal.Rendering.ExternalProgramResources();
            try
            {
                material.SetTexture("_TestTexture", texture);
                pool.Begin();
                var original = pool.Capture(program).Identity;
                texture.SetPixel(0, 0, Color.red);
                texture.Apply();
                pool.Begin();
                var next = pool.Capture(program).Identity;
                Assert.That(next, Is.Not.EqualTo(original), "同一个 Texture2D 的像素版本属于层缓存依赖。");
                pool.Begin();
                Assert.That(pool.Capture(program).Identity, Is.EqualTo(next));
                var renderTexture = new RenderTexture(2, 2, 0);
                renderTexture.Create();
                try
                {
                    material.SetTexture("_TestTexture", renderTexture);
                    pool.Begin();
                    Assert.That(pool.Capture(program).Dynamic, Is.True, "外部 RT 写入没有可靠内容版本时必须保持动态。");
                }
                finally
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        [Test]
        public void MaterialSnapshotChecksExactValuesEvenWhenCrcMatches()
        {
            var program = new ExternalGeometryProgram(material, Descriptor);
            using var pool = new URUI.Internal.Rendering.ExternalProgramResources();
            var changes = new Action[]{
                ()=>material.SetFloat("_FactFloat",.3125f),
                ()=>material.SetInteger("_FactInt",16777217),
                ()=>material.SetColor("_FactColor",new Color(2,-1,.25f,.75f)),
                ()=>material.SetVector("_TestTint",new Vector4(.2f,.3f,.4f,.5f)),
                ()=>material.SetTextureOffset("_TestTexture",new Vector2(.125f,-.25f)),
                ()=>material.SetTextureScale("_TestTexture",new Vector2(2,.5f)),
                ()=>material.renderQueue=3100,
                ()=>material.enableInstancing=true,
                ()=>material.doubleSidedGI=true,
                ()=>material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive
            };
            foreach (var change in changes)
            {
                pool.Begin();
                var before = pool.Capture(program);
                before.Owner.RetainReference();
                try
                {
                    change();
                    // 强制哈希相等，精确事实仍必须拒绝旧版本。
                    before.Owner.Crc = material.ComputeCRC();
                    pool.Begin();
                    var after = pool.Capture(program);
                    Assert.That(after.Identity, Is.Not.EqualTo(before.Identity));
                    Assert.That(before.Material, Is.Not.SameAs(after.Material), "仍被引用的快照不能覆盖。");
                    pool.Begin();
                    Assert.That(pool.Capture(program).Identity, Is.EqualTo(after.Identity));
                }
                finally
                {
                    before.Owner.ReleaseReference();
                }
            }
            Assert.That(material.GetInteger("_FactInt"), Is.EqualTo(16777217), "整数属性不能压进 float 事实缓存。");
        }
        [Test]
        public void UnknownProgramKeepsExtraVertexChannels()
        {
            var raw = new Material(UnityEngine.Resources.Load<Shader>("URUI/Tests/ExternalRawGeometry"));
            try
            {
                mesh.SetUVs(2, new List<Vector4> { new(.25f, .5f, .75f, 1), new(.25f, .5f, .75f, 1), new(.25f, .5f, .75f, 1), new(.25f, .5f, .75f, 1) });
                var program = new ExternalGeometryProgram(raw);
                using var registration = Attach(context => context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity));
                document.SynchronizeLayout();
                camera.Render();
                var pixel = Pixel(3, 3);
                Assert.That(pixel.r, Is.EqualTo(.25f).Within(3f / 255));
                Assert.That(pixel.g, Is.EqualTo(.5f).Within(3f / 255));
                Assert.That(pixel.b, Is.EqualTo(.75f).Within(3f / 255));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(raw);
            }
        }
        [UnityTest]
        public IEnumerator ContextDrawingPreservesOrderWithoutCopyingTheOutput()
        {
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = new RenderTexture(192, 160, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            camera.allowHDR = true;
            var html = new System.Text.StringBuilder("<!doctype html><html><head><style>body{margin:0;}</style></head><body>");
            for (var i = 0; i < 9; i++)
            {
                html.Append("<div style='position:absolute;width:8px;height:8px;background:rgba(0,255,0,.5);'></div><div id='p").Append(i).Append("' style='position:absolute;width:8px;height:8px;'></div>");
            }
            html.Append("<div style='position:absolute;width:8px;height:8px;background:rgba(0,255,0,.5);'></div></body></html>");
            document.LoadHtml(html.ToString(), "/shared-color-pass.html");
            var registrations = new List<IDisposable>();
            var program = new ExternalGeometryProgram(material, Descriptor);
            var previous = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<QueuedPipelineAsset>();
            var submitted = false;
            var before = URUI.Rendering.Effects.EffectStatistics.GetTargets().LeasedColorBytes;
            try
            {
                for (var i = 0; i < 9; i++)
                {
                    var element = document.Q("p" + i);
                    registrations.Add(document.AttachGeometryProvider(element, "geometry", context => context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity)));
                    element.Style.Set("--urui-decorator", "shader(geometry)");
                }
                pipeline.Draw = context =>
                {
                    if (submitted)
                    {
                        return;
                    }
                    submitted = true;
                    context.SetupCameraProperties(camera);
                    using var clear = new CommandBuffer();
                    clear.SetRenderTarget(target);
                    clear.ClearRenderTarget(false, true, Color.blue);
                    context.ExecuteCommandBuffer(clear);
                    Assert.That(document.Surface.SubmitRendering(context, target, new Rect(32, 16, 128, 128), RenderTargetInfo.FromTexture(target)), Is.True);
                };
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                Assert.That(submitted, Is.True);
                var old = RenderTexture.active;
                var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(35, 140, 1, 1), 0, 0);
                    read.Apply();
                    var pixel = read.GetPixel(0, 0);
                    var expected = new Color(.5f, .5f, 0);
                    Assert.That(pixel.r, Is.EqualTo(expected.r).Within(3f / 255));
                    Assert.That(pixel.g, Is.EqualTo(expected.g).Within(3f / 255));
                    Assert.That(pixel.b, Is.LessThan(.01f));
                }
                finally
                {
                    RenderTexture.active = old;
                    UnityEngine.Object.DestroyImmediate(read);
                }
                var after = URUI.Rendering.Effects.EffectStatistics.GetTargets().LeasedColorBytes;
                var targets = (Dictionary<(ulong, int), URUI.Internal.Rendering.EffectTargetPool.Lease>)typeof(RenderHost)
                    .GetField("preparedLayerTargets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Renderer);
                Assert.That(targets, Is.Empty, "普通绘制与外部几何应直接使用输出目标。");
                Assert.That(after, Is.EqualTo(before));
            }
            finally
            {
                pipeline.Draw = null;
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                foreach (var registration in registrations)
                {
                    registration.Dispose();
                }
                UnityEngine.Object.DestroyImmediate(pipeline);
            }
        }

        [UnityTest]
        public IEnumerator OffscreenCallbackPreservesParentColorQuantization()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || !SystemInfo.supportsMemorylessTextures)
            {
                Assert.Ignore("需要Linear及原生memoryless附件。");
            }
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            camera.allowHDR = true;
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}</style></head><body><div style='position:absolute;width:8px;height:8px;background:rgba(255,0,0,.1);'></div><div id='p' style='position:absolute;width:8px;height:8px;'></div><div style='position:absolute;width:8px;height:8px;background:rgba(0,255,0,.1);'></div></body></html>", "/offscreen-color-boundary.html");
            for (var i = 0; i < 4; i++)
            {
                document.SynchronizeLayout();
            }
            camera.Render();
            var expected = Pixel(3, 3);
            var output = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            output.Create();
            var green = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            green.SetPixel(0, 0, Color.green);
            green.Apply();
            var program = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_Mode", 2);
            properties.SetFloat("_ParameterIndex", 0);
            properties.SetTexture("_Parameters", green);
            var previous = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<QueuedPipelineAsset>();
            var submitted = false;
            using var painter = document.AttachPainter(document.Q("p"), "offscreen", context => context.RecordSurface(output, mesh, program, Matrix4x4.Ortho(0, 8, 8, 0, -1, 1), properties, 0));
            document.Q("p").Style.Set("--urui-decorator", "shader(offscreen)");
            try
            {
                pipeline.Draw = context =>
                {
                    if (submitted)
                    {
                        return;
                    }
                    submitted = true;
                    context.SetupCameraProperties(camera);
                    using var clear = new CommandBuffer();
                    clear.SetRenderTarget(target);
                    clear.ClearRenderTarget(false, true, Color.blue);
                    context.ExecuteCommandBuffer(clear);
                    Assert.That(document.Surface.SubmitRendering(context, target, new Rect(0, 0, 128, 128)), Is.True);
                };
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                Assert.That(submitted, Is.True);
                Assert.That(Pixel(3, 3), Is.EqualTo(expected));
            }
            finally
            {
                pipeline.Draw = null;
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                UnityEngine.Object.DestroyImmediate(pipeline);
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(program);
                UnityEngine.Object.DestroyImmediate(green);
            }
        }
        [UnityTest]
        public IEnumerator QueuedKnownMaterialVersionsAreImmutable() => VerifyQueuedMaterial(false);
        [UnityTest]
        public IEnumerator QueuedUnknownMaterialVersionsAreImmutable() => VerifyQueuedMaterial(true);
        [UnityTest]
        public IEnumerator ContextKeepsQueuedMaterialVersions() => VerifyQueuedMaterial(false, true);
        IEnumerator VerifyQueuedMaterial(bool unknown, bool shared = false)
        {
            if (shared)
            {
                PrepareSharedQueue();
            }
            var source = unknown ? new Material(UnityEngine.Resources.Load<Shader>("URUI/Tests/ExternalRawGeometry")) : material;
            mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            if (unknown)
            {
                mesh.SetUVs(2, new List<Vector4> { Vector4.one, Vector4.one, Vector4.one, Vector4.one });
            }
            source.SetColor("_TestTint", Color.red);
            var program = new ExternalGeometryProgram(source, unknown ? default : Descriptor);
            using var registration = Attach(context => context.Draw(new ExternalGeometryInput(mesh, 1), program, Matrix4x4.identity));
            var second = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            second.Create();
            var previous = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<QueuedPipelineAsset>();
            var submitted = false;
            try
            {
                pipeline.Draw = context =>
                {
                    if (submitted)
                    {
                        return;
                    }
                    submitted = true;
                    context.SetupCameraProperties(camera);
                    using var clear = new CommandBuffer();
                    clear.SetRenderTarget(target);
                    clear.ClearRenderTarget(false, true, Color.black);
                    context.ExecuteCommandBuffer(clear);
                    Assert.That(document.Surface.SubmitRendering(context, target, new Rect(0, 0, 128, 128), shared ? RenderTargetInfo.FromTexture(target) : default), Is.True);
                    source.SetColor("_TestTint", Color.green);
                    document.SynchronizeLayout();
                    clear.Clear();
                    clear.SetRenderTarget(second);
                    clear.ClearRenderTarget(false, true, Color.black);
                    context.ExecuteCommandBuffer(clear);
                    if (shared)
                    {
                        camera.targetTexture = second;
                    }
                    Assert.That(document.Surface.SubmitRendering(context, second, new Rect(0, 0, 128, 128), shared ? RenderTargetInfo.FromTexture(second) : default), Is.True);
                    source.SetColor("_TestTint", Color.blue);
                };
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                Assert.That(submitted, Is.True);
                var firstColor = Pixel(3, 3);
                Assert.That(firstColor.r, Is.GreaterThan(.99f), "排队帧保留录制时的红色材质值。");
                Assert.That(firstColor.g + firstColor.b, Is.LessThan(.01f));
                var first = target;
                target = second;
                try
                {
                    var next = Pixel(3, 3);
                    Assert.That(next.g, Is.GreaterThan(.99f), "第二帧保留绿色值；录制结束后改源材质不能回写排队帧。");
                    Assert.That(next.r + next.b, Is.LessThan(.01f));
                }
                finally
                {
                    target = first;
                }
            }
            finally
            {
                pipeline.Draw = null;
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                camera.targetTexture = target;
                UnityEngine.Object.DestroyImmediate(pipeline);
                second.Release();
                UnityEngine.Object.DestroyImmediate(second);
                if (unknown)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }
        }
        [UnityTest]
        public IEnumerator QueuedKnownGeometryVersionsAreImmutable() => VerifyQueued(false);
        [UnityTest]
        public IEnumerator QueuedUnknownGeometryVersionsAreImmutable() => VerifyQueued(true);
        [UnityTest]
        public IEnumerator ContextKeepsQueuedGeometryVersions() => VerifyQueued(false, true);
        [UnityTest]
        public IEnumerator ContextPreservesQueuedUnknownGeometryVersions() => VerifyQueued(true, true);
        void PrepareSharedQueue()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || !SystemInfo.supportsMemorylessTextures)
            {
                Assert.Ignore("需要Linear及原生memoryless附件。");
            }
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            camera.allowHDR = true;
            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}</style></head><body><div style='position:absolute;width:8px;height:8px;background:rgba(0,255,0,.5);'></div><div id='p' style='position:absolute;width:8px;height:8px;'></div></body></html>", "/shared-queue.html");
        }
        IEnumerator VerifyQueued(bool unknown, bool shared = false)
        {
            if (shared)
            {
                PrepareSharedQueue();
            }
            var raw = unknown ? new Material(UnityEngine.Resources.Load<Shader>("URUI/Tests/ExternalRawGeometry")) : null;
            if (unknown)
            {
                mesh.SetUVs(2, new List<Vector4> { new(1, 0, 0, 1), new(1, 0, 0, 1), new(1, 0, 0, 1), new(1, 0, 0, 1) });
            }
            var program = new ExternalGeometryProgram(unknown ? raw : material, unknown ? default : Descriptor);
            ulong version = 1;
            using var registration = Attach(context => context.Draw(new ExternalGeometryInput(mesh, version), program, Matrix4x4.identity));
            var second = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            second.Create();
            var previous = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<QueuedPipelineAsset>();
            var submitted = false;
            try
            {
                pipeline.Draw = context =>
                {
                    if (submitted)
                    {
                        return;
                    }
                    submitted = true;
                    context.SetupCameraProperties(camera);
                    using var clear = new CommandBuffer();
                    clear.SetRenderTarget(target);
                    clear.ClearRenderTarget(false, true, Color.black);
                    context.ExecuteCommandBuffer(clear);
                    Assert.That(document.Surface.SubmitRendering(context, target, new Rect(0, 0, 128, 128), shared ? RenderTargetInfo.FromTexture(target) : default), Is.True);
                    mesh.vertices = new[] { new Vector3(16, 0), new Vector3(24, 0), new Vector3(24, 8), new Vector3(16, 8) };
                    version++;
                    document.SynchronizeGeometry();
                    clear.Clear();
                    clear.SetRenderTarget(second);
                    clear.ClearRenderTarget(false, true, Color.black);
                    context.ExecuteCommandBuffer(clear);
                    if (shared)
                    {
                        camera.targetTexture = second;
                    }
                    Assert.That(document.Surface.SubmitRendering(context, second, new Rect(0, 0, 128, 128), shared ? RenderTargetInfo.FromTexture(second) : default), Is.True);
                };
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                for (var i = 0; i < 4; i++)
                {
                    yield return null;
                }
                Assert.That(submitted, Is.True);
                Assert.That(Pixel(3, 3).r, Is.GreaterThan(.99f));
                Assert.That(Pixel(19, 3).r, Is.LessThan(.01f));
                var first = target;
                target = second;
                try
                {
                    Assert.That(Pixel(3, 3).r, Is.LessThan(.01f));
                    Assert.That(Pixel(19, 3).r, Is.GreaterThan(.99f));
                }
                finally
                {
                    target = first;
                }
            }
            finally
            {
                pipeline.Draw = null;
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                camera.targetTexture = target;
                UnityEngine.Object.DestroyImmediate(pipeline);
                second.Release();
                UnityEngine.Object.DestroyImmediate(second);
                if (raw)
                {
                    UnityEngine.Object.DestroyImmediate(raw);
                }
            }
        }
        IDisposable Attach(Action<ExternalGeometryContext> prepare, Action<int> onRecorded = null)
        {
            var element = document.Q("p");
            var registration = document.AttachGeometryProvider(element, "geometry", prepare, onRecorded);
            element.Style.Set("--urui-decorator", "shader(geometry)");
            return registration;
        }
        Color Pixel(int x, int y)
        {
            var previous = RenderTexture.active;
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                read.ReadPixels(new Rect(x, 127 - y, 1, 1), 0, 0);
                read.Apply();
                return read.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(read);
            }
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(host);
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
