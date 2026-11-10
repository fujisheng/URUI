using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;

namespace URUI.Effect.Tests
{
    public sealed class EffectCombinationTests
    {
        sealed class Clock : IUiClock { internal double time; public double Now=>time; public float Delta=>0; }
        const string pattern="--urui-fx-transition-texture:url('pattern.png');--urui-fx-detail-texture:url('pattern.png');--urui-fx-transition-width:.25;--urui-fx-transition-softness:.4;--urui-fx-detail-intensity:.5;--urui-fx-sampling-intensity:.5;--urui-fx-tone-intensity:.5;--urui-fx-color-intensity:.5;--urui-fx-color:#b33366cc;--urui-fx-edge-shiny-auto-play-speed:0;";
        [UnityTest]
        public IEnumerator PatternAreaAllInnerEdge()
        {
            using var fixture=new EffectRenderFixture();
            var report=new EffectTransitionTests.CaptureSet {colorSpace=QualitySettings.activeColorSpace.ToString()};
            foreach(var mode in new[]{PatternArea.All,PatternArea.Inner,PatternArea.Edge})
            {
                var id="PatternArea."+mode+".050";
                fixture.Load(pattern+"--urui-fx-transition-filter:pattern;--urui-fx-edge-mode:plain;--urui-fx-pattern-area:"+EffectRenderFixture.Kebab(mode.ToString())+";");
                fixture.Compare(id);report.cases.Add(id);
            }
            File.WriteAllText(Path.Combine(fixture.Output,"pattern-captures.json"),JsonUtility.ToJson(report,true));
            yield return null;
        }
        [UnityTest]
        public IEnumerator TransitionThenDetailThenEdge()
        {
            using var fixture=new EffectRenderFixture();
            var report=new EffectTransitionTests.CaptureSet {colorSpace=QualitySettings.activeColorSpace.ToString()};
            var cases=new Dictionary<string,string> {
                ["Combination.TransitionDetailEdge"]="--urui-fx-transition-filter:shiny;--urui-fx-detail-filter:additive;--urui-fx-edge-mode:plain;--urui-fx-tone-filter:negative;",
                ["Combination.RgbTarget"]="--urui-fx-sampling-filter:rgb-shift;--urui-fx-transition-filter:shiny;--urui-fx-detail-filter:multiply;--urui-fx-edge-mode:plain;--urui-fx-target-mode:hue;--urui-fx-target-range:.35;--urui-fx-color-filter:replace;",
                ["Combination.MeltSampling"]="--urui-fx-sampling-filter:blur-fast;--urui-fx-transition-filter:melt;",
                ["Parameters.Transition"]="--urui-fx-transition-filter:pattern;--urui-fx-transition-texture-scale-x:2;--urui-fx-transition-texture-scale-y:.8;--urui-fx-transition-texture-offset-x:.2;--urui-fx-transition-texture-offset-y:-.1;--urui-fx-transition-rotation:37;--urui-fx-transition-keep-aspect-ratio:false;--urui-fx-transition-reverse:true;--urui-fx-transition-pattern-reverse:true;--urui-fx-transition-range-min:.15;--urui-fx-transition-range-max:.8;--urui-fx-edge-mode:plain;--urui-fx-pattern-area:edge;",
                ["Parameters.Detail"]="--urui-fx-detail-filter:masking;--urui-fx-detail-texture-scale-x:2;--urui-fx-detail-texture-scale-y:.8;--urui-fx-detail-texture-offset-x:.2;--urui-fx-detail-texture-offset-y:-.1;--urui-fx-detail-threshold-min:.3;--urui-fx-detail-threshold-max:.7;",
                ["Parameters.Edge"]="--urui-fx-edge-mode:shiny;--urui-fx-edge-shiny-progress:.35;--urui-fx-edge-shiny-width:.7;--urui-fx-edge-width:.2;--urui-fx-edge-color-filter:hsv-modifier;--urui-fx-edge-color-hue-shift:-.1;--urui-fx-edge-color-saturation-shift:.2;--urui-fx-edge-color-value-shift:-.1;--urui-fx-edge-color-alpha:.7;--urui-fx-edge-color-glow:true;",
                ["Time.Transition"]="--urui-fx-transition-filter:shiny;--urui-fx-transition-auto-play-speed:.35;--urui-fx-transition-texture-speed-x:.2;--urui-fx-transition-texture-speed-y:-.1;",
                ["Time.Detail"]="--urui-fx-detail-filter:multiply;--urui-fx-detail-texture-speed-x:.2;--urui-fx-detail-texture-speed-y:.1;",
                ["Time.Edge"]="--urui-fx-edge-mode:shiny;--urui-fx-edge-shiny-auto-play-speed:-.7;"
            };
            foreach(var item in cases)
            {
                fixture.Load(pattern+item.Value);fixture.Document.SynchronizeLayout();
                using var clock=fixture.Document.Effects.BindClock(fixture.Document.Q("subject"),new Clock {time=item.Key.StartsWith("Time.") ? 1.25 : 0});
                fixture.Compare(item.Key);report.cases.Add(item.Key);
            }
            File.WriteAllText(Path.Combine(fixture.Output,"combination-captures.json"),JsonUtility.ToJson(report,true));
            yield return null;
        }
        [UnityTest]
        public IEnumerator MeltAndBurnMoveSourceCoordinates()
        {
            using var fixture=new EffectRenderFixture();
            var pixels=new Color[4096]; for(var i=0;i<pixels.Length;i++)pixels[i]=new Color(1,1,1,.5f);
            fixture.Pattern.SetPixels(pixels);fixture.Pattern.Apply();
            fixture.Load("--urui-fx-transition-filter:melt;--urui-fx-transition-texture:url('pattern.png');--urui-fx-transition-progress:.5;--urui-fx-transition-width:.25;--urui-fx-transition-color-filter:none;");
            var melt=fixture.Render("MeltCoordinateProbe");
            fixture.Load("--urui-fx-transition-filter:none;");var plain=fixture.Render();
            var shifted=melt[128*256+128];var original=plain[128*256+128];var background=plain[0];
            // 源 Melt 的 bandColor 还会再乘一次 alpha；先还原该已知因子和目标背景。
            var shiftedGreen=(shifted.g-background.g*(1-shifted.a))/shifted.a;
            var originalGreen=original.g-background.g*(1-original.a);
            Assert.That(shiftedGreen-originalGreen,Is.GreaterThan(.04f),"Melt 必须采样位移后的源坐标");
            fixture.Load("--urui-fx-transition-filter:burn;--urui-fx-transition-texture:url('pattern.png');--urui-fx-transition-progress:.5;--urui-fx-transition-width:.25;--urui-fx-transition-color-filter:none;");
            fixture.Render("BurnCoordinateProbe");
            Assert.That(fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"),out var frame),Is.True);
            Assert.That(frame.SourceGeometry.Bounds.height,Is.EqualTo(128),"转场不能改写原始几何");
            yield return null;
        }
        [UnityTest]
        public IEnumerator StaticTimeDoesNotInvalidate()
        {
            using var fixture=new EffectRenderFixture();fixture.Load(pattern+"--urui-fx-transition-filter:shiny;");fixture.Document.SynchronizeLayout();
            var clock=new Clock();using var binding=fixture.Document.Effects.BindClock(fixture.Document.Q("subject"),clock);
            var a=fixture.Render();fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"),out var first);
            clock.time=100;var b=fixture.Render("StaticTime");fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"),out var second);
            Assert.That(b,Is.EqualTo(a));Assert.That(second.SourceGeometry,Is.SameAs(first.SourceGeometry));
            yield return null;
        }
        [UnityTest]
        public IEnumerator DynamicTextureReplacementReprocesses()
        {
            using var fixture=new EffectRenderFixture();fixture.Load(pattern+"--urui-fx-detail-filter:multiply;");var before=fixture.Render();
            var acquisitions=fixture.ImageAcquisitions;
            var pixels=new Color[4096];for(var i=0;i<pixels.Length;i++)pixels[i]=Color.red;fixture.Pattern.SetPixels(pixels);fixture.Pattern.Apply();
            var after=fixture.Render("DynamicPattern");
            Assert.That(after[128*256+128].g,Is.LessThan(before[128*256+128].g-.03f));
            Assert.That(fixture.ImageAcquisitions,Is.EqualTo(acquisitions),"纹理内容更新复用原租约");
            yield return null;
        }
        [UnityTest]
        public IEnumerator TextAndSubtreeUseSameParameters()
        {
            using var fixture=new EffectRenderFixture();
            var pixels=new Color[4096];for(var i=0;i<pixels.Length;i++)pixels[i]=Color.white;
            fixture.Pattern.SetPixels(pixels);fixture.Pattern.Apply();
            foreach(var text in new[]{false,true})
            {
                var content=text ? "<div id='subject' style='font-size:64px;color:white;'>UI</div>" :
                    "<div id='subject'><img src='input.png' style='width:128px;height:128px;'></div>";
                fixture.Load("--urui-effect:none;",content);var original=fixture.Render();
                fixture.Load("--urui-fx-transition-filter:fade;--urui-fx-transition-texture:url('pattern.png');--urui-fx-transition-progress:.75;",content);
                var faded=fixture.Render(text ? "TextTransition" : "SubtreeTransition");
                Assert.That(fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"),out var frame),Is.True);
                Assert.That(frame.SourceIsCaptured,Is.True);var maximum=0f;var error=0f;
                for(var i=0;i<original.Length;i++) {maximum=Mathf.Max(maximum,original[i].a);error=Mathf.Max(error,Mathf.Abs(faded[i].a-original[i].a*.50015f));}
                Assert.That(maximum,Is.GreaterThan(.5f));Assert.That(error,Is.LessThan(.003f));
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator FailedPatternSwapKeepsPreviousLease()
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("--urui-fx-detail-filter:multiply;--urui-fx-detail-texture:url('pattern.png');");
            var original=fixture.Render();var acquisitions=fixture.ImageAcquisitions;
            fixture.Missing=true;
            fixture.Document.Q("subject").Style.Set("--urui-fx-detail-texture","url('missing.png')");
            Assert.That(fixture.Render("FailedPatternSwap"),Is.EqualTo(original));
            Assert.That(fixture.ImageAcquisitions,Is.EqualTo(acquisitions));Assert.That(fixture.ImageReleases,Is.Zero);
            fixture.Missing=false;
            fixture.Document.Q("subject").Style.Set("--urui-fx-detail-texture","url('pattern.png')");
            Assert.That(fixture.Render(),Is.EqualTo(original));
            yield return null;
        }
        [UnityTest]
        public IEnumerator CapturedInputDomainAndHaloMatchLeaf()
        {
            using var fixture=new EffectRenderFixture();
            var input=fixture.Source.GetPixels();for(var i=0;i<input.Length;i++)input[i].a=.8f;fixture.Source.SetPixels(input);fixture.Source.Apply();
            foreach(var item in new[]{
                ("Melt","--urui-fx-transition-filter:melt;--urui-fx-transition-texture:url('pattern.png');--urui-fx-transition-width:.25;--urui-fx-transition-softness:.4;"),
                ("Outline","--urui-fx-shadow-mode:outline8;--urui-fx-shadow-distance-x:8;--urui-fx-shadow-distance-y:-8;")})
            {
                fixture.Load(item.Item2);var leaf=fixture.Render("LeafDomain"+item.Item1);
                fixture.Load(item.Item2,"<div id='subject'><img src='input.png' style='display:block;width:128px;height:128px;'></div>");
                var captured=fixture.Render("CapturedDomain"+item.Item1);var maximum=0f;
                for(var i=0;i<leaf.Length;i++)for(var channel=0;channel<4;channel++)maximum=Mathf.Max(maximum,Mathf.Abs(leaf[i][channel]-captured[i][channel]));
                Assert.That(maximum,Is.LessThanOrEqualTo(4f/255),item.Item1+"：捕获 padding 不能扩大转场 UV 域，也不能裁掉阴影");
            }
            yield return null;
        }
    }
}
