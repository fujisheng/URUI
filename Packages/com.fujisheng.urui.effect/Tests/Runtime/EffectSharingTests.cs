using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
using URUI.Styles;

namespace URUI.Effect.Tests
{
    public sealed partial class EffectSharingTests
    {
        sealed class Clock : IUiClock { internal double time;public double Now=>time;public float Delta=>0; }
        [UnityTest]
        public IEnumerator RegisteredDefaultsAndAnimationOriginsAreDistinct()
        {
            using var fixture=new EffectRenderFixture();var clock=new Clock();fixture.Surface.UiClock=clock;
            fixture.Document.LoadHtml("<!doctype html><html><head><style>@property --probe{syntax:'<number>';inherits:false;initial-value:1;}"+
                "@keyframes pulse{from{--probe:0;}to{--probe:1;}}div{width:10px;height:10px;}"+
                "#rule{--probe:.3;}#initial{--probe:initial;}#animated,#important{animation:pulse 1s linear infinite;}"+
                "#important{--probe:.7 !important;}#transition{--probe:0;transition:--probe 1s linear;}"+
                "</style></head><body><div id='default'></div><div id='rule'></div><div id='initial'></div>"+
                "<div id='inline' style='--probe:.6;'></div><div id='animated'></div><div id='important'></div><div id='transition'></div></body></html>","/origins.html");
            fixture.Document.SynchronizeLayout();clock.time=.25;fixture.Document.SynchronizeLayout();
            var method=typeof(ComputedStyleSnapshot).GetMethod("GetOrigin");
            Assert.That(method,Is.Not.Null,"预设与播放控制不能把注册默认值误当成作者覆盖");
            void Check(string id,string expected,float value)
            {
                var style=fixture.Document.Styles.GetComputed(fixture.Document.Q(id));
                Assert.That(method.Invoke(style,new object[]{"--probe"}).ToString(),Is.EqualTo(expected),id);
                Assert.That(style.GetNumber("--probe"),Is.EqualTo(value).Within(.002f),id);
            }
            Check("default","Initial",1);Check("rule","Author",.3f);Check("initial","Author",1);Check("inline","Author",.6f);
            Check("animated","Animation",.25f);Check("important","Important",.7f);
            fixture.Document.Q("transition").Style.SetNumber("--probe",1);fixture.Document.SynchronizeLayout();
            clock.time=.75;fixture.Document.SynchronizeLayout();Check("transition","Transition",.5f);
            yield return null;
        }
        [Test]
        public void ControllerAndPresetExposeIndependentSnapshots()
        {
            Assert.That(typeof(EffectModule).GetMethod("GetController"),Is.Not.Null,"需要节点级通用控制器");
            var setter=typeof(EffectPreset).GetMethod("SetParameters");Assert.That(setter,Is.Not.Null);
            var preset=ScriptableObject.CreateInstance<EffectPreset>();
            try
            {
                var parameters=new EffectParameters {ToneFilter=ToneFilter.Sepia,ToneIntensity=.4f};
                setter.Invoke(preset,new object[]{parameters});parameters.ToneIntensity=.8f;
                var first=preset.CreateParameters();Assert.That(first.ToneIntensity,Is.EqualTo(.4f));
                first.ToneIntensity=.1f;Assert.That(preset.CreateParameters().ToneIntensity,Is.EqualTo(.4f));
            }
            finally {UnityEngine.Object.DestroyImmediate(preset);}
        }
    }
}
