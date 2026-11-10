using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class EffectRootTests
    {
        [UnityTest]
        public IEnumerator RootResizeAndRemoval()
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("--urui-fx-transition-filter:shiny;--urui-fx-transition-texture:url('pattern.png');",
                "<div id='root' style='position:absolute;left:64px;top:64px;width:128px;height:128px;'></div><img id='subject' src='input.png' data-urui-effect-root='root'>");
            var original=fixture.Render();fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"),out var first);
            var layout=typeof(URUI.Rendering.Effects.EffectFrame).GetProperty("Layout");
            Assert.That(layout,Is.Not.Null,"根矩形、身份和完整坐标变换必须在回调前冻结");
            var root=fixture.Document.Q("root");root.Style.SetWidth(256);var resized=fixture.Render("RootResized");
            var difference=0f;for(var i=0;i<original.Length;i++)difference=Mathf.Max(difference,Mathf.Abs(original[i].b-resized[i].b));
            Assert.That(difference,Is.GreaterThan(.05f));
            fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"),out var second);
            Assert.That(second.SourceGeometry,Is.SameAs(first.SourceGeometry),"根改变不能重建原图网格");
            root.RemoveFromHierarchy();var removed=fixture.Render("RootRemoved");
            for(var i=0;i<removed.Length;i++)Assert.That(removed[i].b,Is.EqualTo(original[i].b).Within(.002f),"失效根回退节点自身，不访问旧身份");
            yield return null;
        }
        [UnityTest]
        public IEnumerator OverflowClipsExpandedInk()
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("left:64px;top:64px;width:32px;height:32px;--urui-fx-shadow-mode:outline8;--urui-fx-shadow-distance-x:12;--urui-fx-shadow-distance-y:-12;--urui-fx-shadow-iteration:2;",
                "<div style='position:absolute;left:0;top:0;width:96px;height:96px;overflow:hidden;'><img id='subject' src='input.png'></div>");
            var pixels=fixture.Render("ShadowClipped");
            Assert.That(pixels[(255-80)*256+99].a,Is.LessThan(.002f));
            var outsideOriginal=0f;for(var y=50;y<64;y++)for(var x=55;x<95;x++)outsideOriginal=Mathf.Max(outsideOriginal,pixels[(255-y)*256+x].a);
            Assert.That(outsideOriginal,Is.GreaterThan(.01f));yield return null;
        }
    }
}
