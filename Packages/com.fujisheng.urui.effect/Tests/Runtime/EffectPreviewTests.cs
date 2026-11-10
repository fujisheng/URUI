using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace URUI.Effect.Tests
{
    public sealed class EffectPreviewTests
    {
        [UnityTest]
        public IEnumerator PreviewUsesTheProductionParametersAndGeometry()
        {
            using var fixture=new EffectRenderFixture();fixture.Load("");fixture.Render();var controller=fixture.Module.GetController(fixture.Document.Q("subject"));
            var preset=ScriptableObject.CreateInstance<EffectPreset>();var target=new RenderTexture(256,256,0,RenderTextureFormat.ARGBHalf);target.Create();
            var read=new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);
            var cases=new[]{
                new EffectParameters{ToneFilter=ToneFilter.Sepia,ColorFilter=ColorFilter.HsvModifier,Color=new Color(-.2f,.1f,.2f,.8f)},
                new EffectParameters{SamplingFilter=SamplingFilter.RgbShift,SamplingIntensity=.4f},
                new EffectParameters{ShadowMode=ShadowMode.Outline8,ShadowDistance=new Vector2(3,-3),ShadowColor=Color.blue,GradationMode=GradationMode.Angle,GradationColor1=Color.red,GradationColor2=Color.cyan},
                new EffectParameters{TransitionFilter=TransitionFilter.Dissolve,TransitionTexture=fixture.Pattern,TransitionTextureSpeed=new Vector2(.25f,.125f),TransitionProgress=.35f,EdgeMode=EdgeMode.Plain},
                new EffectParameters{ShadowMode=ShadowMode.Mirror,ShadowDistance=new Vector2(0,-2),Flip=Flip.Horizontal|Flip.Effect},
                new EffectParameters{BlendType=BlendType.Additive,ColorFilter=ColorFilter.Multiply,Color=new Color(.2f,.3f,.4f,.5f)} };
            try
            {
                for(var index=0;index<cases.Length;index++)
                {
                    var p=cases[index];preset.SetParameters(p);controller.Apply(preset);
                    using var preview=new EffectPreview(p);using var clock=fixture.Document.Effects.BindClock(controller.Element,new PreviewClock());
                    var expected=fixture.Render("Preview.Document."+index);
                    preview.Render(fixture.Source,new Rect(64,64,128,128),1.25,target,fixture.Camera.backgroundColor);
                    var previous=RenderTexture.active;try{RenderTexture.active=target;read.ReadPixels(new Rect(0,0,256,256),0,0);read.Apply();}finally{RenderTexture.active=previous;}
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(fixture.Output,"Preview.Actual."+index+".png"),read.EncodeToPNG());
                    var actual=read.GetPixels();double total=0;float max=0;bool finite=true;
                    for(var i=0;i<actual.Length;i++)for(var c=0;c<4;c++){finite&=float.IsFinite(actual[i][c]);var error=Mathf.Abs(Mathf.Clamp01(actual[i][c])-Mathf.Clamp01(expected[i][c]));max=Mathf.Max(max,error);total+=error;}
                    Assert.That(finite,Is.True);Assert.That(max,Is.LessThanOrEqualTo(4f/255),"case "+index);Assert.That(total/(actual.Length*4),Is.LessThanOrEqualTo(.5/255),"case "+index);
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(preset);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);}
            yield return null;
        }
        sealed class PreviewClock:URUI.Animation.IUiClock {public double Now=>1.25;public float Delta=>0;}
    }
}
