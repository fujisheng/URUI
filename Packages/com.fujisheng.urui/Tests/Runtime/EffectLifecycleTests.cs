using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Rendering.Effects;
using URUI.Styles;
using URUI.Resources;
using URUI.Animation;
namespace URUI.Tests
{
    public sealed class EffectLifecycleTests
    {
        GameObject root,camera; Document document; ProbeFactory factory;
        sealed class Resource : IDisposable { readonly ProbeFactory owner; public Resource(ProbeFactory owner) { this.owner=owner; owner.Resources++; } public void Dispose(){owner.Resources--;} }
        sealed class ProbeFactory : IEffectFactory
        {
            internal int Resources,Released; internal Action<EffectFrame> DuringPrepare; internal Action<EffectRenderContext> DuringRecord;
            internal readonly List<Probe> Instances=new();
            public IElementEffect Create(EffectContext context) { context.Own(new Resource(this)); var probe=new Probe(this); Instances.Add(probe); return probe; }
        }
        sealed class Probe : IElementEffect
        {
            readonly ProbeFactory factory; internal EffectFrame Frame; internal int Records;
            internal Probe(ProbeFactory factory){this.factory=factory;}
            public void Prepare(EffectFrame frame){Frame=frame;factory.DuringPrepare?.Invoke(frame);}
            public void Record(EffectRenderContext context){Records++;factory.DuringRecord?.Invoke(context);}
            public void Dispose(){factory.Released++;Frame=default;}
        }
        sealed class Failing : IEffectFactory
        {
            readonly ProbeFactory owner; internal Failing(ProbeFactory owner){this.owner=owner;}
            public IElementEffect Create(EffectContext context){context.Own(new Resource(owner));throw new InvalidOperationException("probe construction failure");}
        }
        static EffectDefinition Definition(string name,IEffectFactory factory)=>new(name,EffectKind.Image,factory,"probe-program","premultiplied/linear HDR",new[]{new StylePropertyDefinition("--probe-progress",StyleValueType.Number)});
        [SetUp] public void SetUp()
        {
            camera=new GameObject("Effect camera",typeof(Camera));camera.tag="MainCamera";
            root=new GameObject("Effects");document=HtmlFixtureAssets.Create(root);document.Images=new[]{new ImageAsset("probe.png",Texture2D.whiteTexture)};factory=new ProbeFactory();
            document.Effects.Register(Definition("probe",factory)); document.Effects.Register(Definition("failing-probe",new Failing(factory)));
            document.Load(global::UnityEngine.Resources.Load<TextAsset>("URUI/Tests/EffectLifecycle"), "URUI/Tests/EffectLifecycle.html");
        }
        IEnumerator AwaitResources(int expected) { for(int i=0;i<30 && factory.Resources!=expected;i++) yield return null; Assert.AreEqual(expected,factory.Resources); }
        [TearDown] public void TearDown(){UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(camera);}
        sealed class Clock:IUiClock {public double Now{get;set;}public float Delta=>0;}

