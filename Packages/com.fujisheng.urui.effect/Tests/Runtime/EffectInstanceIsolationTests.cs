using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Resources;
using URUI.Effect.Rendering;
namespace URUI.Effect.Tests
{
    public sealed class EffectInstanceIsolationTests
    {
        [UnityTest] public IEnumerator SamePresetDifferentSizesAndRatesKeepIndependentPixelsAndOpacity()
        {
            var host=new GameObject("Independent image effects");var cameraObject=new GameObject("Image effect camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            var target=new RenderTexture(Screen.width,Screen.height,0,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;
            var authored=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            var pattern=new Texture2D(64,2,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp};var colors=new Color[128];for(int i=0;i<128;i++)colors[i]=new Color(1,1,1,(i%64)/63f);pattern.SetPixels(colors);pattern.Apply();
            authored.SetTexture("_PatternTex",pattern);authored.SetFloat("_FilterMode",2);authored.SetFloat("_ColorBlend",1);authored.SetVector("_BandColor",new Vector4(1,0,0,1));
            var read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try
            {
                var document=EffectHtmlFixtureAssets.Create(host);document.Images=new[]{new ImageAsset("white.png",Texture2D.whiteTexture)};
                document.Effects.Register(TextureColorEffect.Define("probe-texture",authored,"--image-rate"));EffectHtmlFixtureAssets.Load(document,"URUI.Effect.Tests/TextureInstances");
                for(int i=0;i<4;i++)yield return null;camera.Render();
                Color Sample(int x,int y){var old=RenderTexture.active;try{RenderTexture.active=target;read.ReadPixels(new Rect(x,target.height-y-1,1,1),0,0);read.Apply();return read.GetPixel(0,0);}finally{RenderTexture.active=old;}}
                var a=Sample(44,50);var b=Sample(228,80);
                Assert.That(a.r,Is.EqualTo(.5f).Within(.025f));Assert.That(b.r,Is.EqualTo(.5f).Within(.025f));
                Assert.That(a.g,Is.LessThan(.4f));Assert.That(b.g,Is.GreaterThan(.47f),"共享程序不能共用节点的 Rate 或尺寸参数");
                Assert.That(a.a,Is.EqualTo(.5f).Within(.025f));Assert.That(b.a,Is.EqualTo(.5f).Within(.025f));document.Unload();
            }
            finally{UnityEngine.Object.DestroyImmediate(host);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);GpuRetirement.Retire(()=>{UnityEngine.Object.Destroy(authored);UnityEngine.Object.Destroy(pattern);});}
        }
    }
}
