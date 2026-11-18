using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Effect.Tests
{
    public sealed class PixelProgramTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DetailMultiplyUsesWhiteWhenNoTextureIsAuthored(bool loadWithoutEffectCssFirst)
        {
            using var fixture = new EffectRenderFixture();
            var pixels = new Color[fixture.Source.width * fixture.Source.height];
            for (var i = 0; i < pixels.Length; i++) { pixels[i] = Color.red; }
            fixture.Source.SetPixels(pixels); fixture.Source.Apply();
            if (loadWithoutEffectCssFirst)
            {
                fixture.Document.LoadHtml("<!doctype html><html><head></head><body></body></html>", "/plain.html");
                fixture.Render();
            }
            fixture.Load("--urui-fx-detail-filter:multiply;");
            var result = fixture.Render()[(255 - 128) * 256 + 128];
            Assert.That(result.r, Is.EqualTo(1).Within(3f / 255));
            Assert.That(result.g, Is.EqualTo(0).Within(3f / 255));
            Assert.That(result.b, Is.EqualTo(0).Within(3f / 255));
            Assert.That(result.a, Is.EqualTo(1).Within(3f / 255));
        }

        [Test]
        public void DifferentPixelAlgorithmsDoNotMutateAnAlreadySharedProgram()
        {
            var cache=typeof(EffectModule).Assembly.GetType("URUI.Effect.Rendering.EffectProgramCache");
            var acquire=cache.GetMethod("Acquire",BindingFlags.Static|BindingFlags.NonPublic);
            IDisposable plain=null,same=null,complex=null;
            Material MaterialOf(IDisposable lease)
            {
                var program=lease.GetType().GetProperty("Program",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(lease);
                return (Material)program.GetType().GetProperty("Material",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(program);
            }
            IDisposable Acquire(bool complexPixel)
            {
                var arguments = new object[]{BlendMode.One,BlendMode.OneMinusSrcAlpha,0,null,null,null,null,complexPixel};
                return (IDisposable)acquire.Invoke(null,arguments);
            }
            try
            {
                plain=Acquire(false);
                same=Acquire(false);
                complex=Acquire(true);
                Assert.That(MaterialOf(same),Is.SameAs(MaterialOf(plain)));
                Assert.That(MaterialOf(complex),Is.Not.SameAs(MaterialOf(plain)),"新增算法不能改写其他录制仍在引用的程序。");
                Assert.That(MaterialOf(plain).IsKeywordEnabled("URUI_COMPLEX_PIXEL"),Is.False);
                Assert.That(MaterialOf(complex).IsKeywordEnabled("URUI_COMPLEX_PIXEL"),Is.True);
            }
            finally{complex?.Dispose();same?.Dispose();plain?.Dispose();}
        }
    }
}
