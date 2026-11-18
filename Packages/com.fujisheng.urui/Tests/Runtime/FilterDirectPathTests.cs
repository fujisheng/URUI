using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Rendering.Effects;
using URUI.Resources;

namespace URUI.Tests
{
    public sealed class FilterDirectPathTests
    {
        [System.Serializable] sealed class StructureCapture
        {
            public int baselineTargets, filteredTargets;
            public long baselinePasses, filteredPasses;
        }
        [UnityTest]
        public IEnumerator FilterFusionPreservesOrderAndClamping()
        {
            var host = new GameObject("CSS color fusion");
            var cameraObject = new GameObject("CSS filter camera", typeof(Camera)); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>(); camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(160, 48, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create(); camera.targetTexture = target;
            var source = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
            source.SetPixel(0, 0, new Color32(204, 51, 153, 128)); source.Apply();
            var read = new Texture2D(160, 48, TextureFormat.RGBAFloat, false, true);
            try
            {
                var surface = host.AddComponent<Surface>(); surface.Viewport = new Vector2Int(160, 48); surface.Input.UseUnityInput = false;
                var document = host.AddComponent<Document>(); document.Surface = surface;
                document.Images = new[] { new ImageAsset("CssFilterDirectInput.png", source) };
                document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/CssFilterDirect"), "/CssFilterDirect.html");
                document.Body.RemoveFromClassList("filtered");
                for (var index = 0; index < 3; index++) { yield return null; }
                document.SynchronizeLayout();
                var beforePasses = EffectStatistics.FilterProcessingPasses; camera.Render();
                var baselinePasses = EffectStatistics.FilterProcessingPasses - beforePasses;
                var baselineTargets = EffectStatistics.GetTargets().LeasedTargets;
                document.Body.AddToClassList("filtered"); document.SynchronizeLayout();
                // 结构变化后的首帧提交新的缓存依赖；比较相同稳定状态下的处理量。
                camera.Render(); document.SynchronizeLayout();
                beforePasses = EffectStatistics.FilterProcessingPasses; camera.Render();
                var structure = new StructureCapture { baselineTargets = baselineTargets,
                    filteredTargets = EffectStatistics.GetTargets().LeasedTargets, baselinePasses = baselinePasses,
                    filteredPasses = EffectStatistics.FilterProcessingPasses - beforePasses };
                Assert.That(EffectStatistics.GetTargets().LeasedTargets, Is.EqualTo(baselineTargets), "简单图片颜色链不能增加 RT 租约");
                Assert.That(EffectStatistics.FilterProcessingPasses - beforePasses, Is.LessThanOrEqualTo(baselinePasses), "新增三条图片滤镜链不能增加处理 Pass；已有子层可在 fence 完成后命中缓存");
                Capture();
                var alpha = 128f / 255;
                AssertPixel(16, new Color(.75f, .45f, .75f) * (alpha * .5f));
                AssertPixel(56, new Color(1, .7f, 1) * (alpha * .5f));
                var gray = (.2126f * .8f + .7152f * .2f + .0722f * .6f) * .5f * alpha;
                AssertPixel(96, new Color(gray, gray, gray));
                AssertPixel(136, new Color(.75f, .45f, .75f) * (alpha * .5f));
                var captureRoot = System.Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT") ??
                    Path.GetFullPath(Path.Combine(Application.dataPath, "../../../candidate"));
                var output = Path.Combine(captureRoot, QualitySettings.activeColorSpace.ToString());
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "css-structure.json"), JsonUtility.ToJson(structure, true));
                // 浏览器截图使用 sRGB；浮点读回值单独归一化用于同一表示的比较。
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    var pixels = read.GetPixels();
                    for (var index = 0; index < pixels.Length; index++) { pixels[index] = pixels[index].gamma; }
                    read.SetPixels(pixels); read.Apply();
                }
                File.WriteAllBytes(Path.Combine(output, "CssFilterDirect.png"), read.EncodeToPNG());
                document.Q("fallback-input").Style.Set("filter", "brightness(.5)"); document.SynchronizeLayout(); camera.Render(); Capture();
                AssertPixel(136, new Color(.65f, .35f, .55f) * (alpha * .5f));
                document.SynchronizeLayout(); camera.Render();
                document.Q("fallback-input").Style.Set("filter", "brightness(.25)"); document.SynchronizeLayout(); camera.Render(); Capture();
                AssertPixel(136, new Color(.45f, .3f, .4f) * (alpha * .5f));
                void Capture()
                {
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 160, 48), 0, 0); read.Apply(); }
                    finally { RenderTexture.active = previous; }
                }
                void AssertPixel(int x, Color encoded)
                {
                    var expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? encoded.linear : encoded;
                    var actual = read.GetPixel(x, 31);
                    for (var channel = 0; channel < 3; channel++) { Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(2f / 255), "x=" + x + " channel=" + channel); }
                    Assert.That(actual.a, Is.EqualTo(1).Within(.002f));
                }
            }
            finally
            {
                camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(read);
                GpuRetirement.Retire(() => UnityEngine.Object.Destroy(source));
            }
        }
    }
}
