using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using URUI.Documents;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class FeatherMaskTests
    {
        [UnityTest]
        public IEnumerator FeatheredMaskPreservesOpacityAndFollowsResizing()=>Verify(false);

        [UnityTest]
        public IEnumerator FeatheredMaskPreservesSrpTargetAndFollowingSibling()=>Verify(true);

        static IEnumerator Verify(bool srpIntermediate)
        {
            var host=new GameObject("Independent feather mask document");
            var cameraObject=new GameObject("Main Camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.blue;
            camera.allowHDR=true;
            var target=new RenderTexture(Screen.width,Screen.height,0);target.Create();camera.targetTexture=target;
            var read=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            FeatherMaskPainter painter=null;
            var previousPipeline=GraphicsSettings.defaultRenderPipeline;var previousQuality=QualitySettings.renderPipeline;
            var intermediate=srpIntermediate?new RenderTexture(Screen.width/2,Screen.height/2,0):null;
            var pipeline=srpIntermediate?ScriptableObject.CreateInstance<LayerMaskTests.IntermediatePipelineAsset>():null;
            var submissions=0;
            try
            {
                var document=EffectHtmlFixtureAssets.Create(host);
                document.TextResources=new[]{new TextResource("/URUI.Effect.Tests/Feathered.styles.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Feathered.styles")),};
                EffectHtmlFixtureAssets.Load(document,"URUI.Effect.Tests/Feathered");
                var node=document.Q("masked");painter=new FeatherMaskPainter(node);
                if(srpIntermediate)
                {
                    intermediate.Create();
                    pipeline.draw=context=>
                    {
                        context.SetupCameraProperties(camera);
                        using var setup=new CommandBuffer();setup.SetRenderTarget(intermediate);setup.ClearRenderTarget(false,true,Color.blue);
                        context.ExecuteCommandBuffer(setup);
                        if(document.SubmitRendering(context,intermediate,new Rect(0,0,intermediate.width,intermediate.height))){submissions++;}
                    };
                    QualitySettings.renderPipeline=null;GraphicsSettings.defaultRenderPipeline=pipeline;
                }
                for(var frame=0;frame<4;frame++){yield return null;}
                Color Sample(int x,int y)
                {
                    var previous=RenderTexture.active;
                    var sampled=intermediate?intermediate:target;
                    try{RenderTexture.active=sampled;read.ReadPixels(new Rect(x*sampled.width/target.width,sampled.height-y*sampled.height/target.height-1,1,1),0,0);read.Apply();var value=read.GetPixel(0,0);return sampled.sRGB?value.linear:value;}
                    finally{RenderTexture.active=previous;}
                }
                if(srpIntermediate){Assert.That(submissions,Is.GreaterThan(0));}else{camera.Render();}
                var center=Sample(70,60);var top=Sample(70,22);var bottom=Sample(70,110);
                float Expected(float encoded)=>EffectHtmlFixtureAssets.TargetChannel(encoded);
                Assert.That(center.r,Is.EqualTo(Expected(.5f)).Within(.025f),"浏览器 sRGB source-over 只应用一次组透明度。");
                Assert.That(center.b,Is.EqualTo(Expected(.5f)).Within(.025f));
                Assert.That(top.r,Is.InRange(Expected(.025f),Expected(.12f)));Assert.That(bottom.r,Is.InRange(Expected(.08f),Expected(.17f)));
                node.Style.SetHeight(140);yield return null;yield return null;if(!srpIntermediate){camera.Render();}
                var resized=Sample(70,150);Assert.That(resized.r,Is.InRange(Expected(.08f),Expected(.17f)));
                node.AddToClassList("opaque");yield return null;yield return null;if(!srpIntermediate){camera.Render();}
                var opaque=Sample(70,60);var sibling=Sample(220,50);var empty=Sample(10,50);
                Assert.That(opaque.r,Is.GreaterThan(.95f));Assert.That(opaque.b,Is.LessThan(.02f));
                Assert.That(sibling.g,Is.GreaterThan(.95f));Assert.That(sibling.r,Is.LessThan(.02f));
                Assert.That(empty.b,Is.GreaterThan(.95f));Assert.That(empty.r,Is.LessThan(.02f));
                document.Unload();painter.Dispose();painter=null;
            }
            finally
            {
                if(pipeline){pipeline.draw=null;}
                GraphicsSettings.defaultRenderPipeline=previousPipeline;QualitySettings.renderPipeline=previousQuality;
                painter?.Dispose();Object.DestroyImmediate(host);camera.targetTexture=null;Object.DestroyImmediate(cameraObject);
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(read);
                if(intermediate){intermediate.Release();Object.DestroyImmediate(intermediate);}
                if(pipeline){Object.DestroyImmediate(pipeline);}
            }
        }
    }
}
