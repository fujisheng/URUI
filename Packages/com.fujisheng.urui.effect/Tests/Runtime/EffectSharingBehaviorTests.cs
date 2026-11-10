using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Styles;
namespace URUI.Effect.Tests
{
    public sealed partial class EffectSharingTests
    {
        static EffectPreset Preset(EffectParameters p){var value=ScriptableObject.CreateInstance<EffectPreset>();value.SetParameters(p);return value;}
        [UnityTest]
        public IEnumerator CssAnimationPrioritySurvivesController()
        {
            using var fixture=new EffectRenderFixture();var clock=new Clock();fixture.Surface.UiClock=clock;
            fixture.Load("--urui-fx-tone-filter:sepia;--urui-fx-tone-intensity:.7 !important;");fixture.Render();
            var controller=fixture.Module.GetController(fixture.Document.Q("subject"));var preset=Preset(new EffectParameters{ToneFilter=ToneFilter.Negative,ToneIntensity=.4f,ColorFilter=ColorFilter.Multiply,ColorIntensity=.6f});
            try
            {
                controller.Apply(preset);fixture.Render();var p=controller.CreateParameters();Assert.That(p.ToneFilter,Is.EqualTo(ToneFilter.Sepia));Assert.That(p.ToneIntensity,Is.EqualTo(.7f));Assert.That(p.ColorFilter,Is.EqualTo(ColorFilter.Multiply));
                var playback=controller.Play(new EffectTweenDefinition{UpdateMode=EffectUpdateMode.Manual});playback.SetTime(.2);fixture.Render();
                p=controller.CreateParameters();Assert.That(p.ToneIntensity,Is.EqualTo(.7f));Assert.That(p.ColorIntensity,Is.EqualTo(.2f).Within(.001));
                fixture.Document.Q("subject").Style.Set("animation","test-pulse 1s linear infinite");
                // The author stylesheet owns the keyframes; reload exercises old-handle invalidation too.
                fixture.Load("--urui-fx-tone-filter:sepia;animation:pulse 1s linear infinite;}@keyframes pulse{from{--urui-fx-tone-intensity:0;}to{--urui-fx-tone-intensity:1;}}#subject{");fixture.Render();
                controller=fixture.Module.GetController(fixture.Document.Q("subject"));controller.Apply(preset);controller.Play(new EffectTweenDefinition{UpdateMode=EffectUpdateMode.Manual}).SetTime(.9);
                clock.time=.25;fixture.Render();Assert.That(controller.CreateParameters().ToneIntensity,Is.EqualTo(.25f).Within(.003));
                Assert.That(fixture.Document.Styles.GetComputed(fixture.Document.Q("subject")).GetOrigin("--urui-fx-tone-intensity"),Is.EqualTo(StyleValueOrigin.Animation));
            }
            finally{UnityEngine.Object.DestroyImmediate(preset);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator LinkedInstanceUpdatesWithoutSharedMutableMaterial()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("", "<img id='subject' src='input.png'><img id='target' src='input.png' style='left:0px;'>","img");fixture.Render();
            var source=fixture.Module.GetController(fixture.Document.Q("subject"));var target=fixture.Module.GetController(fixture.Document.Q("target"));
            var preset=Preset(new EffectParameters{ToneFilter=ToneFilter.Sepia,ToneIntensity=.4f,SamplingFilter=SamplingFilter.RgbShift,SamplingScale=2});
            try
            {
                source.Apply(preset);using var link=target.Link(source);fixture.Render();
                Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.4f));Assert.That(target.CreateParameters().SamplingScale,Is.EqualTo(1));
                fixture.Document.Effects.TryGetFrame(source.Element,out var a);fixture.Document.Effects.TryGetFrame(target.Element,out var b);Assert.That(b.Layout.RootToken,Is.EqualTo(a.Layout.RootToken));
                var playback=source.Play(new EffectTweenDefinition{UpdateMode=EffectUpdateMode.Manual});playback.SetTime(.2);fixture.Render();Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.2f).Within(.001));
                var own=target.CreateParameters();own.ToneIntensity=.9f;fixture.Render();Assert.That(source.CreateParameters().ToneIntensity,Is.EqualTo(.2f).Within(.001));
                fixture.Document.Q("target").Style.SetNumber("--urui-fx-tone-intensity",.8f);fixture.Render();Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.8f));Assert.That(source.CreateParameters().ToneIntensity,Is.EqualTo(.2f).Within(.001));
            }
            finally{UnityEngine.Object.DestroyImmediate(preset);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator ReplicaCycleRejectedAndLostSourceKeepsLastSnapshot()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("", "<img id='subject' src='input.png'><img id='target' src='input.png' style='left:0px;'>","img");fixture.Render();
            var source=fixture.Module.GetController(fixture.Document.Q("subject"));var target=fixture.Module.GetController(fixture.Document.Q("target"));
            var preset=Preset(new EffectParameters{ToneFilter=ToneFilter.Sepia,ToneIntensity=.4f});
            try
            {
                source.Apply(preset);var link=target.Link(source,false);Assert.Throws<InvalidOperationException>(()=>source.Link(target,false));Assert.Throws<InvalidOperationException>(()=>source.Link(source));
                fixture.Render();source.Element.RemoveFromHierarchy();fixture.Render();Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.4f));
                link.Dispose();fixture.Render();Assert.That(target.CreateParameters().ToneFilter,Is.EqualTo(ToneFilter.Sepia));
                Assert.Throws<ObjectDisposedException>(()=>source.Apply(preset));
            }
            finally{UnityEngine.Object.DestroyImmediate(preset);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator MergeActiveGroupsAndPresetTextureAreIndependent()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("");fixture.Render();var controller=fixture.Module.GetController(fixture.Document.Q("subject"));
            var initial=Preset(new EffectParameters{ToneFilter=ToneFilter.Sepia,ToneIntensity=.4f,Flip=Flip.Horizontal,TransitionTexture=fixture.Pattern,TransitionFilter=TransitionFilter.Dissolve});
            var append=Preset(new EffectParameters{ColorFilter=ColorFilter.Multiply,Color=Color.red,Flip=Flip.Vertical});
            var saved=ScriptableObject.CreateInstance<EffectPreset>();
            try
            {
                controller.Apply(initial);controller.Apply(append,EffectApplyMode.MergeActiveGroups);fixture.Render();var p=controller.CreateParameters();
                Assert.That(p.ToneFilter,Is.EqualTo(ToneFilter.Sepia));Assert.That(p.ToneIntensity,Is.EqualTo(.4f));Assert.That(p.ColorFilter,Is.EqualTo(ColorFilter.Multiply));Assert.That(p.Flip,Is.EqualTo(Flip.Horizontal|Flip.Vertical));Assert.That(p.TransitionTexture,Is.SameAs(fixture.Pattern));
                controller.SaveTo(saved);controller.Apply(append);fixture.Render();Assert.That(controller.CreateParameters().ToneFilter,Is.EqualTo(ToneFilter.None));Assert.That(saved.CreateParameters().ToneFilter,Is.EqualTo(ToneFilter.Sepia));
                fixture.Document.Q("subject").Style.Set("--urui-fx-tone-intensity","initial");controller.Apply(initial);fixture.Render();Assert.That(controller.CreateParameters().ToneIntensity,Is.EqualTo(1),"显式 initial 仍是作者声明");
            }
            finally{UnityEngine.Object.DestroyImmediate(initial);UnityEngine.Object.DestroyImmediate(append);UnityEngine.Object.DestroyImmediate(saved);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator EveryPlaybackChannelUsesTheSourceMapping()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("");fixture.Render();var controller=fixture.Module.GetController(fixture.Document.Q("subject"));
            var preset=Preset(new EffectParameters{ToneFilter=ToneFilter.Sepia,ColorFilter=ColorFilter.Multiply,SamplingFilter=SamplingFilter.RgbShift,TransitionFilter=TransitionFilter.Fade,GradationMode=GradationMode.Angle,EdgeMode=EdgeMode.Shiny});
            try
            {
                controller.Apply(preset);controller.Play(new EffectTweenDefinition{UpdateMode=EffectUpdateMode.Manual}).SetTime(.25);fixture.Render();var p=controller.CreateParameters();
                Assert.That(p.ToneIntensity,Is.EqualTo(.25f));Assert.That(p.ColorIntensity,Is.EqualTo(.25f));Assert.That(p.SamplingIntensity,Is.EqualTo(.25f));Assert.That(p.TransitionProgress,Is.EqualTo(.25f));Assert.That(p.GradationOffset,Is.EqualTo(-.5f));Assert.That(p.GradationRotation,Is.EqualTo(90));Assert.That(p.EdgeShinyProgress,Is.EqualTo(.25f));
            }
            finally{UnityEngine.Object.DestroyImmediate(preset);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator TransitionHitPolicyIsExplicitAndDoesNotRewriteCss()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("--urui-fx-transition-filter:fade;--urui-fx-transition-progress:1;");fixture.Render();var element=fixture.Document.Q("subject");var controller=fixture.Module.GetController(element);
            Assert.That(fixture.Document.Pick(new Vector2(96,96)),Is.SameAs(element));controller.InputPolicy=TransitionInputPolicy.DisableWhenComplete;fixture.Render();Assert.That(fixture.Document.Pick(new Vector2(96,96)),Is.Not.SameAs(element));
            foreach(var mode in new[]{"none","shiny","mask","pattern"}){element.Style.Set("--urui-fx-transition-filter",mode);fixture.Render();Assert.That(fixture.Document.Pick(new Vector2(96,96)),Is.SameAs(element),mode);}
            element.Style.Set("--urui-fx-transition-filter","fade");fixture.Render();controller.Dispose();fixture.Render();Assert.That(fixture.Document.Pick(new Vector2(96,96)),Is.SameAs(element));
            Assert.That(element.Style.Get("pointer-events"),Is.Null.Or.Empty);yield return null;
        }
        [UnityTest]
        public IEnumerator StableControllerDoesNotReacquireGradientResources()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("");fixture.Render();
            var preset=Preset(new EffectParameters{GradationMode=GradationMode.HorizontalGradient,GradationGradient=EffectGradientTests.ReferenceGradient()});
            try
            {
                fixture.Module.GetController(fixture.Document.Q("subject")).Apply(preset);fixture.Render();
                var type=typeof(EffectModule).Assembly.GetType("URUI.Effect.Rendering.EffectGradientCache");
                var entries=(System.Collections.IDictionary)type.GetField("entries",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
                int References(){var count=0;foreach(var value in entries.Values)count+=(int)value.GetType().GetField("References",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(value);return count;}
                var before=References();for(var i=0;i<12;i++)fixture.Render();
                Assert.That(References(),Is.EqualTo(before),"稳定参数不得重复取得同一 LUT 租约并进入 GPU 退役队列");
            }
            finally{UnityEngine.Object.DestroyImmediate(preset);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator CompletedTextTransitionDoesNotPickItsTextProxy()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("--urui-fx-transition-filter:fade;--urui-fx-transition-progress:1;", "<span id='subject'>TEXT</span>");fixture.Render();
            var element=fixture.Document.Q("subject");var point=new Vector2(68,72);
            Assert.That(fixture.Document.Pick(point),Is.SameAs(element),"探针必须命中文字代理");
            fixture.Module.GetController(element).InputPolicy=TransitionInputPolicy.DisableWhenComplete;fixture.Render();
            Assert.That(fixture.Document.Pick(point),Is.Not.SameAs(element));yield return null;
        }
        [UnityTest]
        public IEnumerator DocumentsMayShareParametersButCannotShareRootCoordinates()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("--urui-fx-tone-filter:sepia;--urui-fx-tone-intensity:.4;");fixture.Render();
            var other=new GameObject("Linked independent document");var document=other.AddComponent<URUI.Documents.Document>();document.Surface=fixture.Surface;
            var module=new EffectModule(document,fixture);
            try
            {
                document.Images=new[]{new URUI.Resources.ImageAsset("input.png",fixture.Source)};
                document.TextResources=new[]{new URUI.Resources.TextResource("/effect.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect"))};
                document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>img{position:absolute;left:0;top:0;width:32px;height:32px;--urui-effect:ui;}</style></head><body><img id='target' src='input.png'></body></html>","/other.html");document.SynchronizeLayout();
                var source=fixture.Module.GetController(fixture.Document.Q("subject"));var target=module.GetController(document.Q("target"));
                Assert.Throws<ArgumentException>(()=>target.Link(source,true));using var link=target.Link(source,false);document.SynchronizeLayout();
                Assert.That(target.CreateParameters().ToneFilter,Is.EqualTo(ToneFilter.Sepia));Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.4f));
                fixture.Document.Effects.TryGetFrame(source.Element,out var a);document.Effects.TryGetFrame(target.Element,out var b);Assert.That(b.Layout.RootToken,Is.Not.EqualTo(a.Layout.RootToken));
                source.Element.Style.SetNumber("--urui-fx-tone-intensity",.8f);document.SynchronizeLayout();Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.8f));
                fixture.Module.Dispose();document.SynchronizeLayout();Assert.That(target.CreateParameters().ToneIntensity,Is.EqualTo(.8f));
            }
            finally{module.Dispose();UnityEngine.Object.DestroyImmediate(other);}
            yield return null;
        }
    }
}
