using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using URUI.Internal.Rendering;

namespace URUI.Tests
{
    public sealed class EffectTargetPoolTests
    {
        static object probe;
        [Test]
        public void SmallDescriptorSurvivesLargeTransientReturns()
        {
            if (SystemInfo.maxTextureSize < 2048)
            {
                Assert.Ignore("Requires 2048-pixel render targets.");
            }
            using var pool = new EffectTargetPool();
            var small = pool.Rent(64, 64, RenderTextureFormat.ARGB32);
            var original = small.Texture;
            small.Dispose();
            for (var index = 0; index < 5; index++)
            {
                using var transient = pool.Rent(2048, 2048 - index, RenderTextureFormat.ARGB32);
            }
            using var reused = pool.Rent(64, 64, RenderTextureFormat.ARGB32);
            Assert.That(reused.Texture, Is.SameAs(original),
                "Large transient descriptors must not discard a much cheaper reusable target first.");
        }

        [Test]
        public void IdleStorageStaysWithinFiftySixMiB()
        {
            if (SystemInfo.maxTextureSize < 2048)
            {
                Assert.Ignore("Requires 2048-pixel render targets.");
            }
            using var pool = new EffectTargetPool();
            var before = EffectTargetPool.Statistics.IdleColorBytes;
            for (var index = 0; index < 5; index++)
            {
                using var target = pool.Rent(2048, 2048 - index, RenderTextureFormat.ARGB32);
            }
            Assert.That(EffectTargetPool.Statistics.IdleColorBytes - before,
                Is.LessThanOrEqualTo(56L * 1024 * 1024));
        }

        [TestCase(RenderTextureFormat.ARGBFloat, 16)]
        [TestCase(RenderTextureFormat.R8, 1)]
        public void ColorStorageUsesActualRenderTargetFormat(RenderTextureFormat format, int bytesPerPixel)
        {
            if (!SystemInfo.SupportsRenderTextureFormat(format))
            {
                Assert.Ignore("Unsupported target format.");
            }
            using var pool = new EffectTargetPool();
            var before = EffectTargetPool.Statistics.LeasedColorBytes;
            using var target = pool.Rent(8, 8, format);
            Assert.That(EffectTargetPool.Statistics.LeasedColorBytes - before,
                Is.EqualTo(8L * 8 * bytesPerPixel));
        }

        [Test]
        public void LargeTargetAndSmallCompanionStillReuseWithinTheOriginalCeiling()
        {
            if (SystemInfo.maxTextureSize < 3840 || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                Assert.Ignore("Requires a 4K half-float target.");
            }
            using var pool = new EffectTargetPool();
            var large = pool.Rent(3840, 2160, RenderTextureFormat.ARGBHalf);
            var original = large.Texture;
            large.Dispose();
            using (var companion = pool.Rent(256, 256, RenderTextureFormat.ARGBHalf))
            {
            }
            using var reused = pool.Rent(3840, 2160, RenderTextureFormat.ARGBHalf);
            Assert.That(reused.Texture, Is.SameAs(original),
                "Reducing ordinary idle storage must not make a large reusable target churn.");
        }

