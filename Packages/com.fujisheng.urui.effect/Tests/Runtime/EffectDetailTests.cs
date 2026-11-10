using System;
using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class EffectDetailTests
    {
        [UnityTest]
        public IEnumerator EveryDetailAndEdgeMatchesReference()
        {
            using var fixture=new EffectRenderFixture();
            var report=new EffectTransitionTests.CaptureSet {colorSpace=QualitySettings.activeColorSpace.ToString()};
            foreach(var mode in (DetailFilter[])Enum.GetValues(typeof(DetailFilter))) foreach(var phase in new[]{0f,.5f,1f})
            {
                var id="DetailFilter."+mode+"."+Mathf.RoundToInt(phase*100).ToString("000");
                fixture.Load("--urui-fx-detail-filter:"+EffectRenderFixture.Kebab(mode.ToString())+";--urui-fx-detail-texture:url('pattern.png');"+
                    "--urui-fx-detail-intensity:"+phase.ToString(CultureInfo.InvariantCulture)+";");
                fixture.Compare(id);report.cases.Add(id);
            }
            foreach(var mode in (EdgeMode[])Enum.GetValues(typeof(EdgeMode))) foreach(var phase in new[]{0f,.5f,1f})
            {
                var id="EdgeMode."+mode+"."+Mathf.RoundToInt(phase*100).ToString("000");
                fixture.Load("--urui-fx-edge-mode:"+EffectRenderFixture.Kebab(mode.ToString())+";--urui-fx-edge-shiny-auto-play-speed:0;"+
                    "--urui-fx-edge-shiny-progress:"+phase.ToString(CultureInfo.InvariantCulture)+";");
                fixture.Compare(id);report.cases.Add(id);
            }
            File.WriteAllText(Path.Combine(fixture.Output,"detail-captures.json"),JsonUtility.ToJson(report,true));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DetailMaskThresholdEndpoints()
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("--urui-fx-detail-filter:masking;--urui-fx-detail-texture:url('pattern.png');--urui-fx-detail-threshold-min:.5;--urui-fx-detail-threshold-max:.5;");
            void PatternAlpha(float alpha)
            {
                var pixels=new Color[4096];for(var i=0;i<pixels.Length;i++)pixels[i]=new Color(1,1,1,alpha);
                fixture.Pattern.SetPixels(pixels);fixture.Pattern.Apply();
            }
            PatternAlpha(0); var hidden=fixture.Render("DetailMaskZero");
            Assert.That(hidden[128*256+128].a,Is.LessThan(.002));
            PatternAlpha(1); var visible=fixture.Render("DetailMaskOne");
            Assert.That(visible[128*256+128].a,Is.GreaterThan(.7));
            // 源 Masking 只修改 alpha；RGB 保留原值，不能悄悄改成标准 mask-image。
            var a=hidden[128*256+128];var b=visible[128*256+128];var background=visible[0];
            Assert.That(a.r-background.r*(1-a.a),Is.EqualTo(b.r-background.r*(1-b.a)).Within(.002));
            yield return null;
        }
    }
}
