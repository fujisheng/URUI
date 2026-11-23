using NUnit.Framework;
using UnityEngine;
using URUI.Resources;
using URUI.Internal.Resources;

namespace URUI.Tests
{
    public sealed class BorrowedTexturePinsTests
    {
        [Test]
        public void FutureSubmissionsDoNotRepinNativeReleasedImages()
        {
            var texture = new Texture2D(1, 1);
            var registry = new BorrowedTextureRegistry();
            var releases = 0;
            var first = new BorrowedTextureRegistry.SubmissionPins();
            var second = new BorrowedTextureRegistry.SubmissionPins();
            try
            {
                var id = registry.RegisterResolved(new ImageLease(texture, () => releases++), out _);
                first.Acquire(registry);
                registry.ReleaseResolved(id);
                Assert.That(releases, Is.Zero, "已有 GPU 消费者仍持有图片。");
                second.Acquire(registry);
                first.Release();
                Assert.That(releases, Is.EqualTo(1), "未来提交不能再次保留已被原生释放的图片。");
                second.Release();
                Assert.That(releases, Is.EqualTo(1));
            }
            finally
            {
                first.Release();
                second.Release();
                registry.Clear();
                Object.DestroyImmediate(texture);
            }
        }
    }
}
