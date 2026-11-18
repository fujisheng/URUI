using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Effect.Rendering;

namespace URUI.Effect.Tests
{
    public sealed class BorderImageEffectTests
    {
        sealed class CountingFactory : global::URUI.Rendering.Effects.IEffectFactory
        {
            internal int Created, Disposed;
            public global::URUI.Rendering.Effects.IElementEffect Create(global::URUI.Rendering.Effects.EffectContext context)
            { Created++; return new Instance(this); }
            sealed class Instance : global::URUI.Rendering.Effects.IElementEffect
            {
                readonly CountingFactory owner;
                internal Instance(CountingFactory owner) { this.owner = owner; }
                public void Prepare(global::URUI.Rendering.Effects.EffectFrame frame) { }
                public void Record(global::URUI.Rendering.Effects.EffectRenderContext context) { context.DrawOriginal(); }
                public void Dispose() { owner.Disposed++; }
            }
        }

        [UnityTest]
        public IEnumerator GeometryAnimationRetainsEachImageEffectInstance()
        {
            using var fixture = new EffectRenderFixture();
            var factory = new CountingFactory();
            fixture.Document.Effects.Register(new global::URUI.Rendering.Effects.EffectDefinition(
                "count", global::URUI.Rendering.Effects.EffectKind.Image, factory, "count:v1", "test"));
            foreach (var border in new[] { "", "border:12px solid transparent;border-image:url(input.png) 2 / 12px stretch;" })
            {
                fixture.Load("--urui-effect:none;--urui-image-effect:count;" + border);
                fixture.Render();
                yield return null;
                var created = factory.Created;
                for (var i = 0; i < 6; i++)
                {
                    fixture.Document.Q("subject").Style.Set("width", (128 + i).ToString() + "px");
                    fixture.Render();
                    yield return null;
                }
                Assert.That(factory.Created, Is.EqualTo(created), "几何重建必须复用每个绘制槽的效果实例。");
            }
            fixture.Document.Unload();
            for (var i = 0; i < 120 && factory.Disposed != factory.Created; i++)
            { global::URUI.Resources.GpuRetirement.Collect(); yield return null; }
            Assert.That(factory.Disposed, Is.EqualTo(factory.Created));
        }

        [UnityTest]
        public IEnumerator ContentAndNinePatchKeepIndependentEffectInputs()
        {
            using var fixture = new EffectRenderFixture();
            var border = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            try
            {
                var pixels = new Color[64];
                for (var i = 0; i < pixels.Length; i++) { pixels[i] = Color.green; }
                border.SetPixels(pixels); border.Apply();
                fixture.Document.Images = new[] { new global::URUI.Resources.ImageAsset("input.png", fixture.Source),
                    new global::URUI.Resources.ImageAsset("border.png", border) };
                material.SetVector("_BandColor", Vector4.one);
                material.SetFloat("_Width", 1);
                fixture.Document.Effects.Register(TextureColorEffect.Define("identity", material));
                const string css = "--urui-effect:none;border:12px solid transparent;border-image:url(border.png) 2 / 12px stretch;";
                fixture.Load(css);
                var expected = fixture.Render();
                fixture.Document.Q("subject").Style.Set("--urui-image-effect", "identity");
                var actual = fixture.Render();
                var maximum = 0f;
                for (var i = 0; i < actual.Length; i++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    { maximum = Mathf.Max(maximum, Mathf.Abs(expected[i][channel] - actual[i][channel])); }
                }
                // 调色shader和普通图片路径在Linear色彩转换时有小于2/255的舍入差。
                Assert.That(maximum, Is.LessThan(2f / 255), "内容图和边框必须保留各自的纹理及几何输入。");
                fixture.Document.Q("subject").Style.Set("--urui-image-effect", "none");
                fixture.Render();
                fixture.Document.Unload();
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(border); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PatternUsesSurfaceCoordinatesAcrossNinePatchSlices()
        {
            using var fixture = new EffectRenderFixture();
            var pixels = new Color[fixture.Source.width * fixture.Source.height];
            for (var i = 0; i < pixels.Length; i++) { pixels[i] = Color.white; }
            fixture.Source.SetPixels(pixels); fixture.Source.Apply();
            var material = new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            try
            {
                material.SetTexture("_PatternTex", fixture.Pattern);
                material.SetFloat("_FilterMode", 1);
                material.SetFloat("_Width", 1);
                material.SetFloat("_ColorBlend", 0);
                material.SetVector("_BandColor", new Vector4(0, 0, 0, .6f));
                fixture.Document.Effects.Register(TextureColorEffect.Define("pattern", material));
                const string effect = "--urui-effect:none;--urui-image-effect:pattern;";
                fixture.Load(effect);
                var expected = fixture.Render();
                fixture.Load(effect + "border-image:url(input.png) 2 fill / 12px stretch;", "<span id='subject'></span>");
                var actual = fixture.Render();
                var maximum = 0f;
                for (var y = 67; y < 189; y++)
                {
                    for (var x = 67; x < 189; x++)
                    {
                        var index = y * 256 + x;
                        maximum = Mathf.Max(maximum, Mathf.Abs(expected[index].r - actual[index].r));
                    }
                }
                Assert.That(maximum, Is.LessThan(.005f), "切片纹理UV不能拉伸或压缩效果图案。");
                fixture.Document.Q("subject").Style.Set("--urui-image-effect", "none");
                var plain = fixture.Render();
                var difference = 0f;
                for (var i = 0; i < plain.Length; i++) { difference = Mathf.Max(difference, Mathf.Abs(plain[i].r - actual[i].r)); }
                Assert.That(difference, Is.GreaterThan(.1f), "九宫格实际绘制必须应用效果。");
            }
            finally { Object.DestroyImmediate(material); }
            yield return null;
        }
    }
}
