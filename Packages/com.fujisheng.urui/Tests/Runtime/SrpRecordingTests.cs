using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Tests
{
    public sealed class SrpRecordingTests
    {
        sealed class PipelineAsset : RenderPipelineAsset
        {
            internal Action<ScriptableRenderContext> Draw;
            public override Type pipelineType=>typeof(Pipeline);
            protected override RenderPipeline CreatePipeline()=>new Pipeline(this);
        }
        sealed class Pipeline : RenderPipeline
        {
            readonly PipelineAsset asset;
            internal Pipeline(PipelineAsset asset){this.asset=asset;}
            protected override void Render(ScriptableRenderContext context,List<Camera> cameras)
            {asset.Draw?.Invoke(context);context.Submit();}
        }
        [UnityTest]
        public IEnumerator SharedColorPassKeepsContextValidationAndPixels()
        {
            if(QualitySettings.activeColorSpace!=ColorSpace.Linear||SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Metal||!SystemInfo.supportsMemorylessTextures)
            {Assert.Ignore("此用例验证真实Metal共享颜色Pass。");}
            var previous=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<PipelineAsset>();
            var cameraObject=new GameObject("Shared color validation camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;camera.allowMSAA=false;
            var actual=new RenderTexture(64,64,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var reference=new RenderTexture(64,64,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            actual.Create();reference.Create();camera.targetTexture=actual;
            GameObject root=null;var invoked=false;var valid=false;Exception failure=null;
            using var clear=new CommandBuffer();
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;yield return null;
                root=new GameObject("Shared color validation Surface");var surface=root.AddComponent<Surface>();
                surface.Viewport=new Vector2Int(64,64);surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;
                pipeline.Draw=context=>
                {
                    if(invoked){return;}invoked=true;
                    try
                    {
                        document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{position:absolute;width:24px;height:24px;}</style></head><body><div style='opacity:.5;left:4px;top:4px;'><div style='background:red;'></div><div style='left:12px;top:12px;background:lime;'></div></div><div style='left:44px;top:44px;background:rgba(255,0,0,.25);'></div></body></html>","/shared-color-validation.html");
                        clear.SetRenderTarget(actual);clear.ClearRenderTarget(false,true,Color.blue);
                        clear.SetRenderTarget(reference);clear.ClearRenderTarget(false,true,Color.blue);
                        context.SetupCameraProperties(camera);context.ExecuteCommandBuffer(clear);
                        Assert.IsTrue(surface.SubmitRendering(context,actual,new Rect(0,0,64,64),URUI.Rendering.RenderTargetInfo.FromTexture(actual)));
                        Assert.Greater(surface.Host.RecordedRasterResolves,0,"必须实际录制颜色转换，不能由未命中路径虚假通过。");
                        var targets=(System.Collections.IDictionary)surface.Host.GetType().GetField("preparedLayerTargets",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(surface.Host);
                        var transient=false;
                        foreach(var value in targets.Values)
                        {
                            var texture=(RenderTexture)value.GetType().GetProperty("Texture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).GetValue(value);
                            transient|=texture.memorylessMode==RenderTextureMemoryless.Color;
                        }
                        Assert.IsTrue(transient,"必须实际使用原生共享Pass的memoryless附件。");
                        // 未知目标元数据使用既有独立转换，作为相同输入的完整像素参考。
                        Assert.IsTrue(surface.SubmitRendering(context,reference,new Rect(0,0,64,64)));
                        valid=context.SubmitForRenderPassValidation();context.Submit();
                    }
                    catch(Exception error){failure=error;}
                };
                for(var n=0;n<10&&!invoked;n++){yield return null;}
                pipeline.Draw=null;Assert.IsTrue(invoked);Assert.IsNull(failure);
                var a=UnityEngine.Rendering.AsyncGPUReadback.Request(actual,0);a.WaitForCompletion();
                var b=UnityEngine.Rendering.AsyncGPUReadback.Request(reference,0);b.WaitForCompletion();
                Assert.IsFalse(a.hasError||b.hasError);
                CollectionAssert.AreEqual(b.GetData<byte>().ToArray(),a.GetData<byte>().ToArray(),"共享与完整转换必须逐字节一致。");
                Assert.IsTrue(valid,"宿主录制的复制/清屏与原生Pass不能污染URP提交校验并触发整管线回退。");
            }
            finally
            {
                pipeline.Draw=null;if(root){UnityEngine.Object.DestroyImmediate(root);}camera.targetTexture=null;
                GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=quality;
                UnityEngine.Object.DestroyImmediate(pipeline);UnityEngine.Object.DestroyImmediate(cameraObject);
                actual.Release();reference.Release();UnityEngine.Object.DestroyImmediate(actual);UnityEngine.Object.DestroyImmediate(reference);
            }
        }
        [UnityTest]
        public IEnumerator SubmissionUsesActualTargetInformationAndUnknownIdentifiersRemainConservative()
        {
            if(QualitySettings.activeColorSpace!=ColorSpace.Linear||SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Metal)
                Assert.Ignore("验证Metal颜色附件路径的目标信息。");
            var fetch=UnityEngine.Resources.Load<Shader>("URUI/PremultipliedFetch");
            if(!fetch||!fetch.isSupported)Assert.Ignore("当前设备不支持颜色附件读取。");
            var previous=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<PipelineAsset>();
            var cameraObject=new GameObject("Output metadata camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=true;camera.allowMSAA=false;
            var half=new RenderTexture(32,32,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);half.Create();
            var ldr=new RenderTexture(32,32,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);ldr.Create();camera.targetTexture=half;
            GameObject root=null;var invoked=false;Exception failure=null;
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;yield return null;
                root=new GameObject("Output metadata Surface");var surface=root.AddComponent<Surface>();surface.Viewport=new Vector2Int(32,32);surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;
                pipeline.Draw=context=>
                {
                    if(invoked)return;invoked=true;
                    try
                    {
                        document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:24px;height:24px;background:red;}</style></head><body><div></div></body></html>","/output-info.html");
                        context.SetupCameraProperties(camera);
                        var viewport=new Rect(0,0,32,32);var identifier=new RenderTargetIdentifier(half);
                        bool Active()=>(bool)typeof(URUI.Internal.Hosting.RenderHost).GetField("framebufferFetchActive",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(surface.Host);
                        Assert.IsTrue(surface.SubmitRendering(context,identifier,viewport));Assert.IsFalse(Active(),"只有identifier时不能借用相机的格式证明。");
                        Assert.IsTrue(surface.SubmitRendering(context,half,viewport));Assert.IsTrue(Active());Assert.Zero(surface.Host.RecordedRasterResolves);
                        Assert.IsTrue(surface.SubmitRendering(context,identifier,viewport));Assert.IsFalse(Active(),"同一目标的信息变为未知也必须重新录制。");
                        Assert.IsTrue(surface.SubmitRendering(context,ldr,viewport));Assert.Greater(surface.Host.RecordedRasterResolves,0,"相机为Half也不能让线性LDR目标绕过编码累积。");
                    }
                    catch(Exception error){failure=error;}
                };
                for(var n=0;n<10&&!invoked;n++)yield return null;
                pipeline.Draw=null;Assert.IsTrue(invoked);Assert.IsNull(failure);
            }
            finally
            {
                pipeline.Draw=null;if(root)UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;
                GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=quality;
                UnityEngine.Object.DestroyImmediate(pipeline);UnityEngine.Object.DestroyImmediate(cameraObject);
                half.Release();ldr.Release();UnityEngine.Object.DestroyImmediate(half);UnityEngine.Object.DestroyImmediate(ldr);
            }
        }

        [UnityTest]
        public IEnumerator GeometryOnlyChangesAreVisibleToRepeatedSameFrameSubmissions()
        {
            var previous = GraphicsSettings.defaultRenderPipeline; var quality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<PipelineAsset>();
            var cameraObject = new GameObject("Geometry submit camera", typeof(Camera)); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>(); camera.allowHDR = false; camera.allowMSAA = false;
            var target = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create(); camera.targetTexture = target;
            GameObject root = null; var invoked = false; var submissions = 0; Exception failure = null;
            using var clear = new CommandBuffer();
            try
            {
                GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline; yield return null;
                root = new GameObject("Geometry submit Surface"); var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(32, 32); surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>(); document.Surface = surface;
                pipeline.Draw = context =>
                {
                    if (invoked) { return; } invoked = true;
                    try
                    {
                        document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:24px;height:24px;background:red;}</style></head><body><div id='color'></div></body></html>", "/geometry-submit.html");
                        clear.SetRenderTarget(target); clear.ClearRenderTarget(false, true, Color.blue);
                        context.SetupCameraProperties(camera); context.ExecuteCommandBuffer(clear);
                        document.SynchronizeGeometry();
                        if (surface.SubmitRendering(context, target, new Rect(0, 0, 32, 32))) { submissions++; }
                        // 同一目标、同一帧再次修改，必须更新记录，不能复用上一次红色命令。
                        document.Q("color").Style.Set("background-color", "lime"); document.SynchronizeGeometry();
                        if (surface.SubmitRendering(context, target, new Rect(0, 0, 32, 32))) { submissions++; }
                    }
                    catch (Exception error) { failure = error; }
                };
                for (var i = 0; i < 10 && !invoked; i++) { yield return null; }
                pipeline.Draw = null;
                Assert.That(invoked, Is.True); Assert.That(failure, Is.Null); Assert.That(submissions, Is.EqualTo(2));
                var active = RenderTexture.active; var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = target; read.ReadPixels(new Rect(8, 24, 1, 1), 0, 0); read.Apply();
                    var actual = read.GetPixel(0, 0);
                    Assert.That(actual.r, Is.LessThan(3f / 255)); Assert.That(actual.g, Is.GreaterThan(252f / 255));
                    Assert.That(actual.b, Is.LessThan(3f / 255));
                }
                finally { RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(read); }
            }
            finally
            {
                pipeline.Draw = null; if (root) { UnityEngine.Object.DestroyImmediate(root); }
                camera.targetTexture = null; GraphicsSettings.defaultRenderPipeline = previous; QualitySettings.renderPipeline = quality;
                UnityEngine.Object.DestroyImmediate(pipeline); UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
        [UnityTest]
        public IEnumerator MultipleResourceRetirementsPreserveDrawsUntilDelayedSrpSubmit()
        {
            var previous=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<PipelineAsset>();
            var cameraObject=new GameObject("Delayed retirement camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;camera.allowMSAA=false;
            var targets=new RenderTexture[3];
            for(var i=0;i<targets.Length;i++){targets[i]=new RenderTexture(64,64,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);targets[i].Create();}
            camera.targetTexture=targets[0];GameObject root=null;var invoked=false;var submissions=0;Exception failure=null;
            using var clear=new CommandBuffer();
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;yield return null;
                root=new GameObject("Delayed retirement Surface");var surface=root.AddComponent<Surface>();surface.Viewport=new Vector2Int(64,64);surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;
                pipeline.Draw=context=>
                {
                    if(invoked)return;invoked=true;
                    try
                    {
                        var colors=new[]{"red","lime","yellow"};
                        for(var i=0;i<targets.Length;i++)
                        {
                            // 同一回调内反复撤销 Mesh、缓存层、编码和背景目标；
                            // 三份绘制全部等到管线末尾才 Submit，不能借即时 fence 释放读取输入。
                            document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#group{width:"+(16+i*8)+"px;height:16px;opacity:.5;}#group div{position:absolute;width:16px;height:16px;background:"+colors[i]+";}</style></head><body><div id='group'><div></div><div style='left:4px;'></div></div></body></html>","/delayed-retirement.html");
                            document.SynchronizeLayout();
                            clear.Clear();clear.SetRenderTarget(targets[i]);clear.ClearRenderTarget(false,true,Color.blue);
                            context.SetupCameraProperties(camera);context.ExecuteCommandBuffer(clear);
                            if(surface.SubmitRendering(context,targets[i],new Rect(0,0,64,64)))submissions++;
                            GpuRetirement.Collect();
                        }
                        document.Unload();GpuRetirement.Collect();
                    }
                    catch(Exception error){failure=error;}
                };
                for(var i=0;i<10&&!invoked;i++)yield return null;
                pipeline.Draw=null;Assert.That(invoked,Is.True);Assert.That(failure,Is.Null);Assert.That(submissions,Is.EqualTo(3));
                var active=RenderTexture.active;var read=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
                try
                {
                    var colors=new[]{Color.red,Color.green,new Color(1,1,0)};
                    for(var i=0;i<targets.Length;i++)
                    {
                        RenderTexture.active=targets[i];read.ReadPixels(new Rect(8,64-8,1,1),0,0);read.Apply();
                        var expected=Color.Lerp(Color.blue,colors[i],.5f);if(QualitySettings.activeColorSpace==ColorSpace.Linear)expected=expected.linear;
                        var actual=read.GetPixel(0,0);Assert.That(actual.r,Is.EqualTo(expected.r).Within(3f/255),"r target "+i);
                        Assert.That(actual.g,Is.EqualTo(expected.g).Within(3f/255),"g target "+i);Assert.That(actual.b,Is.EqualTo(expected.b).Within(3f/255),"b target "+i);
                    }
                }
                finally{RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(read);}
                for(var i=0;i<4;i++){yield return null;GpuRetirement.Collect();}
            }
            finally
            {
                pipeline.Draw=null;if(root)UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;
                GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=quality;
                UnityEngine.Object.DestroyImmediate(pipeline);UnityEngine.Object.DestroyImmediate(cameraObject);
                foreach(var target in targets){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            }
        }

        [UnityTest]
        public IEnumerator TransferredSubmissionsStillConsumeTheSameAdmissionBudget()
        {
            var old=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<PipelineAsset>();
            var cameraObject=new GameObject("Admission transfer camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;camera.allowMSAA=false;
            var target=new RenderTexture(32,32,0,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            GameObject root=null;var invoked=false;var rejected=false;Exception failure=null;
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;yield return null;
                root=new GameObject("Admission transfer Surface");var surface=root.AddComponent<Surface>();surface.Viewport=new Vector2Int(32,32);surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:16px;height:16px;background:red;}</style></head><body><div></div></body></html>","/admission-transfer.html");document.SynchronizeLayout();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var host=typeof(Document).GetField("host",flags).GetValue(document);
                pipeline.Draw=context=>
                {
                    if(invoked)return;invoked=true;
                    try
                    {
                        for(var i=0;i<8;i++)if(!surface.SubmitRendering(context,target,new Rect(0,0,32,32)))throw new InvalidOperationException("未生成真实 SRP 提交。");
                        host.GetType().GetMethod("RetireLegacyImages",flags).Invoke(host,null);
                        try{surface.SubmitRendering(context,target,new Rect(0,0,32,32));}
                        catch(InvalidOperationException){rejected=true;}
                    }
                    catch(Exception error){failure=error;}
                };
                for(var i=0;i<10&&!invoked;i++)yield return null;
                pipeline.Draw=null;
                Assert.That(invoked,Is.True);Assert.That(failure,Is.Null);Assert.That(rejected,Is.True,"移动到全局退役不能让八份在途提交从准入计数消失。");
                for(var i=0;i<4;i++){yield return null;GpuRetirement.Collect();}
            }
            finally
            {
                pipeline.Draw=null;
                if(root)UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);
                GraphicsSettings.defaultRenderPipeline=old;QualitySettings.renderPipeline=quality;UnityEngine.Object.DestroyImmediate(pipeline);
            }
        }

        [UnityTest]
        public IEnumerator SurfaceCommandsPreserveTextureCameraTargetAcrossBufferBoundaries()
        {
            var previous=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<PipelineAsset>();
            var cameraObject=new GameObject("SRP surface boundary camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;camera.allowMSAA=false;
            const int width=64,height=64;
            var target=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            var output=new RenderTexture(16,16,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);output.Create();
            var red=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);red.SetPixel(0,0,Color.red);red.Apply();
            var green=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);green.SetPixel(0,0,Color.green);green.Apply();
            var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1),Vector3.up};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,1,2,0,2,3};
            var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Tests/RenderParameterContract"));
            var properties=new MaterialPropertyBlock();properties.SetFloat("_Mode",2);properties.SetFloat("_ParameterIndex",0);properties.SetTexture("_Parameters",green);
            GameObject root=null;IDisposable painter=null;var callbacks=0;var submissions=0;
            using var clear=new CommandBuffer();clear.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);clear.ClearRenderTarget(false,true,Color.blue);
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;yield return null;
                root=new GameObject("SRP surface boundary");var surface=root.AddComponent<Surface>();surface.Viewport=new Vector2Int(width,height);surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;document.ImageResolver=path=>new ImageLease(path.EndsWith("green.png",StringComparison.Ordinal)?(Texture)output:red);
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img,div{position:absolute;top:0;width:16px;height:16px;}</style></head><body><img src='red.png'><div id='surface-work' style='left:8px;'></div><img src='green.png' style='left:8px;'><img src='red.png' style='left:16px;'></body></html>","/srp-surface-boundary.html");
                painter=document.AttachPainter(document.Q("surface-work"),"surface-work",context=>
                {
                    callbacks++;context.RecordSurface(output,mesh,material,Matrix4x4.Ortho(0,1,1,0,-1,1),properties,0);
                });
                document.Q("surface-work").Style.Set("--urui-decorator","shader(surface-work)");
                pipeline.Draw=context=>
                {
                    context.SetupCameraProperties(camera);context.ExecuteCommandBuffer(clear);
                    if(surface.SubmitRendering(context,BuiltinRenderTextureType.CameraTarget,new Rect(0,0,width,height)))submissions++;
                };
                for(var frame=0;frame<64;frame++){document.SynchronizeLayout();yield return null;}
                Assert.That(callbacks,Is.GreaterThanOrEqualTo(64));Assert.That(submissions,Is.GreaterThanOrEqualTo(64));
                var read=new Texture2D(width,height,TextureFormat.RGBA32,false,true);var active=RenderTexture.active;
                try
                {
                    RenderTexture.active=target;read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();
                    foreach(var sample in new[]{(4,height-9,Color.red),(12,height-9,Color.green),(28,height-9,Color.red),(48,16,Color.blue)})
                    {
                        var pixel=read.GetPixel(sample.Item1,sample.Item2);var expected=sample.Item3;
                        Assert.That(pixel.r,Is.EqualTo(expected.r).Within(3f/255),$"r at {sample.Item1},{sample.Item2}");
                        Assert.That(pixel.g,Is.EqualTo(expected.g).Within(3f/255),$"g at {sample.Item1},{sample.Item2}");
                        Assert.That(pixel.b,Is.EqualTo(expected.b).Within(3f/255),$"b at {sample.Item1},{sample.Item2}");
                    }
                }
                finally{RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(read);}
            }
            finally
            {
                pipeline.Draw=null;painter?.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;
                GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=quality;
                UnityEngine.Object.DestroyImmediate(pipeline);UnityEngine.Object.DestroyImmediate(cameraObject);
                GpuRetirement.Retire(()=>{target.Release();output.Release();foreach(var value in new UnityEngine.Object[]{target,output,red,green,mesh,material})GpuRetirement.DestroyOwnedObject(value);});
            }
        }
        [UnityTest]
        public IEnumerator StableSrpRecordingDoesNotAttachBuiltInCameraBuffersOrAllocate()
        {
            var previous=GraphicsSettings.defaultRenderPipeline;var quality=QualitySettings.renderPipeline;
            var pipeline=ScriptableObject.CreateInstance<PipelineAsset>();
            var cameraObject=new GameObject("SRP recording camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.allowHDR=false;camera.allowMSAA=false;
            var target=new RenderTexture(64,64,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            GameObject root=null;var cameraWarnings=0;var submissions=0;
            var submitRecorder=default(ProfilerRecorder);var measureSubmit=false;long submitAllocations=0;var measuredSubmissions=0;
            Application.LogCallback logs=(message,stack,type)=>{if(message.Contains("CommandBuffer")&&message.Contains("built-in renderer"))cameraWarnings++;};
            Application.logMessageReceived+=logs;
            using var clear=new CommandBuffer();clear.SetRenderTarget(target);clear.ClearRenderTarget(false,true,Color.blue);
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;yield return null;
                Assert.That(GraphicsSettings.currentRenderPipeline,Is.SameAs(pipeline));
                root=new GameObject("SRP recording surface");var surface=root.AddComponent<Surface>();surface.Viewport=new Vector2Int(64,64);surface.Input.UseUnityInput=false;
                var document=root.AddComponent<Document>();document.Surface=surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:32px;height:32px;background:red;}</style></head><body><div></div></body></html>","/srp-recording.html");
                pipeline.Draw=context=>
                {
                    context.SetupCameraProperties(camera);context.ExecuteCommandBuffer(clear);
                    if(measureSubmit){submitRecorder.Reset();submitRecorder.Start();}
                    try{if(surface.SubmitRendering(context,target,new Rect(0,0,64,64)))submissions++;}
                    finally
                    {
                        if(measureSubmit)
                        {
                            submitRecorder.Stop();Assert.That(submitRecorder.Valid&&!submitRecorder.WrappedAround,Is.True);
                            for(var i=0;i<submitRecorder.Count;i++)submitAllocations+=submitRecorder.GetSample(i).Count;
                            measuredSubmissions++;
                        }
                    }
                };
                for(var frame=0;frame<300;frame++){document.SynchronizeLayout();yield return null;}
                Assert.That(cameraWarnings,Is.Zero,"SRP 只通过显式上下文提交，不能调用 Built-in 相机挂载接口。");
                using(var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",8192,ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
                {
                    for(var frame=0;frame<32;frame++)document.SynchronizeLayout();recorder.Stop();long allocations=0;
                    for(var index=0;index<recorder.Count;index++)allocations+=recorder.GetSample(index).Count;
                    Assert.That(recorder.Valid&&!recorder.WrappedAround,Is.True);Assert.That(allocations,Is.Zero);
                }
                // 无效调用必须在租约获取前失败，不能使后续真实提交进入故障状态。
                Assert.Throws<InvalidOperationException>(()=>surface.SubmitRendering(default,target,new Rect(0,0,64,64)));
                // 同步布局之外还必须单独测真实管线上下文的提交；不能遗漏结构体装箱。
                submitRecorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",8192,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                submitRecorder.Stop();measureSubmit=true;
                for(var frame=0;frame<120&&measuredSubmissions<32;frame++)yield return null;
                measureSubmit=false;submitRecorder.Dispose();
                Assert.That(measuredSubmissions,Is.GreaterThanOrEqualTo(32));
                Assert.That(submitAllocations,Is.Zero,"实际 SRP 提交不得产生托管分配。");
                Assert.That(submissions,Is.GreaterThan(0));
                var read=new Texture2D(1,1,TextureFormat.RGBA32,false,true);var active=RenderTexture.active;
                try{RenderTexture.active=target;read.ReadPixels(new Rect(8,55,1,1),0,0);read.Apply();Assert.That(read.GetPixel(0,0).r,Is.GreaterThan(.98f));}
                finally{RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(read);}
                UnityEngine.Object.DestroyImmediate(root);root=null;Assert.That(cameraWarnings,Is.Zero,"释放 SRP Surface 也不能调用未挂载的 Built-in 缓冲。");
            }
            finally
            {
                measureSubmit=false;submitRecorder.Dispose();pipeline.Draw=null;if(root)UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;
                GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=quality;
                UnityEngine.Object.DestroyImmediate(pipeline);UnityEngine.Object.DestroyImmediate(cameraObject);
                GpuRetirement.Retire(()=>{target.Release();GpuRetirement.DestroyOwnedObject(target);});
                Application.logMessageReceived-=logs;
            }
        }
    }
}
