using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if URUI_ORIGINAL_EFFECT
using UnityEngine.UI;
#else
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Styles;
#endif
namespace URUI.Effect.ModePerformance
{
    /// <summary>同一输入和计时窗口下比较原 UIEffect 与 URUI 的代表性新增效果。</summary>
    public sealed class ModePerformanceTests
    {
        [Serializable] sealed class Case
        {
            public string mode,inputSha256;public int elements,round;
            public List<double> synchronizeMs=new(1000),renderSubmitWallMs=new(1000);
            public List<long> allocatedBytes=new(1000);
        }
        [Serializable] sealed class Report
        {
            public string backend,unity,graphics,device,cpu,colorSpace;
            public int width=1280,height=720,warmup=300,samples=1000,rounds=3;
            public string method="Stopwatch: all per-node progress updates plus Canvas.ForceUpdateCanvases / Document.SynchronizeLayout; separately Camera.Render submission wall time. Automatic engine camera work is outside these windows; this is not GPU duration.";
            public bool allocationCounterAvailable;
            public List<Case> cases=new();
        }
        static Texture2D Input(bool pattern)
        {
            var texture=new Texture2D(64,64,TextureFormat.RGBA32,false,true){wrapMode=pattern?TextureWrapMode.Repeat:TextureWrapMode.Clamp};var pixels=new Color[4096];
            for(var y=0;y<64;y++)for(var x=0;x<64;x++){var u=x/63f;var v=y/63f;pixels[y*64+x]=pattern?new Color(u,v,1-u,Mathf.Repeat(u+v*.35f,1)):new Color(.15f+.7f*u,.1f+.4f*v,.8f-.5f*u,1);}
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        static string Hash(byte[] bytes){using var algorithm=SHA256.Create();return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static void Snapshot(RenderTexture target,string path)
        {
            var read=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
            try{RenderTexture.active=target;read.ReadPixels(new Rect(0,0,target.width,target.height),0,0);read.Apply();File.WriteAllBytes(path,read.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(read);}
        }
        [UnityTest,Timeout(900000)]
        public IEnumerator CompareRepresentativeModes()
        {
#if URUI_ORIGINAL_EFFECT
            const string backend="UIEffect5.8.4";
#else
            const string backend="URUI.Effect";
#endif
            var report=new Report{backend=backend,unity=Application.unityVersion,graphics=SystemInfo.graphicsDeviceType.ToString(),device=SystemInfo.graphicsDeviceName,cpu=SystemInfo.processorType,colorSpace=QualitySettings.activeColorSpace.ToString()};
            var output=Path.Combine(Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT"),"mode-performance");Directory.CreateDirectory(output);
            var probeBefore=GC.GetAllocatedBytesForCurrentThread();var probe=new byte[4096];var probeAfter=GC.GetAllocatedBytesForCurrentThread();GC.KeepAlive(probe);report.allocationCounterAvailable=probeAfter-probeBefore>=4096;
            var input=Input(false);var pattern=Input(true);var resourceHash=Hash(input.EncodeToPNG())+Hash(pattern.EncodeToPNG());
            var oldCapture=Time.captureDeltaTime;var oldVsync=QualitySettings.vSyncCount;var oldTarget=Application.targetFrameRate;
            Time.captureDeltaTime=1f/60;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            var cameraObject=new GameObject("Mode benchmark camera",typeof(Camera));cameraObject.tag="MainCamera";var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            var target=new RenderTexture(1280,720,0,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;
            try
            {
                foreach(var mode in new[]{"tone","blur-fast","transition-shiny"})foreach(var count in new[]{1,100})for(var round=0;round<3;round++)
                {
                    var host=new GameObject("Mode benchmark",typeof(RectTransform));var updates=new List<Action<float>>(count);Action sync;Action release;
#if URUI_ORIGINAL_EFFECT
                    var canvas=host.AddComponent<Canvas>();host.layer=30;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;camera.orthographic=true;camera.orthographicSize=360;camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.cullingMask=1<<30;
                    for(var index=0;index<count;index++)
                    {
                        var item=new GameObject("Node "+index,typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));item.layer=30;item.transform.SetParent(host.transform,false);
                        var rect=item.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.up;rect.sizeDelta=new Vector2(32,32);rect.anchoredPosition=new Vector2(index%35*34,-index/35*34);
                        item.GetComponent<RawImage>().texture=input;var effect=item.AddComponent<Coffee.UIEffects.UIEffect>();effect.transitionAutoPlaySpeed=0;effect.edgeShinyAutoPlaySpeed=0;
                        if(mode=="tone"){effect.toneFilter=Coffee.UIEffects.ToneFilter.Sepia;updates.Add(value=>effect.toneIntensity=value);}
                        else if(mode=="blur-fast"){effect.samplingFilter=Coffee.UIEffects.SamplingFilter.BlurFast;updates.Add(value=>effect.samplingIntensity=value);}
                        else{effect.transitionFilter=Coffee.UIEffects.TransitionFilter.Shiny;effect.transitionTexture=pattern;effect.transitionColor=new Color32(0,128,255,255);updates.Add(value=>effect.transitionRate=value);}
                    }
                    sync=Canvas.ForceUpdateCanvases;release=()=>UnityEngine.Object.DestroyImmediate(host);
#else
                    var surface=host.AddComponent<Surface>();surface.Viewport=new Vector2Int(1280,720);surface.Input.UseUnityInput=false;
                    var document=host.AddComponent<Document>();document.Surface=surface;document.Images=new[]{new ImageAsset("input.png",input)};document.TextResources=new[]{new TextResource("/effect.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect"))};
                    var module=new EffectModule(document,new Resolver(pattern));var css=mode=="tone"?"--urui-fx-tone-filter:sepia;":mode=="blur-fast"?"--urui-fx-sampling-filter:blur-fast;":"--urui-fx-transition-filter:shiny;--urui-fx-transition-texture:url('pattern.png');";
                    var html=new System.Text.StringBuilder("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}img{position:absolute;width:32px;height:32px;--urui-effect:ui;"+css+"}</style></head><body>");
                    for(var index=0;index<count;index++)html.Append("<img id='i").Append(index).Append("' src='input.png' style='left:").Append(index%35*34).Append("px;top:").Append(index/35*34).Append("px;'>");html.Append("</body></html>");document.LoadHtml(html.ToString(),"/benchmark.html");document.SynchronizeLayout();
                    var key=mode=="tone"?"--urui-fx-tone-intensity":mode=="blur-fast"?"--urui-fx-sampling-intensity":"--urui-fx-transition-progress";
                    for(var index=0;index<count;index++){var node=document.Q("i"+index);updates.Add(value=>node.Style.SetNumber(key,value));}
                    sync=()=>document.SynchronizeLayout();release=()=>{module.Dispose();document.Unload();UnityEngine.Object.DestroyImmediate(host);};
#endif
                    var sample=new Case{mode=mode,elements=count,round=round,inputSha256=Hash(System.Text.Encoding.UTF8.GetBytes(mode+"|"+count+"|32x32|35 columns,34 spacing|0.4/0.6|"+resourceHash))};
                    try
                    {
                        for(var frame=0;frame<1300;frame++)
                        {
                            yield return null;var before=GC.GetAllocatedBytesForCurrentThread();var started=Stopwatch.GetTimestamp();
                            var progress=frame%2==0?.4f:.6f;foreach(var update in updates)update(progress);sync();var synchronize=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
                            started=Stopwatch.GetTimestamp();camera.Render();var submission=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                            if(frame==299&&round==0)Snapshot(target,Path.Combine(output,mode+"-"+count+".png"));
                            if(frame>=300){sample.synchronizeMs.Add(synchronize);sample.renderSubmitWallMs.Add(submission);sample.allocatedBytes.Add(report.allocationCounterAvailable?allocated:-1);}
                        }
                        report.cases.Add(sample);File.WriteAllText(Path.Combine(output,"costs.json"),JsonUtility.ToJson(report,true));
                    }
                    finally{release();}
                    for(var frame=0;frame<4;frame++)yield return null;
                }
                Assert.That(report.cases.Count,Is.EqualTo(18));
            }
            finally{camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(input);UnityEngine.Object.DestroyImmediate(pattern);Time.captureDeltaTime=oldCapture;QualitySettings.vSyncCount=oldVsync;Application.targetFrameRate=oldTarget;}
        }
#if !URUI_ORIGINAL_EFFECT
        sealed class Resolver:IEffectResourceResolver
        {
            readonly Texture2D pattern;internal Resolver(Texture2D pattern){this.pattern=pattern;}
            public ImageLease AcquireImage(ComputedImageValue image)=>new(pattern);
            public GameObject ResolveParticles(string key)=>null;
        }
#endif
    }
}
