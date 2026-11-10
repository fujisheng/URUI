using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Resources;
namespace URUI.Effect.Editor.Tests
{
    public sealed class EffectEditModeLifetimeTests
    {
        [UnityTest]
        public IEnumerator RetiredTextureSurfaceReleasesOwnedObjectsOutsidePlayMode()
        {
            Assert.IsFalse(Application.isPlaying);
            var surface = new TextureEffectSurface(16, 16);
            var output = surface.Texture;
            surface.Dispose();
            for (var frame = 0; frame < 120 && output; frame++)
            {
                GpuRetirement.Collect();
                yield return null;
            }
            Assert.IsFalse(output, "GPU 退役后须释放输出纹理，编辑模式不能残留或调用 Destroy。");
        }
    }
}
