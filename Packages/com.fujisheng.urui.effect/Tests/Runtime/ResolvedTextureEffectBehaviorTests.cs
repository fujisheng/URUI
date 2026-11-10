using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Effect.Rendering;
namespace URUI.Effect.Tests
{
    public sealed partial class ResolvedTextureEffectTests
    {
        static Material Material(Color color)
        {
            var result=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            result.SetFloat("_FilterMode",1);result.SetFloat("_ColorBlend",1);result.SetTexture("_PatternTex",Texture2D.whiteTexture);result.SetVector("_BandColor",color);return result;
        }
        const string Properties="} @property --test-preset {syntax:'a | b';inherits:false;initial-value:a;} @property --test-rate {syntax:'<number>';inherits:false;initial-value:0;} img{";
        [UnityTest]
        public IEnumerator ResourceKeysChangeOnlyTheirOwnInstanceAndKeepMaterialsImmutable()
        {
            using var fixture=new EffectRenderFixture();var a=Material(Color.red);var b=Material(Color.blue);
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.DefineResolved("resolved",key=>key=="a"?a:b,"--test-preset"));
                fixture.Load("--urui-effect:none;--urui-image-effect:resolved;"+Properties,"<img id='subject' src='input.png'><img id='target' src='input.png' style='left:0;width:64px;--test-preset:b;'>","img");
                var first=fixture.Render();var center=first[128*256+128];var left=first[128*256+32];
                Assert.That(center.r,Is.GreaterThan(.5));Assert.That(center.b,Is.LessThan(.2));Assert.That(left.b,Is.GreaterThan(.5));Assert.That(left.r,Is.LessThan(.2));
                fixture.Document.Q("subject").Style.Set("--test-preset","b");var second=fixture.Render();Assert.That(second[128*256+128].b,Is.GreaterThan(.5));Assert.That(second[128*256+32],Is.EqualTo(left));
                Assert.That(a.GetVector("_BandColor"),Is.EqualTo((Vector4)Color.red));Assert.That(b.GetVector("_BandColor"),Is.EqualTo((Vector4)Color.blue));
            }
            finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator RegisteredRateDefaultDoesNotReplaceTheAuthoredMaterialRate()
        {
            using var fixture=new EffectRenderFixture();var material=Material(Color.red);material.SetFloat("_FilterMode",2);material.SetTexture("_PatternTex",fixture.Pattern);material.SetFloat("_Rate",.7f);material.SetFloat("_Width",.5f);material.SetFloat("_Softness",.5f);
            try
            {
                fixture.Document.Effects.Register(TextureColorEffect.Define("fixed",material));
                fixture.Document.Effects.Register(TextureColorEffect.DefineResolved("resolved",_=>material,"--test-preset","--test-rate"));
                fixture.Load("--urui-effect:none;--urui-image-effect:fixed;"+Properties);var expected=fixture.Render();
                fixture.Load("--urui-effect:none;--urui-image-effect:resolved;"+Properties);var initial=fixture.Render();float max=0;
                for(var i=0;i<expected.Length;i++)for(var c=0;c<4;c++)max=Mathf.Max(max,Mathf.Abs(expected[i][c]-initial[i][c]));Assert.That(max,Is.LessThan(.0001));
                fixture.Document.Q("subject").Style.SetNumber("--test-rate",.1f);var overridden=fixture.Render();float difference=0;
                for(var i=0;i<expected.Length;i++)difference=Mathf.Max(difference,Mathf.Abs(initial[i].r-overridden[i].r));Assert.That(difference,Is.GreaterThan(.05));
            }
            finally{UnityEngine.Object.DestroyImmediate(material);}
            yield return null;
        }
    }
}
