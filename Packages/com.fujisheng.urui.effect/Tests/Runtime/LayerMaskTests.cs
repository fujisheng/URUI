using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using URUI.Documents;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class LayerMaskTests
    {
        internal sealed class IntermediatePipelineAsset:RenderPipelineAsset
        {
            internal System.Action<ScriptableRenderContext> draw;
            public override System.Type pipelineType=>typeof(IntermediatePipeline);
            protected override RenderPipeline CreatePipeline()=>new IntermediatePipeline(this);
        }
        sealed class IntermediatePipeline:RenderPipeline
        {
            readonly IntermediatePipelineAsset asset;
            internal IntermediatePipeline(IntermediatePipelineAsset asset){this.asset=asset;}
            protected override void Render(ScriptableRenderContext context,List<Camera> cameras)
            { asset.draw?.Invoke(context); context.Submit(); }
        }
        [UnityTest]
        public IEnumerator MaskImagePreservesNestedAlphaAndFollowingSibling()=>Verify(false);

        [UnityTest]
        public IEnumerator MaskImagePreservesSrpIntermediateTargetAndFollowingSibling()=>Verify(true);

        [UnityTest]
        public IEnumerator SrpOffsetAndScaledViewportUseItsActualBackdrop()
        {
            var host=new GameObject("Viewport backdrop document");
            var cameraObject=new GameObject("Viewport camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;
            var target=new RenderTexture(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();camera.targetTexture=target;
            var background=new Texture2D(2,2,TextureFormat.RGBA32,false);background.filterMode=FilterMode.Point;
            background.SetPixels(new[]{Color.blue,Color.green,Color.yellow,Color.cyan});background.Apply();
            var read=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            var previousPipeline=GraphicsSettings.defaultRenderPipeline;var previousQuality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<IntermediatePipelineAsset>();
            var viewport=new Rect(128,64,128,128);var submissions=0;
            try
            {
                var document=EffectHtmlFixtureAssets.Create(host);document.Surface.Viewport=new Vector2Int(128,128);
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}"+
                    "#group{position:absolute;left:0;top:0;width:128px;height:128px;background-color:red;opacity:.5;}"+
                    "</style></head><body><div id='group'></div></body></html>","/viewport.html");
                pipeline.draw=context=>{
                    context.SetupCameraProperties(camera);
                    using var setup=new CommandBuffer();setup.Blit(background,target);context.ExecuteCommandBuffer(setup);
                    if(document.SubmitRendering(context,target,viewport))submissions++;
                };
                QualitySettings.renderPipeline=null;GraphicsSettings.defaultRenderPipeline=pipeline;
                Color Sample(int x,int y)
                {
                    var previous=RenderTexture.active;
                    try{RenderTexture.active=target;read.ReadPixels(new Rect(x,y,1,1),0,0);read.Apply();return read.GetPixel(0,0);}
                    finally{RenderTexture.active=previous;}
                }
                void Equal(Color actual,Color expected)
                {
                    Assert.That(actual.r,Is.EqualTo(expected.r).Within(.025f));
                    Assert.That(actual.g,Is.EqualTo(expected.g).Within(.025f));
                    Assert.That(actual.b,Is.EqualTo(expected.b).Within(.025f));
                }
                foreach(var region in new[]{new Rect(128,64,128,128),new Rect(128,64,96,160)})
                {
                    viewport=region;for(var frame=0;frame<4;frame++)yield return null;
                    Assert.Greater(submissions,0);
                    Equal(Sample(64,80),Color.blue);Equal(Sample(250,250),Color.cyan);
                    Equal(Sample(160,80),new Color(.5f,.5f,0));
                    Equal(Sample(160,160),new Color(.5f,.5f,.5f));
                }
            }
            finally
            {
                pipeline.draw=null;GraphicsSettings.defaultRenderPipeline=previousPipeline;QualitySettings.renderPipeline=previousQuality;
                Object.DestroyImmediate(host);camera.targetTexture=null;Object.DestroyImmediate(cameraObject);
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(background);Object.DestroyImmediate(read);Object.DestroyImmediate(pipeline);
            }
        }

        static IEnumerator Verify(bool srpIntermediate)
        {
            var host=new GameObject("Independent layer mask document");
            var cameraObject=new GameObject("Main Camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.blue;
            camera.allowHDR=true;
            var target=new RenderTexture(Screen.width,Screen.height,0);target.Create();camera.targetTexture=target;
            var mask=new Texture2D(2,2,TextureFormat.RGBA32,false,true);mask.SetPixels(new[]{new Color(1,1,1,.5f),new Color(1,1,1,.5f),new Color(1,1,1,.5f),new Color(1,1,1,.5f)});mask.Apply();
            var read=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
            var previousPipeline=GraphicsSettings.defaultRenderPipeline;
            var previousQuality=QualitySettings.renderPipeline;
            var intermediate=srpIntermediate?new RenderTexture(Screen.width/2,Screen.height/2,0):null;
            var pipeline=srpIntermediate?ScriptableObject.CreateInstance<IntermediatePipelineAsset>():null;
            var submissions=0;
            try
            {
                var document=EffectHtmlFixtureAssets.Create(host);document.Images=new[]{new ImageAsset("test-mask.png",mask)};
                document.TextResources=new[]{new TextResource("/URUI.Effect.Tests/LayerMasks.styles.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/LayerMasks.styles"))};
                EffectHtmlFixtureAssets.Load(document,"URUI.Effect.Tests/LayerMasks");
                if(srpIntermediate)
                {
                    intermediate.Create();
                    pipeline.draw=context=>
                    {
                        context.SetupCameraProperties(camera);
                        using var setup=new CommandBuffer();
                        setup.SetRenderTarget(intermediate);setup.ClearRenderTarget(false,true,Color.blue);
                        context.ExecuteCommandBuffer(setup);
                        if(document.SubmitRendering(context,intermediate,new Rect(0,0,intermediate.width,intermediate.height))){submissions++;}
                    };
                    QualitySettings.renderPipeline=null;GraphicsSettings.defaultRenderPipeline=pipeline;
                }
                for(var frame=0;frame<4;frame++){yield return null;}
                if(srpIntermediate){Assert.That(submissions,Is.GreaterThan(0),"必须观察到真实 SRP 提交。");}
                else{camera.Render();}
                Color Sample(int x,int y)
                {
                    var previous=RenderTexture.active;
                    var sampled=intermediate?intermediate:target;
                    try{RenderTexture.active=sampled;read.ReadPixels(new Rect(x*sampled.width/target.width,sampled.height-y*sampled.height/target.height-1,1,1),0,0);read.Apply();var value=read.GetPixel(0,0);return sampled.sRGB?value.linear:value;}
                    finally{RenderTexture.active=previous;}
                }
                var single=Sample(50,50);var nested=Sample(150,50);var sibling=Sample(220,50);var outside=Sample(10,50);
                float Expected(float encoded)=>EffectHtmlFixtureAssets.TargetChannel(encoded);
                Assert.That(single.r,Is.EqualTo(Expected(.5f)).Within(.025f));Assert.That(single.b,Is.EqualTo(Expected(.5f)).Within(.025f));
                Assert.That(nested.r,Is.EqualTo(Expected(.25f)).Within(.025f));Assert.That(nested.b,Is.EqualTo(Expected(.75f)).Within(.025f));
                Assert.That(sibling.g,Is.GreaterThan(.95f));Assert.That(sibling.r,Is.LessThan(.02f));
                Assert.That(outside.b,Is.GreaterThan(.95f));Assert.That(outside.r,Is.LessThan(.02f));
                document.Unload();
            }
            finally
            {
                if(pipeline){pipeline.draw=null;}
                GraphicsSettings.defaultRenderPipeline=previousPipeline;QualitySettings.renderPipeline=previousQuality;
                Object.DestroyImmediate(host);camera.targetTexture=null;Object.DestroyImmediate(cameraObject);target.Release();
                Object.DestroyImmediate(target);Object.DestroyImmediate(mask);Object.DestroyImmediate(read);
                if(intermediate){intermediate.Release();Object.DestroyImmediate(intermediate);}
                if(pipeline){Object.DestroyImmediate(pipeline);}
            }
        }
    }
}
