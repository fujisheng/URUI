using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.TestTools;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;
using URUI.Hosting;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;

namespace URUI.Tests
{
    public sealed class ReviewRegressionTests
    {
        sealed class OverloadedEventElement : Element
        {
            internal int Clicks;
            internal OverloadedEventElement(ElementContext context) : base(context) { }
            protected override void OnEvent(EventBase value)
            {
                if (value is ClickEvent click)
                {
                    OnEvent(click);
                }
            }
            void OnEvent(ClickEvent value)
            {
                Clicks++;
            }
        }
        [Test]
        public void EventOverrideWithTypedOverloadStillInitializesAndReceivesNativeClick()
        {
            var camera = new GameObject("Review event camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Review event surface");
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.RegisterElementType("urui-overload", context => new OverloadedEventElement(context));
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}urui-overload{display:block;width:50px;height:50px;}</style></head><body><urui-overload id='control'></urui-overload></body></html>", "/review-overload.html");
                document.SynchronizeLayout();
                var target = document.Q<OverloadedEventElement>("control");
                surface.Input.SendPointer(PointerPhase.Down, new Vector2(10, 10));
                surface.Input.SendPointer(PointerPhase.Up, new Vector2(10, 10));
                Assert.That(target.Clicks, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }
        [UnityTest]
        public IEnumerator RetiredMeshAndItsBudgetSurviveOutstandingSubmissionReader()
        {
            var camera = new GameObject("Review mesh camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Review mesh surface");
            var readers = new List<RenderPlanResources.Entry>();
            using var resources = new RenderPlanResources(value => value.Dispose());
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:16px;height:16px;background:red;}</style></head><body><div></div></body></html>", "/review-mesh.html");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var lease = input.Compile(default, out var packet))
                    {
                        resources.Apply(lease, packet);
                    }
                }
                resources.PinSubmission(readers, new HashSet<RenderPlanResources.Entry>());
                Assert.That(readers.Count, Is.GreaterThan(0));
                var mesh = readers[0].Mesh;
                var bytes = resources.RetainedBytes;
                Assert.That(bytes, Is.GreaterThan(0));
                resources.Dispose();
                Assert.That(mesh, Is.Not.Null);
                Assert.That((bool)mesh, Is.True, "其他批次的 fence 不能代替当前读取者。");
                Assert.That(resources.RetainedBytes, Is.EqualTo(bytes), "物理缓冲仍活着时不能提前扣预算。");
                foreach (var reader in readers)
                {
                    reader.ReleaseReference();
                }
                readers.Clear();
                Assert.That(resources.RetainedBytes, Is.Zero);
                yield return null;
                Assert.That((bool)mesh, Is.False);
            }
            finally
            {
                foreach (var reader in readers)
                {
                    reader.ReleaseReference();
                }
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }
        [Test]
        public void CachedReadOnlyLayerIsPinnedByTheCurrentSubmission()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var camera = new GameObject("Review cached layer camera", typeof(Camera));
            camera.tag = "MainCamera";
            var output = new RenderTexture(64, 64, 0);
            output.Create();
            camera.GetComponent<Camera>().targetTexture = output;
            var root = new GameObject("Review cached layer Surface");
            var submission = new URUI.Internal.Resources.ImageSubmissionFence();
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(64, 64);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#group{opacity:.5;width:32px;height:16px;}div.red,div.green{position:absolute;width:24px;height:16px;background:red;}div.green{left:8px;background:green;}</style></head><body><div id='group'><div class='red'></div><div class='green'></div></div></body></html>", "/review-cached-read.html");
                document.SynchronizeLayout();
                var host = typeof(Document).GetField("host", flags).GetValue(document);
                var type = host.GetType();
                var contents = (Dictionary<ulong, LayerContentLease>)type.GetField("layerContents", flags).GetValue(host);
                Assert.That(contents.Count, Is.GreaterThan(0));
                // 单独推进已经由 LayerContentLease 测试覆盖的 fence 完成状态，
                // 再让真实原生依赖图选择只读复用；此用例验证引用所有权而非像素。
                foreach (var value in contents.Values)
                {
                    value.Retain();
                    value.Release(true);
                }
                document.SynchronizeLayout();
                var writes = (System.Collections.ICollection)type.GetField("recordedLayerWrites", flags).GetValue(host);
                Assert.That(writes.Count, Is.Zero, "夹具必须走实际 cached-read 操作，不能混入写入引用。");
                LayerContentLease content = null;
                foreach (var value in contents.Values)
                {
                    if (value.Available)
                    {
                        content = value;
                        break;
                    }
                }
                Assert.That(content, Is.Not.Null);
                var texture = content.Target.Texture;
                var linear = (bool)typeof(EffectTargetPool.Lease).GetField("linear", flags).GetValue(content.Target);
                var references = typeof(LayerContentLease).GetField("references", flags);
                var before = (int)references.GetValue(content);
                type.GetMethod("PinLayerWrites", flags).Invoke(host, new object[] { submission });
                Assert.That((int)references.GetValue(content), Is.EqualTo(before + 1), "本次只读提交必须取得自己的引用。");
                type.GetMethod("ClearNativeRecording", flags).Invoke(host, null);
                // 模拟真实退役时先撤销宿主所有权，避免清理夹具再次退役已归池的包装器。
                ulong retiredKey = 0;
                foreach (var pair in contents)
                {
                    if (ReferenceEquals(pair.Value, content))
                    {
                        retiredKey = pair.Key;
                        break;
                    }
                }
                contents.Remove(retiredKey);
                ((Dictionary<(ulong, int), EffectTargetPool.Lease>)type.GetField("preparedLayerTargets", flags).GetValue(host)).Remove((retiredKey, 0));
                content.Dispose();
                var pool = (EffectTargetPool)type.GetField("effectTargets", flags).GetValue(host);
                using var replacement = pool.Rent(texture.width, texture.height, texture.format, linear);
                Assert.That(replacement.Texture, Is.Not.SameAs(texture), "只读合成输入也必须受本次提交的 fence 保护。");
            }
            finally
            {
                submission.Release();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
            }
        }

        [Test]
        public void CurrentSubmissionPinsPrivateAuxiliaryTarget()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var camera = new GameObject("Review encoded reader camera", typeof(Camera));
            camera.tag = "MainCamera";
            var output = new RenderTexture(64, 64, 0);
            output.Create();
            camera.GetComponent<Camera>().targetTexture = output;
            var root = new GameObject("Review encoded reader Surface");
            var submission = new URUI.Internal.Resources.ImageSubmissionFence();
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(64, 64);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:16px;height:16px;background:red;filter:blur(1px);}</style></head><body><div></div></body></html>", "/review-encoded-reader.html");
                document.SynchronizeLayout();
                var host = typeof(Document).GetField("host", flags).GetValue(document);
                var type = host.GetType();
                var targets = (Dictionary<(ulong, int), EffectTargetPool.Lease>)type.GetField("preparedLayerTargets", flags).GetValue(host);
                (ulong, int) key = (0, -1);
                if (!targets.ContainsKey(key))
                {
                    foreach (var candidate in targets.Keys)
                    {
                        if (candidate.Item2 != 0)
                        {
                            key = candidate;
                            break;
                        }
                    }
                }
                Assert.That(targets.ContainsKey(key) && key.Item2 != 0, Is.True, "夹具必须实际使用独立的编码或滤镜临时目标。");
                var lease = targets[key];
                var texture = lease.Texture;
                var linear = (bool)typeof(EffectTargetPool.Lease).GetField("linear", flags).GetValue(lease);
                type.GetMethod("PinLayerWrites", flags).Invoke(host, new object[] { submission });
                type.GetMethod("ClearNativeRecording", flags).Invoke(host, null);
                targets.Remove(key);
                lease.Dispose();
                var pool = (EffectTargetPool)type.GetField("effectTargets", flags).GetValue(host);
                using var replacement = pool.Rent(texture.width, texture.height, texture.format, linear);
                Assert.That(replacement.Texture, Is.Not.SameAs(texture), "编码、背景和临时 RT 也必须受消费提交的 fence 保护。");
            }
            finally
            {
                submission.Release();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
            }
        }

        [Test]
        public void BuiltinBudgetRejectionHappensBeforeAnyUIDraw()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var cameraObject = new GameObject("Builtin admission camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            var target = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            var root = new GameObject("Builtin admission Surface");
            var synthetic = new List<URUI.Internal.Resources.ImageSubmissionFence>();
            System.Collections.IList current = null;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(32, 32);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:32px;height:32px;background:red;}</style></head><body><div></div></body></html>", "/builtin-admission.html");
                document.SynchronizeLayout();
                var host = typeof(Document).GetField("host", flags).GetValue(document);
                var type = host.GetType();
                current = (System.Collections.IList)type.GetField("imageSubmissions", flags).GetValue(host);
                var budget = type.GetField("submissionBudget", flags)?.GetValue(host);
                for (var i = 0; i < 8; i++)
                {
                    var item = new URUI.Internal.Resources.ImageSubmissionFence { uncertain = true };
                    // 对旧列表和新共享计数使用相同的八份未完成提交输入；不模拟 GPU 已完成。
                    if (budget != null)
                    {
                        budget.GetType().GetMethod("Reserve", flags).Invoke(budget, null);
                        item.GetType().GetField("Budget", flags).SetValue(item, budget);
                    }
                    current.Add(item);
                    synthetic.Add(item);
                }
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("URUI.*GPU.*"));
                camera.Render();
                var old = RenderTexture.active;
                var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(16, 16, 1, 1), 0, 0);
                    read.Apply();
                    var pixel = read.GetPixel(0, 0);
                    Assert.That(pixel.r, Is.LessThan(3f / 255));
                    Assert.That(pixel.b, Is.GreaterThan(.99f), "被拒绝的第九次 UI 提交不能先画红色再报错。");
                }
                finally
                {
                    RenderTexture.active = old;
                    UnityEngine.Object.DestroyImmediate(read);
                }
            }
            finally
            {
                current?.Clear();
                foreach (var item in synthetic)
                {
                    item.Release();
                }
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [TestCase(2)]
        [TestCase(64)]
        public void ComplexMaskSamplesOpaqueInteriorOnTheSameBackend(int maskWidth)
        {
            var cameraObject = new GameObject("Complex mask reference camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            var root = new GameObject("Complex mask reference Surface");
            var red = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            red.SetPixel(0, 0, Color.red);
            red.Apply();
            var green = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            green.SetPixel(0, 0, Color.green);
            green.Apply();
            var mask = new Texture2D(maskWidth, 1, TextureFormat.RGBA32, false, true);
            mask.filterMode = FilterMode.Bilinear;
            var colors = new Color[maskWidth];
            for (var i = 0; i < maskWidth / 2; i++)
            {
                colors[i] = Color.white;
            }
            mask.SetPixels(colors);
            mask.Apply();
            var read = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
            var active = RenderTexture.active;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(128, 128);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.Images = new[] { new URUI.Resources.ImageAsset("red.png", red), new URUI.Resources.ImageAsset("green.png", green), new URUI.Resources.ImageAsset("mask.png", mask) };
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#group{position:absolute;left:8px;top:8px;width:32px;height:24px;mask-image:url(mask.png);mask-repeat:no-repeat;mask-size:100% 100%;}img{position:absolute;width:24px;height:24px;}</style></head><body><div id='group'><img src='red.png'><img src='green.png' style='left:8px;'></div></body></html>", "/complex-mask-reference.html");
                document.SynchronizeLayout();
                camera.Render();
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                read.Apply();
                var pixel = read.GetPixel(20, 127 - 16);
                var alpha = maskWidth == 2 ? .71875f : 1f;
                var expected = alpha;
                Assert.That(pixel.g, Is.EqualTo(expected).Within(3f / 255), "两像素蒙版保留线性过滤；高分辨率硬边蒙版采样不透明内部。");
            }
            finally
            {
                RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                UnityEngine.Object.DestroyImmediate(red);
                UnityEngine.Object.DestroyImmediate(green);
                UnityEngine.Object.DestroyImmediate(mask);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GroupedUnknownPainterPreservesExtendedInkWithEitherScale(bool negativeScale)
        {
            var cameraObject = new GameObject("Extended painter camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            var target = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            var root = new GameObject("Extended painter Surface");
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, new Vector3(32, 0), new Vector3(32, 8), new Vector3(0, 8) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            var red = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            red.SetPixel(0, 0, Color.red);
            red.Apply();
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_Mode", 2);
            properties.SetFloat("_ParameterIndex", 0);
            properties.SetTexture("_Parameters", red);
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var active = RenderTexture.active;
            IDisposable painter = null;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(64, 64);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#group{position:absolute;left:" + (negativeScale ? "48" : "16") + "px;top:16px;width:8px;height:8px;opacity:.5;}#paint{width:8px;height:8px;--urui-decorator:shader(extended);" + (negativeScale ? "transform:scaleX(-1);transform-origin:0px 0px;" : "") + "}</style></head><body><div id='group'><div id='paint'></div></div></body></html>", "/extended-painter.html");
                var calls = 0;
                painter = document.AttachPainter(document.Q("paint"), "extended", context =>
                {
                    calls++;
                    context.DrawMesh(mesh, material, Matrix4x4.identity, properties);
                });
                var before = calls;
                document.SynchronizeLayout();
                Assert.That(calls - before, Is.EqualTo(1), "准备边界不能额外执行用户绘制回调。");
                camera.Render();
                RenderTexture.active = target;
                read.ReadPixels(new Rect(negativeScale ? 24 : 40, 64 - 20, 1, 1), 0, 0);
                read.Apply();
                var expected = new Color(.5f, 0, .5f);
                var actual = read.GetPixel(0, 0);
                Assert.That(actual.r, Is.EqualTo(expected.r).Within(3f / 255), "组边界必须包含作者盒之外的 painter 输出。");
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(3f / 255));
            }
            finally
            {
                RenderTexture.active = active;
                painter?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                foreach (var item in new UnityEngine.Object[] { mesh, material, red, read })
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }
        }

        [TestCase(0, false)]
        [TestCase((int)UnityEngine.Rendering.CopyTextureSupport.Basic, true)]
        public void PrivateLayerCopyRequiresActualPlatformSupport(int support, bool expected)
        {
            var source = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var destination = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var different = new RenderTexture(16, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                Assert.That(URUI.Internal.Hosting.RenderHost.CanCopyLayerData(source, destination, (UnityEngine.Rendering.CopyTextureSupport)support), Is.EqualTo(expected));
                Assert.That(URUI.Internal.Hosting.RenderHost.CanCopyLayerData(source, different, (UnityEngine.Rendering.CopyTextureSupport)support), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(destination);
                UnityEngine.Object.DestroyImmediate(different);
            }
        }

        [Test]
        public void MovingOnePainterThroughCommandPositionsKeepsOneRecording()
        {
            var camera = new GameObject("Review painter camera", typeof(Camera));
            camera.tag = "MainCamera";
            var output = new RenderTexture(64, 64, 0);
            output.Create();
            camera.GetComponent<Camera>().targetTexture = output;
            var root = new GameObject("Review painter Surface");
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            IDisposable painter = null;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(64, 64);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:1px;height:1px;background:red;}</style></head><body><div id='paint' style='--urui-decorator:shader(probe);'></div></body></html>", "/review-painter-positions.html");
                var callbacks = 0;
                painter = document.AttachPainter(document.Q("paint"), "probe", context =>
                {
                    callbacks++;
                    context.DrawMesh(mesh, material, Matrix4x4.identity);
                });
                for (var i = 0; i < 32; i++)
                {
                    document.Body.Insert(0, document.CreateElement("div"));
                    document.SynchronizeLayout();
                }
                var host = typeof(Document).GetField("host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(document);
                var recordings = (System.Collections.IDictionary)host.GetType().GetField("externalRecordings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(host);
                Assert.That(callbacks, Is.GreaterThanOrEqualTo(32));
                Assert.That(recordings.Count, Is.LessThanOrEqualTo(1), "一个活动 painter 的历史命令位置不能变成永久 native CommandBuffer。");
            }
            finally
            {
                painter?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
                output.Release();
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void RetiredLayerTargetCannotReturnToPoolBeforeReaderCompletes()
        {
            using var pool = new EffectTargetPool();
            var content = LayerContentLease.Rent(pool.Rent(8, 8, RenderTextureFormat.ARGB32), 1, true);
            var texture = content.Target.Texture;
            content.Retain();
            content.Dispose();
            try
            {
                using var next = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
                Assert.That(next.Texture, Is.Not.SameAs(texture), "退役所有者还有读取者时，物理 RT 不能被下一租约覆盖。");
            }
            finally
            {
                content.Release(false);
            }
        }
    }
}
