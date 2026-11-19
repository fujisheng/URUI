using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Animation;
using URUI.Controls;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Rendering.Effects;

namespace URUI.Validation.DefaultControlPerformance
{
    /// <summary>Measures identical image-button scenes against frozen pre-change and current packages.</summary>
    public sealed class DefaultControlPerformanceTests
    {
        sealed class Clock : IUIClock { public double Now { get; set; } public float Delta => 1f/60; }
        [Serializable] sealed class Row
        {
            public int count,round,renderTexturesBefore,renderTexturesAfter;
            public string inputSha256;
            public bool dynamic;
            public List<double> synchronizeMs=new(),submitMs=new();
        }
        [Serializable] sealed class Report
        {
            public string unity,graphics,device,cpu,colorSpace,kind;
            public int width=1280,height=720,warmup=300,samples=1000,rounds=3;
            public string method="Stopwatch around focus change + SynchronizeLayout and explicit Camera.Render; submit wall time is not GPU duration.";
            public bool allocationCounterAvailable;
            public List<Row> rows=new();
        }
        [UnityTest,Timeout(900000)] public IEnumerator Measure()
        {
            var output=Environment.GetEnvironmentVariable("URUI_DEFAULT_PERFORMANCE_OUTPUT");
            Assert.That(output,Is.Not.Null.And.Not.Empty);Directory.CreateDirectory(output);
            bool legacy=Environment.GetEnvironmentVariable("URUI_DEFAULT_PERFORMANCE_KIND")=="baseline";
            var report=new Report{unity=Application.unityVersion,graphics=SystemInfo.graphicsDeviceType.ToString(),device=SystemInfo.graphicsDeviceName,cpu=SystemInfo.processorType,colorSpace=QualitySettings.activeColorSpace.ToString(),kind=legacy?"baseline":"candidate"};
            long before=GC.GetAllocatedBytesForCurrentThread();var allocation=new byte[4096];report.allocationCounterAvailable=GC.GetAllocatedBytesForCurrentThread()-before>=4096;GC.KeepAlive(allocation);
            int vsync=QualitySettings.vSyncCount,fps=Application.targetFrameRate;float delta=Time.captureDeltaTime;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Time.captureDeltaTime=1f/60;
            var cameraObject=new GameObject("Default controls benchmark camera",typeof(Camera));cameraObject.tag="MainCamera";
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var target=new RenderTexture(1280,720,0,RenderTextureFormat.ARGBHalf);Assert.That(target.Create(),Is.True);camera.targetTexture=target;
            var white=new Texture2D(1,1,TextureFormat.RGBA32,false);white.SetPixel(0,0,Color.white);white.Apply();
            try
            {
                foreach(int count in new[]{100,1000}) foreach(bool dynamic in new[]{false,true}) for(int round=0;round<3;round++)
                {
                    var host=new GameObject("Default controls workload");
                    try
                    {
                        var surface=host.AddComponent<Surface>();surface.Viewport=new Vector2Int(1280,720);surface.Input.UseUnityInput=false;var clock=new Clock();surface.UIClock=clock;
                        var document=host.AddComponent<Document>();document.Surface=surface;document.Images=new[]{new ImageAsset("white.png",white)};
                        document.TextResources=new[]{new TextResource("/urui/controls.css",UnityEngine.Resources.Load<TextAsset>("URUI/controls"))};
                        var html=new StringBuilder("<!doctype html><html><head><link rel='stylesheet' href='/urui/controls.css'><style>body{margin:0;background:black;}button.bench,button.bench:hover,button.bench:focus,button.bench:active{position:absolute;width:24px;height:24px;min-width:0;min-height:0;padding:0;border:0;background-color:transparent;}img{position:absolute;left:0;top:0;width:24px;height:24px;}");
                        // Only the feedback implementation differs; both paths render the same states and textures.
                        if(legacy)html.Append(".bench>img{transition:--urui-image-color .1s linear;}.bench:hover>img,.bench:focus>img{--urui-image-color:#f5f5f5;}.bench:active>img{--urui-image-color:#c8c8c8;}.bench:disabled>img{--urui-image-color:#c8c8c880;}");
                        html.Append("</style></head><body>");
                        for(int i=0;i<count;i++)html.Append($"<button id='b{i}' class='bench' data-urui-skin='image' style='left:{i%40*28}px;top:{i/40*28}px;'><img src='white.png' data-urui-graphic></button>");
                        html.Append("</body></html>");document.LoadHtml(html.ToString(),"/buttons.html");document.SynchronizeLayout();
                        var buttons=new Button[count];for(int i=0;i<count;i++)
                        {
                            buttons[i]=document.Q<Button>("b"+i);
                            Assert.That(buttons[i].Layout.width,Is.EqualTo(24).Within(.001f));Assert.That(buttons[i].Layout.height,Is.EqualTo(24).Within(.001f));
                            var image=buttons[i].Query<Image>().First();
                            Assert.That(image.WorldBound.position,Is.EqualTo(buttons[i].WorldBound.position));
                        }
                        string structure=html.ToString();structure=structure.Substring(structure.IndexOf("<body>",StringComparison.Ordinal));
                        using var digest=System.Security.Cryptography.SHA256.Create();
                        var row=new Row{count=count,dynamic=dynamic,round=round,inputSha256=BitConverter.ToString(digest.ComputeHash(Encoding.UTF8.GetBytes(structure))).Replace("-", "").ToLowerInvariant()};
                        for(int i=0;i<1300;i++)
                        {
                            clock.Now+=1d/60;
                            var t=Stopwatch.GetTimestamp();if(dynamic)buttons[i%count].Focus();document.SynchronizeLayout();var u=Stopwatch.GetTimestamp();camera.Render();var v=Stopwatch.GetTimestamp();
                            if(i==299)row.renderTexturesBefore=UnityEngine.Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
                            if(i>=300){row.synchronizeMs.Add((u-t)*1000d/Stopwatch.Frequency);row.submitMs.Add((v-u)*1000d/Stopwatch.Frequency);}
                            yield return null;
                        }
                        row.renderTexturesAfter=UnityEngine.Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
                        Assert.That(row.renderTexturesAfter,Is.EqualTo(row.renderTexturesBefore),"Simple controls must not grow RT count");
                        Assert.That(EffectStatistics.GetTargets().LeasedTargets,Is.Zero,"Simple controls must not lease effect targets");
                        var previous=RenderTexture.active;RenderTexture.active=target;var screenshot=new Texture2D(1280,720,TextureFormat.RGBA32,false,true);
                        screenshot.ReadPixels(new Rect(0,0,1280,720),0,0);screenshot.Apply();File.WriteAllBytes(Path.Combine(output,$"buttons-{count}-{dynamic}-{round}.png"),screenshot.EncodeToPNG());RenderTexture.active=previous;UnityEngine.Object.Destroy(screenshot);
                        report.rows.Add(row);File.WriteAllText(Path.Combine(output,"costs.json"),JsonUtility.ToJson(report,true));document.Unload();
                    }
                    finally{UnityEngine.Object.DestroyImmediate(host);}
                    yield return null;
                }
            }
            finally
            {
                camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(white);UnityEngine.Object.DestroyImmediate(cameraObject);
                QualitySettings.vSyncCount=vsync;Application.targetFrameRate=fps;Time.captureDeltaTime=delta;
            }
        }
    }
}
