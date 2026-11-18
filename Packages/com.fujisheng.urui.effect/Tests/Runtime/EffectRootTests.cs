using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace URUI.Effect.Tests
{
    public sealed class EffectRootTests
    {
        [UnityTest]
        public IEnumerator LinkedRootChangesTheActualGpuGradientCoordinates()
        {
            using var fixture=new EffectRenderFixture();var pixels=new Color[4096];for(var i=0;i<pixels.Length;i++)pixels[i]=Color.white;fixture.Source.SetPixels(pixels);fixture.Source.Apply();
            fixture.Load("--urui-fx-gradation-mode:horizontal;--urui-fx-gradation-color1:red;--urui-fx-gradation-color2:blue;",
                "<img id='subject' class='fx' src='input.png' style='left:16px;width:64px;height:64px;'><img id='peer' class='fx' src='input.png' style='left:160px;width:64px;height:64px;'>",".fx");
            var source=fixture.Module.GetController(fixture.Document.Q("subject"));var target=fixture.Module.GetController(fixture.Document.Q("peer"));
            Color[] shared;using(target.Link(source,true))shared=fixture.Render();
            yield return null;
            Color[] own;using(target.Link(source,false))own=fixture.Render();
            var index=(255-96)*256+176;
            Assert.That(Mathf.Abs(shared[index].r-own[index].r)+Mathf.Abs(shared[index].b-own[index].b),Is.GreaterThan(.25f),"公开 Frame 的根和实际 Shader 使用的根必须一致。");
            yield return null;
        }
        [UnityTest]
        public IEnumerator ZeroScaleCapturedUiIsEmptyAndFinite()
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("transform:scale(0);--urui-fx-sampling-filter:blur-fast;","<div id='subject'><img src='input.png' style='width:128px;height:128px;'><img src='input.png' style='position:absolute;left:16px;top:16px;width:128px;height:128px;'></div>");
            foreach(var pixel in fixture.Render()){Assert.That(float.IsFinite(pixel.a),Is.True);Assert.That(pixel.a,Is.LessThan(.002f));}
            yield return null;
        }
        [UnityTest]
        public IEnumerator FractionalFlipUsesTheSameRoundingAsTheLayoutContract()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("--urui-fx-flip:2;");var expected=fixture.Render();yield return null;
            fixture.Document.Q("subject").Style.SetNumber("--urui-fx-flip",2.5f);var actual=fixture.Render();
            for(var i=0;i<expected.Length;i++)for(var channel=0;channel<4;channel++)Assert.That(actual[i][channel],Is.EqualTo(expected[i][channel]).Within(.002f));
            yield return null;
        }
        [UnityTest]
        public IEnumerator TwoDistinctUiSourcesKeepTheSameBlurKernelInOneBatch()
        {
            Texture2D clone=null;
            try {
                using var fixture=new EffectRenderFixture();clone=Object.Instantiate(fixture.Source);
                fixture.Document.Images=new[]{new URUI.Resources.ImageAsset("input.png",fixture.Source),new URUI.Resources.ImageAsset("duplicate.png",clone)};
                string Content(string second)=>"<img class='fx' src='input.png' style='left:24px;width:64px;height:64px;'><img class='fx' src='"+second+"' style='left:128px;width:64px;height:64px;'>";
                fixture.Load("--urui-fx-sampling-filter:blur-detail;",Content("input.png"),".fx");var expected=fixture.Render();yield return null;
                fixture.Load("--urui-fx-sampling-filter:blur-detail;",Content("duplicate.png"),".fx");var actual=fixture.Render();
                object renderer=null;foreach(var component in fixture.Surface.GetComponentsInChildren<MonoBehaviour>(true))if(component.GetType().FullName=="URUI.Internal.Hosting.RenderHost")renderer=component;
                Assert.That((int)renderer.GetType().GetProperty("RecordedGeometryDraws",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic).GetValue(renderer),Is.EqualTo(1));
                for(var i=0;i<expected.Length;i++)for(var channel=0;channel<4;channel++)Assert.That(actual[i][channel],Is.EqualTo(expected[i][channel]).Within(.002f));
            } finally {URUI.Resources.GpuRetirement.Retire(()=>Object.Destroy(clone));}
            yield return null;
        }
        [UnityTest]
        public IEnumerator CapturedInkIncludesGpuExpansionInDocumentPixelsUnderScale()
        {
            using var fixture=new EffectRenderFixture();
            fixture.Load("transform:scale(.5);transform-origin:0px 0px;--urui-fx-sampling-filter:blur-detail;--urui-fx-sampling-intensity:1;",
                "<div id='subject'><img src='input.png' style='display:block;width:128px;height:128px;'><img src='input.png' style='position:absolute;left:0px;top:0px;width:128px;height:128px;'></div>");
            fixture.Render();Assert.That(fixture.Document.Effects.TryGetFrame(fixture.Document.Q("subject"),out var frame),Is.True);Assert.That(frame.SourceIsCaptured,Is.True);Assert.That(frame.ElementToDocument.m00,Is.EqualTo(.5f).Within(.00001f));
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
            var entries=(System.Collections.IDictionary)typeof(URUI.Documents.Document).GetProperty("effectInstances",flags).GetValue(fixture.Document);var found=false;
            foreach(System.Collections.DictionaryEntry entry in entries)if(entry.Value.GetType().GetField("instance",flags).GetValue(entry.Value) is URUI.Rendering.Effects.IEffectBounds bounds) {
                found=true;Assert.That(bounds.InkBounds.xMin,Is.LessThanOrEqualTo(frame.ContentOffset.x-20),"捕获图扩边为文档像素；半缩放后的局部范围至少需要20px，不能裁掉GPU的10px扩边。");
            }
            Assert.That(found,Is.True);yield return null;
        }
        [UnityTest]
        public IEnumerator ThinImageInkIncludesTheFullGpuExpandedTriangle()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("width:1px;height:4px;--urui-fx-sampling-filter:rgb-shift;");fixture.Render();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
            var entries=(System.Collections.IDictionary)typeof(URUI.Documents.Document).GetProperty("effectInstances",flags).GetValue(fixture.Document);var found=false;
            foreach(System.Collections.DictionaryEntry entry in entries)if(entry.Value.GetType().GetField("instance",flags).GetValue(entry.Value) is URUI.Rendering.Effects.IEffectBounds bounds) {
                found=true;Assert.That(bounds.InkBounds.xMin,Is.LessThanOrEqualTo(-20.04f),"1px三角形的扩展系数为0.5/(1-0.002)，不能用固定的一半近似。");
            }
            Assert.That(found,Is.True);yield return null;
        }
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