        [Test]
        public void SavedEffectContextRemainsExpiredDuringLaterCallbacks()
        {
            EffectRenderContext first = default;
            EffectFrame snapshot = default;
            var captured = false;
            var checkedLater = 0;
            var clock = new Clock { Now = 2 };
            using var clockBinding = document.Effects.BindClock(document.Q("a"), clock);
            factory.DuringRecord = context =>
            {
                if (captured)
                {
                    Assert.Throws<InvalidOperationException>(() => first.DrawOriginal());
                    Assert.AreEqual(2, snapshot.Time);
                    Assert.AreEqual(2, first.Frame.Time);
                    ++checkedLater;
                }
                else { first = context; snapshot = context.Frame; captured = true; }
                var copy = context;
                copy.DrawOriginal();
            };
            document.SynchronizeLayout();
            clock.Now = 3;
            document.SynchronizeLayout();
            Assert.That(checkedLater, Is.GreaterThan(0));
            Assert.AreEqual(2, snapshot.Time);
            Assert.Throws<InvalidOperationException>(() => first.DrawOriginal());
        }
        [Test]public void EffectInstancesCanUseIndependentClocksAndRestoreTheDocumentClock()
        {
            document.SynchronizeLayout();var a=document.Q("a");var b=document.Q("b");var first=new Clock{Now=2};var second=new Clock{Now=8};
            using var one=document.Effects.BindClock(a,first);var two=document.Effects.BindClock(b,second);document.SynchronizeLayout();
            Assert.AreEqual(2,factory.Instances[0].Frame.Time);Assert.AreEqual(8,factory.Instances[1].Frame.Time);
            first.Now=3;document.SynchronizeLayout();Assert.AreEqual(3,factory.Instances[0].Frame.Time);Assert.AreEqual(8,factory.Instances[1].Frame.Time);
            two.Dispose();document.SynchronizeLayout();Assert.AreEqual(document.Surface.UiClock.Now,factory.Instances[1].Frame.Time);
        }
        [Test] public void ClonedAuthorSubtreePreparesUnwrappedImagesOnFirstLayout()
        {
            document.SynchronizeLayout();var clone=document.Q("prototype").Clone();
            clone.RemoveFromClassList("hidden");document.DocumentElement.Hierarchy.Add(clone);
            Assert.DoesNotThrow(()=>document.SynchronizeLayout());
            Assert.AreEqual(3,factory.Instances.Count);
            Assert.AreEqual(new Vector2(40,20),factory.Instances[2].Frame.Size);
            Assert.AreEqual(.4f,factory.Instances[2].Frame.Style.GetNumber("--probe-progress"));
        }
        [Test] public void OriginalImagesHaveIndependentPreparedSizeAndParameters()
        {
            document.SynchronizeLayout();Assert.AreEqual(2,factory.Instances.Count);Assert.AreEqual(2,factory.Resources);
            Assert.AreEqual(new Vector2(120,60),factory.Instances[0].Frame.Size);Assert.AreEqual(new Vector2(240,120),factory.Instances[1].Frame.Size);
            Assert.AreEqual(.2f,factory.Instances[0].Frame.Style.GetNumber("--probe-progress"));Assert.AreEqual(.8f,factory.Instances[1].Frame.Style.GetNumber("--probe-progress"));
            var first=factory.Instances[0].Frame;
            document.Q("a").Style.SetNumber("--probe-progress",.6f);document.SynchronizeLayout();
            Assert.AreEqual(.2f,first.Style.GetNumber("--probe-progress"));Assert.AreEqual(.6f,factory.Instances[0].Frame.Style.GetNumber("--probe-progress"));
            Assert.AreEqual(.8f,factory.Instances[1].Frame.Style.GetNumber("--probe-progress"));
            Assert.Throws<InvalidOperationException>(()=>{var ignored=document.RenderCommandBuffer;});
        }
        [UnityTest] public IEnumerator HiddenRemovedUnsubmittedAndUnloadedInstancesReleaseExactlyOnce()
        {
            document.SynchronizeLayout();document.Q("a").AddToClassList("hidden");document.SynchronizeLayout();yield return AwaitResources(1);
            Assert.AreEqual(1,factory.Resources);Assert.AreEqual(1,factory.Released);
            document.Q("a").RemoveFromClassList("hidden");document.SynchronizeLayout();Assert.AreEqual(3,factory.Instances.Count);Assert.AreEqual(2,factory.Resources);
            document.Q("b").Destroy();document.SynchronizeLayout();yield return AwaitResources(1);Assert.AreEqual(1,factory.Resources);Assert.AreEqual(2,factory.Released);
            document.Unload();yield return AwaitResources(0);Assert.AreEqual(0,factory.Resources);Assert.AreEqual(3,factory.Released);
        }
        [Test] public void PrepareAndRecordRejectDomMutationButAllowImmutableSnapshotReads()
        {
            var a=document.Q("a");
            factory.DuringPrepare=frame=>{Assert.DoesNotThrow(()=>frame.Style.GetNumber("--probe-progress"));Assert.Throws<InvalidOperationException>(()=>a.Style.SetNumber("opacity",.5f));Assert.Throws<InvalidOperationException>(()=>document.CreateElement());Assert.Throws<InvalidOperationException>(()=>document.Unload());};
            factory.DuringRecord=context=>Assert.Throws<InvalidOperationException>(()=>a.AddToClassList("hidden"));
            document.SynchronizeLayout();Assert.That(factory.Instances[0].Records,Is.GreaterThan(0));
        }
        [UnityTest] public IEnumerator InvalidDefinitionsAndUnknownOrFailedInstancesDoNotLeak()
        {
            Assert.Throws<InvalidOperationException>(()=>document.Effects.Register(Definition("later",factory)));
            document.SynchronizeLayout();document.Q("a").AddToClassList("unknown");
            var unknown=Assert.Throws<InvalidOperationException>(()=>document.SynchronizeLayout());Assert.That(unknown.Message,Does.Contain("missing-probe").And.Contain("#a"));
            document.Q("a").RemoveFromClassList("unknown");document.Q("a").AddToClassList("failing");
            Assert.Throws<InvalidOperationException>(()=>document.SynchronizeLayout());
            document.Unload();yield return AwaitResources(0);Assert.AreEqual(0,factory.Resources);
            document.Effects.Register(Definition("extra",factory));Assert.Throws<InvalidOperationException>(()=>document.Effects.Register(Definition("extra",factory)));
        }
    }
}
