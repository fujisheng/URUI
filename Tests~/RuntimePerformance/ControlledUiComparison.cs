using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Profiling;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using URUI.Documents;
using URUI.Hosting;

namespace URUI.Validation
{
    public sealed class ControlledUiComparison
    {
        [Serializable] sealed class Row
        {
            public string backend,scenario;
            public int graphics,geometrySubmissions=-1,pixelError,round,warmFrames=300,timingFrames=1000;
            public bool gpuTimingEnabled;public ulong gpuTimerFrequency;
            public int gpuMissingFrames;public List<double> gpuFrameSamplesMs=new(1000);
            public bool drawCounterValid,drawEvidenceUsable;
            public long scopedAllocations;
            public List<long> drawSamples=new();
            public double refreshP50Ms,refreshP95Ms;
            public double[] refreshSamplesMs;
        }
        [Serializable] sealed class Report
        {
            public string unity,space,scope="128x128 opaque 3px images on a 4px grid; same textures and pixels; explicit camera.Render and enabled MainCamera; global draw samples may include automatic Editor render. GC scope is 32 explicit stable refreshes after 300 actual warm frames, excludes input/camera/game loop. CPU is synchronous refresh only, 3 rounds of 1000 actual frames with stack sampling disabled. GPU samples are FrameTimingManager entire-frame timings, including explicit and automatic camera renders; positive finite unique-timestamp samples only, empty means missing. No GPU readback in timing windows. Draw counter unusable when no positive sample; never infer zero draws from unavailable data.";
            public long positiveGcEvents;
            public List<Row> rows=new();
        }
        static long Allocations(Action action)
        {
            using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC.Alloc",8192,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            action();recorder.Stop();Assert.That(recorder.Valid&&!recorder.WrappedAround,Is.True);
            long count=0;for(var i=0;i<recorder.Count;i++)count+=recorder.GetSample(i).Count;return count;
        }
        static Color32[] Capture(Camera camera,RenderTexture target)
        {
            camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            var read=new Texture2D(128,128,TextureFormat.RGBA32,false,true);
            try{read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();return read.GetPixels32();}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(read);}
        }
        [UnityTest]
        public IEnumerator CompareMatchingOpaqueImages()
        {
#if UNITY_EDITOR
            var oldRecording=UnityEditorInternal.ProfilerDriver.enabled;
            UnityEditorInternal.ProfilerDriver.enabled=false;
#endif
            var oldStacks=UnityEngine.Profiling.Profiler.enableAllocationCallstacks;
            UnityEngine.Profiling.Profiler.enableAllocationCallstacks=false;
            var report=new Report{unity=Application.unityVersion,space=QualitySettings.activeColorSpace.ToString()};
            report.positiveGcEvents=Allocations(()=>{var probe=new byte[4096];GC.KeepAlive(probe);});
            Assert.That(report.positiveGcEvents,Is.GreaterThan(0));Assert.That(Allocations(()=>{}),Is.Zero);
            var output=Environment.GetEnvironmentVariable("URUI_CONTROLLED_PERFORMANCE_OUTPUT")??Path.Combine(Application.dataPath,"../ControlledResults");
            Directory.CreateDirectory(output);
            var cameraObject=new GameObject("Comparison camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.enabled=true;camera.allowHDR=false;camera.allowMSAA=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            var red=new Texture2D(1,1,TextureFormat.RGBA32,false);red.SetPixel(0,0,Color.red);red.Apply();
            var blue=new Texture2D(1,1,TextureFormat.RGBA32,false);blue.SetPixel(0,0,Color.blue);blue.Apply();
            GameObject root=null;
            try
            {
                foreach(var scenario in new[]{"same-100","same-1000","alternating-1000"})
                {
                    var count=scenario=="same-100"?100:1000;var alternating=scenario.StartsWith("alternating",StringComparison.Ordinal);
                    Color32[] reference=null;
                    for(var round=0;round<3;round++)
                    foreach(var backend in new[]{"UGUI","URUI"})
                    {
                        var row=new Row{backend=backend,scenario=scenario,graphics=count,round=round};Document document=null;Action refresh;
                        if(backend=="UGUI")
                        {
                            root=new GameObject("UGUI reference",typeof(RectTransform),typeof(Canvas));
                            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                            for(var i=0;i<count;i++)
                            {
                                var node=new GameObject("Image",typeof(RectTransform),typeof(RawImage));node.transform.SetParent(root.transform,false);
                                var image=node.GetComponent<RawImage>();image.texture=alternating&&i%2!=0?blue:red;image.maskable=false;image.raycastTarget=false;
                                var rect=node.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
                                rect.sizeDelta=new Vector2(3,3);rect.anchoredPosition=new Vector2(i%32*4,-i/32*4);
                            }
                            refresh=Canvas.ForceUpdateCanvases;
                        }
                        else
                        {
                            root=new GameObject("URUI candidate");var surface=root.AddComponent<Surface>();surface.Viewport=new Vector2Int(128,128);surface.Input.UseUnityInput=false;
                            document=root.AddComponent<Document>();document.Surface=surface;
                            document.Images=new[]{new URUI.Resources.ImageAsset("red.png",red),new URUI.Resources.ImageAsset("blue.png",blue)};
                            var html=new StringBuilder("<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:3px;height:3px;}</style></head><body>");
                            for(var i=0;i<count;i++)html.Append("<img src='").Append(alternating&&i%2!=0?"blue.png":"red.png").Append("' style='left:")
                                .Append(i%32*4).Append("px;top:").Append(i/32*4).Append("px;'>");
                            document.LoadHtml(html.Append("</body></html>").ToString(),"/controlled.html");refresh=document.SynchronizeLayout;
                        }
                        for(var frame=0;frame<row.warmFrames;frame++){refresh();camera.Render();yield return null;}
                        row.scopedAllocations=Allocations(()=>{for(var i=0;i<32;i++)refresh();});
                        Assert.That(row.scopedAllocations,Is.Zero,backend+" "+scenario);
                        // 相同静态内容，比较显式刷新本身；相机与帧等待不在 CPU 计时范围。
                        row.gpuTimingEnabled=FrameTimingManager.IsFeatureEnabled();row.gpuTimerFrequency=FrameTimingManager.GetGpuTimerFrequency();
                        var timings=new FrameTiming[1];ulong previousTimestamp=0;
                        row.refreshSamplesMs=new double[row.timingFrames];
                        for(var sample=0;sample<row.refreshSamplesMs.Length;sample++)
                        {
                            var start=Stopwatch.GetTimestamp();refresh();
                            row.refreshSamplesMs[sample]=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                            camera.Render();FrameTimingManager.CaptureFrameTimings();yield return null;
                            var available=FrameTimingManager.GetLatestTimings(1,timings);var timing=timings[0];
                            if(available>0&&timing.frameStartTimestamp!=0&&timing.frameStartTimestamp!=previousTimestamp&&double.IsFinite(timing.gpuFrameTime)&&timing.gpuFrameTime>0)
                            {row.gpuFrameSamplesMs.Add(timing.gpuFrameTime);previousTimestamp=timing.frameStartTimestamp;}
                            else row.gpuMissingFrames++;
                        }
                        var sorted=(double[])row.refreshSamplesMs.Clone();Array.Sort(sorted);
                        row.refreshP50Ms=sorted[sorted.Length/2];row.refreshP95Ms=sorted[(int)(sorted.Length*.95)];
                        using(var counter=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",16))
                        {
                            for(var frame=0;frame<8;frame++){refresh();camera.Render();yield return null;}
                            counter.Stop();row.drawCounterValid=counter.Valid;
                            for(var i=0;i<counter.Count;i++)row.drawSamples.Add(counter.GetSample(i).Value);
                            row.drawEvidenceUsable=row.drawCounterValid&&row.drawSamples.Exists(value=>value>0);
                        }
                        if(document)
                        {
                            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                            var host=typeof(Document).GetField("host",flags).GetValue(document);
                            row.geometrySubmissions=(int)host.GetType().GetProperty("RecordedGeometryDraws",flags).GetValue(host);
                            Assert.That(row.geometrySubmissions,Is.EqualTo(1));
                        }
                        var pixels=Capture(camera,target);
                        if(reference==null)reference=pixels;
                        else for(var i=0;i<pixels.Length;i++)
                        {
                            row.pixelError=Math.Max(row.pixelError,Math.Abs(pixels[i].r-reference[i].r));
                            row.pixelError=Math.Max(row.pixelError,Math.Abs(pixels[i].g-reference[i].g));
                            row.pixelError=Math.Max(row.pixelError,Math.Abs(pixels[i].b-reference[i].b));
                        }
                        Assert.That(row.pixelError,Is.LessThanOrEqualTo(1));report.rows.Add(row);
                        if(document&&scenario=="same-1000")
                        {
                            var ugui=report.rows.Find(value=>value.backend=="UGUI"&&value.scenario==scenario&&value.round==round);
                            Assert.That(row.refreshP95Ms,Is.LessThanOrEqualTo(ugui.refreshP95Ms+.25),"相同1000图片同步P95须处于UGUI+0.25ms预算内。");
                        }
                        UnityEngine.Object.DestroyImmediate(root);root=null;yield return null;
                    }
                }
            }
            finally
            {
#if UNITY_EDITOR
                UnityEditorInternal.ProfilerDriver.enabled=oldRecording;
#endif
                UnityEngine.Profiling.Profiler.enableAllocationCallstacks=oldStacks;
                File.WriteAllText(Path.Combine(output,"controlled-"+report.space+".json"),JsonUtility.ToJson(report,true));
                if(root)UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(red);UnityEngine.Object.DestroyImmediate(blue);
            }
        }
    }
}
