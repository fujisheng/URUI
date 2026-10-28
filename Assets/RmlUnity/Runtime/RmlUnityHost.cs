using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace RmlUnity {
 [DisallowMultipleComponent]
 public sealed class RmlUnityHost : MonoBehaviour {
  public TextAsset document;
  public Shader shader;
  public string overrideFontPath="";
  public event Action<string> Clicked;

  sealed class MeshCache { public Mesh mesh; public int lastFrame; }
  readonly Dictionary<ulong,MeshCache> meshes=new Dictionary<ulong,MeshCache>();
  readonly Dictionary<ulong,Texture2D> textures=new Dictionary<ulong,Texture2D>();
  CommandBuffer buffer;
  Material material;
  Camera targetCamera;
  bool active;
  int gold=1200;
  int lastWidth,lastHeight;
  bool touchDown;
  CommandNative[] commands=Array.Empty<CommandNative>();
  byte[] eventBuffer=new byte[4096];
  Texture2D white;

  void Start(){
   if(!document){Debug.LogError("RmlUnity: assign RML TextAsset.");enabled=false;return;}
   if(!shader)shader=Shader.Find("RmlUnity/Premultiplied");
   if(!shader){Debug.LogError("RmlUnity: shader missing.");enabled=false;return;}
   targetCamera=Camera.main;
   if(!targetCamera){Debug.LogError("RmlUnity: camera missing.");enabled=false;return;}
   material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
   white=Texture2D.whiteTexture;
   buffer=new CommandBuffer{name="RmlUnity UI"};
   targetCamera.AddCommandBuffer(CameraEvent.AfterEverything,buffer);
   lastWidth=Screen.width; lastHeight=Screen.height;
   try {
    if(RmlNative.rui_create(lastWidth,lastHeight,ResolveFontPath())==0)throw new Exception(RmlNative.LastError());
    active=true;
    var fontFile=Path.GetFileNameWithoutExtension(ResolveFontPath()).ToLowerInvariant();
    var family=fontFile.Contains("roboto")?"Roboto":
      fontFile.Contains("noto")?"Noto Sans":
      fontFile.Contains("dejavu")?"DejaVu Sans":"Arial";
    var rml=document.text.Replace("__RMLUNITY_FONT__",family);
    if(RmlNative.rui_load_document(rml)==0)throw new Exception(RmlNative.LastError());
   }catch(DllNotFoundException ex){Debug.LogError("Build Native/RmlUnityBridge first: "+ex.Message);enabled=false;}
   catch(Exception ex){Debug.LogError("RmlUnity: "+ex);enabled=false;}
  }
  static string ResolveFont(){
   #if UNITY_ANDROID && !UNITY_EDITOR
   foreach(var path in new [] {"/system/fonts/Roboto-Regular.ttf", "/system/fonts/NotoSans-Regular.ttf", "/system/fonts/DroidSans.ttf"})
    if(File.Exists(path)) return path;
   return "/system/fonts/Roboto-Regular.ttf";
   #elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
   return "C:/Windows/Fonts/arial.ttf";
   #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
   return "/System/Library/Fonts/Supplemental/Arial.ttf";
   #else
   return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
   #endif
  }
  string ResolveFontPath()=>string.IsNullOrEmpty(overrideFontPath)?ResolveFont():overrideFontPath;
  void Update(){
   if(!active)return;
   if(lastWidth!=Screen.width||lastHeight!=Screen.height){
    lastWidth=Screen.width;lastHeight=Screen.height;RmlNative.rui_resize(lastWidth,lastHeight);
   }
   #if UNITY_ANDROID && !UNITY_EDITOR
   if(Input.touchCount>0){
    var touch=Input.GetTouch(0);
    RmlNative.rui_mouse_move((int)touch.position.x,Screen.height-1-(int)touch.position.y);
    if(touch.phase==TouchPhase.Began&&!touchDown){
     touchDown=true;RmlNative.rui_mouse_button(0,1);
    }
    if(touch.phase==TouchPhase.Ended||touch.phase==TouchPhase.Canceled){
     if(touchDown)RmlNative.rui_mouse_button(0,0);
     touchDown=false;
    }
   }else if(touchDown){
    RmlNative.rui_mouse_button(0,0);touchDown=false;
   }
   #else
   var mouse=Input.mousePosition;
   RmlNative.rui_mouse_move((int)mouse.x,Screen.height-1-(int)mouse.y);
   for(int b=0;b<3;b++){
    if(Input.GetMouseButtonDown(b))RmlNative.rui_mouse_button(b,1);
    if(Input.GetMouseButtonUp(b))RmlNative.rui_mouse_button(b,0);
   }
   if(Mathf.Abs(Input.mouseScrollDelta.y)>0.001f)RmlNative.rui_mouse_wheel(-Input.mouseScrollDelta.y);
   #endif
   RmlNative.rui_update();
   DrainEvents();
   DrawFrame();
  }
  void DrainEvents(){
   int n;while((n=RmlNative.rui_pop_event(eventBuffer,eventBuffer.Length))>0){
    var id=Encoding.UTF8.GetString(eventBuffer,0,n);
    if(id=="claim"){gold+=50;SetText("gold",gold.ToString());SetText("status","Claimed 50 gold from Unity C#!");}
    else SetText("status","Clicked: "+id);
    Clicked?.Invoke(id);
   }
  }
  public void SetText(string id,string value){if(active)RmlNative.rui_set_text(id,value);}
  void DrawFrame(){
   RmlNative.rui_render();
   int count=RmlNative.rui_command_count();
   if(commands.Length<count)commands=new CommandNative[Math.Max(count,commands.Length*2+16)];
   int read=RmlNative.rui_get_commands(commands,count);
   buffer.Clear();
   // Construct a screen-space projection; RmlUi has top-left UI coordinates.
   Matrix4x4 projection=Matrix4x4.Ortho(0,Screen.width,Screen.height,0,-1,1);
   buffer.SetViewProjectionMatrices(Matrix4x4.identity,projection);
   var used=new HashSet<ulong>();
   for(int c=0;c<read;c++){
    var cmd=commands[c];
    if(cmd.geometry==0)continue;
    if(!meshes.TryGetValue(cmd.geometry,out var entry)){
     if(!RmlNative.rui_get_geometry_info(cmd.geometry,out int nv,out int ni))continue;
     if(nv==0||ni==0)continue;
     var vs=new VertexNative[nv];var ids=new int[ni];
     if(RmlNative.rui_copy_geometry(cmd.geometry,vs,nv,ids,ni)==0)continue;
     var positions=new Vector3[nv];var uv=new Vector2[nv];var colors=new Color32[nv];
     for(int v=0;v<nv;v++){
      positions[v]=new Vector3(vs[v].x,vs[v].y,0);
      uv[v]=new Vector2(vs[v].u,vs[v].v);
      colors[v]=new Color32(vs[v].r,vs[v].g,vs[v].b,vs[v].a);
     }
     var mesh=new Mesh{name="RmlGeometry"};
     if(nv>65535)mesh.indexFormat=IndexFormat.UInt32;
     mesh.vertices=positions;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=ids;
     mesh.RecalculateBounds();
     entry=new MeshCache{mesh=mesh};meshes.Add(cmd.geometry,entry);
    }
    entry.lastFrame=Time.frameCount;used.Add(cmd.geometry);
    var tex=LoadTexture(cmd.texture);
    var properties=new MaterialPropertyBlock();
    properties.SetTexture("_MainTex",tex?tex:white);
    properties.SetFloat("_RmlClipEnabled",cmd.clip_enabled);
    properties.SetVector("_RmlClipRect",new Vector4(cmd.clip_x,cmd.clip_y,cmd.clip_x+cmd.clip_w,cmd.clip_y+cmd.clip_h));
    properties.SetVector("_RmlTranslation",new Vector4(cmd.tx,cmd.ty,0,0));
    var m=Matrix4x4.Translate(new Vector3(cmd.tx,cmd.ty,0));
    buffer.DrawMesh(entry.mesh,m,material,0,0,properties);
   }
   // Avoid leaking meshes invalidated by RmlUi.
   var stale=new List<ulong>();foreach(var pair in meshes)if(!used.Contains(pair.Key)&&RmlNative.rui_geometry_exists(pair.Key)==0)stale.Add(pair.Key);
   foreach(var id in stale){Destroy(meshes[id].mesh);meshes.Remove(id);}
  }
  Texture2D LoadTexture(ulong id){
   if(id==0)return white;
   if(textures.TryGetValue(id,out var old))return old;
   if(!RmlNative.rui_get_texture_info(id,out int w,out int h,out int length))return white;
   if(w<=0||h<=0||length!=w*h*4)return white;
   var rgba=new byte[length];
   if(RmlNative.rui_copy_texture(id,rgba,length)==0)return white;
   var texture=new Texture2D(w,h,TextureFormat.RGBA32,false,false){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
   texture.LoadRawTextureData(rgba);texture.Apply(false,true);
   textures[id]=texture;return texture;
  }
  void OnDestroy(){
   if(targetCamera&&buffer)targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything,buffer);
   if(buffer!=null){buffer.Release();buffer=null;}
   foreach(var item in meshes.Values)if(item.mesh)Destroy(item.mesh);
   meshes.Clear();
   foreach(var item in textures.Values)if(item)Destroy(item);
   textures.Clear();
   if(material)Destroy(material);
   if(active){RmlNative.rui_destroy();active=false;}
  }
 }
}
