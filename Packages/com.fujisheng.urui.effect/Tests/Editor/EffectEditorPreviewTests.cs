using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Resources;
namespace URUI.Effect.Editor.Tests
{
    public sealed class EffectEditorPreviewTests
    {
        [UnityTest]
        public IEnumerator ProductionPreviewWorksWithoutAnActiveSurfaceAndRetiresItsResources()
        {
            var target = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            var read = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            var retired = false;
            try
            {
                using (var preview = new EffectPreview(new EffectParameters
                {
                    ColorFilter = ColorFilter.Replace,
                    Color = Color.red,
                    GradationMode = GradationMode.HorizontalGradient
                }))
                {
                    preview.Render(Texture2D.whiteTexture, new Rect(16, 16, 32, 32), .25, target, Color.clear);
                }
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    read.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                Assert.That(read.GetPixel(32, 32).r, Is.GreaterThan(.99));
                Assert.That(read.GetPixel(32, 32).g, Is.LessThan(.01));
                GpuRetirement.Retire(() => retired = true);
                for (var i = 0; i < 120 && !retired; i++)
                {
                    GpuRetirement.Collect();
                    yield return null;
                }
                Assert.That(retired, Is.True, "EditMode 不依赖 MonoBehaviour.Update 也必须回收已完成的 GPU 工作");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(read);
            }
        }
    }
}