        [Test]
        public void ANewHotDescriptorReplacesColdIdleStorageInsteadOfBeingRecreated()
        {
            if (SystemInfo.maxTextureSize < 4096)
            {
                Assert.Ignore("此容量回归需要可创建4096像素的纹理。");
            }
            using var pool = new EffectTargetPool();
            var baseline = EffectTargetPool.Statistics;
            var cold = pool.Rent(4096, 4096, RenderTextureFormat.ARGB32);
            var coldTexture = cold.Texture;
            cold.Dispose();
            var hot = pool.Rent(64, 64, RenderTextureFormat.ARGB32);
            var hotTexture = hot.Texture;
            hot.Dispose();
            using var next = pool.Rent(64, 64, RenderTextureFormat.ARGB32);
            Assert.That(next.Texture, Is.SameAs(hotTexture), "新热尺寸不能被永久占满缓存的冷纹理挤掉。");
            Assert.That(EffectTargetPool.Statistics.CreatedTargets - baseline.CreatedTargets, Is.EqualTo(2));
            Assert.That(EffectTargetPool.Statistics.LiveColorBytes - baseline.LiveColorBytes, Is.EqualTo(16384), "未使用的64MiB冷负载应释放，只保留热目标。");
            Assert.That(coldTexture && coldTexture.IsCreated(), Is.False);
        }
        [Test]
        public void BorrowingAnIdleDescriptorProtectsItFromOlderDescriptors()
        {
            if (SystemInfo.maxTextureSize < 4096)
            {
                Assert.Ignore("此容量回归需要可创建4096像素的纹理。");
            }
            using var pool = new EffectTargetPool();
            // Seven equal 8MiB descriptors fill the budget before the recency test.
            var a = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32);
            var textureA = a.Texture;
            var b = pool.Rent(4096, 512, RenderTextureFormat.ARGB32);
            var textureB = b.Texture;
            var c = pool.Rent(1024, 2048, RenderTextureFormat.ARGB32);
            var textureC = c.Texture;
            var d = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32, true);
            var textureD = d.Texture;
            var e = pool.Rent(4096, 512, RenderTextureFormat.ARGB32, true);
            var f = pool.Rent(1024, 2048, RenderTextureFormat.ARGB32, true);
            var g = pool.Rent(512, 4096, RenderTextureFormat.ARGB32);
            a.Dispose();
            b.Dispose();
            c.Dispose();
            d.Dispose();
            e.Dispose();
            f.Dispose();
            g.Dispose();
            using (var touched = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32))
            {
                Assert.That(touched.Texture, Is.SameAs(textureA));
            }
            var incoming = pool.Rent(512, 4096, RenderTextureFormat.ARGB32, true);
            var textureIncoming = incoming.Texture;
            incoming.Dispose();
            using var nextA = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32);
            using var nextC = pool.Rent(1024, 2048, RenderTextureFormat.ARGB32);
            using var nextD = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32, true);
            using var nextIncoming = pool.Rent(512, 4096, RenderTextureFormat.ARGB32, true);
            Assert.That(nextA.Texture, Is.SameAs(textureA));
            Assert.That(nextC.Texture, Is.SameAs(textureC));
            Assert.That(nextD.Texture, Is.SameAs(textureD));
            Assert.That(nextIncoming.Texture, Is.SameAs(textureIncoming));
            Assert.That(textureB && textureB.IsCreated(), Is.False, "应淘汰最久未借还的尺寸，而不是刚使用过的尺寸。");
        }
        [Test]
        public void CapacityPressurePreservesTheOtherTargetsInTheSameDescriptor()
        {
            using var pool = new EffectTargetPool();
            var leases = new EffectTargetPool.Lease[17];
            for (var i = 0; i < leases.Length; i++)
            {
                leases[i] = pool.Rent(1024, 1024, RenderTextureFormat.ARGB32);
            }
            foreach (var lease in leases)
            {
                lease.Dispose();
            }
            var created = EffectTargetPool.Statistics.CreatedTargets;
            try
            {
                for (var i = 0; i < 14; i++)
                {
                    leases[i] = pool.Rent(1024, 1024, RenderTextureFormat.ARGB32);
                }
                Assert.That(EffectTargetPool.Statistics.CreatedTargets, Is.EqualTo(created), "归还一个额外目标不得淘汰同尺寸的整组空闲目标。");
            }
            finally
            {
                for (var i = 0; i < 14; i++)
                {
                    leases[i]?.Dispose();
                }
            }
        }
        [Test]
        public void ReturningADescriptorProtectsItsOtherIdleTargetsBeforeEviction()
        {
            if (SystemInfo.maxTextureSize < 4096)
            {
                Assert.Ignore("此容量回归需要可创建4096像素的纹理。");
            }
            using var pool = new EffectTargetPool();
            var first = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32);
            var firstTexture = first.Texture;
            var returning = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32);
            var returningTexture = returning.Texture;
            var cold = pool.Rent(4096, 512, RenderTextureFormat.ARGB32);
            var coldTexture = cold.Texture;
            var others = new[]{pool.Rent(1024,2048,RenderTextureFormat.ARGB32),
                pool.Rent(2048,1024,RenderTextureFormat.ARGB32,true),
                pool.Rent(4096,512,RenderTextureFormat.ARGB32,true),
                pool.Rent(1024,2048,RenderTextureFormat.ARGB32,true),
                pool.Rent(512,4096,RenderTextureFormat.ARGB32)};
            first.Dispose();
            cold.Dispose();
            foreach (var other in others)
            {
                other.Dispose();
            }
            returning.Dispose();
            using var a = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32);
            using var b = pool.Rent(2048, 1024, RenderTextureFormat.ARGB32);
            CollectionAssert.AreEquivalent(new[] { firstTexture, returningTexture }, new[] { a.Texture, b.Texture }, "归还刚使用的描述符时，应先更新其时序，保留同组的另一个空闲目标。");
            Assert.That(coldTexture && coldTexture.IsCreated(), Is.False);
        }
        [Test]
        public void OversizedReturnsDoNotEvictReusableSmallerTargets()
        {
            if (SystemInfo.maxTextureSize < 8192)
            {
                Assert.Ignore("此超预算单体回归需要可创建8192像素的纹理。");
            }
            using var pool = new EffectTargetPool();
            var small = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
            var texture = small.Texture;
            small.Dispose();
            var large = pool.Rent(8192, 4096, RenderTextureFormat.ARGB32);
            large.Dispose();
            using var next = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
            Assert.That(next.Texture, Is.SameAs(texture), "超过整个空闲预算的单个目标不能先清空已有热缓存。");
        }
        [Test]
        public void AHotDescriptorCanReplaceColdEntriesAtTheObjectAndKeyLimit()
        {
            if (SystemInfo.maxTextureSize < 2049)
            {
                Assert.Ignore("此尺寸键容量回归需要可创建2049像素的纹理。");
            }
            using var pool = new EffectTargetPool();
            var baseline = EffectTargetPool.Statistics;
            for (var width = 1; width <= 2048; width++)
            {
                using var item = pool.Rent(width, 1, RenderTextureFormat.ARGB32);
            }
            var hot = pool.Rent(2049, 1, RenderTextureFormat.ARGB32);
            var texture = hot.Texture;
            hot.Dispose();
            var created = EffectTargetPool.Statistics.CreatedTargets;
            for (var iteration = 0; iteration < 32; iteration++)
            {
                using var next = pool.Rent(2049, 1, RenderTextureFormat.ARGB32);
                Assert.That(next.Texture, Is.SameAs(texture));
            }
            Assert.That(EffectTargetPool.Statistics.CreatedTargets, Is.EqualTo(created));
            Assert.That(EffectTargetPool.Statistics.LiveTargets - baseline.LiveTargets, Is.EqualTo(2048));
            Assert.That(EffectTargetPool.Statistics.IdleColorBytes - baseline.IdleColorBytes, Is.LessThanOrEqualTo(56L * 1024 * 1024));
        }
        [Test]
        public void RetiredTargetsWithSubmissionReferencesAreNotEvictionCandidates()
        {
            if (SystemInfo.maxTextureSize < 4096)
            {
                Assert.Ignore("此容量回归需要可创建4096像素的纹理。");
            }
            using var pool = new EffectTargetPool();
            var retained = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
            var retainedTexture = retained.Texture;
            retained.RetainReference();
            retained.Dispose();
            try
            {
                var cold = pool.Rent(4096, 4096, RenderTextureFormat.ARGB32);
                cold.Dispose();
                var hot = pool.Rent(64, 64, RenderTextureFormat.ARGB32);
                hot.Dispose();
                Assert.That(retained.Texture, Is.SameAs(retainedTexture));
                Assert.That(retainedTexture.IsCreated(), Is.True);
            }
            finally
            {
                retained.ReleaseReference();
            }
        }
        [Test]
        public void ConcurrentTargetsHaveDistinctStableDebuggerNames()
        {
            using var pool = new EffectTargetPool();
            using var first = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
            using var second = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
            var a = first.Texture.name;
            var b = second.Texture.name;
            Assert.That(a, Is.Not.EqualTo(b), "Frame Debugger 必须能区分同尺寸的实际目标。");
            first.Dispose();
            second.Dispose();
            using var next = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
            using var last = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
            CollectionAssert.AreEquivalent(new[] { a, b }, new[] { next.Texture.name, last.Texture.name });
        }
        [Test]
        public void AFullSubmissionWindowReusesItsTargetsAfterWarmup()
        {
            using var pool = new EffectTargetPool();
            var leases = new EffectTargetPool.Lease[9];
            for (var cycle = 0; cycle < 300; cycle++)
            {
                for (var i = 0; i < leases.Length; i++)
                {
                    leases[i] = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
                }
                foreach (var lease in leases)
                {
                    lease.Dispose();
                }
            }
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (var cycle = 0; cycle < 32; cycle++)
            {
                for (var i = 0; i < leases.Length; i++)
                {
                    leases[i] = pool.Rent(8, 8, RenderTextureFormat.ARGB32);
                }
                foreach (var lease in leases)
                {
                    lease.Dispose();
                }
            }
            recorder.Stop();
            long count = 0;
            for (var i = 0; i < recorder.Count; i++)
            {
                count += recorder.GetSample(i).Count;
            }
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            Assert.That(count, Is.Zero, "缓存要覆盖完整在途窗口，不能在四个空闲目标后反复销毁重建。");
        }
        [Test]
        public void StableTargetRentsDoNotAllocateAfterWarmup()
        {
            using var pool = new EffectTargetPool();
            for (var i = 0; i < 300; i++)
            {
                using var lease = pool.Rent(16, 16, RenderTextureFormat.ARGB32);
            }
            using (var positive = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                probe = new byte[4096];
                System.GC.KeepAlive(probe);
                positive.Stop();
                Assert.That(positive.Valid && positive.Count > 0, Is.True);
                probe = null;
            }
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for (var i = 0; i < 32; i++)
            {
                using var lease = pool.Rent(16, 16, RenderTextureFormat.ARGB32);
            }
            recorder.Stop();
            long count = 0;
            for (var i = 0; i < recorder.Count; i++)
            {
                count += recorder.GetSample(i).Count;
            }
            Assert.That(recorder.Valid && !recorder.WrappedAround, Is.True);
            Assert.That(count, Is.Zero);
        }
    }
}
