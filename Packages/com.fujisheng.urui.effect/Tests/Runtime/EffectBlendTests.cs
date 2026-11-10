using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class EffectBlendTests
    {
        [Serializable] sealed class BlendCase { public string source,destination;public float maximumError; }
        [Serializable] sealed class BlendReport { public string colorSpace;public List<BlendCase> cases=new(); }
        static Color Factor(BlendMode mode,Color source,Color destination)
        {
            return mode switch {
                BlendMode.Zero=>Color.clear,BlendMode.One=>Color.white,BlendMode.DstColor=>destination,BlendMode.SrcColor=>source,
                BlendMode.OneMinusDstColor=>Color.white-destination,BlendMode.SrcAlpha=>Color.white*source.a,
                BlendMode.OneMinusSrcColor=>Color.white-source,BlendMode.DstAlpha=>Color.white*destination.a,
                BlendMode.OneMinusDstAlpha=>Color.white*(1-destination.a),BlendMode.OneMinusSrcAlpha=>Color.white*(1-source.a),
                BlendMode.SrcAlphaSaturate=>new Color(Mathf.Min(source.a,1-destination.a),Mathf.Min(source.a,1-destination.a),Mathf.Min(source.a,1-destination.a),1),
                _=>throw new ArgumentOutOfRangeException(nameof(mode)) };
        }
        static Color Blend(Color source,Color destination,BlendMode src,BlendMode dst) => source*Factor(src,source,destination)+destination*Factor(dst,source,destination);
        static float Difference(Color a,Color b) => Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b),Mathf.Abs(a.a-b.a));
        static Color SetConstantSource(EffectRenderFixture fixture)
        {
            var pixels=new Color[4096];for(var i=0;i<pixels.Length;i++)pixels[i]=new Color(.5f,.25f,.75f,.5f);
            fixture.Source.SetPixels(pixels);fixture.Source.Apply();var sample=fixture.Source.GetPixel(0,0);
            return new Color(sample.r*sample.a,sample.g*sample.a,sample.b*sample.a,sample.a);
        }
        [UnityTest]
        public IEnumerator EveryBlendFactorUsesDestinationAlpha()
        {
            using var fixture=new EffectRenderFixture();var source=SetConstantSource(fixture);
            fixture.Camera.backgroundColor=new Color(.2f,.3f,.4f,.6f);
            var report=new BlendReport {colorSpace=QualitySettings.activeColorSpace.ToString()};
            foreach(var src in (BlendMode[])Enum.GetValues(typeof(BlendMode))) foreach(var dst in (BlendMode[])Enum.GetValues(typeof(BlendMode)))
            {
                fixture.Load("--urui-fx-blend-type:custom;--urui-fx-src-blend-mode:"+EffectRenderFixture.Kebab(src.ToString())+";--urui-fx-dst-blend-mode:"+EffectRenderFixture.Kebab(dst.ToString())+";");
                var pixels=fixture.Render();var destination=pixels[0];var expected=Blend(source,destination,src,dst);var actual=pixels[128*256+128];
                var error=Difference(expected,actual);
                Assert.That(error,Is.LessThan(.0035f),src+"/"+dst+": "+actual+" != "+expected);
                report.cases.Add(new BlendCase {source=src.ToString(),destination=dst.ToString(),maximumError=error});
            }
            File.WriteAllText(Path.Combine(fixture.Output,"blend-numeric.json"),JsonUtility.ToJson(report,true));yield return null;
        }
        [UnityTest]
        public IEnumerator EveryBlendPresetMatchesReference()
        {
            using var fixture=new EffectRenderFixture();
            var report=new EffectTransitionTests.CaptureSet {colorSpace=QualitySettings.activeColorSpace.ToString()};
            foreach(var type in (BlendType[])Enum.GetValues(typeof(BlendType)))
            {
                var id="BlendType."+type+".050";
                fixture.Load("--urui-fx-blend-type:"+EffectRenderFixture.Kebab(type.ToString())+";");fixture.Compare(id);report.cases.Add(id);
            }
            File.WriteAllText(Path.Combine(fixture.Output,"blend-captures.json"),JsonUtility.ToJson(report,true));yield return null;
        }
        static Color Over(Color source,Color destination)
        {
            if(QualitySettings.activeColorSpace!=ColorSpace.Linear)return source+destination*(1-source.a);
            float Encode(float x) => x<=.0031308f ? x*12.92f : 1.055f*Mathf.Pow(Mathf.Max(0,x),1/2.4f)-.055f;
            float Decode(float x) => x<=.04045f ? x/12.92f : Mathf.Pow(Mathf.Max(0,(x+.055f)/1.055f),2.4f);
            var alpha=source.a+destination.a*(1-source.a);var result=new Color(0,0,0,alpha);
            for(var i=0;i<3;i++)result[i]=alpha>1e-6f ? Decode((Encode(source[i]/source.a)*source.a+Encode(destination[i]/destination.a)*destination.a*(1-source.a))/alpha)*alpha : 0;
            return result;
        }
        [UnityTest]
        public IEnumerator CustomBlendUnderNestedOpacityAndMask()
        {
            using var fixture=new EffectRenderFixture();var source=SetConstantSource(fixture);
            const string body="<div id='outer'><div style='position:absolute;left:64px;top:64px;width:128px;height:128px;background-color:rgba(80,120,160,.4);'></div><img id='subject' src='input.png'></div>";
            fixture.Camera.backgroundColor=Color.clear;
            fixture.Load("display:none;",body);var parent=fixture.Render()[128*256+128];
            fixture.Camera.backgroundColor=new Color(.2f,.3f,.4f,.6f);
            fixture.Load("opacity:.5;mask-image:linear-gradient(rgba(0,0,0,.5),rgba(0,0,0,.5));--urui-fx-blend-type:custom;--urui-fx-src-blend-mode:dst-alpha;--urui-fx-dst-blend-mode:one-minus-src-alpha;",body.Replace("id='outer'","id='outer' style='opacity:.75;'"));
            var pixels=fixture.Render("CustomBlendNested");
            var expected=Over(Blend(source*.25f,parent,BlendMode.DstAlpha,BlendMode.OneMinusSrcAlpha)*.75f,pixels[0]);
            Assert.That(Difference(expected,pixels[128*256+128]),Is.LessThan(.006f),pixels[128*256+128]+" != "+expected);
            yield return null;
        }
    }
}
