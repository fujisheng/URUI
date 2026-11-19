using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using URUI.Documents;

namespace URUI.Effect.Tests
{
    public sealed class BinaryRectangleMaskTests
    {
        [UnityTest]
        public IEnumerator TerminalRectangleKeepsIsolatedAlphaAndDynamicCoverage() => Verify(false);

        [UnityTest]
        public IEnumerator TerminalRectangleKeepsSrpTargetAndParentScope() => Verify(true);

        static IEnumerator Verify(bool srp)
        {
            if (srp && (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            {
                Assert.Ignore("别名目标测试需要同格式 RT 复制能力。");
            }
            var host = new GameObject("Binary mask pixel document");
            var cameraObject = new GameObject("Binary mask camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            var target = new RenderTexture(320, 128, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            var read = new Texture2D(320, 128, TextureFormat.RGBAFloat, false, true);
            var previousPipeline = GraphicsSettings.defaultRenderPipeline;
            var previousQuality = QualitySettings.renderPipeline;
            var pipeline = srp ? ScriptableObject.CreateInstance<LayerMaskTests.IntermediatePipelineAsset>() : null;
            var submissions = 0;
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.Surface.Viewport = new Vector2Int(320, 128);
                document.Surface.Input.UseUnityInput = false;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}" +
                    "#outer{width:256px;height:128px;opacity:.8;}" +
                    ".mask{position:absolute;top:16px;width:64px;height:64px;transform-origin:0 0;--urui-mask-image:shader(feather-mask);}" +
                    "#actual{left:16px;}#reference{left:144px;}" +
                    ".red,.green{position:absolute;left:-24px;top:-8px;width:112px;height:96px;}" +
                    ".red{background:rgba(255,0,0,.5);}.green{left:8px;background:rgba(0,255,0,.5);}" +
                    "#sibling{position:absolute;left:280px;top:24px;width:16px;height:32px;background:#0f0;}" +
                    "</style></head><body><div id='outer'><div id='actual' class='mask'><div class='red'></div><div class='green'></div></div>" +
                    "<div id='reference' class='mask'><div class='red'></div><div class='green'></div></div></div><div id='sibling'></div></body></html>", "/binary-mask.html");
                var actual = document.Q("actual");
                var reference = document.Q("reference");
                using var actualPainter = new FeatherMaskPainter(actual);
                using var referencePainter = new FeatherMaskPainter(reference);
                if (srp)
                {
                    var alias = Shader.PropertyToID("_URUIBinaryMaskTestAlias");
                    pipeline.draw = context =>
                    {
                        context.SetupCameraProperties(camera);
                        using var setup = new CommandBuffer();
                        setup.GetTemporaryRT(alias, target.descriptor);
                        setup.SetRenderTarget(alias);
                        setup.ClearRenderTarget(false, true, Color.blue);
                        context.ExecuteCommandBuffer(setup);
                        if (document.SubmitRendering(context, new RenderTargetIdentifier(alias), new Rect(0, 0, 320, 128), URUI.Rendering.RenderTargetInfo.FromTexture(target)))
                        {
                            submissions++;
                        }
                        using var present = new CommandBuffer();
                        present.CopyTexture(new RenderTargetIdentifier(alias), new RenderTargetIdentifier(target));
                        present.ReleaseTemporaryRT(alias);
                        context.ExecuteCommandBuffer(present);
                    };
                    QualitySettings.renderPipeline = null;
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                }
                for (var phase = 0; phase < 5; phase++)
                {
                    camera.allowHDR = phase != 4;
                    if (phase == 2)
                    {
                        camera.targetTexture = null;
                        target.Release();
                        Object.DestroyImmediate(target);
                        target = new RenderTexture(320, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                        target.Create();
                        camera.targetTexture = target;
                    }
                    var feather = phase == 1 ? "12px" : "0px";
                    actual.Style.Set("--urui-mask-feather-top", feather);
                    // 正的极小羽化不满足二值证明；所采样网格内的距离均远大于此值，保留独立旧路径参考。
                    reference.Style.Set("--urui-mask-feather-top", phase == 1 ? "12px" : ".00001px");
                    actual.Style.Set("opacity", phase == 3 ? ".6" : "1");
                    reference.Style.Set("opacity", phase == 3 ? ".6" : "1");
                    var scale = phase == 2 ? 1.125f : 1f;
                    var offset = phase == 3 ? new Vector2(.25f, .25f) : phase == 4 ? new Vector2(64.25f, .25f) : Vector2.zero;
                    var axes = phase == 4 ? new Vector2(-1, 1) : Vector2.one * scale;
                    actual.SetTransform(offset, axes);
                    reference.SetTransform(offset, axes);
                    document.Q("outer").Style.Set("opacity", phase < 2 ? ".8" : "1");
                    document.SynchronizeLayout();
                    for (var frame = 0; frame < 5; frame++)
                    {
                        yield return null;
                    }
                    if (!srp)
                    {
                        camera.Render();
                    }
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, 320, 128), 0, 0);
                        read.Apply();
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                    }
                    Assert.That(read.GetPixel(48, 128 - 48 - 1).g, Is.GreaterThan(.02f), "阳性像素必须证明重叠子图元实际绘制。");
                    Assert.That(read.GetPixel(288, 128 - 32 - 1).g, Is.GreaterThan(.95f), "后续兄弟必须恢复父目标和裁剪。");
                    if (phase == 2)
                    {
                        Assert.That(URUI.Rendering.Effects.EffectStatistics.GetTargets().LeasedTargets, Is.LessThanOrEqualTo(6),
                            "源层1 + 旧路径参考3 + 根编码1；允许1份退役重叠，已知别名像素域不能退回三目标遮罩。");
                    }
                    for (var y = 4; y < 112; y++)
                    {
                        for (var x = 4; x < 112; x++)
                        {
                            var value = read.GetPixel(x, 128 - y - 1);
                            var expected = read.GetPixel(x + 128, 128 - y - 1);
                            var error = Mathf.Max(Mathf.Abs(value.r - expected.r), Mathf.Abs(value.g - expected.g), Mathf.Abs(value.b - expected.b), Mathf.Abs(value.a - expected.a));
                            Assert.That(error, Is.LessThanOrEqualTo(2f / 255), $"phase={phase}, srp={srp}, pixel=({x},{y})");
                        }
                    }
                }
                if (srp)
                {
                    Assert.That(submissions, Is.GreaterThan(0));
                }
            }
            finally
            {
                if (pipeline)
                {
                    pipeline.draw = null;
                }
                GraphicsSettings.defaultRenderPipeline = previousPipeline;
                QualitySettings.renderPipeline = previousQuality;
                Object.DestroyImmediate(host);
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
                if (pipeline)
                {
                    Object.DestroyImmediate(pipeline);
                }
            }
        }
    }
}
