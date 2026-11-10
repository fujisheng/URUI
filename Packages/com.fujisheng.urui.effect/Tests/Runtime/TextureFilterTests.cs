using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace URUI.Effect.Tests
{
    public sealed class TextureFilterTests
    {
        [UnityTest]
        public IEnumerator TransparentSheenRetainsBrightnessUntilAlphaCompositing()
        {
            var input=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            input.SetPixel(0,0,new Color(1,1,1,.25f));input.Apply();
            var pattern=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            pattern.SetPixel(0,0,new Color(1,1,1,.5f));pattern.Apply();
            var material=new Material(Shader.Find("URUI.Effect/TextureFilter"));
            var pixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            using var surface=new TextureEffectSurface(8,8);
            try
            {
                material.SetTexture("_PatternTex",pattern);material.SetFloat("_FilterMode",2);
                material.SetFloat("_ColorBlend",2);material.SetFloat("_Width",1);material.SetFloat("_Softness",1);material.SetFloat("_Rate",.5f);
                material.SetColor("_BandColor",new Color(.5f,.5f,.5f,1));
                surface.Render(input,material,new Vector2(100,100));
                var previous=RenderTexture.active;
                try{RenderTexture.active=surface.Texture;pixel.ReadPixels(new Rect(4,4,1,1),0,0);pixel.Apply();}
                finally{RenderTexture.active=previous;}
                var actual=pixel.GetPixel(0,0);if(surface.Texture.sRGB){actual=actual.linear;}
                // 原 UI shader 在增亮后再应用透明度；白色半强度扫光对黑底贡献约 1.25 × .25。
                Assert.That(actual.r*actual.a,Is.EqualTo(.3125f).Within(.012f));
                Assert.That(actual.a,Is.EqualTo(.25f).Within(.01f));
                material.SetFloat("_BandSourceAlpha",1);
                surface.Render(input,material,new Vector2(100,100));
                previous=RenderTexture.active;
                try{RenderTexture.active=surface.Texture;pixel.ReadPixels(new Rect(4,4,1,1),0,0);pixel.Apply();}
                finally{RenderTexture.active=previous;}
                actual=pixel.GetPixel(0,0);if(surface.Texture.sRGB){actual=actual.linear;}
                // 不透明度加权的源参与色带混合：.25 × lerp(1,1.5 × .25,.5)。
                Assert.That(actual.r*actual.a,Is.EqualTo(.171875f).Within(.012f));
            }
            finally{Object.DestroyImmediate(input);Object.DestroyImmediate(pattern);Object.DestroyImmediate(material);Object.DestroyImmediate(pixel);}
            yield return null;
        }

        [UnityTest]
        public IEnumerator PatternAndSheenChangeOnlyRgbAndPreserveSourceAlpha()
        {
            var input = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            input.SetPixel(0, 0, new Color(.25f, .25f, .25f, .75f)); input.Apply();
            var pattern = new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            pattern.SetPixels(new[] { new Color(1, 1, 1, 0), Color.white }); pattern.Apply();
            var material = new Material(Shader.Find("URUI.Effect/TextureFilter"));
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            using var surface = new TextureEffectSurface(8, 8);
            try
            {
                Color Sample(int x)
                {
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = surface.Texture; pixel.ReadPixels(new Rect(x, 4, 1, 1), 0, 0); pixel.Apply(); return pixel.GetPixel(0, 0); }
                    finally { RenderTexture.active = previous; }
                }
                material.SetTexture("_PatternTex", pattern); material.SetFloat("_Width", 1);
                material.SetColor("_BandColor", new Color(0, 0, 0, .5f));
                surface.Render(input, material, new Vector2(100, 100));
                var left = Sample(1); var right = Sample(6);
                Assert.Greater(left.r - right.r, .1f, "点阵应在原有图像内调色。");
                Assert.That(left.a, Is.EqualTo(.75f).Within(.01f)); Assert.That(right.a, Is.EqualTo(left.a).Within(.01f));
                pattern.SetPixels(new[] { new Color(1, 1, 1, .5f), new Color(1, 1, 1, .5f) }); pattern.Apply();
                material.SetFloat("_FilterMode", 2); material.SetFloat("_Width", .25f);
                material.SetFloat("_ColorBlend", 2);
                material.SetColor("_BandColor", new Color(.5f, .5f, .5f, 1)); material.SetFloat("_Rate", .5f);
                surface.Render(input, material, new Vector2(100, 100)); var lit = Sample(4);
                material.SetFloat("_Rate", .95f); surface.Render(input, material, new Vector2(100, 100)); var plain = Sample(4);
                Assert.Greater(lit.r - plain.r, .03f, "扫光相位应改变亮度。");
                Assert.That(lit.a, Is.EqualTo(plain.a).Within(.01f));
            }
            finally { Object.DestroyImmediate(input); Object.DestroyImmediate(pattern); Object.DestroyImmediate(material); Object.DestroyImmediate(pixel); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SheenSupportsReplaceMultiplyAddAndAdditiveWithPartialAlpha()
        {
            var input = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            input.SetPixel(0, 0, new Color(.2f, .4f, .6f, .5f)); input.Apply();
            var pattern = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            pattern.SetPixel(0, 0, new Color(1, 1, 1, .5f)); pattern.Apply();
            var material = new Material(Shader.Find("URUI.Effect/TextureFilter"));
            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            using var surface = new TextureEffectSurface(8, 8);
            try
            {
                material.SetTexture("_PatternTex", pattern); material.SetFloat("_FilterMode", 2);
                material.SetFloat("_Width", 1); material.SetFloat("_Softness", 1); material.SetFloat("_Rate", .5f);
                material.SetColor("_BandColor", new Color(.8f, .3f, .1f, 1));
                foreach (var mode in new[] { 1, 2, 3 })
                {
                    material.SetFloat("_ColorBlend", mode); surface.Render(input, material, new Vector2(100, 100));
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = surface.Texture; pixel.ReadPixels(new Rect(4, 4, 1, 1), 0, 0); pixel.Apply(); }
                    finally { RenderTexture.active = previous; }
                    var actual = pixel.GetPixel(0, 0);
                    // At this phase the authored band is half strength. Texture output remains straight alpha.
                    var band = new Color(.8f, .3f, .1f, 1);
                    var source = new Color(.2f, .4f, .6f, .5f);
                    var target = mode == 1 ? band : mode == 2 ? new Color(source.r*(1+band.r),source.g*(1+band.g),source.b*(1+band.b),.5f)
                        : new Color(source.r+band.r,source.g+band.g,source.b+band.b,.5f);
                    var expected = Color.Lerp(source,target,.5f); expected.a = .5f;
                    if (surface.Texture.sRGB) { expected = expected.gamma; }
                    Assert.That(actual.r, Is.EqualTo(expected.r).Within(.015f), "blend="+mode);
                    Assert.That(actual.g, Is.EqualTo(expected.g).Within(.015f), "blend="+mode);
                    Assert.That(actual.b, Is.EqualTo(expected.b).Within(.015f), "blend="+mode);
                    Assert.That(actual.a, Is.EqualTo(expected.a).Within(.01f));
                }
            }
            finally { Object.DestroyImmediate(input); Object.DestroyImmediate(pattern); Object.DestroyImmediate(material); Object.DestroyImmediate(pixel); }
            yield return null;
        }
    }
}
