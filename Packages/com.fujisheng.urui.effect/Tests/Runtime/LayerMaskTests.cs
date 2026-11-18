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
        sealed class OriginalMaskReference : URUI.Rendering.Effects.IEffectFactory, URUI.Rendering.Effects.IElementEffect
        {
            public URUI.Rendering.Effects.IElementEffect Create(URUI.Rendering.Effects.EffectContext context)=>this;
            public void Prepare(URUI.Rendering.Effects.EffectFrame frame) { }
            public void Record(URUI.Rendering.Effects.EffectRenderContext context)=>context.DrawOriginal();
            public void Dispose() { }
        }
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
        [Test]
        public void UnsubmittedComplexMaskParameterChangesKeepTheirTargetStorage()
        {
            var host=new GameObject("Unsubmitted mask storage");var cameraObject=new GameObject("Mask camera",typeof(Camera));cameraObject.tag="MainCamera";
            var target=new RenderTexture(128,128,0);target.Create();cameraObject.GetComponent<Camera>().targetTexture=target;
            try {
                var document=EffectHtmlFixtureAssets.Create(host);document.Surface.Viewport=new Vector2Int(128,128);document.Surface.Input.UseUnityInput=false;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#masked{width:96px;height:96px;--urui-mask-image:shader(feather-mask);--urui-mask-feather-top:32px;}.red{position:absolute;width:80px;height:96px;background:rgba(255,0,0,.5);}.green{position:absolute;left:16px;width:80px;height:96px;background:rgba(0,255,0,.5);}</style></head><body><div id='masked'><div class='red'></div><div class='green'></div></div></body></html>","/unsubmitted-mask.html");
                var node=document.Q("masked");using var painter=new FeatherMaskPainter(node);
                void Cycle(int i){node.Style.Set("--urui-mask-feather-top",i%2==0?"32px":"40px");document.SynchronizeLayout();}
                for(var i=0;i<64;i++)Cycle(i);
                var before=URUI.Rendering.Effects.EffectStatistics.GetTargets();
                using var allocations=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory,"GC.Alloc",8192,Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                for(var i=0;i<32;i++)Cycle(i);allocations.Stop();
                var after=URUI.Rendering.Effects.EffectStatistics.GetTargets();
                Assert.That(after.CreatedTargets+after.Reuses-before.CreatedTargets-before.Reuses,Is.Zero,"Re-recording private unsubmitted content needs no new target lease.");
                Assert.That(allocations.Valid&&!allocations.WrappedAround,Is.True);long events=0;for(var i=0;i<allocations.Count;i++)events+=allocations.GetSample(i).Count;Assert.That(events,Is.Zero);
            }
            finally {UnityEngine.Object.DestroyImmediate(host);cameraObject.GetComponent<Camera>().targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }

        [UnityTest]
        public IEnumerator MaskImagePreservesNestedAlphaAndFollowingSibling()=>Verify(false);

        [UnityTest]
        public IEnumerator MaskImagePreservesSrpIntermediateTargetAndFollowingSibling()=>Verify(true);

        [UnityTest]
        public IEnumerator TerminalMaskClipsLongContentStorageAndPreservesFractionalFeatherPixels()
        {
            var existingTargets=new HashSet<int>();
            foreach(var texture in UnityEngine.Resources.FindObjectsOfTypeAll<RenderTexture>())existingTargets.Add(texture.GetInstanceID());
            var host=new GameObject("Bounded terminal mask");
            var cameraObject=new GameObject("Terminal mask camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.blue;
            var target=new RenderTexture(256,128,0,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            var read=new Texture2D(256,128,TextureFormat.RGBA32,false,true);
            try
            {
                var document=EffectHtmlFixtureAssets.Create(host);document.Surface.Viewport=new Vector2Int(256,128);
                document.Surface.Input.UseUnityInput=false;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}"+
                    ".mask{position:absolute;top:16px;width:64px;height:64px;transform-origin:0 0;--urui-mask-image:shader(feather-mask);--urui-mask-feather-top:12px;}"+
                    "#long{left:16px;}#reference{left:144px;}.red,.green{position:absolute;top:0;height:64px;width:64px;}"+
                    ".red{background:rgba(255,0,0,.5);}.green{background:rgba(0,255,0,.5);}"+
                    "#long .red,#long .green{left:-1024px;width:2100px;}"+
                    "</style></head><body><div id='long' class='mask'><div class='red'></div><div class='green'></div></div>"+
                    "<div id='reference' class='mask'><div class='red'></div><div class='green'></div></div></body></html>","/terminal-mask.html");
                var large=document.Q("long");var reference=document.Q("reference");
                using var largePainter=new FeatherMaskPainter(large);using var referencePainter=new FeatherMaskPainter(reference);
                foreach(var scale in new[]{1f,1.13f})
                {
                    large.SetTransform(new Vector2(.25f,.5f),Vector2.one*scale);
                    reference.SetTransform(new Vector2(.25f,.5f),Vector2.one*scale);
                    document.SynchronizeLayout();for(var frame=0;frame<5;frame++)yield return null;
                    camera.Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=target;read.ReadPixels(new Rect(0,0,256,128),0,0);read.Apply();}
                    finally{RenderTexture.active=previous;}
                    Assert.That(read.GetPixel(48,128-48-1).g,Is.GreaterThan(.1f),"阳性像素必须证明内容已实际绘制。");
                    // 相同遮罩内的长内容与紧凑参考应保持相同像素，包括羽化和亚像素边缘。
                    for(var y=8;y<104;y++)for(var x=8;x<104;x++)
                    {
                        var actual=read.GetPixel(x,128-y-1);var expected=read.GetPixel(x+128,128-y-1);
                        Assert.That(Mathf.Max(Mathf.Abs(actual.r-expected.r),Mathf.Abs(actual.g-expected.g),Mathf.Abs(actual.b-expected.b),Mathf.Abs(actual.a-expected.a)),Is.LessThanOrEqualTo(2f/255),$"scale={scale} pixel=({x},{y})");
                    }
                    foreach(var texture in UnityEngine.Resources.FindObjectsOfTypeAll<RenderTexture>())
                    {
                        if(existingTargets.Contains(texture.GetInstanceID())||!texture.name.StartsWith("URUI pooled effect target"))continue;
                        Assert.That(texture.width,Is.LessThanOrEqualTo(256),"最终遮罩之外的滚动内容不应扩大离屏目标。");
                        Assert.That(texture.height,Is.LessThanOrEqualTo(128));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(host);camera.targetTexture=null;Object.DestroyImmediate(cameraObject);
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(read);
            }
        }

        [UnityTest]
        public IEnumerator ScaledRootTerminalMaskMatchesAnUncroppedSource()
        {
            var host=new GameObject("Scaled terminal mask");
            var cameraObject=new GameObject("Scaled mask camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;
            var target=new RenderTexture(512,256,0,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            var read=new Texture2D(512,256,TextureFormat.RGBA32,false,true);
            var previous=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<IntermediatePipelineAsset>();var submissions=0;
            try
            {
                var document=EffectHtmlFixtureAssets.Create(host);document.Surface.Viewport=new Vector2Int(256,128);
                document.Surface.Input.UseUnityInput=false;
                document.Effects.Register(new URUI.Rendering.Effects.EffectDefinition("mask-reference",URUI.Rendering.Effects.EffectKind.Element,new OriginalMaskReference(),"mask-reference","premultiplied"));
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}"+
                    ".mask{position:absolute;top:16px;width:64px;height:64px;--urui-mask-image:shader(feather-mask);}"+
                    "#actual{left:16px;}#reference{left:144px;--urui-effect:mask-reference;}"+
                    ".red,.green{position:absolute;left:-1024px;top:0;width:2100px;height:64px;}"+
                    ".red{background:rgba(255,0,0,.5);}.green{background:rgba(0,255,0,.5);}"+
                    "</style></head><body><div id='actual' class='mask'><div class='red'></div><div class='green'></div></div>"+
                    "<div id='reference' class='mask'><div class='red'></div><div class='green'></div></div></body></html>","/scaled-terminal-mask.html");
                using var actualPainter=new FeatherMaskPainter(document.Q("actual"));
                using var referencePainter=new FeatherMaskPainter(document.Q("reference"));
                pipeline.draw=context=>{
                    context.SetupCameraProperties(camera);using var setup=new CommandBuffer();
                    setup.SetRenderTarget(target);setup.ClearRenderTarget(false,true,Color.blue);context.ExecuteCommandBuffer(setup);
                    if(document.SubmitRendering(context,target,new Rect(0,0,512,256)))submissions++;
                };
                QualitySettings.renderPipeline=null;GraphicsSettings.defaultRenderPipeline=pipeline;
                for(var frame=0;frame<6;frame++)yield return null;
                Assert.That(submissions,Is.GreaterThan(0));
                var active=RenderTexture.active;
                try{RenderTexture.active=target;read.ReadPixels(new Rect(0,0,512,256),0,0);read.Apply();}
                finally{RenderTexture.active=active;}
                Assert.That(read.GetPixel(96,256-96-1).g,Is.GreaterThan(.1f));
                // 参考含明确的原图捕获消费者，保留完整输入；比较真正2倍输出下的硬边两侧。
                for(var y=16;y<208;y++)for(var x=16;x<208;x++)
                {
                    var actual=read.GetPixel(x,256-y-1);var expected=read.GetPixel(x+256,256-y-1);
                    Assert.That(Mathf.Max(Mathf.Abs(actual.r-expected.r),Mathf.Abs(actual.g-expected.g),Mathf.Abs(actual.b-expected.b),Mathf.Abs(actual.a-expected.a)),Is.LessThanOrEqualTo(2f/255),$"pixel=({x},{y})");
                }
            }
            finally
            {
                pipeline.draw=null;GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=quality;
                Object.DestroyImmediate(host);camera.targetTexture=null;Object.DestroyImmediate(cameraObject);
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(read);Object.DestroyImmediate(pipeline);
            }
        }

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
