using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;
using URUI.Effect.Rendering;
namespace URUI.Effect.Tests
{
    public sealed class EffectSchedulingTests
    {
        [Test]
        public void RecordingIsProvisionalAndOlderOrCancelledSubmissionsCannotReplaceNewInput()
        {
            var scheduler = new EffectScheduler();
            var source = new object();
            EffectInput Input(ulong version) => new(source, version, new Vector2(80, 40), 0, "probe:v1", RenderTextureFormat.ARGBHalf);
            Assert.IsTrue(scheduler.TryPrepare(Input(1), out var first));
            Assert.IsTrue(scheduler.TryPrepare(Input(1), out _), "未提交记录不能缓存");
            first.Submitted();
            for (int frame = 0; frame < 120; frame++)
            {
                Assert.IsFalse(scheduler.TryPrepare(Input(1), out _));
            }
            Assert.IsTrue(scheduler.TryPrepare(Input(2), out var next));
            next.Submitted();
            first.Submitted();
            Assert.IsFalse(scheduler.TryPrepare(Input(2), out _));
            scheduler.TryPrepare(Input(3), out var cancelled);
            scheduler.Invalidate();
            cancelled.Submitted();
            Assert.IsTrue(scheduler.TryPrepare(Input(3), out _));
            Assert.Throws<ArgumentException>(() => scheduler.TryPrepare(default, out _));
        }
        [UnityTest]
        public IEnumerator StaticWarm120FramesAndHiddenVersionChangesHaveZeroProcessing() => Verify(false);
        [UnityTest]
        public IEnumerator UnsubmittedRecordsAndSrpTargetRerecordStillProcessTheFirstPresentedFrame() => Verify(true);
        [UnityTest]
        public IEnumerator SameSourceVersionStillRefreshesChangedAndTimeDrivenMaterialParameters() => Verify(false, true);
        static IEnumerator Verify(bool srp, bool changeParameters = false)
        {
            var host = new GameObject("Scheduled image");
            var cameraObject = new GameObject("Schedule camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var surface = new TextureEffectSurface(32, 32);
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            material.SetFloat("_Speed", 0);
            material.SetVector("_PatternSpeed", Vector4.zero);
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var previous = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = srp ? ScriptableObject.CreateInstance<LayerMaskTests.IntermediatePipelineAsset>() : null;
            EffectModule module = null;
            IDisposable binding = null;
            ulong version = 5;
            double latestTime = -1;
            var submissions = 0;
            float brightness = 1;
            bool animate = false;
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                module = new EffectModule(document);
                document.ImageResolver = uri => uri == "/URUI.Effect.Tests/surface.png" ? surface.Output.Acquire() : null;
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/ScheduledSurface");
                var image = document.Q("surface");
                binding = module.BindSurface(image, new SurfaceEffectInput(surface, Texture2D.whiteTexture, material, () => version, prepare: frame =>
                {
                    latestTime = frame.Time;
                    material.SetFloat("_Brightness", animate ? .5f + .2f * Mathf.Sin((float)frame.Time * 4) : brightness);
                }));
                var before = TextureEffectSurface.GetStatistics().ProcessingPasses;
                document.SynchronizeLayout();
                document.SynchronizeLayout();
                Assert.AreEqual(before, TextureEffectSurface.GetStatistics().ProcessingPasses, "未提交的记录没有 GPU 处理工作");
                if (srp)
                {
                    pipeline.draw = context =>
                    {
                        context.SetupCameraProperties(camera);
                        using var setup = new CommandBuffer();
                        setup.SetRenderTarget(target);
                        setup.ClearRenderTarget(false, true, Color.clear);
                        context.ExecuteCommandBuffer(setup);
                        if (document.SubmitRendering(context, target, new Rect(0, 0, target.width, target.height)))
                        {
                            submissions++;
                        }
                    };
                    QualitySettings.renderPipeline = null;
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                }
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                }
                if (srp)
                {
                    Assert.That(submissions, Is.GreaterThan(0));
                }
                Assert.AreEqual(before + 1, TextureEffectSurface.GetStatistics().ProcessingPasses, "首次实际提交必须处理一次");
                var old = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(40, target.height - 40, 1, 1), 0, 0);
                    read.Apply();
                    var pixel = read.GetPixel(0, 0);
                    Assert.That(pixel.a, Is.GreaterThan(.95f), "首帧目标重录不能漏掉处理输出");
                    Assert.That((target.sRGB ? pixel.linear : pixel).r, Is.EqualTo(.5f).Within(.025f), "未显式设置的纹理应保留 Shader 自身的白色默认值");
                }
                finally
                {
                    RenderTexture.active = old;
                }
                var warm = TextureEffectSurface.GetStatistics().ProcessingPasses;
                for (int i = 0; i < 120; i++)
                {
                    yield return null;
                }
                Assert.AreEqual(warm, TextureEffectSurface.GetStatistics().ProcessingPasses, "同一呈现版本的120帧不处理");
                if (changeParameters)
                {
                    brightness = .5f;
                    for (int i = 0; i < 5; i++)
                    {
                        yield return null;
                    }
                    Assert.AreEqual(warm + 1, TextureEffectSurface.GetStatistics().ProcessingPasses, "源视频版本不变时，Prepare更新的材质参数仍须刷新。");
                    old = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(40, target.height - 40, 1, 1), 0, 0);
                        read.Apply();
                        var pixel = read.GetPixel(0, 0);
                        Assert.That((target.sRGB ? pixel.linear : pixel).r, Is.EqualTo(.25f).Within(.025f));
                    }
                    finally
                    {
                        RenderTexture.active = old;
                    }
                    warm = TextureEffectSurface.GetStatistics().ProcessingPasses;
                    animate = true;
                    for (int i = 0; i < 6; i++)
                    {
                        yield return null;
                    }
                    Assert.That(TextureEffectSurface.GetStatistics().ProcessingPasses, Is.GreaterThan(warm + 1), "固定视频帧不能冻结独立动态光效。");
                    animate = false;
                    for (int i = 0; i < 3; i++)
                    {
                        yield return null;
                    }
                    warm = TextureEffectSurface.GetStatistics().ProcessingPasses;
                }
                image.AddToClassList("hidden");
                for (int i = 0; i < 4; i++)
                {
                    yield return null;
                }
                version++;
                for (int i = 0; i < 6; i++)
                {
                    yield return null;
                }
                Assert.AreEqual(warm, TextureEffectSurface.GetStatistics().ProcessingPasses, "隐藏实例不会刷新");
                var hiddenTime = latestTime;
                image.RemoveFromClassList("hidden");
                for (int i = 0; i < 5; i++)
                {
                    yield return null;
                }
                Assert.AreEqual(warm + 1, TextureEffectSurface.GetStatistics().ProcessingPasses);
                Assert.That(latestTime, Is.GreaterThan(hiddenTime), "恢复使用当前时钟");
                document.Unload();
            }
            finally
            {
                if (pipeline)
                {
                    pipeline.draw = null;
                }
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                binding?.Dispose();
                module?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                surface.Dispose();
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(material));
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
                if (pipeline)
                {
                    UnityEngine.Object.DestroyImmediate(pipeline);
                }
            }
        }
    }
}
