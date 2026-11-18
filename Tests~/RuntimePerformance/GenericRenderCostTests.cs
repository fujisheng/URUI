using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;
using URUI.Resources;
using URUI.Rendering.Effects;
using URUI.Effect;
namespace URUI.Validation
{
    public sealed class GenericRenderCostTests
    {
        [Serializable] sealed class Result
        {
            public string name; public int round, count, textures, emitters, documents, gpuMissing;
            public double p50, p95, submitP95;public double[] sync, submit;
            public List<double> gpu = new(1000);public long gcEvents;
            public long geometryBytes=-1, parameterBytes=-1,parameterCopyBytes=-1;
            public int geometryDraws=-1,externalDraws=-1,rasterResolves=-1;
            public int liveTargets,leasedTargets,paintedPixels,actualParticles;public long particleBodyBakes,particleDrawCalls,unknownPainterCalls;public long liveBytes,leasedBytes,createdTargets;
        }
        [Serializable] sealed class Report
        {
            public string unity,device,graphics,colorSpace,selectedCases;
            public int warm=300,frames=1000,rounds=3,seed=1234;
            public bool gpuFeature;public long positiveGcEvents;
            public string scope="Synchronous mutation + one shared Surface refresh; Camera.Render separately measured wall time, not GPU duration. 300 actual warm frames then 3x1000 actual frames, profiling and allocation stacks disabled. GC.Alloc events cover 32 explicit synchronous cycles only, engine camera/PlayerLoop excluded. Unique timestamp positive finite FrameTimingManager GPU values cover entire frames (automatic plus explicit camera), empty list means missing. Reflection statistics are read outside timing; -1 means absent in that package. Native allocator traffic is independently measured by C++ contract tests; this fixture does not infer native zero from managed GC.";
            public List<Result> cases=new();
        }
        sealed class Input
        {
            internal string Name;internal int Count,Textures=1,Documents=1,Emitters;
            internal Input(string n,int c,int t=1){Name=n;Count=c;Textures=t;}
        }
        sealed class Fixture:IDisposable
        {
            internal Document Document;internal readonly List<Element> Nodes=new();internal readonly Input Input;
            readonly GameObject root;readonly Texture2D[] textures;readonly List<IDisposable> resources=new();readonly List<ParticlePainter> painters=new();
            internal long PainterCalls;
            readonly List<Document> docs=new();readonly List<EffectModule> modules=new();readonly GameObject particleSource;
            readonly Material material;readonly Mesh mesh;readonly object host;
            const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            internal Fixture(Input input)
            {
                Input=input;root=new GameObject("Generic cost Surface");var surface=root.AddComponent<Surface>();surface.Viewport=new Vector2Int(640,360);surface.Input.UseUnityInput=false;
                textures=new Texture2D[input.Textures];var images=new ImageAsset[textures.Length];
                for(var t=0;t<textures.Length;t++)
                {
                    var texture=new Texture2D(16,16,TextureFormat.RGBA32,false,true);texture.filterMode=FilterMode.Point;
                    var pixels=new Color32[256];for(var i=0;i<256;i++)pixels[i]=new Color((t+1)/10f,(i%16+1)/16f,(i/16+1)/16f,.75f);
                    texture.SetPixels32(pixels);texture.Apply();textures[t]=texture;images[t]=new ImageAsset("texture-"+t+".png",texture);
                }
                var ui=input.Name.StartsWith("ui-",StringComparison.Ordinal);var masks=input.Name.StartsWith("mask-",StringComparison.Ordinal);
                if(input.Emitters>0)
                {
                    material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle"));particleSource=new GameObject("Generic deterministic emitters");
                    for(var i=0;i<input.Emitters;i++)
                    {
                        var child=new GameObject("Emitter "+i);child.transform.SetParent(particleSource.transform,false);var system=child.AddComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);system.useAutoRandomSeed=false;system.randomSeed=(uint)(1234+i);
                        var main=system.main;main.startLifetime=2;main.startSpeed=2;main.startSize=3;main.maxParticles=16;main.playOnAwake=false;
                        var emission=system.emission;emission.rateOverTime=16;emission.SetBursts(new[]{new ParticleSystem.Burst(0,8)});var renderer=child.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.enabled=false;
                    }
                }
                if(input.Name=="unknown-painter")
                {
                    material=new Material(UnityEngine.Resources.Load<Shader>("URUI.GenericCosts/Unknown"));material.SetTexture("_MainTex",textures[0]);material.SetFloat("_Progress",.25f);
                    mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right*8,Vector3.up*8};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};mesh.colors=new[]{Color.white,Color.white,Color.white};mesh.triangles=new[]{0,1,2};
                }
                for(var d=0;d<input.Documents;d++)
                {
                    var obj=new GameObject("Document "+d);obj.transform.SetParent(root.transform,false);var doc=obj.AddComponent<Document>();doc.Surface=surface;doc.Images=images;docs.Add(doc);Document??=doc;
                    if(ui){modules.Add(new EffectModule(doc));doc.TextResources=new[]{new TextResource("/effect.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect"))};}
                    var random=new System.Random(1234+d);var html=new StringBuilder("<!doctype html><html><head>");if(ui)html.Append("<link rel='stylesheet' href='/effect.css'>");
                    html.Append("<style>body{margin:0;}img,.node{position:absolute;width:3px;height:3px;} .fx{--urui-effect:ui;--urui-fx-tone-filter:grayscale;width:16px;height:16px;}");
                    if(input.Name=="ui-shadow")html.Append(".fx{--urui-fx-shadow-mode:shadow3;--urui-fx-shadow-iteration:2;--urui-fx-shadow-distance-x:2;}");
                    if(input.Name=="ui-blur169")html.Append(".fx{--urui-fx-sampling-filter:blur-detail;}");
                    if(input.Name=="filter")html.Append("body{filter:blur(3px) brightness(.8);}");
                    if(masks)html.Append("#masked{position:absolute;left:16px;top:16px;width:96px;height:96px;--urui-mask-image:shader(feather-mask);--urui-mask-feather-top:32px;} .red{position:absolute;width:80px;height:96px;background:rgba(255,0,0,.5);} .green{position:absolute;left:16px;width:80px;height:96px;background:rgba(0,255,0,.5);}");
                    if(input.Emitters>0)html.Append(".node.has-particles{--urui-decorator:shader(particles);}");
                    html.Append("</style></head><body>");var names=new string[input.Count];
                    if(masks)html.Append("<div id='masked'><div class='red'></div>").Append(input.Name=="mask-complex"?"<div class='green'></div>":"").Append("</div>");
                    else for(var n=0;n<input.Count;n++)
                    {
                        names[n]="node-"+random.Next().ToString("x");var x=n%100*6;var y=n/100*6;
                        if(input.Name=="text")html.Append("<div class='node' id='").Append(names[n]).Append("' style='left:").Append(x).Append("px;top:").Append(y).Append("px;'>A</div>");
                        else if(input.Emitters>0||input.Name=="unknown-painter")html.Append("<div class='node' id='").Append(names[n]).Append("' style='width:128px;height:128px;'></div>");
                        else if(input.Name=="ui-captured"||input.Name=="ui-blur169")html.Append("<div class='fx' id='").Append(names[n]).Append("' style='width:128px;height:128px;left:32px;top:32px;'><img src='texture-0.png' style='width:96px;height:128px;'><img src='texture-0.png' style='left:32px;width:96px;height:128px;'></div>");
                        else html.Append("<img class='").Append(ui?"fx":"").Append("' id='").Append(names[n]).Append("' src='texture-").Append(n%input.Textures).Append(".png' style='left:").Append(ui?n%8*28:x).Append("px;top:").Append(ui?n/8*28:y).Append("px;'>");
                    }
                    doc.LoadHtml(html.Append("</body></html>").ToString(),"/generic-"+input.Name+".html");
                    if(masks){var node=doc.Q("masked");Nodes.Add(node);resources.Add(new FeatherMaskPainter(node));}
                    else for(var n=0;n<input.Count;n++)
                    {
                        var node=doc.Q(names[n]);Nodes.Add(node);
                        if(input.Emitters>0){var painter=new ParticlePainter(node,particleSource);painters.Add(painter);resources.Add(painter);}
                        if(input.Name=="unknown-painter"){resources.Add(doc.AttachPainter(node,"opaque-probe",context=>{PainterCalls++;context.DrawMesh(mesh,material,Matrix4x4.identity);}));node.Style.Set("--urui-decorator","shader(opaque-probe)");}
                    }
                }
                Document.SynchronizeLayout();
                host=typeof(Document).GetField("host",Flags).GetValue(Document);
            }
            internal void Witness(Camera camera,RenderTexture target,Result result)
            {
                camera.Render();var read=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);var previous=RenderTexture.active;
                try {RenderTexture.active=target;read.ReadPixels(new Rect(0,0,target.width,target.height),0,0);read.Apply();foreach(var pixel in read.GetPixels32())if(pixel.r!=0||pixel.g!=0||pixel.b!=255)result.paintedPixels++;}
                finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(read);}
                Assert.That(result.paintedPixels,Is.GreaterThan(0),Input.Name+" must paint real pixels");
                foreach(var painter in painters){result.actualParticles+=painter.ParticleCount;var stats=painter.GetStatistics();result.particleBodyBakes+=stats.BodyBakes;result.particleDrawCalls+=stats.DrawCalls;}
                if(Input.Emitters>0){Assert.That(result.actualParticles,Is.GreaterThan(0));Assert.That(result.particleBodyBakes,Is.GreaterThan(0));Assert.That(result.particleDrawCalls,Is.GreaterThan(0));}
                result.unknownPainterCalls=PainterCalls;if(Input.Name=="unknown-painter")Assert.That(result.unknownPainterCalls,Is.GreaterThan(0));
            }
            internal long Stat(string name)
            {var p=host.GetType().GetProperty(name,Flags);return p==null?-1:Convert.ToInt64(p.GetValue(host));}
            internal void Cycle(int frame)
            {
                var even=frame%2==0;
                if (Input.Name is "transform" or "tint" or "uv" or "text" or "ui-image" or "ui-captured" or "ui-shadow" or "ui-blur169" or "mask-leaf" or "mask-complex")
                foreach(var node in Nodes)
                {
                    switch(Input.Name)
                    {
                        case "transform":node.Style.Set("transform",even?"translate(0px,0px)":"translate(.5px,-.25px)");break;
                        case "tint":node.Style.Set("--urui-graphic-tint",even?"rgba(255,128,64,.5)":"rgba(128,255,64,.75)");break;
                        case "uv":node.SetAttribute("data-urui-rect",even?"0 0 8 8":"8 0 8 8");break;
                        case "text":node.Text=even?"A":"B";break;
                        case "ui-image":case "ui-captured":node.Style.SetNumber("--urui-fx-tone-intensity",even?.25f:.75f);break;
                        case "ui-shadow":node.Style.SetNumber("--urui-fx-shadow-distance-x",even?2:3);break;
                        case "ui-blur169":node.Style.SetNumber("--urui-fx-sampling-intensity",even?.25f:.75f);break;
                        case "mask-leaf":case "mask-complex":node.Style.Set("--urui-mask-feather-top",even?"32px":"40px");break;
                    }
                }
                Document.SynchronizeLayout();
            }
            public void Dispose()
            {
                foreach(var value in resources)value.Dispose();foreach(var module in modules)module.Dispose();UnityEngine.Object.DestroyImmediate(root);
                foreach(var texture in textures)GpuRetirement.Retire(()=>UnityEngine.Object.Destroy(texture));
                if(mesh)GpuRetirement.Retire(()=>UnityEngine.Object.Destroy(mesh));if(material)GpuRetirement.Retire(()=>UnityEngine.Object.Destroy(material));if(particleSource)UnityEngine.Object.DestroyImmediate(particleSource);
            }
        }
        static long Events(ProfilerRecorder recorder)
        {recorder.Stop();Assert.That(recorder.Valid&&!recorder.WrappedAround,Is.True);long result=0;for(var i=0;i<recorder.Count;i++)result+=recorder.GetSample(i).Count;return result;}
        static double P95(double[] values){var sorted=(double[])values.Clone();Array.Sort(sorted);return sorted[(int)(sorted.Length*.95)];}
        static List<Input> Inputs()
        {
            var inputs=new List<Input>();foreach(var n in new[]{10,100,1000,5000})foreach(var t in new[]{1,8,9})inputs.Add(new Input("images-"+n+"-"+t,n,t));
            foreach(var name in new[]{"transform","tint","uv"})inputs.Add(new Input(name,1000));
            inputs.Add(new Input("text",100));inputs.Add(new Input("multidoc",100){Documents=3});
            foreach(var name in new[]{"mask-leaf","mask-complex","filter","unknown-painter"})inputs.Add(new Input(name,name=="filter"?100:1));
            foreach(var n in new[]{1,4,16})inputs.Add(new Input("particles-"+n,1){Emitters=n});
            inputs.Add(new Input("ui-image",64));inputs.Add(new Input("ui-shadow",16));inputs.Add(new Input("ui-captured",1));inputs.Add(new Input("ui-blur169",1));
            return inputs;
        }
        [UnityTest,Timeout(300000)]
        public IEnumerator SmokeAllInputs()
        {
            var cameraObject=new GameObject("Generic smoke camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.blue;var oldCapture=Time.captureDeltaTime;Time.captureDeltaTime=1f/60;var target=new RenderTexture(640,360,0,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;
            try {foreach(var input in Inputs()) {using var fixture=new Fixture(input);for(var frame=0;frame<10;frame++){fixture.Cycle(frame);camera.Render();yield return null;}Assert.That(fixture.Document.IsLoaded,Is.True,input.Name);fixture.Witness(camera,target,new Result());}}
            finally {Time.captureDeltaTime=oldCapture;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        [UnityTest,Timeout(1800000)]
        public IEnumerator Capture()
        {
            var output=Environment.GetEnvironmentVariable("URUI_GENERIC_COST_OUTPUT")??Path.Combine(Application.dataPath,"../GenericCosts");Directory.CreateDirectory(output);
            var report=new Report{unity=Application.unityVersion,device=SystemInfo.graphicsDeviceName,graphics=SystemInfo.graphicsDeviceType.ToString(),colorSpace=QualitySettings.activeColorSpace.ToString(),gpuFeature=FrameTimingManager.IsFeatureEnabled()};
#if UNITY_EDITOR
            var oldRecording=UnityEditorInternal.ProfilerDriver.enabled;UnityEditorInternal.ProfilerDriver.enabled=false;
#endif
            var oldStacks=UnityEngine.Profiling.Profiler.enableAllocationCallstacks;UnityEngine.Profiling.Profiler.enableAllocationCallstacks=false;
            var oldCapture=Time.captureDeltaTime;Time.captureDeltaTime=1f/60;
            var obj=new GameObject("Generic cost camera",typeof(Camera));obj.tag="MainCamera";var camera=obj.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.blue;camera.allowMSAA=false;camera.allowHDR=true;
            var target=new RenderTexture(640,360,0,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;
            using(var probe=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",131072,ProfilerRecorderOptions.CollectOnlyOnCurrentThread)){GC.KeepAlive(new byte[4096]);report.positiveGcEvents=Events(probe);Assert.That(report.positiveGcEvents,Is.GreaterThan(0));}
            var inputs=Inputs();report.selectedCases=Environment.GetEnvironmentVariable("URUI_GENERIC_COST_CASES");
            if(!string.IsNullOrEmpty(report.selectedCases))
            {
                var names=new HashSet<string>(report.selectedCases.Split(';'));inputs=inputs.FindAll(input=>names.Contains(input.Name));
                Assert.That(inputs.Count,Is.EqualTo(names.Count),"Every requested case must exist");
            }
            try
            {
                foreach(var input in inputs)for(var round=0;round<report.rounds;round++)
                {
                    using var fixture=new Fixture(input);for(var frame=0;frame<report.warm;frame++){fixture.Cycle(frame);camera.Render();yield return null;}
                    var result=new Result{name=input.Name,round=round,count=input.Count,textures=input.Textures,documents=input.Documents,emitters=input.Emitters,sync=new double[report.frames],submit=new double[report.frames]};
                    fixture.Witness(camera,target,result);
                    using(var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",131072,ProfilerRecorderOptions.CollectOnlyOnCurrentThread)){for(var i=0;i<32;i++)fixture.Cycle(i);result.gcEvents=Events(gc);}
                    var geometry=fixture.Stat("GeometryUploadBytes");var parameters=fixture.Stat("ParameterUploadBytes");var copies=fixture.Stat("ParameterCopyBytes");var created=EffectStatistics.GetTargets().CreatedTargets;
                    var timing=new FrameTiming[1];ulong previous=0;
                    for(var frame=0;frame<report.frames;frame++)
                    {
                        var start=Stopwatch.GetTimestamp();fixture.Cycle(frame);result.sync[frame]=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                        start=Stopwatch.GetTimestamp();camera.Render();result.submit[frame]=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                        FrameTimingManager.CaptureFrameTimings();yield return null;
                        var count=FrameTimingManager.GetLatestTimings(1,timing);var value=timing[0];
                        if(count>0&&value.frameStartTimestamp!=0&&value.frameStartTimestamp!=previous&&double.IsFinite(value.gpuFrameTime)&&value.gpuFrameTime>0){result.gpu.Add(value.gpuFrameTime);previous=value.frameStartTimestamp;}else result.gpuMissing++;
                        var targets=EffectStatistics.GetTargets();result.liveTargets=Math.Max(result.liveTargets,targets.LiveTargets);result.leasedTargets=Math.Max(result.leasedTargets,targets.LeasedTargets);result.liveBytes=Math.Max(result.liveBytes,targets.LiveColorBytes);result.leasedBytes=Math.Max(result.leasedBytes,targets.LeasedColorBytes);
                    }
                    result.p95=P95(result.sync);result.submitP95=P95(result.submit);var sorted=(double[])result.sync.Clone();Array.Sort(sorted);result.p50=sorted[sorted.Length/2];
                    result.geometryBytes=geometry<0?-1:fixture.Stat("GeometryUploadBytes")-geometry;result.parameterBytes=parameters<0?-1:fixture.Stat("ParameterUploadBytes")-parameters;result.parameterCopyBytes=copies<0?-1:fixture.Stat("ParameterCopyBytes")-copies;
                    result.geometryDraws=(int)fixture.Stat("RecordedGeometryDraws");result.externalDraws=(int)fixture.Stat("RecordedExternalGeometryDraws");result.rasterResolves=(int)fixture.Stat("RecordedRasterResolves");result.createdTargets=EffectStatistics.GetTargets().CreatedTargets-created;
                    report.cases.Add(result);File.WriteAllText(Path.Combine(output,"generic-costs.json"),JsonUtility.ToJson(report,true));
                }
            }
            finally
            {
#if UNITY_EDITOR
                UnityEditorInternal.ProfilerDriver.enabled=oldRecording;
#endif
                UnityEngine.Profiling.Profiler.enableAllocationCallstacks=oldStacks;Time.captureDeltaTime=oldCapture;
                File.WriteAllText(Path.Combine(output,"generic-costs.json"),JsonUtility.ToJson(report,true));camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(obj);target.Release();UnityEngine.Object.DestroyImmediate(target);
            }
            Assert.That(report.cases.Count,Is.EqualTo(inputs.Count*report.rounds));
        }
    }
}
