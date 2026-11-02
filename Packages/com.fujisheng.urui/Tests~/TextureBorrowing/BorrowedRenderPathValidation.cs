#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using URUI.Internal.Interop;
using URUI.Internal.Resources;
using URUI.Internal.Rendering.Parameters;
using URUI.Internal.Rendering.Frames;

namespace URUI.Tests
{
    public sealed class BorrowedRenderPathValidation : MonoBehaviour
    {
        const string Output="Artifacts/ImageMemoryAudit/Implementation/render-paths.json";
        [Serializable] sealed class Report { public bool passed;public int assertions;public string error;public List<string> checks=new List<string>(); }
        readonly Report report=new Report(); Texture2D source; BorrowedTextureRegistry registry; RenderTexture target;
        void Check(bool ok,string label){report.assertions++;report.checks.Add(label+": "+ok);if(!ok)throw new Exception(label);}
        static async Task Frames(int n=3){int end=Time.frameCount+n;float started=Time.realtimeSinceStartup;while(Time.frameCount<end){if(Time.realtimeSinceStartup-started>10)throw new Exception("frame timeout");await Task.Delay(20);}}
        async Task Wait(Func<bool> complete){float started=Time.realtimeSinceStartup;while(!complete()){if(Time.realtimeSinceStartup-started>6)throw new Exception("GPU retirement timeout");await Frames(1);}}
        ulong Context()
        {
            Check(NativeMethods.urui_create(640,360,"/System/Library/Fonts/Supplemental/Arial.ttf")==1,"create test context");
            registry=new BorrowedTextureRegistry();registry.Register(new[]{new ImageBinding{source="image-a",texture=source},new ImageBinding{source="image-b",texture=source}});
            Check(NativeMethods.urui_load_document("<rml><head><style>body{margin:0;font-family:Arial}img{width:64px;height:32px}</style></head><body><img src='image-a'/><img src='image-b'/><p>owned glyph atlas</p></body></rml>")==1,"load real images and generated atlas");
            Check(NativeMethods.urui_render_plan_enable(1)==1,"enable explicit retained-plan capability");
            NativeMethods.urui_update();NativeMethods.urui_render();var commands=new CommandNative[NativeMethods.urui_command_count()];NativeMethods.urui_get_commands(commands,commands.Length);
            ulong id=0;foreach(var c in commands)if(BorrowedTextureRegistry.IsBorrowed(c.texture)){if(id!=0)Check(id==c.texture,"aliases share logical ID in prepared paths");id=c.texture;}
            Check(id!=0,"borrowed image is present in actual native commands");return id;
        }
        void Unregister(ulong id)
        {
            NativeMethods.urui_unload_document();Check(NativeMethods.urui_unregister_texture("image-a")==1,"unregister first alias");Check(NativeMethods.urui_texture_exists(id)==1,"other alias retains metadata");Check(NativeMethods.urui_unregister_texture("image-b")==1,"unregister last alias");Check(NativeMethods.urui_texture_exists(id)==0,"last alias releases native metadata");registry.Clear();
        }
        public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("borrowed render path checks").AddComponent<BorrowedRenderPathValidation>();EditorApplication.delayCall+=()=>EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;}
        async void Start()
        {
            try
            {
                source=new Texture2D(8,8,TextureFormat.RGBA32,false,false);var pixels=new Color32[64];for(int i=0;i<64;i++)pixels[i]=new Color32(20,100,200,128);source.SetPixels32(pixels);source.Apply(false,true);
                target=new RenderTexture(640,360,0,RenderTextureFormat.ARGB32);target.Create();
                var legacy=Shader.Find("URUI/Premultiplied");var parameter=Shader.Find("URUI/ParameterTable");
                foreach(bool shared in new[]{false,true})
                {
                    ulong id=Context();var frame=new ParameterFrame();Check(frame.Capture(),"capture parameter frame");
                    var renderer=new ParameterTableRenderer(legacy,parameter,shareResources:shared,borrowedTextures:registry);
                    Check(renderer.AcquirePlan(frame,out var plan)==ParameterTableRenderer.Result.Success,"acquire parameter plan: "+renderer.LastFailure);
                    Check(ReferenceEquals(plan.textures[id],source),"parameter path binds caller texture without upload");
                    Check(plan.borrowedTextureIds.Contains(id),"parameter plan records borrowed ownership");
                    var owned=new List<Texture2D>();foreach(var t in plan.textures)if(!BorrowedTextureRegistry.IsBorrowed(t.Key))owned.Add(t.Value);Check(owned.Count>0,"parameter plan retains generated owned atlas");
                    Check(renderer.TryRecord(frame,target,false,out var ticket)==ParameterTableRenderer.Result.Success,"record parameter shader draw");renderer.Submit(ticket);
                    Unregister(id);Check(source&&ReferenceEquals(plan.textures[id],source),"submitted plan retains borrowed reference after unregister");renderer.ReleasePlan(plan);
                    await Wait(renderer.TryDispose);Check(renderer.TryDispose(),"parameter disposal is idempotent");await Frames();Check(source,"parameter retirement never destroys borrowed texture");foreach(var t in owned)Check(!t,"parameter retirement destroys owned atlas");NativeMethods.urui_destroy();
                }
                {
                    ulong id=Context();var commands=new CommandBuffer();var root=new NativeFrameRoot(commands,legacy,parameter,registry);
                    Check(root.Record(640,360,target)==ParameterTableRenderer.Result.Success,"record native borrowed-frame shader draw: "+root.FailureReason);Graphics.ExecuteCommandBuffer(commands);
                    var cache=(IDictionary)typeof(NativeFrameRoot).GetField("textures",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(root);var owned=new List<Texture2D>();bool found=false;
                    foreach(DictionaryEntry p in cache)
                    {
                        var value=p.Value;var t=(Texture2D)value.GetType().GetField("texture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);bool owns=(bool)value.GetType().GetField("owned",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
                        if(ReferenceEquals(t,source)){found=true;Check(!owns,"native-frame cache borrows original with no destruction rights");}else if(owns)owned.Add(t);
                    }
                    Check(found,"native-frame path uses exact original Texture2D");Check(owned.Count>0,"native-frame path still owns generated atlas");
                    Unregister(id);root.Close();await Wait(root.TryDispose);Check(root.TryDispose(),"native-frame disposal is idempotent");await Frames();Check(source,"native-frame retirement preserves borrowed source");foreach(var t in owned)Check(!t,"native-frame retirement destroys owned atlas");commands.Release();NativeMethods.urui_destroy();
                }
                Destroy(source);target.Release();Destroy(target);await Frames();Check(!source,"only caller destroys source");report.passed=true;
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);NativeMethods.urui_destroy();}
            Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,JsonUtility.ToJson(report,true));Debug.Log("BORROWED_RENDER_PATH_"+(report.passed?"PASS":"FAIL")+" "+report.assertions);EditorApplication.Exit(report.passed?0:1);
        }
    }
}
#endif
