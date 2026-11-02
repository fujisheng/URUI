#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using URUI.Internal.Hosting;
using URUI.Internal.Interop;
using URUI.Internal.Resources;

namespace URUI.Tests
{
    // Small draw-count comparison. Legacy reference explicitly uses the retained
    // RGBA registration API; production ImageAsset always takes the borrowed path.
    public sealed class BorrowedBatchValidation : MonoBehaviour
    {
        [Serializable] sealed class Row
        {
            public string registration, scenario, shader, keywords;
            public bool batching, profilerCounterValid, profilerCounterUsable;
            public int nativeCommands, distinctTextureIds, combinedGroups, uiDrawSubmissions, commandBufferBytes;
            public long unityDrawCalls, emptyCameraDrawCalls; public int unityImmediateDrawCalls;
        }
        [Serializable] sealed class Report { public bool passed;public int assertions;public string error;public List<Row> rows=new List<Row>();public string scope="Editor / Metal, three tiny ordered layouts; native commands, actual steady-frame combined caches and recorded DrawMesh submissions/command-buffer bytes. Global Draw Calls Count gives no usable samples for this headless CommandBuffer rendering, so it is NOT used as a GPU draw count. Legacy reference uses the retained RGBA path in the same version. No performance matrix or transparent reordering."; }
        readonly Report report=new Report();Texture2D a,b;ProfilerRecorder counter;Camera camera;
        void Check(bool value,string label){report.assertions++;if(!value)throw new Exception(label);}
        static async Task Frames(int count=7){int end=Time.frameCount+count;float start=Time.realtimeSinceStartup;while(Time.frameCount<end){if(Time.realtimeSinceStartup-start>12)throw new Exception("frame timeout");await Task.Delay(20);}}
        public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Small batch comparison").AddComponent<BorrowedBatchValidation>();EditorApplication.delayCall+=()=>EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;}
        static Texture2D Image(Color32 color){var t=new Texture2D(4,4,TextureFormat.RGBA32,false,false);var p=new Color32[16];for(int i=0;i<p.Length;i++)p[i]=color;t.SetPixels32(p);t.Apply();return t;}
        static string Markup(string scenario)
        {
            string first="<img style='left:0px' src='a.png'/>";
            string middle=scenario=="image-font-image"?"<span style='position:absolute;left:48px;top:0px'>AA</span>":"<img style='left:48px' src='b.png'/>";
            string last="<img style='left:96px' src='"+(scenario=="image-font-image"?"b.png":"a.png")+"'/>";
            return "<rml><head><style>body{margin:0;font-family:Arial;font-size:16px;color:white}img{display:block;position:absolute;top:0px;width:32px;height:32px}</style></head><body>"+first+middle+last+"</body></rml>";
        }
        static void RegisterRgba(string source,Texture2D t)
        {
            var p=t.GetPixels32();var bytes=new byte[p.Length*4];int k=0;
            for(int y=t.height-1;y>=0;y--)for(int x=0;x<t.width;x++){var c=p[y*t.width+x];bytes[k++]=c.r;bytes[k++]=c.g;bytes[k++]=c.b;bytes[k++]=c.a;}
            if(NativeMethods.urui_register_rgba_texture(source,t.width,t.height,bytes,bytes.Length,out _)!=1)throw new Exception(NativeMethods.LastError());
        }
        long CounterMaximum(){long value=0;for(int i=0;i<counter.Count;i++)value=Math.Max(value,counter.GetSample(i).Value);return value;}
        async Task<Row> Measure(bool borrowed,string scenario,bool batching)
        {
            // Batch mode may not automatically render a Game View. Render exactly
            // once with the camera disabled, then read the completed frame counter.
            camera.enabled=false;counter.Reset();camera.Render();await Frames(3);long empty=counter.Valid?CounterMaximum():-1;
            camera.enabled=true;
            var markup=Markup(scenario);var text=new TextAsset(borrowed?markup:"<rml><body/></rml>");
            var host=new GameObject("batch fixture host").AddComponent<RenderHost>();host.document=text;host.batchDisjointCommands=batching;
            var second=scenario=="different-images"?b:a;
            if(borrowed)host.imageAssets=new[]{new ImageBinding{source="a.png",texture=a},new ImageBinding{source="b.png",texture=second}};
            host.InitializeForDocument();Check(host.Ready,"host initialization");
            if(!borrowed){NativeMethods.urui_unload_document();RegisterRgba("a.png",a);RegisterRgba("b.png",second);host.LoadMarkup(markup);}
            camera.enabled=false;await Frames(9);
            counter.Reset();camera.Render();int immediate=UnityStats.drawCalls;await Frames(3);long actual=counter.Valid?CounterMaximum():-1;
            var commands=new CommandNative[NativeMethods.urui_command_count()];Check(NativeMethods.urui_get_commands(commands,commands.Length)==commands.Length,"actual ordered native commands");
            var ids=new HashSet<ulong>();foreach(var c in commands)if(c.texture!=0)ids.Add(c.texture);
            var caches=(IDictionary)typeof(RenderHost).GetField("batches",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);int saved=0;
            foreach(DictionaryEntry pair in caches){var items=(Array)pair.Value.GetType().GetField("items").GetValue(pair.Value);saved+=items.Length-1;}
            var material=(Material)typeof(RenderHost).GetField("material",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
            var buffer=(CommandBuffer)typeof(RenderHost).GetField("buffer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
            var row=new Row{commandBufferBytes=buffer.sizeInBytes,profilerCounterUsable=counter.Valid&&actual>empty,registration=borrowed?"borrowed":"legacy-rgba-reference",scenario=scenario,batching=batching,nativeCommands=commands.Length,distinctTextureIds=ids.Count,combinedGroups=caches.Count,uiDrawSubmissions=commands.Length-saved,unityDrawCalls=actual,unityImmediateDrawCalls=immediate,emptyCameraDrawCalls=empty,profilerCounterValid=counter.Valid,shader=material.shader.name,keywords=string.Join(",",material.shaderKeywords)};
            Check(row.nativeCommands==3,"three real ordered draws before host batching");
            Check(row.uiDrawSubmissions==(batching&&borrowed&&scenario=="same-texture-aliases"?1:3),"expected batching boundary: "+row.registration+" / "+scenario+" / "+batching);
            report.rows.Add(row);
            host.ReleaseForDocument();Destroy(host.gameObject);Destroy(text);await Frames(3);return row;
        }
        async void Start()
        {
            try
            {
                camera=new GameObject("Batch camera").AddComponent<Camera>();camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                a=Image(new Color32(200,100,50,180));b=Image(new Color32(30,100,200,180));counter=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",16);
                foreach(var scenario in new[]{"same-texture-aliases","different-images","image-font-image"})foreach(bool batching in new[]{false,true})
                {
                    var old=await Measure(false,scenario,batching);var current=await Measure(true,scenario,batching);
                    Check(current.uiDrawSubmissions<=old.uiDrawSubmissions,"borrowing adds no submissions");Check(current.shader==old.shader&&current.keywords==old.keywords,"same shader and keyword set");
                }
                report.passed=true;
            }
            catch(Exception error){report.error=error.ToString();Debug.LogException(error);RenderHost.ActiveHost?.ReleaseForDocument();}
            finally{counter.Dispose();if(a)Destroy(a);if(b)Destroy(b);}
            const string path="Artifacts/ImageMemoryAudit/Implementation/batching.json";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));Debug.Log("BORROWED_BATCH_"+(report.passed?"PASS":"FAIL")+" rows="+report.rows.Count);EditorApplication.Exit(report.passed?0:1);
        }
    }
}
#endif
