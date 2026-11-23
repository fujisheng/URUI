using NUnit.Framework;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Rendering;
using URUI.Resources;

namespace URUI.Tests
{
    public sealed class RenderParameterPageTests
    {
        static Color Read(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                image.Apply();
                return image.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }
        [Test]
        public void QueuedParameterPagesAreImmutable()
        {
            var camera = new GameObject("Parameter camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Parameter surface");
            var first = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var second = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var shader = UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract");
            var a = new Material(shader);
            var b = new Material(shader);
            var commands = new CommandBuffer();
            using var pages = new RenderParameterPages();
            RenderParameterPages.Page firstPage = null;
            RenderParameterPages.Page secondPage = null;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(64, 64);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;left:1px;width:8px;height:8px;transform:translate(4px,0px);}</style></head><body><img id='moving' src='white.png'></body></html>", "/parameter-pages.html");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        pages.Apply(plan, packet);
                    }
                }
                firstPage = pages.Current;
                firstPage.RetainSubmission();
                pages.EndRecording();
                a.SetTexture("_Parameters", firstPage.Texture);
                a.SetFloat("_ParameterIndex", 3);
                a.SetFloat("_Mode", 2);
                first.Create();
                second.Create();
                commands.Blit(Texture2D.whiteTexture, first, a);

                document.Q("moving").Style.Set("left", "2px");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        pages.Apply(plan, packet);
                    }
                }
                secondPage = pages.Current;
                secondPage.RetainSubmission();
                pages.EndRecording();
                Assert.That(secondPage.Texture, Is.Not.SameAs(firstPage.Texture), "待提交的旧参数纹理不能被第二次录制覆盖。");
                b.SetTexture("_Parameters", secondPage.Texture);
                b.SetFloat("_ParameterIndex", 3);
                b.SetFloat("_Mode", 2);
                commands.Blit(Texture2D.whiteTexture, second, b);
                Graphics.ExecuteCommandBuffer(commands);
                Assert.That(Read(first).r, Is.EqualTo(5).Within(.001f));
                Assert.That(Read(second).r, Is.EqualTo(6).Within(.001f));
                Assert.That(pages.ParameterUploadBytes, Is.GreaterThan(0));
            }
            finally
            {
                // ReadPixels above has completed the queued GPU work before these test pins release.
                firstPage?.ReleaseSubmission();
                secondPage?.ReleaseSubmission();
                commands.Release();
                first.Release();
                second.Release();
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camera);
            }
        }
        [Test]
        public void OneSubmissionCannotPinTwoDifferentParameterVersions()
        {
            var camera = new GameObject("Parameter window camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Parameter window surface");
            using var pages = new RenderParameterPages();
            var submission = new URUI.Internal.Resources.ImageSubmissionFence();
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:8px;height:8px;background:red;}</style></head><body><div id='p'></div></body></html>", "/parameter-window.html");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        pages.Apply(plan, packet);
                    }
                }
                var first = pages.Current;
                submission.PinParameters(first);
                submission.PinParameters(first);
                pages.EndRecording();
                Assert.That(submission.Parameters.Count, Is.EqualTo(1));
                document.Q("p").Style.Set("transform", "translate(1px,0px)");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        pages.Apply(plan, packet);
                    }
                }
                Assert.That(pages.Current, Is.Not.SameAs(first));
                Assert.Throws<System.InvalidOperationException>(() => submission.PinParameters(pages.Current), "一个合并 fence 不能隐藏多个参数窗口，八份在途预算必须约束实际版本。");
            }
            finally
            {
                submission.Release();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camera);
            }
        }
        [Test]
        public void OneChangedParameterUploadsOnlyItsRow()
        {
            var camera = new GameObject("Sparse camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Sparse parameters");
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            using var pages = new RenderParameterPages();
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                var html = new StringBuilder("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:8px;height:8px;}</style></head><body>");
                for (var i = 0; i < 100; i++)
                {
                    html.Append("<img id='n").Append(i).Append("' src='white.png'>");
                }
                document.LoadHtml(html.Append("</body></html>").ToString(), "/sparse-parameters.html");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        Assert.That(packet.ParameterCount, Is.EqualTo(100));
                        pages.Apply(plan, packet);
                    }
                }
                var pageHeight = pages.Current.Texture.height;
                Assert.That(pages.RetainedPayloadBytes, Is.EqualTo(pageHeight * (256 * 16 * 2 + 12L)),
                    "参数页只持有 CPU/GPU 纹理数据和行索引，不需要额外上传缓冲。");
                pages.EndRecording();
                var before = pages.ParameterUploadBytes;
                var retainedBefore = pages.RetainedPayloadBytes;
                document.Q("n65").Style.Set("left", "3px");
                document.SynchronizeLayout();
                using (var positive = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory,
                    "GC.Alloc", 128, Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    var probe = new byte[4096];
                    positive.Stop();
                    System.GC.KeepAlive(probe);
                    Assert.That(positive.Count, Is.GreaterThan(0), "分配探针必须捕获真实托管分配。");
                }
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        // 新行只需创建纹理包装器，不应重复通过枚举反射验证已知的精确格式。
                        using var allocations = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory,
                            "GC.Alloc", 128, Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                        pages.Apply(plan, packet);
                        allocations.Stop();
                        TestContext.WriteLine("首次局部参数纹理分配事件：" + allocations.Count);
                        Assert.That(allocations.Count, Is.LessThanOrEqualTo(2), "局部参数纹理创建不能触发成批的格式反射分配。");
                    }
                }
                Assert.That(pages.ParameterUploadBytes - before, Is.EqualTo(256 * 16), "只改变一个参数时只上传一行；不能上传整张纹理。");
                Assert.That(pages.RetainedPayloadBytes - retainedBefore, Is.EqualTo(256 * 16 * 2), "只增加该行的CPU/GPU纹理数据，不能靠额外预分配隐藏GC。");
                Assert.That(pages.Current.Texture.graphicsFormat, Is.EqualTo(UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat));
                Assert.That(pages.Current.Texture.mipmapCount, Is.EqualTo(1));
                material.SetTexture("_Parameters", pages.Current.Texture);
                material.SetFloat("_ParameterIndex", 65 * 8 + 3);
                material.SetFloat("_Mode", 2);
                target.Create();
                Graphics.Blit(Texture2D.whiteTexture, target, material);
                Assert.That(Read(target).r, Is.EqualTo(3).Within(.001f), "局部上传还必须实际更新GPU内容。");
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void DisjointChangedRowsUploadIndependentlyWithoutClobbering()
        {
            var camera = new GameObject("Disjoint camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Disjoint parameters");
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            using var pages = new RenderParameterPages();
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                var html = new StringBuilder("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:8px;height:8px;left:7px;}</style></head><body>");
                for (var i = 0; i < 220; i++)
                {
                    html.Append("<img id='n").Append(i).Append("' src='white.png'>");
                }
                document.LoadHtml(html.Append("</body></html>").ToString(), "/disjoint-parameters.html");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        Assert.That(packet.ParameterCount, Is.EqualTo(220));
                        pages.Apply(plan, packet);
                    }
                }
                Assert.That(pages.Current.Texture.height, Is.EqualTo(7));
                pages.EndRecording();

                // Mutate 3 disjoint rows (row 0, row 2, row 5): 3 * 2 < 7 triggers span CopyTexture upload.
                document.Q("n2").Style.Set("left", "11px");
                document.Q("n68").Style.Set("left", "29px");
                document.Q("n165").Style.Set("left", "47px");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        pages.Apply(plan, packet);
                    }
                }
                target.Create();
                float ReadLeft(int parameterIndex)
                {
                    material.SetTexture("_Parameters", pages.Current.Texture);
                    material.SetFloat("_ParameterIndex", parameterIndex * 8 + 3);
                    material.SetFloat("_Mode", 2);
                    Graphics.Blit(Texture2D.whiteTexture, target, material);
                    return Read(target).r;
                }
                Assert.That(ReadLeft(2), Is.EqualTo(11f).Within(.001f), "Row 0 must not be clobbered by later disjoint spans.");
                Assert.That(ReadLeft(35), Is.EqualTo(7f).Within(.001f), "Untouched Row 1 must preserve its original value.");
                Assert.That(ReadLeft(68), Is.EqualTo(29f).Within(.001f), "Row 2 must receive its exact disjoint span.");
                Assert.That(ReadLeft(165), Is.EqualTo(47f).Within(.001f), "Row 5 must receive its exact disjoint span.");

                // Second back-to-back update on the same frame without GPU fence retirement must not race spanUpload.
                pages.EndRecording();
                document.Q("n35").Style.Set("left", "19px");
                document.Q("n130").Style.Set("left", "61px");
                document.SynchronizeLayout();
                using (var input = NativeRenderPlanLease.Begin(surface.SurfaceHandle, out _))
                {
                    using (var plan = input.Compile(default, out var packet))
                    {
                        pages.Apply(plan, packet);
                    }
                }
                Assert.That(ReadLeft(2), Is.EqualTo(11f).Within(.001f));
                Assert.That(ReadLeft(35), Is.EqualTo(19f).Within(.001f));
                Assert.That(ReadLeft(68), Is.EqualTo(29f).Within(.001f));
                Assert.That(ReadLeft(130), Is.EqualTo(61f).Within(.001f));
                Assert.That(ReadLeft(165), Is.EqualTo(47f).Within(.001f));
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camera);
            }
        }
    }
}
