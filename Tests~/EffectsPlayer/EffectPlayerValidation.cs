using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using URUI.Effect.Examples;
using URUI.Effect.Elements;
using URUI.Events;
using URUI.Controls;
using URUI.Resources;
using URUI.Rendering.Effects;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
namespace URUI.Effect.PlayerValidation
{
    /// <summary>无测试框架依赖的实际 Player 验收：全部模式、像素、播控与粒子。</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class EffectPlayerValidation:MonoBehaviour
    {
        [Serializable] sealed class State {public string name,className;}
        [Serializable] sealed class Card {public string id,family,mode,coverageId;public int value;public State[] states;}
        [Serializable] sealed class Manifest {public Card[] cards;}
        [Serializable] sealed class Report
        {
            public string status="running",error,unity,graphics,device,colorSpace;
            public int cards,modeImages,particles,finitePixels,shaderErrors,magentaPixels,defaultControls,defaultStates;
            public bool controls,link,allocationCounterAvailable,gpuTimeAvailable,nativeVisualRegression;
            public long allocationProbeBytes,leasedBytes,liveBytes;
            public double clockTime;
            public List<double> gpuFrameSamplesMs=new();public int gpuMissingFrames;
            public long positiveAllocationCount,stableThousandSyncAllocations;
            public string allocationUnit;
            public string allocationScope="Main-thread GC.Alloc; 32 stable SynchronizeLayout calls after 300 actual warm frames; 1000 graphics, input disabled; excludes camera and MonoBehaviour loop";
            public List<string> captures=new();
        }
        public EffectGallery gallery;
        public Camera captureCamera;
        public TextAsset manifest;
        public bool AutoQuit=true;
        public string CaptureDirectory;
        public bool Finished {get;private set;}
        public bool Passed=>report.status=="passed";
        sealed class Clock:URUI.Animation.IUIClock {public double Now {get;set;} public float Delta=>.2f;}
        readonly Report report=new();
        float originalCaptureDelta;
        string output;
        RenderTexture target;
        readonly List<string> logErrors=new();
        void ErrorLog(string message,string stack,LogType type){if(type is LogType.Error or LogType.Exception or LogType.Assert)logErrors.Add(message+"\n"+stack);}
        void Awake()
        {
            originalCaptureDelta=Time.captureDeltaTime;Time.captureDeltaTime=1f/60;
            if(!gallery)gallery=GetComponent<EffectGallery>();
            if(gallery)gallery.UIClock=new Clock();
        }
        IEnumerator Start()
        {
            output=CaptureDirectory??Environment.GetEnvironmentVariable("URUI_EFFECT_PLAYER_OUTPUT");
            var args=Environment.GetCommandLineArgs();for(var i=0;i+1<args.Length;i++)if(args[i]=="-urui-output")output=args[i+1];
            output=string.IsNullOrEmpty(output)?Path.Combine(Application.persistentDataPath,"URUI.Effect.Validation"):output;Directory.CreateDirectory(output);
            Application.logMessageReceived+=ErrorLog;
            report.unity=Application.unityVersion;report.graphics=SystemInfo.graphicsDeviceType.ToString();report.device=SystemInfo.graphicsDeviceName;report.colorSpace=QualitySettings.activeColorSpace.ToString();
            var routine=Validate();
            while(true)
            {
                bool more;
                try{more=routine.MoveNext();}
                catch(Exception error){report.status="failed";report.error=error.ToString();break;}
                if(!more)break;
                yield return routine.Current;
            }
            report.shaderErrors=logErrors.Count;if(logErrors.Count>0){report.status="failed";report.error=(report.error??"")+"\n"+string.Join("\n",logErrors);}
            File.WriteAllText(Path.Combine(output,"acceptance.json"),JsonUtility.ToJson(report,true));
            Application.logMessageReceived-=ErrorLog;
            Finished=true;if(AutoQuit)Application.Quit(report.status=="passed"?0:2);
        }
        UnityEngine.Rendering.CommandBuffer validationFence;
        IEnumerator WaitForSubmittedRendering()
        {
            // 夹具主动请求渲染可快于显示/GPU；等待真实提交，不提高生产在途上限。
            validationFence??=new UnityEngine.Rendering.CommandBuffer{name="Gallery validation fence"};
            validationFence.Clear();var fence=validationFence.CreateGraphicsFence(UnityEngine.Rendering.GraphicsFenceType.CPUSynchronisation,UnityEngine.Rendering.SynchronisationStageFlags.PixelProcessing);
            Graphics.ExecuteCommandBuffer(validationFence);yield return null;
            for(var wait=0;!fence.passed;wait++){Require(wait<240,"Gallery GPU fence timeout");yield return null;}
            GpuRetirement.Collect();
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        IEnumerator Validate()
        {
            for(var i=0;i<6;i++)yield return null;
            Require(gallery&&gallery.Document&&gallery.Document.IsLoaded,"Gallery did not load");
            foreach(var path in new[]{"UIEffect","TextureFilter","Particle"})
            {var shader=UnityEngine.Resources.Load<Shader>("URUI.Effect/"+path);Require(shader&&shader.isSupported,"Missing/unsupported shader: "+path);}
            Require(gallery.shader&&gallery.shader.isSupported,"Missing gallery shader");
            var document=gallery.Document;document.Surface.Viewport=new Vector2Int(1280,720);document.Surface.Input.UseUnityInput=false;
            target=new RenderTexture(1280,720,0,RenderTextureFormat.ARGBHalf);Require(target.Create(),"Capture target");captureCamera.targetTexture=target;
            report.clockTime=document.Surface.UIClock.Now;Require(report.clockTime==0,"Gallery clock was not frozen before its Surface initialized: "+report.clockTime);
            document.SynchronizeLayout();captureCamera.Render();Capture("Gallery.png",new Rect(0,0,1280,720));
            ValidateAllocationProbe();
            var playback=gallery.Playback;document.Q("pause").SendEvent(new ClickEvent());Require(playback.IsPaused,"Pause callback");
            document.Q("resume").SendEvent(new ClickEvent());Require(!playback.IsPaused,"Resume callback");
            document.Q<Slider>("seek").Value=.75f;Require(Math.Abs(playback.Time-1.5)<.001&&playback.IsPaused,"Seek callback");
            document.SynchronizeLayout();
            // 链接通过控制器快照生效，不改写作者 CSS；比较生产输出中的参数。
            var module=gallery.Module;
            var left=module.GetController(document.Q("controlled")).CreateParameters();var right=module.GetController(document.Q("linked")).CreateParameters();
            Require(Math.Abs(left.TransitionProgress-right.TransitionProgress)<.0001,"Linked snapshots diverged");report.link=true;
            document.Q("stop").SendEvent(new ClickEvent());Require(playback.Time==0&&playback.IsPaused,"Stop callback");document.Q("restart").SendEvent(new ClickEvent());Require(playback.IsPlaying,"Restart callback");report.controls=true;playback.Pause();
            var data=JsonUtility.FromJson<Manifest>(manifest.text);Require(data.cards.Length==83,"Complete gallery manifest");
            var container=document.Query(className:"gallery")[0];container.Style.Set("position","relative");document.SynchronizeLayout();
            var positions=new Dictionary<string,Rect>();foreach(var card in data.cards)positions.Add(card.id,document.Q(card.id).WorldBound);
            foreach(var card in data.cards)
            {
                foreach(var state in card.states)
                {
                    var node=document.Query(className:state.className)[0];var p=module.GetController(node).CreateParameters();
                    var expected=state.name=="off"&&card.family!="PatternArea"?(card.family=="BlendType"?(int)BlendType.AlphaBlend:0):card.value;
                    if(card.family=="Flip"&&state.name=="extreme")expected=15;
                    Require(Mode(p,card.family)==expected,card.coverageId+"/"+state.name+" mode mismatch");report.modeImages++;
                }
                container.Style.Set("top",(96-positions[card.id].y).ToString(System.Globalization.CultureInfo.InvariantCulture)+"px");document.SynchronizeLayout();yield return null;
                captureCamera.Render();var r=document.Q(card.id).WorldBound;Require(r.y>=0&&r.yMax<=720,"Card outside capture");
                Capture(card.id+".png",r);report.cards++;
            }
            container.Style.Set("top","0px");document.SynchronizeLayout();
            var particles=document.Q<ParticlesElement>("particle-demo");Require(particles.CurrentSource=="sample-particles","Declarative particle source");
            var particleBounds=particles.WorldBound;container.Style.Set("top",(96-particleBounds.y).ToString(System.Globalization.CultureInfo.InvariantCulture)+"px");document.SynchronizeLayout();
            particles.Restart();for(var i=0;i<40;i++)yield return null;captureCamera.Render();report.particles=particles.ParticleCount;Require(report.particles>0,"Particles did not simulate");particles.Pause();particles.Stop();particles.Play();
            var timings=new FrameTiming[1];ulong previousTiming=0;
            for(var i=0;i<128;i++){
                FrameTimingManager.CaptureFrameTimings();yield return null;
                var available=FrameTimingManager.GetLatestTimings(1,timings);var value=timings[0];
                if(available>0&&value.frameStartTimestamp!=0&&value.frameStartTimestamp!=previousTiming&&double.IsFinite(value.gpuFrameTime)&&value.gpuFrameTime>0){report.gpuFrameSamplesMs.Add(value.gpuFrameTime);previousTiming=value.frameStartTimestamp;}
                else report.gpuMissingFrames++;
            }
            report.gpuTimeAvailable=report.gpuFrameSamplesMs.Count>0;
            var stats=EffectStatistics.GetTargets();report.leasedBytes=stats.LeasedColorBytes;report.liveBytes=stats.LiveColorBytes;
            document.Unload();for(var i=0;i<120&&EffectStatistics.GetTargets().LeasedTargets>0;i++){GpuRetirement.Collect();yield return null;}
            Require(EffectStatistics.GetTargets().LeasedTargets==0,"Leases remain after unload");
            ValidateDefaultControls();yield return WaitForSubmittedRendering();
            document.Unload();yield return ValidateNativeVisualRegression();Require(report.nativeVisualRegression,"Native visual regression did not execute");document.Unload();yield return ValidateStableAllocation();document.Unload();report.status="passed";
        }
        IEnumerator ValidateNativeVisualRegression()
        {
            var document=gallery.Document;
            var originalImages=document.Images;
            var content=new Texture2D(16,16,TextureFormat.RGBA32,false,true);
            var border=new Texture2D(8,8,TextureFormat.RGBA32,false,true);
            var material=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureFilter"));
            try
            {
                var red=new Color[256];for(var i=0;i<red.Length;i++)red[i]=Color.red;content.SetPixels(red);content.Apply();
                var green=new Color[64];for(var i=0;i<green.Length;i++)green[i]=Color.green;border.SetPixels(green);border.Apply();
                document.Images=new[]{new ImageAsset("content.png",content),new ImageAsset("border.png",border)};
                material.SetVector("_BandColor",Vector4.one);material.SetFloat("_Width",1);
                document.Effects.Register(global::URUI.Effect.Rendering.TextureColorEffect.Define("native-identity",material));
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;left:80px;top:80px;width:80px;height:80px;border:12px solid transparent;border-image:url(border.png) 2 / 12px stretch;--urui-image-effect:native-identity;}</style></head><body><img id='image' src='content.png'></body></html>","/native-image.html");
                document.SynchronizeLayout();captureCamera.Render();
                Color Pixel(int x,int y)
                {
                    var previous=RenderTexture.active;var read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
                    try{RenderTexture.active=target;read.ReadPixels(new Rect(x,target.height-y-1,1,1),0,0);read.Apply();return read.GetPixel(0,0);}
                    finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(read);}
                }
                Require(Pixel(110,110).r>.98f&&Pixel(84,84).g>.98f,"Content and border-image lost independent effect textures");
                for(var warm=0;warm<300;warm++){document.SynchronizeLayout();captureCamera.Render();yield return WaitForSubmittedRendering();}
                var leased=EffectStatistics.GetTargets().LeasedTargets;
                foreach(var name in new[]{"none","native-identity","none"})
                {
                    document.Q("image").Style.Set("--urui-image-effect",name);
                    for(var frame=0;frame<12;frame++){document.SynchronizeLayout();captureCamera.Render();yield return WaitForSubmittedRendering();}
                    Require(EffectStatistics.GetTargets().LeasedTargets<=Math.Max(leased,1),"Direct nine-patch exceeded the single root encoding target budget: "+EffectStatistics.GetTargets().LeasedTargets+" vs "+leased);
                }
                document.Unload();
                document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'><style>span{position:absolute;left:20px;top:20px;font-family:'Liberation Sans';font-size:40px;color:#4080c080;}</style></head><body><span id='text'>ABC</span></body></html>","/native-font.html");
                double Alpha()
                {
                    document.SynchronizeLayout();captureCamera.Render();var previous=RenderTexture.active;
                    var read=new Texture2D(160,100,TextureFormat.RGBAFloat,false,true);
                    try{RenderTexture.active=target;read.ReadPixels(new Rect(0,target.height-100,160,100),0,0);read.Apply();double alpha=0;foreach(var pixel in read.GetPixels())alpha+=pixel.a;return alpha;}
                    finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(read);}
                }
                var plain=Alpha();Require(plain>10,"Native font fixture did not draw");
                document.Q("text").Style.Set("--urui-font-effect","dilate(.0125em)");var relative=Alpha();
                document.Q("text").Style.Set("--urui-font-effect","dilate(.5px)");var pixels=Alpha();
                Require(relative>plain&&Math.Abs(relative-pixels)<.1,"Fractional/em dilation changed text coverage inconsistently");
                document.Unload();report.nativeVisualRegression=true;
            }
            finally
            {document.Unload();document.Images=originalImages;GpuRetirement.Retire(()=>{GpuRetirement.DestroyOwnedObject(material);GpuRetirement.DestroyOwnedObject(content);GpuRetirement.DestroyOwnedObject(border);});}
        }
        void ValidateAllocationProbe()
        {
            using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",256,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            var probe=new byte[4096];GC.KeepAlive(probe);recorder.Stop();
            report.allocationCounterAvailable=recorder.Valid&&!recorder.WrappedAround;
            report.allocationUnit=recorder.UnitType.ToString();
            for(var index=0;index<recorder.Count;index++)
            {var sample=recorder.GetSample(index);report.positiveAllocationCount+=sample.Count;report.allocationProbeBytes+=sample.Value;}
            // GC.Alloc在此后端是CPU标记：Count为分配次数，Value是耗时，不能把它当作字节。
            if(recorder.UnitType!=ProfilerMarkerDataUnit.Bytes)report.allocationProbeBytes=-1;
            Require(report.allocationCounterAvailable&&report.positiveAllocationCount>0,"GC.Alloc positive allocation probe failed");
        }
        IEnumerator ValidateStableAllocation()
        {
            var document=gallery.Document;
            var html=new System.Text.StringBuilder("<!doctype html><html><head><style>body{margin:0;}"+
                "div{position:absolute;width:3px;height:3px;background:red;}</style></head><body>");
            for(var index=0;index<1000;index++)html.Append("<div style='left:").Append(index%32*4).Append("px;top:").Append(index/32*4).Append("px;'></div>");
            document.LoadHtml(html.Append("</body></html>").ToString(),"/static-thousand.html");
            using var warmFence=new UnityEngine.Rendering.CommandBuffer{name="Gallery validation warm fence"};
            for(var index=0;index<300;index++)
            {
                document.SynchronizeLayout();captureCamera.Render();
                warmFence.Clear();var fence=warmFence.CreateGraphicsFence(UnityEngine.Rendering.GraphicsFenceType.CPUSynchronisation,UnityEngine.Rendering.SynchronisationStageFlags.PixelProcessing);
                Graphics.ExecuteCommandBuffer(warmFence);yield return null;
                for(var wait=0;!fence.passed;wait++){Require(wait<240,"Gallery warm GPU fence timeout");yield return null;}
                GpuRetirement.Collect();
            }
            using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",8192,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            for(var index=0;index<32;index++)document.SynchronizeLayout();
            recorder.Stop();
            Require(recorder.Valid&&!recorder.WrappedAround,"Stable GC.Alloc recording unavailable");
            for(var index=0;index<recorder.Count;index++)report.stableThousandSyncAllocations+=recorder.GetSample(index).Count;
            Require(report.stableThousandSyncAllocations==0,"Stable ordinary UI allocated: "+report.stableThousandSyncAllocations);
        }
        void ValidateDefaultControls()
        {
            var document=gallery.Document;
            var files=new List<TextResource>(document.TextResources){new TextResource("/urui/controls.css",UnityEngine.Resources.Load<TextAsset>("URUI/controls"))};
            document.TextResources=files.ToArray();
            var clock=new Clock();document.Surface.UIClock=clock;
            document.Load(UnityEngine.Resources.Load<TextAsset>("URUI/Tests/DefaultControlsGallery"),"/samples/urui-effects/DefaultControls.html");
            void Tick(){clock.Now+=.2;document.SynchronizeLayout();clock.Now+=.2;document.SynchronizeLayout();}
            Tick();captureCamera.Render();Capture("DefaultControls-normal.png",new Rect(0,0,1280,720));
            Require(document.Q("normal").Layout.width==160&&document.Q("normal").Layout.height==30,"Default button dimensions");
            Require(document.Q("toggle").Layout.width==20,"Default toggle dimensions");
            foreach(var id in new[]{"field","placeholder","readonly","number","area"})
                Require(document.Q(id).IsVisibleInHierarchy,"Default field missing: "+id);
            foreach(var id in new[]{"normal","disabled","image","toggle","radio","range","slider","field","placeholder","readonly","number","area","dropdown","scroll","progress","foldout"})
            {Require(document.Q(id).IsVisibleInHierarchy,"Missing gallery control: "+id);report.defaultControls++;}
            var button=document.Q<Button>("image");var graphic=document.Q("graphic");var decoration=document.Q("decoration");
            var before=EffectStatistics.GetTargets().LeasedTargets;
            foreach(var mode in new[]{"hover","pressed","disabled"})
            {
                if(mode=="hover")document.Surface.Input.SendPointer(PointerPhase.Move,button.WorldBound.center);
                if(mode=="pressed")document.Surface.Input.SendPointer(PointerPhase.Down,button.WorldBound.center,pressedButtons:1);
                if(mode=="disabled")button.SetEnabled(false);
                Tick();var color=document.Styles.GetComputed(graphic).GetColor("--urui-graphic-tint");
                Require(Mathf.Abs(color.r-(mode=="hover"?245:200)/255f)<1.1f/255,"Graphic feedback: "+mode);
                Require(Mathf.Abs(color.a-(mode=="disabled"?128:255)/255f)<1.1f/255,"Graphic alpha: "+mode);
                Require(document.Styles.GetComputed(decoration).GetColor("--urui-graphic-tint")==Color.white,"Decoration was tinted");
                Require(document.Styles.GetComputed(button).GetColor("background-color").a==0,"Image button background");
                captureCamera.Render();Capture("DefaultControls-"+mode+".png",new Rect(0,0,1280,720));report.defaultStates++;
            }
            Require(EffectStatistics.GetTargets().LeasedTargets==before,"Simple graphic feedback allocated an effect target");
        }
        static int Mode(EffectParameters p,string family)=>family switch{
            "ToneFilter"=>(int)p.ToneFilter,"ColorFilter"=>(int)p.ColorFilter,"SamplingFilter"=>(int)p.SamplingFilter,"TransitionFilter"=>(int)p.TransitionFilter,"TargetMode"=>(int)p.TargetMode,"BlendType"=>(int)p.BlendType,"ShadowMode"=>(int)p.ShadowMode,"GradationMode"=>(int)p.GradationMode,"GradationColorFilter"=>(int)p.GradationColorFilter,"EdgeMode"=>(int)p.EdgeMode,"DetailFilter"=>(int)p.DetailFilter,"PatternArea"=>(int)p.PatternArea,"Flip"=>(int)p.Flip,_=>throw new ArgumentException(family)};
        void Capture(string name,Rect rectangle)
        {
            var x=Mathf.FloorToInt(rectangle.x);var y=Mathf.FloorToInt(rectangle.y);var width=Mathf.CeilToInt(rectangle.width);var height=Mathf.CeilToInt(rectangle.height);
            var texture=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=target;texture.ReadPixels(new Rect(x,target.height-y-height,width,height),0,0);texture.Apply();
                foreach(var pixel in texture.GetPixels()){Require(float.IsFinite(pixel.r)&&float.IsFinite(pixel.g)&&float.IsFinite(pixel.b)&&float.IsFinite(pixel.a),"Nonfinite pixel: "+name);if(pixel.r>.999f&&pixel.g<.001f&&pixel.b>.999f&&pixel.a>.999f)report.magentaPixels++;report.finitePixels++;}
                File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());report.captures.Add(name);
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(texture);}
        }
        void OnDestroy(){validationFence?.Release();validationFence=null;Time.captureDeltaTime=originalCaptureDelta;if(captureCamera)captureCamera.targetTexture=null;if(target){target.Release();UnityEngine.Object.Destroy(target);}}
    }
}
