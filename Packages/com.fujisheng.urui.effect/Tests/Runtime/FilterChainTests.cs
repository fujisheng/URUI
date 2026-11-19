using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;
using URUI.Rendering.Effects;
using URUI.Effect.Rendering;
namespace URUI.Effect.Tests
{
    public sealed class FilterChainTests
    {
        [UnityTest]
        public IEnumerator OrderedFiltersMasksOpacityHdrAndOverflow() => Verify(false);
        [UnityTest]
        public IEnumerator OrderedFiltersPreserveSrpIntermediateTarget() => Verify(true);
        static IEnumerator Verify(bool srp)
        {
            var targetsBefore = EffectStatistics.GetTargets();
            var host = new GameObject("Filter chain document");
            var cameraObject = new GameObject("Filter camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = true;
            var target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            mask.SetPixels(new[] { new Color(1, 1, 1, .5f), new Color(1, 1, 1, .5f), new Color(1, 1, 1, .5f), new Color(1, 1, 1, .5f) });
            mask.Apply();
            var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var previous = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = srp ? ScriptableObject.CreateInstance<LayerMaskTests.IntermediatePipelineAsset>() : null;
            var submissions = 0;
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.Images = new[] { new ImageAsset("half-mask.png", mask) };
                EffectHtmlFixtureAssets.Load(document, "URUI.Effect.Tests/FilterChains");
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
                for (int frame = 0; frame < 5; frame++)
                {
                    yield return null;
                }
                if (srp)
                {
                    Assert.That(submissions, Is.GreaterThan(0));
                }
                else
                {
                    camera.Render();
                }
                Color Sample(int x, int y)
                {
                    var old = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(x, target.height - y - 1, 1, 1), 0, 0);
                        read.Apply();
                        var c = read.GetPixel(0, 0);
                        return target.sRGB ? c.linear : c;
                    }
                    finally
                    {
                        RenderTexture.active = old;
                    }
                }
                var reference = Sample(40, 40).r;
                Debug.Log($"Filter probe srp={srp}: ref={reference}, first={Sample(120, 40)}, second={Sample(200, 40)}, half={Sample(280, 40)}, hdr={Sample(360, 40)}, mask={Sample(40, 110)}, blur={Sample(48, 165)}, outside={Sample(52, 165)}, overflow={Sample(65, 225)}, gray={Sample(25, 270)}, shadow={Sample(95, 270)}");
                Assert.That(reference, Is.GreaterThan(.05f), "参考作者子树必须先产生实际背景像素");
                var encodedReference = QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.LinearToGammaSpace(reference) : reference;
                float Css(float encoded) => QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.GammaToLinearSpace(encoded) : encoded;
                Assert.That(Sample(120, 40).r, Is.EqualTo(Css(encodedReference * .25f + .25f)).Within(.025f), "brightness 后 contrast，在 sRGB 运算");
                Assert.That(Sample(200, 40).r, Is.EqualTo(Css(encodedReference * .25f + .125f)).Within(.025f), "contrast 后 brightness，在 sRGB 运算");
                Assert.That(Sample(280, 40).r, Is.EqualTo(.5f).Within(.025f), "继承 opacity 只应用一次");
                Assert.That(Sample(360, 40).r, Is.EqualTo(1).Within(.01f), "标准 brightness 每步截断；HDR 运算由 URUI 自定义效果提供");
                Assert.That(Sample(40, 110).r, Is.EqualTo(Css(.5f) * .25f).Within(.025f), "sRGB 滤镜结果再应用两个半透明 mask");
                Assert.That(Sample(48, 165).a, Is.GreaterThan(.015f), "blur 必须采样最终裁剪外的源图");
                Assert.That(Sample(52, 165).a, Is.LessThan(.005f), "滤镜输出仍遵守祖先裁剪");
                Assert.That(Sample(65, 225).r, Is.EqualTo(Css(.2126f)).Within(.025f), "变换后溢出的子树必须在图层范围内");
                var gray = Sample(25, 270);
                Assert.That(gray.r, Is.EqualTo(gray.g).Within(.01f));
                Assert.That(gray.r, Is.EqualTo(Css(.2126f)).Within(.025f));
                var shadow = Sample(95, 270);
                Assert.That(shadow.r, Is.GreaterThan(.95f));
                Assert.That(shadow.g, Is.LessThan(.02f));
                for (int frame = 0; frame < 5; frame++)
                {
                    yield return null;
                }
                var processingBefore = EffectStatistics.FilterProcessingPasses;
                for (int frame = 0; frame < 120; frame++)
                {
                    yield return null;
                }
                Assert.AreEqual(processingBefore, EffectStatistics.FilterProcessingPasses, "静态子树预热后120帧不能重复滤镜处理。");
                mask.SetPixels(new[] { new Color(1, 1, 1, .25f), new Color(1, 1, 1, .25f), new Color(1, 1, 1, .25f), new Color(1, 1, 1, .25f) });
                mask.Apply();
                for (int frame = 0; frame < 5; frame++)
                {
                    yield return null;
                }
                Assert.That(EffectStatistics.FilterProcessingPasses, Is.GreaterThan(processingBefore), "纹理内容改变必须使图层缓存失效。");
                Assert.That(Sample(40, 110).r, Is.EqualTo(Css(.5f) * .0625f).Within(.012f), "嵌套遮罩更新必须实际绘制。");
                var originalTargets = EffectStatistics.GetTargets();
                document.Q("first").Style.SetWidth(90);
                document.SynchronizeLayout();
                for (int frame = 0; frame < 30 && EffectStatistics.GetTargets().IdleColorBytes <= originalTargets.IdleColorBytes; frame++)
                {
                    yield return null;
                }
                Assert.That(EffectStatistics.GetTargets().IdleColorBytes, Is.GreaterThan(originalTargets.IdleColorBytes), "旧图层必须先经 GPU fence 退役，再进入空闲池");
                var reuseBefore = EffectStatistics.GetTargets().Reuses;
                document.Q("first").Style.SetWidth(50);
                document.SynchronizeLayout();
                Assert.That(EffectStatistics.GetTargets().Reuses, Is.GreaterThan(reuseBefore), "同尺寸和格式的已退役目标应复用");
                document.Unload();
                for (int frame = 0; frame < 30 && EffectStatistics.GetTargets().LeasedTargets > targetsBefore.LeasedTargets; frame++)
                {
                    yield return null;
                }
                Assert.That(EffectStatistics.GetTargets().LeasedTargets, Is.LessThanOrEqualTo(targetsBefore.LeasedTargets), "卸载必须释放全部本批目标租约");
            }
            finally
            {
                if (pipeline)
                {
                    pipeline.draw = null;
                }
                GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                Object.DestroyImmediate(host);
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(read);
                if (pipeline)
                {
                    Object.DestroyImmediate(pipeline);
                }
            }
        }
    }
}
