using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace URUI.Effect.Reference
{
    public sealed class ReferenceHarnessTests
    {
        [Test]
        public void CapturesAllModesAndChangesRealPixels()
        {
            var type = GetType().Assembly.GetType("URUI.Effect.Reference.ReferenceCapture");
            Assert.That(type, Is.Not.Null, "尚未实现原插件效果参考捕获");
            var output = Environment.GetEnvironmentVariable("URUI_EFFECT_REFERENCE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../../../reference"));
            type.GetMethod("CaptureAll", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { output });
            Assert.That(File.ReadAllBytes(Path.Combine(output,"Time.Transition.png")),
                Is.EqualTo(File.ReadAllBytes(Path.Combine(output,"Time.Repeat.png"))),"显式时间参考必须可重复");
            Assert.That(File.ReadAllBytes(Path.Combine(output,"Time.Transition.png")),
                Is.Not.EqualTo(File.ReadAllBytes(Path.Combine(output,"TransitionFilter.Shiny.050.png"))),"显式时间参考必须实际运动");
            Assert.That(File.ReadAllBytes(Path.Combine(output,"BlendType.Additive.050.png")),
                Is.Not.EqualTo(File.ReadAllBytes(Path.Combine(output,"BlendType.AlphaBlend.050.png"))),"Blend 参考必须应用真实序列化模式");
            Assert.That(Directory.GetFiles(output, "*.png").Length, Is.GreaterThan(100));
            foreach (var name in new[] { "TransitionFilter.Melt.050", "ShadowMode.Mirror.050",
                         "GradationMode.AngleGradient.050", "Flip.15.050" })
            {
                Assert.That(File.Exists(Path.Combine(output, name + ".png")), Is.True, name);
            }
            foreach (var name in new[] { "SamplingFilter.Pixelation.000", "SamplingFilter.RgbShift.000",
                         "SamplingFilter.EdgeAlpha.000", "SamplingFilter.BlurDetail.100" })
            { Assert.That(File.Exists(Path.Combine(output, name + ".png")), Is.True, "采样零点/上界参考：" + name); }
            var plain = new Texture2D(2, 2);
            var gray = new Texture2D(2, 2);
            try
            {
                plain.LoadImage(File.ReadAllBytes(Path.Combine(output, "Neutral.png")));
                gray.LoadImage(File.ReadAllBytes(Path.Combine(output, "ToneFilter.Grayscale.050.png")));
                var a = plain.GetPixel(128, 128);
                var b = gray.GetPixel(128, 128);
                Assert.That(a.a, Is.GreaterThan(.1f));
                Assert.That(Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b),
                    Is.GreaterThan(.03f), "参考应执行原插件 Shader，不能只生成相同缩略图");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plain);
                UnityEngine.Object.DestroyImmediate(gray);
            }
        }
    }
}
