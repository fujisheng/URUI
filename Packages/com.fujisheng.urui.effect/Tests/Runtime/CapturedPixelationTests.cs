using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Rendering.Effects;
namespace URUI.Effect.Tests
{
    public sealed class CapturedPixelationTests
    {
        [UnityTest]
        public IEnumerator TargetPaddingDoesNotMoveThePixelationGrid()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.ImportAsset("Packages/com.fujisheng.urui.effect/Runtime/Resources/URUI.Effect/UIEffect.shader",UnityEditor.ImportAssetOptions.ForceUpdate|UnityEditor.ImportAssetOptions.ForceSynchronousImport);
#endif
            using var f=new EffectRenderFixture();
            var pixels=new Color[64*64];
            for(var y=0;y<64;y++)for(var x=0;x<64;x++)pixels[y*64+x]=new Color(x%4<2?1:0,y%4<2?1:0,.25f,1);
            f.Source.SetPixels(pixels);f.Source.Apply();
            Assert.That(f.Document.Effects.TryGet("ui",out var definition),Is.True);
            f.Document.Effects.Register(new EffectDefinition("ui-no-pad",EffectKind.Element,definition.Factory,"ui-no-pad:v1",definition.ColorContract,definition.Parameters));
            f.Document.Effects.Register(new EffectDefinition("ui-pad",EffectKind.Element,definition.Factory,"ui-pad:v1",definition.ColorContract,definition.Parameters,expansion:new Vector4(8,11,13,17)));
            const string css="width:64px;height:64px;--urui-fx-sampling-filter:pixelation;--urui-fx-sampling-intensity:.5;";
            const string html="<div id='subject'><img src='input.png' style='display:block;width:64px;height:64px;'></div>";
            f.Load(css+"--urui-effect:ui-no-pad;",html);var a=f.Render("PixelationWithoutPadding");
            Assert.That(f.Document.Effects.TryGetFrame(f.Document.Q("subject"),out var first),Is.True);Assert.That(first.SourceIsCaptured,Is.True);
            f.Load(css+"--urui-effect:ui-pad;",html);var b=f.Render("PixelationWithPadding");
            Assert.That(f.Document.Effects.TryGetFrame(f.Document.Q("subject"),out var second),Is.True);Assert.That(second.SourceIsCaptured,Is.True);
            Assert.That(second.SourcePixelSize,Is.Not.EqualTo(first.SourcePixelSize));
            // 两次输入都先经过同一个捕获路径，只改变存储 padding；主体区域验证量化相位。
            var maximum=0f;var visible=0;
            for(var y=80;y<112;y++)for(var x=80;x<112;x++)for(var c=0;c<4;c++)
            {var i=(255-y)*256+x;if(c==0&&a[i].a>.5f)visible++;maximum=Mathf.Max(maximum,Mathf.Abs(a[i][c]-b[i][c]));}
            Assert.That(visible,Is.EqualTo(32*32));Assert.That(maximum,Is.LessThanOrEqualTo(4f/255));yield return null;
        }
    }
}
