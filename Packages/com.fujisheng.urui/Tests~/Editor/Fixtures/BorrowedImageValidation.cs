#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using URUI.Documents;
using URUI.Resources;

public sealed class BorrowedImageValidation : MonoBehaviour
{
    const string Output="Artifacts/ImageMemoryAudit/Implementation";
    const string Markup="<rml><head><style>body{margin:0;} img{width:64px;height:32px;}</style></head><body><img src='borrow/a.png'/><img src='borrow/b.png'/></body></rml>";
    [Serializable] public sealed class Report { public bool passed; public int assertions; public string error, unity, graphics; public List<string> checks=new List<string>(); public List<string> failedChecks=new List<string>(); public int borrowedNativeBytes, ownedImageCopies; public bool sourceReadable; public ulong firstId, reloadedId; }
    [StructLayout(LayoutKind.Sequential)] struct Command { public ulong geometry,texture; public float tx,ty; public int clip,x,y,w,h; }
    static class Native {
        [DllImport("URUIBridge")] internal static extern int urui_command_count();
        [DllImport("URUIBridge")] internal static extern int urui_get_commands([Out]Command[] commands,int capacity);
        [DllImport("URUIBridge")] internal static extern int urui_texture_exists(ulong id);
        [DllImport("URUIBridge")] internal static extern int urui_texture_protocol_version();
        [DllImport("URUIBridge")] internal static extern int urui_get_texture_kind(ulong id);
        [DllImport("URUIBridge")] internal static extern int urui_get_texture_info(ulong id,out int w,out int h,out int bytes);
    }
    readonly Report report=new Report(); Texture2D image; Document doc;
    void Check(bool ok,string label){report.assertions++; report.checks.Add(label+": "+ok);if(!ok)throw new Exception(label);}
    void CheckDeferred(bool ok,string label){report.assertions++;report.checks.Add(label+": "+ok);if(!ok)report.failedChecks.Add(label);}
    static async Task Frames(int n=3){int end=Time.frameCount+n;float start=Time.realtimeSinceStartup;while(Time.frameCount<end){if(Time.realtimeSinceStartup-start>12)throw new Exception("frame timeout");await Task.Delay(20);}}
    object Host=>typeof(Document).GetField("host",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(doc);
    Texture2D Resolve(ulong id)=>(Texture2D)Host.GetType().GetMethod("LoadTexture",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Host,new object[]{id});
    HashSet<ulong> Handles(){int n=Native.urui_command_count();var c=new Command[n];Check(Native.urui_get_commands(c,n)==n,"read actual native draw commands");var ids=new HashSet<ulong>();foreach(var v in c)if(v.texture!=0)ids.Add(v.texture);return ids;}
    public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var c=new GameObject("Borrow audit camera").AddComponent<Camera>();c.tag="MainCamera";new GameObject("Borrowed image checks").AddComponent<BorrowedImageValidation>();EditorApplication.delayCall+=()=>EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;}
    async void Start(){Directory.CreateDirectory(Output);try{
        report.unity=Application.unityVersion;report.graphics=SystemInfo.graphicsDeviceType.ToString();
        image=new Texture2D(64,32,TextureFormat.RGBA32,false,false){name="borrowed unreadable source",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
        var pixels=new Color32[64*32];for(int y=0;y<32;y++)for(int x=0;x<64;x++)pixels[y*64+x]=y<16?new Color32(255,0,0,128):new Color32(0,100,200,64);image.SetPixels32(pixels);image.Apply(false,true);report.sourceReadable=image.isReadable;
        doc=new GameObject("borrowed doc").AddComponent<Document>();doc.Images=new[]{new ImageAsset("borrow/a.png",image),new ImageAsset("borrow/b.png",image),new ImageAsset("borrow/unused.png",image)};
        doc.LoadMarkup(Markup);doc.Input.UseUnityInput=false;await Frames();
        Check(doc.IsLoaded&&!image.isReadable,"non-readable Texture2D loads without CPU read");Check(Native.urui_texture_protocol_version()==1,"native texture protocol v1");
        var ids=Handles();Check(ids.Count==1,"same Unity texture aliases share one native resource ID");foreach(var id in ids)report.firstId=id;
        Check((report.firstId&(1UL<<63))!=0,"borrowed IDs use separate domain");
        Check(Native.urui_get_texture_kind(report.firstId)==2,"borrowed native metadata kind");Check(Native.urui_get_texture_info(report.firstId,out int w,out int h,out int bytes)==1&&w==64&&h==32&&bytes==0,"borrowed metadata retains zero native pixel bytes");report.borrowedNativeBytes=bytes;
        Check(ReferenceEquals(Resolve(report.firstId),image),"renderer binds exact caller texture instance");
        var owned=(IDictionary)Host.GetType().GetField("textures",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Host);report.ownedImageCopies=owned.Count;Check(owned.Count==0,"no owned image upload or converted texture");
        Check(image.filterMode==FilterMode.Point&&image.wrapMode==TextureWrapMode.Repeat,"caller sampler unchanged");await Frames(5);Check(ReferenceEquals(Resolve(report.firstId),image),"steady frames share caller texture");
        doc.LoadMarkup(Markup);doc.Input.UseUnityInput=false;await Frames();ids=Handles();Check(ids.Count==1,"reload keeps alias sharing");foreach(var id in ids)report.reloadedId=id;Check(report.reloadedId!=report.firstId,"resource IDs never recycle across document reload");Check(Native.urui_texture_exists(report.firstId)==0,"old context handle no longer exists");Check(ReferenceEquals(Resolve(report.reloadedId),image),"reload retains original GPU object");
        doc.Unload();await Frames();Check(image,"unload never destroys borrowed source");Check(Native.urui_texture_exists(report.reloadedId)==0,"unload releases borrowed native metadata");
        doc.Images=Array.Empty<ImageAsset>();doc.LoadMarkup("<rml><head><style>body{font-family:Arial;}</style></head><body>Owned font atlas</body></rml>");doc.Input.UseUnityInput=false;await Frames();
        var atlasIds=Handles();Check(atlasIds.Count>0,"owned generated font atlas is rendered");var atlases=new List<Texture2D>();foreach(var id in atlasIds){Check(Native.urui_get_texture_kind(id)==1,"font atlas remains owned");var atlas=Resolve(id);Check(atlas&&atlas!=image&&!atlas.isReadable,"owned atlas has separate uploaded texture");atlases.Add(atlas);}
        doc.Unload();doc.Unload();await Frames();foreach(var atlas in atlases)Check(!atlas,"owned atlas released after repeated unload");Check(image,"borrowed source survives owned cleanup");
        // Metal rejects integer Texture2D creation itself; exercise the actual
        // registry metadata validator directly, without fabricating a GPU asset.
        var registryType=typeof(Document).Assembly.GetType("URUI.Internal.Resources.BorrowedTextureRegistry");
        var validateFormat=registryType.GetMethod("ValidateColorFormat",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var format in new[]{UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UInt,UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat,UnityEngine.Experimental.Rendering.GraphicsFormat.None})
        {
            bool rejected=false;if(validateFormat!=null)try{validateFormat.Invoke(null,new object[]{format});}catch(TargetInvocationException error){rejected=error.InnerException is ArgumentException;}
            Check(rejected,"registry rejects non-color metadata: "+format);
        }
        // Borrowing is live: source updates need no re-registration or texture upload by URUI.
        var mutable=new Texture2D(64,32,TextureFormat.RGBA32,false,false);mutable.SetPixels32(pixels);mutable.Apply(false,false);
        doc.Images=new[]{new ImageAsset("borrow/a.png",mutable),new ImageAsset("borrow/b.png",mutable)};doc.LoadMarkup(Markup);doc.Input.UseUnityInput=false;await Frames();ids=Handles();ulong mutableId=0;foreach(var id in ids)mutableId=id;
        var contractType=typeof(Document).Assembly.GetType("URUI.Internal.Rendering.Shaders.ShaderPropertyContract");
        var contract=ScriptableObject.CreateInstance(contractType);contractType.GetMethod("BindBuiltinForEditor").Invoke(contract,new object[]{Shader.Find("URUI/Premultiplied")});
        Host.GetType().GetField("propertyContract").SetValue(Host,contract);
        Host.GetType().GetField("retainUnchangedCommands").SetValue(Host,true);
        var ownership=Host.GetType().GetProperty("recordingOwnership");ownership.SetValue(Host,Enum.ToObject(ownership.PropertyType,1));
        await Frames(8);
        var dependenciesAlive=Host.GetType().GetMethod("CheckRetainedResources",BindingFlags.Instance|BindingFlags.NonPublic);
        Check((bool)dependenciesAlive.Invoke(Host,null),"retained dependency snapshot initially valid");
        var before=mutable.updateCount;mutable.SetPixel(0,0,Color.green);mutable.Apply(false,false);
        CheckDeferred(!(bool)dependenciesAlive.Invoke(Host,null),"source pixel updates invalidate retained command dependencies");
        await Frames();Check(mutable.updateCount!=before&&ReferenceEquals(Resolve(mutableId),mutable),"source pixel changes keep same live texture binding");
        mutable.Reinitialize(32,32);bool resized=false;try{Resolve(mutableId);}catch(TargetInvocationException error){resized=error.InnerException is InvalidOperationException&&error.InnerException.Message.Contains("dimensions/format");}Check(resized,"reinitializing a registered source is rejected explicitly");doc.Unload();await Frames();Destroy(contract);Check(mutable,"invalid resized borrowed source is not destroyed");Destroy(mutable);await Frames();
        var early=new Texture2D(64,32);doc.Images=new[]{new ImageAsset("borrow/a.png",early),new ImageAsset("borrow/b.png",early)};doc.LoadMarkup(Markup);doc.Input.UseUnityInput=false;await Frames();ids=Handles();ulong earlyId=0;foreach(var id in ids)earlyId=id;DestroyImmediate(early);bool destroyed=false;try{Resolve(earlyId);}catch(TargetInvocationException error){destroyed=error.InnerException is InvalidOperationException&&error.InnerException.Message.Contains("destroyed or unloaded");}Check(destroyed,"caller premature destruction reports explicit lifetime violation");doc.Unload();await Frames();
        Destroy(doc.gameObject);await Frames();Check(image,"destroying document never destroys borrowed source");Destroy(image);await Frames();Check(!image,"caller owns final texture destruction");
        var stateType=typeof(Document).Assembly.GetType("URUI.Internal.Rendering.Shaders.ShaderPropertyState");var state=Activator.CreateInstance(stateType,true);
        var commandType=typeof(Document).Assembly.GetType("URUI.Internal.Interop.CommandNative");var solid=Activator.CreateInstance(commandType);var borrowedWhite=Activator.CreateInstance(commandType);commandType.GetField("texture").SetValue(borrowedWhite,1UL<<63);
        var block=new MaterialPropertyBlock();var material=new Material(Shader.Find("URUI/Premultiplied"));var needsWrite=stateType.GetMethod("NeedsWrite");
        Check((bool)needsWrite.Invoke(state,new object[]{block,Texture2D.whiteTexture,solid,true,material}),"first shader state write required");
        CheckDeferred((bool)needsWrite.Invoke(state,new object[]{block,Texture2D.whiteTexture,borrowedWhite,true,material}),"switching solid/borrowed mode rewrites uniforms even for same white texture");Destroy(material);
        if(report.failedChecks.Count>0)throw new Exception(string.Join("; ",report.failedChecks));
        report.passed=true;File.WriteAllText(Output+"/borrowed-editor.json",JsonUtility.ToJson(report,true));Debug.Log("BORROWED_IMAGE_PASS "+report.assertions);EditorApplication.Exit(0);
    }catch(Exception e){report.error=e.ToString();File.WriteAllText(Output+"/borrowed-editor.json",JsonUtility.ToJson(report,true));Debug.LogException(e);EditorApplication.Exit(1);}}
}
#endif
