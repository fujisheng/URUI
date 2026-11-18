using System;
using NUnit.Framework;
using UnityEngine.Rendering;
using URUI.Rendering;

namespace URUI.Tests
{
    public sealed class RenderProgramDescriptorTests
    {
        static RenderProgramDescriptor Program(RenderColorSpace computation, RenderColorSpace output, RenderBlendDomain blend)
            => new RenderProgramDescriptor(RenderTextureEncoding.TextureDeclared, computation, output,
                RenderAlpha.Premultiplied, blend, BlendMode.One, BlendMode.OneMinusSrcAlpha,
                RenderTargetCapability.Hdr, RenderSampling.BilinearClamp, RenderGeometry.Local, RenderTimeInput.UiClock);

        [Test]
        public void RasterSharingRequiresAllColorAndBlendStagesToMatch()
        {
            var encoded = Program(RenderColorSpace.EncodedSrgb, RenderColorSpace.EncodedSrgb, RenderBlendDomain.EncodedSrgb);
            var linearCompute = Program(RenderColorSpace.Linear, RenderColorSpace.EncodedSrgb, RenderBlendDomain.EncodedSrgb);
            var linearBlend = Program(RenderColorSpace.EncodedSrgb, RenderColorSpace.EncodedSrgb, RenderBlendDomain.Linear);
            Assert.That(encoded.CanShareRaster(encoded), Is.True);
            Assert.That(encoded.CanShareRaster(linearCompute), Is.False, "输出编码相同不能掩盖计算空间不同。");
            Assert.That(encoded.CanShareRaster(linearBlend), Is.False, "输出编码相同不能掩盖硬件混合空间不同。");
            Assert.That(default(RenderProgramDescriptor).CanShareRaster(default), Is.False, "未知用户程序保持明确的外部边界。");
        }
        [Test]
        public void UnknownAndInvalidContractsCannotBecomeKnownPrograms()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Program((RenderColorSpace)99, RenderColorSpace.Linear, RenderBlendDomain.Linear));
            Assert.Throws<ArgumentOutOfRangeException>(() => Program(RenderColorSpace.Unknown, RenderColorSpace.Linear, RenderBlendDomain.Linear));
            var program = Program(RenderColorSpace.Active, RenderColorSpace.Active, RenderBlendDomain.Active);
            Assert.That(program.IsKnown, Is.True); Assert.That(program.Geometry, Is.EqualTo(RenderGeometry.Local));
        }
    }
}
