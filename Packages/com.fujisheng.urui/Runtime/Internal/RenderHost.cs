using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace URUI.Internal {
 [Serializable] internal struct ImageBinding { public string source; public Texture2D texture; public int expectedWidth,expectedHeight; }
 internal enum PointerPhase { Down, Move, Up, Cancel }
 internal struct PointerInputEvent { public PointerPhase phase; public int pointerId; public float x,y; public string hitId; }
 internal static class BatchRules {
  public static bool EquivalentAffine2D(Matrix4x4 a,Matrix4x4 b){
   for(int i=0;i<16;i++)if(float.IsNaN(a[i])||float.IsInfinity(a[i])||a[i]!=b[i])return false;
   if(a.m02!=0||a.m12!=0||a.m20!=0||a.m21!=0||a.m22!=1||a.m23!=0||a.m30!=0||a.m31!=0||a.m32!=0||a.m33!=1)return false;
   return Math.Abs((double)a.m00*a.m11-(double)a.m01*a.m10)>1e-12;
  }
  // Touching bounds are kept separate so edge pixels cannot change blending.
  public static bool Disjoint(float ax0,float ay0,float ax1,float ay1,float bx0,float by0,float bx1,float by1)=>
   ax1<bx0||bx1<ax0||ay1<by0||by1<ay0;
 }
 [DisallowMultipleComponent]
 [AddComponentMenu("")]
 internal sealed partial class RenderHost : MonoBehaviour {
  // Document owns renderer startup and teardown; this type is not a user API.
  internal static RenderHost ActiveHost {get;private set;}
  bool startupAttempted,resourcesReleased;
  internal void InitializeForDocument(){Start();}
  static readonly ProfilerMarker PropertyApiMarker=new ProfilerMarker("URUI.PropertyAPI");
  static readonly ProfilerMarker HostMarker=new ProfilerMarker("URUI.Host"),InputMarker=new ProfilerMarker("URUI.Input"),UpdateMarker=new ProfilerMarker("URUI.NativeUpdate"),RenderMarker=new ProfilerMarker("URUI.NativeRender"),EventMarker=new ProfilerMarker("URUI.Events"),ReadMarker=new ProfilerMarker("URUI.ReadCommands"),LoopMarker=new ProfilerMarker("URUI.CommandLoop"),GeometryMarker=new ProfilerMarker("URUI.CreateGeometry"),FlushMarker=new ProfilerMarker("URUI.FlushGroup"),PropertiesMarker=new ProfilerMarker("URUI.Properties"),SubmitMarker=new ProfilerMarker("URUI.Submit"),DrawMarker=new ProfilerMarker("URUI.DrawFrame"),SetupMarker=new ProfilerMarker("URUI.UnitySetup"),CleanupMarker=new ProfilerMarker("URUI.Cleanup");
  public TextAsset document;
  public ImageBinding[] imageAssets=Array.Empty<ImageBinding>();
  public Shader shader;
  public string overrideFontPath="";
  public bool useInlineLookupQueue;
  public bool useShaderPropertyIds;
  public bool useFrameScratchCollections;
  [Tooltip("Experimental: retain Host-owned command recording for the audited built-in shader only. Do not mutate Host-owned meshes, material or command buffer externally; call NotifyExternalRenderResourcesChanged for external state changes.")]
  public bool retainUnchangedCommands;
  // Runtime-only capability: Unity serialization/Inspector cannot silently grant
  // exclusive ownership. Every public assignment runs the invalidating setter.
  [NonSerialized] RecordingOwnership recordingOwnershipValue=RecordingOwnership.ExternalMutable;
  public RecordingOwnership recordingOwnership {get=>recordingOwnershipValue;set=>SetRecordingOwnership(value);}
  public bool deferRetainedStateCapture;
  PlanStability planStability;bool? retentionSchedule;
  public RecordingStats LastRecording {get;private set;}
  readonly RecordingState retainedRecording=new RecordingState();
  bool nativeRetention,retentionApiMissing,recordingWarmupPending,recordingFinite;
  int recordCallsThisFrame,retainedDraws,retainedBufferBytes;
  string recordedPassName="";
  struct TextureDependency {public Texture texture;public uint update;public int width,height;public FilterMode filter;public TextureWrapMode u,v,w;public float bias;public int aniso;}
  readonly List<TextureDependency> retainedTextures=new List<TextureDependency>();
  public bool reuseEquivalentPropertyState;
  public ShaderPropertyContract propertyContract;
  public bool ShaderContractActive{get;private set;}
  public int PropertyCalls{get;private set;}public int PropertyWriteCalls{get;private set;}public int PropertyReuseHits{get;private set;}
  readonly ShaderPropertyState shaderPropertyState=new ShaderPropertyState();
  [NonSerialized] public int detailedHostMode;
  public event Action<string> Clicked;
  public event Action<PointerInputEvent> PointerInput;
  public bool measureBridge=true;public bool phaseDiagnostics;public bool reusePropertyBlock;public bool cullFullyClipped;public bool skipDrawForDiagnosis;public bool batchDisjointCommands;public bool batchEquivalentTransforms;public bool batchOrderedOverlap;
  public PhaseFrame LastPhase {get;private set;}
  public bool propertyDependencyDiagnostics;
  public readonly PropertyDependencyCounter PropertyDependencies=new PropertyDependencyCounter();
  internal struct ApiFrame {public double StyleMs,AttributeMs,TextMs;public int Styles,Attributes,Texts;}
  ApiFrame apiFrame;
  public ApiFrame TakeApiFrame(){var result=apiFrame;apiFrame=default;return result;}
  double textMilliseconds,frameTextMilliseconds;int textCalls;
  internal struct PhaseFrame {
   public int BatchGroups,BreakTexture,BreakClip,BreakTransform,BreakOverlap,VertexLimitGroups,FallbackExtraSubmissions;
   public double ContextUpdateMs,EventsMs,NativeRenderMs,CommandCopyMs,GeometryCopyMs,MeshBuildMs,PropertiesMs,SubmitMs,CleanupMs,HostTotalMs,SetTextMs,CommandSetupMs,MeshConversionMs,UnityMeshApiMs,BatchMeshBuildMs,BatchUnityMeshApiMs;
   public double InputMs,MatrixDecodeMs,GeometryLookupMs,BoundsApiMs,EntryTrackMs,CanJoinMs,FlushGroupMs,BatchMaintenanceMs;
   public int BoundsReads,DetailClockPairs,ShaderNameLookups;
   public long ManagedAllocBytes;public int SetTextCalls,NativeCalls,CommandsRead,DrawSubmissions,FullyClippedCandidates,PropertyBlocksCreated,MeshesCreated,GeometryCopyBytes,CommandCopyBytes,MatrixCopyBytes,ParameterCopyBytes,TotalCommandCopyBytes;
  }
  static long ThreadAllocationBytes(){
   #if ENABLE_IL2CPP && !UNITY_EDITOR
   // This backend's thread counter is not implemented. Zero is not evidence of
   // zero allocation; use raw Profiler GC.Alloc for scoped IL2CPP measurements.
   return -1;
   #else
   return GC.GetAllocatedBytesForCurrentThread();
   #endif
  }
  static long Now()=>System.Diagnostics.Stopwatch.GetTimestamp();
  static double Millis(long ticks)=>ticks*1000.0/System.Diagnostics.Stopwatch.Frequency;
  long DetailBegin(ref double value,ref PhaseFrame phase){
   if(!phaseDiagnostics||detailedHostMode==0)return 0;
   phase.DetailClockPairs++;
   if(detailedHostMode==2){long t=Now();value+=Millis(Now()-t);return 0;}
   return Now();
  }
  void DetailEnd(long t,ref double value){if(phaseDiagnostics&&detailedHostMode==1)value+=Millis(Now()-t);}
  Bounds GeometryBounds(Mesh mesh,ref PhaseFrame phase){long t=DetailBegin(ref phase.BoundsApiMs,ref phase);var b=mesh.bounds;DetailEnd(t,ref phase.BoundsApiMs);if(phaseDiagnostics)phase.BoundsReads++;return b;}
  public string InitialMarkup;
  public bool Ready=>active;
  public int UiWidth=>Screen.width;
  public int UiHeight=>Screen.height;
  public int DrawCommands {get;private set;}
  public double UpdateMilliseconds {get;private set;}
  public int ElementCount=>active?NativeMethods.urui_element_count():0;
  public void LoadMarkup(string markup){if(active){
   InvalidateRecording(true);ClearBatches();ClearFrameScratch();
   foreach(var mesh in meshes.Values)if(mesh.mesh)Destroy(mesh.mesh);meshes.Clear();
   foreach(var texture in textures.Values)if(texture)Destroy(texture);textures.Clear();
   buffer.Clear();if(NativeMethods.urui_load_document(markup.Replace("__URUI_FONT__","Roboto"))==0)throw new Exception("URUI document load: "+NativeMethods.LastError());
  }else InitialMarkup=markup;}


  sealed class MeshCache { public Mesh mesh;public Vector3[] positions;public Vector2[] uv;public Color32[] colors;public int[] indices; public int lastFrame; }
  struct BatchItem { public CommandNative command;public MeshCache geometry;public Matrix4x4 transform;public bool transformed;public float x0,y0,x1,y1; }
  sealed class BatchCache { public Mesh mesh;public BatchItem[] items; }
  readonly HashSet<ulong> frameUsedGeometry=new HashSet<ulong>();
  readonly List<ulong> frameStaleGeometry=new List<ulong>();
  void ClearFrameScratch(){frameUsedGeometry.Clear();frameUsedGeometry.TrimExcess();frameStaleGeometry.Clear();frameStaleGeometry.TrimExcess();}
  readonly Dictionary<ulong,MeshCache> meshes=new Dictionary<ulong,MeshCache>();
  bool? meshBatchMode;
  readonly Dictionary<ulong,Texture2D> textures=new Dictionary<ulong,Texture2D>();
  readonly List<ulong> retiredTextureIds=new List<ulong>();
  readonly Dictionary<ulong,BatchCache> batches=new Dictionary<ulong,BatchCache>();
  readonly List<BatchItem> batchGroup=new List<BatchItem>(128);
  readonly List<ulong> staleBatchKeys=new List<ulong>();
  HashSet<ulong> previousBatchKeys=new HashSet<ulong>(),currentBatchKeys=new HashSet<ulong>();
  CommandBuffer buffer;
  Material material;
  MaterialPropertyBlock reusableProperties;
  Camera targetCamera;
  bool active;
  int referenceThread;
  int lastWidth,lastHeight;
  CommandNative[] commands=Array.Empty<CommandNative>();
  float[] commandTransforms=Array.Empty<float>();
  byte[] eventBuffer=new byte[4096];
  Texture2D white;
  void ClearBatches(){foreach(var b in batches.Values)if(b.mesh)Destroy(b.mesh);batches.Clear();batchGroup.Clear();previousBatchKeys.Clear();currentBatchKeys.Clear();staleBatchKeys.Clear();}

  void Start(){
   if(startupAttempted||resourcesReleased)return;startupAttempted=true;
   if(!document){Debug.LogError("Unity: assign RML TextAsset.");enabled=false;return;}
   if(!shader)shader=Resources.Load<Shader>("URUI/Premultiplied");
   if(!shader)shader=Shader.Find("URUI/Premultiplied");
   if(!shader){Debug.LogError("Unity: shader missing.");enabled=false;return;}
   targetCamera=Camera.main;
   if(!targetCamera){Debug.LogError("Unity: camera missing.");enabled=false;return;}
   material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};recordedPassName=material.GetPassName(0);
   if(reusePropertyBlock)reusableProperties=new MaterialPropertyBlock();
   white=Texture2D.whiteTexture;
   buffer=new CommandBuffer{name="Unity UI"};
   targetCamera.AddCommandBuffer(CameraEvent.AfterEverything,buffer);
   lastWidth=Screen.width; lastHeight=Screen.height;
   try {
    if(NativeMethods.urui_bridge_abi_version()!=2)throw new Exception("URUIBridge ABI version 2 is required");
    if(NativeMethods.urui_create(UiWidth,UiHeight,ResolveFontPath())==0)throw new Exception(NativeMethods.LastError());
    active=true;ActiveHost=this;referenceThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
    if(useInlineLookupQueue&&!SetInlineLookupQueue(true))throw new Exception("This native build has no inline lookup queue support");
    foreach(var asset in imageAssets??Array.Empty<ImageBinding>()){
     if(string.IsNullOrEmpty(asset.source)||!asset.texture)throw new Exception("URUI image asset needs a source and texture");
     if((asset.expectedWidth>0&&asset.texture.width!=asset.expectedWidth)||(asset.expectedHeight>0&&asset.texture.height!=asset.expectedHeight))
      throw new Exception("URUI image dimensions differ from declared atlas/source pixels: "+asset.source);
     var pixels=asset.texture.GetPixels32();var rgba=new byte[pixels.Length*4];
     // Unity GetPixels32 is bottom-row first; RmlUi's image and spritesheet
     // coordinates are top-row first. Convert row order before registration.
     int j=0;for(int y=asset.texture.height-1;y>=0;y--)for(int x=0;x<asset.texture.width;x++){
      var p=pixels[y*asset.texture.width+x];rgba[j++]=p.r;rgba[j++]=p.g;rgba[j++]=p.b;rgba[j++]=p.a;
     }
     if(NativeMethods.urui_register_rgba_texture(asset.source,asset.texture.width,asset.texture.height,rgba,rgba.Length,out _)!=1)
      throw new Exception("URUI image registration: "+NativeMethods.LastError());
    }
    var fontFile=Path.GetFileNameWithoutExtension(ResolveFontPath()).ToLowerInvariant();
    var family=fontFile.Contains("roboto")?"Roboto":
      fontFile.Contains("noto")?"Noto Sans":
      fontFile.Contains("dejavu")?"DejaVu Sans":"Arial";
    var rml=(string.IsNullOrEmpty(InitialMarkup)?document.text:InitialMarkup).Replace("__URUI_FONT__",family);
    if(NativeMethods.urui_load_document(rml)==0)throw new Exception(NativeMethods.LastError());
   }catch(DllNotFoundException ex){Debug.LogError("Build Native/URUIBridge first: "+ex.Message);AbortStartup();}
   catch(EntryPointNotFoundException ex){Debug.LogError("Update Native/URUIBridge to ABI version 2: "+ex.Message);AbortStartup();}
   catch(Exception ex){Debug.LogError("Unity: "+ex);AbortStartup();}
  }
  void AbortStartup(){
   if(targetCamera&&buffer!=null)targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything,buffer);
   if(buffer!=null){buffer.Release();buffer=null;}
   if(material){Destroy(material);material=null;}
   if(active){NativeMethods.urui_destroy();active=false;}
   if(ReferenceEquals(ActiveHost,this))ActiveHost=null;
   enabled=false;
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
   using var hostMarker=HostMarker.Auto();
   long inputTimer=phaseDiagnostics?Now():0;
   using(InputMarker.Auto()){
   if(lastWidth!=Screen.width||lastHeight!=Screen.height){
    lastWidth=Screen.width;lastHeight=Screen.height;NativeMethods.urui_resize(UiWidth,UiHeight);
   }
   #if UNITY_ANDROID && !UNITY_EDITOR
   for(int i=0;i<Input.touchCount;i++){
    var touch=Input.GetTouch(i);
    int touchPhase=touch.phase==TouchPhase.Began?0:touch.phase==TouchPhase.Moved||touch.phase==TouchPhase.Stationary?1:touch.phase==TouchPhase.Ended?2:3;
    float x=touch.position.x*UiWidth/Screen.width;
    float y=UiHeight-1-touch.position.y*UiHeight/Screen.height;
    if(NativeMethods.urui_touch_event(touchPhase,(ulong)(uint)touch.fingerId,x,y)==1)
     PointerInput?.Invoke(new PointerInputEvent{phase=(PointerPhase)touchPhase,pointerId=touch.fingerId,x=x,y=y,hitId=HitTestId(x,y)});
   }
   #else
   var mouse=Input.mousePosition;
   NativeMethods.urui_mouse_move((int)(mouse.x*UiWidth/Screen.width),UiHeight-1-(int)(mouse.y*UiHeight/Screen.height));
   for(int b=0;b<3;b++){
    if(Input.GetMouseButtonDown(b)){NativeMethods.urui_mouse_button(b,1);if(b==0)EmitMousePointer(PointerPhase.Down,mouse);}
    if(Input.GetMouseButtonUp(b)){NativeMethods.urui_mouse_button(b,0);if(b==0)EmitMousePointer(PointerPhase.Up,mouse);}
   }
   if(Input.GetMouseButton(0)&&!Input.GetMouseButtonDown(0))EmitMousePointer(PointerPhase.Move,mouse);
   if(Mathf.Abs(Input.mouseScrollDelta.y)>0.001f)NativeMethods.urui_mouse_wheel(-Input.mouseScrollDelta.y);
   #endif
   }
   long timer=(measureBridge||phaseDiagnostics)?Now():0;
   long alloc=phaseDiagnostics?ThreadAllocationBytes():-1;
   var phase=new PhaseFrame();if(phaseDiagnostics)phase.InputMs=Millis(timer-inputTimer);long step=phaseDiagnostics?Now():0;
   if(!active||!isActiveAndEnabled)return;
   using(UpdateMarker.Auto())NativeMethods.urui_update();
   
   if(phaseDiagnostics){phase.ContextUpdateMs=Millis(Now()-step);phase.NativeCalls++;step=Now();}
   using(EventMarker.Auto())DrainEvents();
   if(phaseDiagnostics){phase.EventsMs=Millis(Now()-step);step=Now();}
   if(!active||!isActiveAndEnabled)return;
   DrawFrame(ref phase);
   if(phaseDiagnostics){phase.HostTotalMs=Millis(Now()-timer);phase.ManagedAllocBytes=alloc<0?-1:ThreadAllocationBytes()-alloc;phase.SetTextMs=textMilliseconds;phase.SetTextCalls=textCalls;phase.NativeCalls+=textCalls;LastPhase=phase;textMilliseconds=0;textCalls=0;}
   if(measureBridge)UpdateMilliseconds=Millis(Now()-timer);
  }
  void DrainEvents(){
   while(active){
    int n=NativeMethods.urui_pop_event(eventBuffer,eventBuffer.Length);
    if(n==0)break;
    if(n<0){
     int required=checked(-n);
     if(required<=eventBuffer.Length)throw new InvalidOperationException("Invalid Rml event buffer requirement: "+required);
     Array.Resize(ref eventBuffer,required);continue;
    }
    var id=Encoding.UTF8.GetString(eventBuffer,0,n);
    Clicked?.Invoke(id);
   }
  }
  public void SetText(string id,string value){if(!active)return;long t=phaseDiagnostics?Now():0;NativeMethods.urui_set_text(id,value);if(phaseDiagnostics){double ms=Millis(Now()-t);textMilliseconds+=ms;frameTextMilliseconds+=ms;textCalls++;apiFrame.TextMs+=ms;apiFrame.Texts++;}}
  public double TakeFrameTextMilliseconds(){double result=frameTextMilliseconds;frameTextMilliseconds=0;return result;}
  public double FrameTextMilliseconds=>frameTextMilliseconds;
  public float GetScrollTop(string id)=>active?NativeMethods.urui_get_scroll_top(id):-1f;
  public bool SetPixelStyle(string id,string property,float pixels)=>active&&NativeMethods.urui_set_pixel_style(id,property,pixels)!=0;
  public bool ElementExists(string id)=>active&&NativeMethods.urui_element_exists(id)==1;
  public bool SetAttribute(string id,string name,string value){if(!active)return false;long t=phaseDiagnostics?Now():0;bool ok=NativeMethods.urui_set_attribute(id,name,value)==1;if(phaseDiagnostics){apiFrame.AttributeMs+=Millis(Now()-t);apiFrame.Attributes++;}return ok;}
  public bool SetStyle(string id,string property,string value){if(!active)return false;using var marker=PropertyApiMarker.Auto();long t=phaseDiagnostics?Now():0;bool ok=NativeMethods.urui_set_style(id,property,value)==1;if(phaseDiagnostics){apiFrame.StyleMs+=Millis(Now()-t);apiFrame.Styles++;}return ok;}
  public bool SetTranslateScale2D(string id,float x,float y,float sx,float sy){if(!active)return false;using var marker=PropertyApiMarker.Auto();long t=phaseDiagnostics?Now():0;bool ok=NativeMethods.urui_set_translate_scale_2d(id,x,y,sx,sy)==1;if(phaseDiagnostics){apiFrame.StyleMs+=Millis(Now()-t);apiFrame.Styles++;}return ok;}
  /// <summary>Fresh ID query followed by explicit weak object binding. Existing
  /// string setters keep fresh query semantics. Null means missing/inactive/budget.
  /// Old native plugins lacking this additive API throw NotSupportedException.</summary>
  public ElementReference AcquireElementReference(string id){
   if(!active)return null;CheckReferenceThread();
   try{ulong token=NativeMethods.urui_element_ref_acquire(id);return token==0?null:new ElementReference(this,token);}
   catch(EntryPointNotFoundException e){throw new NotSupportedException("Native bridge has no explicit element reference API",e);}
  }
  void CheckReferenceThread(){if(System.Threading.Thread.CurrentThread.ManagedThreadId!=referenceThread)throw new InvalidOperationException("URUI element references require their context thread");}
  internal bool ReferenceIsValid(ulong token){if(!active)return false;CheckReferenceThread();return NativeMethods.urui_element_ref_valid(token)==1;}
  internal void ReleaseReference(ulong token){if(!active)return;CheckReferenceThread();NativeMethods.urui_element_ref_release(token);}
  internal bool SetReferenceTransform(ulong token,float x,float y,float sx,float sy){if(!active)return false;CheckReferenceThread();using var marker=PropertyApiMarker.Auto();long t=phaseDiagnostics?Now():0;bool ok=NativeMethods.urui_element_ref_set_translate_scale_2d(token,x,y,sx,sy)==1;if(phaseDiagnostics){apiFrame.StyleMs+=Millis(Now()-t);apiFrame.Styles++;}return ok;}
  public bool SetInlineLookupQueue(bool enabled){if(!active)return false;if(NativeMethods.urui_lookup_inline_enable(enabled?1:0)!=1)return false;useInlineLookupQueue=enabled;return true;}
  public bool SetInnerRml(string id,string markup)=>active&&NativeMethods.urui_set_inner_rml(id,markup)==1;
  public bool AppendElement(string parentId,string tag,string id)=>active&&NativeMethods.urui_append_element(parentId,tag,id)==1;
  public bool RemoveElement(string id)=>active&&NativeMethods.urui_remove_element(id)==1;
  public string HitTestId(float logicalX,float logicalY){
   if(!active)return null;
   var bytes=new byte[512];int n=NativeMethods.urui_hit_test_id(logicalX,logicalY,bytes,bytes.Length);
   if(n<0){int required=-n;if(required>1024*1024)throw new Exception("URUI hit ID exceeds 1 MiB");bytes=new byte[required];n=NativeMethods.urui_hit_test_id(logicalX,logicalY,bytes,bytes.Length);}
   return n>0?Encoding.UTF8.GetString(bytes,0,n):null;
  }
  public string LastError=>active?NativeMethods.LastError():"Unity host is not active";
  void EmitMousePointer(PointerPhase pointerPhase,Vector3 mouse){
   if(PointerInput==null)return;
   float x=mouse.x*UiWidth/Screen.width,y=UiHeight-1-mouse.y*UiHeight/Screen.height;
   PointerInput(new PointerInputEvent{phase=pointerPhase,pointerId=0,x=x,y=y,hitId=HitTestId(x,y)});
  }
  static void Mix(ref ulong hash,ulong value){unchecked{hash^=value;hash*=1099511628211UL;}}
  ulong GroupHash(){
   ulong hash=1469598103934665603UL;
   foreach(var item in batchGroup){var c=item.command;
    Mix(ref hash,c.geometry);Mix(ref hash,c.texture);Mix(ref hash,(uint)c.tx.GetHashCode());Mix(ref hash,(uint)c.ty.GetHashCode());
    Mix(ref hash,(uint)c.clip_enabled);Mix(ref hash,(uint)c.clip_x);Mix(ref hash,(uint)c.clip_y);Mix(ref hash,(uint)c.clip_w);Mix(ref hash,(uint)c.clip_h);
   }
   return hash;
  }
  static bool SameCommand(CommandNative a,CommandNative b)=>a.geometry==b.geometry&&a.texture==b.texture&&a.tx==b.tx&&a.ty==b.ty&&a.clip_enabled==b.clip_enabled&&a.clip_x==b.clip_x&&a.clip_y==b.clip_y&&a.clip_w==b.clip_w&&a.clip_h==b.clip_h;
  bool SameBatch(BatchCache cache){if(cache.items.Length!=batchGroup.Count)return false;for(int i=0;i<cache.items.Length;i++)if(!SameCommand(cache.items[i].command,batchGroup[i].command))return false;return true;}
  bool CanJoin(BatchItem next,ref PhaseFrame phase){long t=DetailBegin(ref phase.CanJoinMs,ref phase);bool result=CanJoinCore(next,ref phase);DetailEnd(t,ref phase.CanJoinMs);return result;}
  bool CanJoinCore(BatchItem next,ref PhaseFrame phase){
   if(batchGroup.Count==0)return true;
   var a=batchGroup[0].command;var b=next.command;
   if(a.texture!=b.texture){if(phaseDiagnostics)phase.BreakTexture++;return false;}
   if(a.clip_enabled!=b.clip_enabled||a.clip_x!=b.clip_x||a.clip_y!=b.clip_y||a.clip_w!=b.clip_w||a.clip_h!=b.clip_h){if(phaseDiagnostics)phase.BreakClip++;return false;}
   if(!BatchRules.EquivalentAffine2D(batchGroup[0].transform,next.transform)){if(phaseDiagnostics)phase.BreakTransform++;return false;}
   if(!batchOrderedOverlap)foreach(var prior in batchGroup)if(!BatchRules.Disjoint(prior.x0,prior.y0,prior.x1,prior.y1,next.x0,next.y0,next.x1,next.y1)){if(phaseDiagnostics)phase.BreakOverlap++;return false;}
   return true;
  }
  Mesh BuildBatchMesh(ref PhaseFrame phase){
   long start=phaseDiagnostics?Now():0;
   int vertices=0,indices=0;
   foreach(var item in batchGroup){if(item.geometry.positions==null)return null;vertices+=item.geometry.positions.Length;indices+=item.geometry.indices.Length;}
   if(vertices==0||indices==0||vertices>100000||indices>300000){if(phaseDiagnostics)phase.VertexLimitGroups++;return null;}
   var positions=new Vector3[vertices];var uv=new Vector2[vertices];var colors=new Color32[vertices];var ids=new int[indices];
   int vo=0,io=0;
   foreach(var item in batchGroup){var g=item.geometry;var c=item.command;
    for(int i=0;i<g.positions.Length;i++){var p=g.positions[i];positions[vo+i]=new Vector3(p.x+c.tx,p.y+c.ty,p.z);uv[vo+i]=g.uv[i];colors[vo+i]=g.colors[i];}
    for(int i=0;i<g.indices.Length;i++)ids[io+i]=vo+g.indices[i];
    vo+=g.positions.Length;io+=g.indices.Length;
   }
   long unityStart=phaseDiagnostics?Now():0;
   var mesh=new Mesh{name="DisjointBatch"};if(vertices>65535)mesh.indexFormat=IndexFormat.UInt32;
   mesh.vertices=positions;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=ids;mesh.RecalculateBounds();
   if(phaseDiagnostics){phase.BatchUnityMeshApiMs+=Millis(Now()-unityStart);phase.BatchMeshBuildMs+=Millis(Now()-start);}
   return mesh;
  }
  static readonly int MainTextureId=Shader.PropertyToID("_MainTex"),ClipEnabledId=Shader.PropertyToID("_UruiClipEnabled"),ClipRectId=Shader.PropertyToID("_UruiClipRect"),TranslationId=Shader.PropertyToID("_UruiTranslation");
  void WriteShaderProperties(MaterialPropertyBlock properties,Texture texture,CommandNative cmd,bool batch,ref PhaseFrame phase){
   using var marker=PropertiesMarker.Auto();
   var clip=new Vector4(cmd.clip_x,cmd.clip_y,cmd.clip_x+cmd.clip_w,cmd.clip_y+cmd.clip_h);
   var translation=batch?Vector4.zero:new Vector4(cmd.tx,cmd.ty,0,0);
   if(useShaderPropertyIds){
    properties.SetTexture(MainTextureId,texture);properties.SetFloat(ClipEnabledId,cmd.clip_enabled);
    properties.SetVector(ClipRectId,clip);properties.SetVector(TranslationId,translation);
   }else{
    properties.SetTexture("_MainTex",texture);properties.SetFloat("_UruiClipEnabled",cmd.clip_enabled);
    properties.SetVector("_UruiClipRect",clip);properties.SetVector("_UruiTranslation",translation);
    if(phaseDiagnostics)phase.ShaderNameLookups+=4;
   }
  }
  MaterialPropertyBlock PrepareShaderProperties(Texture texture,CommandNative cmd,bool batch,ref PhaseFrame phase){
   if(reusePropertyBlock&&reusableProperties==null)reusableProperties=new MaterialPropertyBlock();
   var properties=reusePropertyBlock?reusableProperties:new MaterialPropertyBlock();
   if(!reusePropertyBlock&&phaseDiagnostics)phase.PropertyBlocksCreated++;
   if(propertyDependencyDiagnostics)PropertyDependencies.Observe(properties,texture,cmd,batch);
   PropertyCalls++;
   if(shaderPropertyState.NeedsWrite(properties,texture,cmd,ShaderContractActive&&reusePropertyBlock,material)){
    if(reusePropertyBlock)properties.Clear();WriteShaderProperties(properties,texture,cmd,batch,ref phase);PropertyWriteCalls++;
   }else PropertyReuseHits++;
   return properties;
  }
  void DrawOne(BatchItem item,ref PhaseFrame phase){
   long t=phaseDiagnostics?Now():0;var cmd=item.command;var tex=LoadTexture(cmd.texture);
   var properties=PrepareShaderProperties(tex?tex:white,cmd,false,ref phase);
   var m=item.transform*Matrix4x4.Translate(new Vector3(cmd.tx,cmd.ty,0));if(phaseDiagnostics){phase.PropertiesMs+=Millis(Now()-t);t=Now();}
   if(!skipDrawForDiagnosis)using(SubmitMarker.Auto()){buffer.DrawMesh(item.geometry.mesh,m,material,0,0,properties);recordCallsThisFrame++;}
   if(phaseDiagnostics){phase.SubmitMs+=Millis(Now()-t);if(!skipDrawForDiagnosis)phase.DrawSubmissions++;}
  }
  void DrawBatch(BatchCache cached,ref PhaseFrame phase){
   long t=phaseDiagnostics?Now():0;var cmd=batchGroup[0].command;var tex=LoadTexture(cmd.texture);
   var properties=PrepareShaderProperties(tex?tex:white,cmd,true,ref phase);if(phaseDiagnostics){phase.PropertiesMs+=Millis(Now()-t);t=Now();}
   if(!skipDrawForDiagnosis)using(SubmitMarker.Auto()){buffer.DrawMesh(cached.mesh,batchGroup[0].transform,material,0,0,properties);recordCallsThisFrame++;}
   if(phaseDiagnostics){phase.SubmitMs+=Millis(Now()-t);if(!skipDrawForDiagnosis)phase.DrawSubmissions++;}
  }
  void FlushGroup(ref PhaseFrame phase){using var marker=FlushMarker.Auto();long t=DetailBegin(ref phase.FlushGroupMs,ref phase);FlushGroupCore(ref phase);DetailEnd(t,ref phase.FlushGroupMs);}
  void FlushGroupCore(ref PhaseFrame phase){
   if(batchGroup.Count==0)return;
   if(phaseDiagnostics)phase.BatchGroups++;
   if(batchGroup.Count==1){DrawOne(batchGroup[0],ref phase);batchGroup.Clear();return;}
   ulong hash=GroupHash();currentBatchKeys.Add(hash);
   if(batches.TryGetValue(hash,out var cached)&&!cached.mesh){batches.Remove(hash);cached=null;recordingWarmupPending=true;}
   if(cached!=null){
    if(SameBatch(cached)){DrawBatch(cached,ref phase);batchGroup.Clear();return;}
    // Hash collision remains the original ordered fallback.
   }else if(previousBatchKeys.Contains(hash)){
    var mesh=BuildBatchMesh(ref phase);
    if(mesh){cached=new BatchCache{mesh=mesh,items=batchGroup.ToArray()};batches.Add(hash,cached);DrawBatch(cached,ref phase);batchGroup.Clear();return;}
   }else recordingWarmupPending=true; // Do not freeze first-frame unbatched warmup.
   if(phaseDiagnostics)phase.FallbackExtraSubmissions+=batchGroup.Count-1;
   foreach(var item in batchGroup)DrawOne(item,ref phase);
   batchGroup.Clear();
  }
  void InvalidateRecording(bool clear){if(clear){parameterPrepared=false;StopParameterRoot();}retainedRecording.Invalidate();planStability.Reset();retainedTextures.Clear();retainedDraws=0;if(clear){retainedTextures.TrimExcess();LastRecording=default;TryClearNativeHostRecording();}}
  // Explicit notification for consumers managing external render state. Existing
  // current-frame Unity snapshots stay alive; next Host Update records anew.
  public void NotifyExternalRenderResourcesChanged(){InvalidateRecording(false);}
  // Camera.GetCommandBuffers exposes mutable references. Exclusive ownership
  // cannot be inferred from private fields or byte size; unknown is fallback.
  public void SetRecordingOwnership(RecordingOwnership ownership){
   if(ownership!=RecordingOwnership.ExternalMutable&&ownership!=RecordingOwnership.HostExclusive)throw new ArgumentOutOfRangeException(nameof(ownership));
   if(recordingOwnershipValue==ownership)return;recordingOwnershipValue=ownership;InvalidateRecording(false);
  }
  static readonly ProfilerMarker RetainedResourceMarker=new ProfilerMarker("URUI.RetainedResourceValidation");
  bool RetainedResourcesAlive(){using(RetainedResourceMarker.Auto())return CheckRetainedResources();}
  bool CheckRetainedResources(){
   if(buffer==null||buffer.sizeInBytes!=retainedBufferBytes)return false;
   foreach(var pair in meshes)if(!pair.Value.mesh)return false;
   foreach(var pair in batches)if(!pair.Value.mesh)return false;
   foreach(var d in retainedTextures){var t=d.texture;if(!t||t.updateCount!=d.update||t.width!=d.width||t.height!=d.height||t.filterMode!=d.filter||t.wrapModeU!=d.u||t.wrapModeV!=d.v||t.wrapModeW!=d.w||t.mipMapBias!=d.bias||t.anisoLevel!=d.aniso)return false;}
   return true;
  }
  void AddTextureDependency(Texture texture){if(!texture)return;retainedTextures.Add(new TextureDependency{texture=texture,update=texture.updateCount,width=texture.width,height=texture.height,filter=texture.filterMode,u=texture.wrapModeU,v=texture.wrapModeV,w=texture.wrapModeW,bias=texture.mipMapBias,aniso=texture.anisoLevel});}
  #if DEVELOPMENT_BUILD || UNITY_EDITOR
  static readonly ProfilerMarker DependencyCaptureMarker=new ProfilerMarker("URUI.RetainedDependencyCapture");
  #endif
  void CaptureTextureDependencies(){
   #if DEVELOPMENT_BUILD || UNITY_EDITOR
   using var dependencyScope=DependencyCaptureMarker.Auto();
   #endif
   retainedTextures.Clear();AddTextureDependency(white);foreach(var pair in textures)AddTextureDependency(pair.Value);
  }
  bool ConfigureNativeRetention(bool enable){
   if(retentionApiMissing)return false;
   if(enable==nativeRetention)return enable;
   try{if(enable&&NativeMethods.urui_render_plan_version()!=1){retentionApiMissing=true;return false;}if(NativeMethods.urui_render_plan_enable(enable?1:0)!=1)return false;nativeRetention=enable;InvalidateRecording(false);return enable;}
   catch(EntryPointNotFoundException){retentionApiMissing=true;nativeRetention=false;InvalidateRecording(false);return false;}
  }
  void OnDisable(){StopParameterHost();InvalidateRecording(true);if(active&&nativeRetention)ConfigureNativeRetention(false);}
  void OnEnable(){ResetParameterRootMetrics();parameterRootFault=false;parameterFault=false;retainedRecording.Invalidate();}
  void DrawFrame(ref PhaseFrame phase){
   bool completed=false;
   try{DrawFrameCore(ref phase);completed=true;}finally{if(!completed)InvalidateRecording(true);}
  }
  void DrawFrameCore(ref PhaseFrame phase){
   using var drawMarker=DrawMarker.Auto();ResetParameterRootMetrics();RequireNativeStopPermission();
   shaderPropertyState.Reset();PropertyCalls=PropertyWriteCalls=PropertyReuseHits=0;recordCallsThisFrame=0;recordingWarmupPending=false;recordingFinite=true;LastRecording=default;
   if(retentionSchedule.HasValue&&retentionSchedule.Value!=deferRetainedStateCapture)InvalidateRecording(false);
   retentionSchedule=deferRetainedStateCapture;
   bool requested=retainUnchangedCommands&&recordingOwnership==RecordingOwnership.HostExclusive;
   RecordingKey recordingKey=default;bool eligible=!deferRetainedStateCapture&&requested&&RecordingKey.TryCapture(this,material,targetCamera,recordedPassName,out recordingKey);
   bool parameterRequested=ParameterRootEligible();if(!parameterRequested){StopParameterRoot();RequireNativeStopPermission();}
   bool tracking=ConfigureNativeRetention(parameterRequested||(deferRetainedStateCapture?requested:eligible));if(!tracking){retainedRecording.Invalidate();planStability.Reset();}
   ShaderContractActive=reuseEquivalentPropertyState&&propertyContract&&propertyContract.Supports(material);
   if(propertyDependencyDiagnostics)PropertyDependencies.Reset();
   if(meshBatchMode.HasValue&&meshBatchMode.Value!=batchDisjointCommands){
    ClearBatches();foreach(var geometry in meshes.Values)if(geometry.mesh)Destroy(geometry.mesh);meshes.Clear();
   }
   meshBatchMode=batchDisjointCommands;
   long t=phaseDiagnostics?Now():0;
   using(RenderMarker.Auto())NativeMethods.urui_render();
   if(phaseDiagnostics){phase.NativeRenderMs=Millis(Now()-t);phase.NativeCalls++;t=Now();}
   parameterPrepared=false;
   if(parameterRequested&&tracking&&PrepareParameterRoot(ref phase))return;
   RequireNativeStopPermission();
   UruiFrameStampNative stamp=default;
   if(tracking){
    if(NativeMethods.urui_render_plan_stamp(out stamp)!=1)retainedRecording.Invalidate();
    if(phaseDiagnostics)phase.NativeCalls++;
    bool repeated=planStability.Observe(stamp);
    if(deferRetainedStateCapture)eligible=repeated&&RecordingKey.TryCapture(this,material,targetCamera,recordedPassName,out recordingKey);
    LastRecording=new RecordingStats{capability=eligible&&stamp.flags==1,nativeCommands=(int)stamp.count,nativeExtraBytes=stamp.retained_bytes,compareBytes=stamp.compare_bytes};
    if(eligible&&retainedRecording.CanReuse(stamp,recordingKey)&&RetainedResourcesAlive()){
     DrawCommands=(int)stamp.count;LastRecording=new RecordingStats{reused=true,capability=true,nativeCommands=DrawCommands,recordCalls=0,retainedDraws=retainedDraws,nativeExtraBytes=stamp.retained_bytes,compareBytes=stamp.compare_bytes};return;
    }
   }
   retainedRecording.Invalidate();retainedTextures.Clear();
   int count,read;using(ReadMarker.Auto()){count=NativeMethods.urui_command_count();DrawCommands=count;
   if(commands.Length<count)commands=new CommandNative[Math.Max(count,commands.Length*2+16)];
   if(commandTransforms.Length<count*16)commandTransforms=new float[Math.Max(count*16,commandTransforms.Length*2+256)];
   read=NativeMethods.urui_get_commands(commands,count);
   int transformRead=NativeMethods.urui_get_command_transforms(commandTransforms,commandTransforms.Length);
   if(transformRead!=read)throw new Exception("URUI command transform count mismatch: "+transformRead+" / "+read);
   if(phaseDiagnostics){phase.CommandCopyMs=Millis(Now()-t);phase.CommandCopyBytes=read*System.Runtime.InteropServices.Marshal.SizeOf(typeof(CommandNative));phase.MatrixCopyBytes=read*16*sizeof(float);phase.TotalCommandCopyBytes=phase.CommandCopyBytes+phase.MatrixCopyBytes;phase.NativeCalls+=3;phase.CommandsRead=read;}
   }
   long unitySetup=phaseDiagnostics?Now():0;
   using(SetupMarker.Auto()){
   buffer.Clear();
   // The prior recording is detached before retiring Host-owned Unity textures.
   // Native existence, not this frame's visibility, is the lifetime authority.
   RetireReleasedTextures(ref phase);
   // Construct a screen-space projection; RmlUi has top-left UI coordinates.
   Matrix4x4 projection=Matrix4x4.Ortho(0,UiWidth,UiHeight,0,-1,1);
   buffer.SetViewProjectionMatrices(Matrix4x4.identity,projection);
   if(phaseDiagnostics)phase.CommandSetupMs+=Millis(Now()-unitySetup);
   }
   var used=useFrameScratchCollections?frameUsedGeometry:new HashSet<ulong>();used.Clear();
   using(LoopMarker.Auto()){
   for(int c=0;c<read;c++){
    var cmd=commands[c];
    long detail=DetailBegin(ref phase.MatrixDecodeMs,ref phase);
    var transform=new Matrix4x4();bool transformed=false;
    for(int col=0;col<4;col++)for(int row=0;row<4;row++){
     float value=commandTransforms[c*16+col*4+row];transform[row,col]=value;if(float.IsNaN(value)||float.IsInfinity(value))recordingFinite=false;
     if(Mathf.Abs(value-(row==col?1f:0f))>0.000001f)transformed=true;
    }
    DetailEnd(detail,ref phase.MatrixDecodeMs);
    if(float.IsNaN(cmd.tx)||float.IsInfinity(cmd.tx)||float.IsNaN(cmd.ty)||float.IsInfinity(cmd.ty))recordingFinite=false;
    if(cmd.geometry==0)continue;
    detail=DetailBegin(ref phase.GeometryLookupMs,ref phase);bool found=meshes.TryGetValue(cmd.geometry,out var entry);if(found&&!entry.mesh){meshes.Remove(cmd.geometry);found=false;}DetailEnd(detail,ref phase.GeometryLookupMs);
    if(!found){using var geometryMarker=GeometryMarker.Auto();
     t=phaseDiagnostics?Now():0;
     if(NativeMethods.urui_get_geometry_info(cmd.geometry,out int nv,out int ni)==0)continue;
     if(nv==0||ni==0)continue;
     var vs=new VertexNative[nv];var ids=new int[ni];
     if(NativeMethods.urui_copy_geometry(cmd.geometry,vs,nv,ids,ni)==0)continue;
     if(phaseDiagnostics){phase.GeometryCopyMs+=Millis(Now()-t);phase.NativeCalls+=2;phase.GeometryCopyBytes+=nv*20+ni*4;t=Now();}
     long convertStart=phaseDiagnostics?Now():0;
     var positions=new Vector3[nv];var uv=new Vector2[nv];var colors=new Color32[nv];
     for(int v=0;v<nv;v++){
      positions[v]=new Vector3(vs[v].x,vs[v].y,0);
      uv[v]=new Vector2(vs[v].u,vs[v].v);
      colors[v]=new Color32(vs[v].r,vs[v].g,vs[v].b,vs[v].a);
     }
     if(phaseDiagnostics)phase.MeshConversionMs+=Millis(Now()-convertStart);
     long unityMeshStart=phaseDiagnostics?Now():0;
     var mesh=new Mesh{name="Geometry"};
     if(nv>65535)mesh.indexFormat=IndexFormat.UInt32;
     mesh.vertices=positions;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=ids;
     mesh.RecalculateBounds();
     if(phaseDiagnostics){phase.UnityMeshApiMs+=Millis(Now()-unityMeshStart);phase.MeshBuildMs+=Millis(Now()-t);phase.MeshesCreated++;}
     entry=new MeshCache{mesh=mesh,positions=batchDisjointCommands?positions:null,uv=batchDisjointCommands?uv:null,colors=batchDisjointCommands?colors:null,indices=batchDisjointCommands?ids:null};meshes.Add(cmd.geometry,entry);
    }
    detail=DetailBegin(ref phase.EntryTrackMs,ref phase);entry.lastFrame=Time.frameCount;used.Add(cmd.geometry);DetailEnd(detail,ref phase.EntryTrackMs);
    if(!transformed&&cmd.clip_enabled!=0&&cmd.clip_w>=0&&cmd.clip_h>=0){
     var bounds=GeometryBounds(entry.mesh,ref phase);
     float minX=bounds.min.x+cmd.tx,maxX=bounds.max.x+cmd.tx,minY=bounds.min.y+cmd.ty,maxY=bounds.max.y+cmd.ty;
     bool outside=maxX<cmd.clip_x||minX>cmd.clip_x+cmd.clip_w||maxY<cmd.clip_y||minY>cmd.clip_y+cmd.clip_h;
     if(outside){if(phaseDiagnostics)phase.FullyClippedCandidates++;if(cullFullyClipped)continue;}
    }
    var renderBounds=GeometryBounds(entry.mesh,ref phase);
    var item=new BatchItem{command=cmd,geometry=entry,transform=transform,transformed=transformed,x0=renderBounds.min.x+cmd.tx,y0=renderBounds.min.y+cmd.ty,x1=renderBounds.max.x+cmd.tx,y1=renderBounds.max.y+cmd.ty};
    if(batchDisjointCommands&&(!transformed||(batchEquivalentTransforms&&BatchRules.EquivalentAffine2D(transform,transform)))){if(!CanJoin(item,ref phase))FlushGroup(ref phase);batchGroup.Add(item);}
    else {if(batchDisjointCommands)FlushGroup(ref phase);if(phaseDiagnostics){phase.BatchGroups++;if(batchDisjointCommands)phase.BreakTransform++;}DrawOne(item,ref phase);}
   }
   }
   if(batchDisjointCommands){
    FlushGroup(ref phase);
    long maintenance=DetailBegin(ref phase.BatchMaintenanceMs,ref phase);
    staleBatchKeys.Clear();foreach(var pair in batches)if(!currentBatchKeys.Contains(pair.Key))staleBatchKeys.Add(pair.Key);
    foreach(var key in staleBatchKeys){Destroy(batches[key].mesh);batches.Remove(key);}
    var swap=previousBatchKeys;previousBatchKeys=currentBatchKeys;currentBatchKeys=swap;currentBatchKeys.Clear();DetailEnd(maintenance,ref phase.BatchMaintenanceMs);
   }
   using var cleanupMarker=CleanupMarker.Auto();
   // Avoid leaking meshes invalidated by RmlUi.
   t=phaseDiagnostics?Now():0;
   var stale=useFrameScratchCollections?frameStaleGeometry:new List<ulong>();stale.Clear();foreach(var pair in meshes)if(!pair.Value.mesh||(!used.Contains(pair.Key)&&NativeMethods.urui_geometry_exists(pair.Key)==0))stale.Add(pair.Key);
   foreach(var id in stale){Destroy(meshes[id].mesh);meshes.Remove(id);}
   if(phaseDiagnostics){phase.CleanupMs=Millis(Now()-t);phase.NativeCalls+=stale.Count;}
   retainedDraws=recordCallsThisFrame;
   LastRecording=new RecordingStats{capability=eligible&&tracking&&stamp.flags==1,nativeCommands=read,recordCalls=recordCallsThisFrame,retainedDraws=recordCallsThisFrame,nativeExtraBytes=stamp.retained_bytes,compareBytes=stamp.compare_bytes};
   if(eligible&&tracking&&stamp.flags==1&&!recordingWarmupPending&&recordingFinite){CaptureTextureDependencies();retainedBufferBytes=buffer.sizeInBytes;retainedRecording.Commit(stamp,recordingKey);}
  }
  Texture2D LoadTexture(ulong id){
   if(id==0)return white;
   if(textures.TryGetValue(id,out var old)){if(old)return old;textures.Remove(id);}
   if(NativeMethods.urui_get_texture_info(id,out int w,out int h,out int length)==0)return white;
   if(w<=0||h<=0||length!=w*h*4)return white;
   var rgba=new byte[length];
   if(NativeMethods.urui_copy_texture(id,rgba,length)==0)return white;
   var texture=new Texture2D(w,h,TextureFormat.RGBA32,false,false){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
   texture.LoadRawTextureData(rgba);texture.Apply(false,true);
   textures[id]=texture;return texture;
  }
  void RetireReleasedTextures(ref PhaseFrame phase){
   retiredTextureIds.Clear();
   foreach(var pair in textures){
    if(!pair.Value||NativeMethods.urui_texture_exists(pair.Key)==0)retiredTextureIds.Add(pair.Key);
    if(phaseDiagnostics)phase.NativeCalls++;
   }
   foreach(var id in retiredTextureIds){if(textures[id])Destroy(textures[id]);textures.Remove(id);}
  }
  void OnDestroy(){ReleaseForDocument();}
  internal void ReleaseForDocument(){
   if(resourcesReleased)return;resourcesReleased=true;
   enabled=false;
   StopParameterHost();
   InvalidateRecording(true);if(active&&nativeRetention)ConfigureNativeRetention(false);
   ClearFrameScratch();
   ClearBatches();
   if(targetCamera&&buffer!=null)targetCamera.RemoveCommandBuffer(CameraEvent.AfterEverything,buffer);
   if(buffer!=null){buffer.Release();buffer=null;}
   foreach(var item in meshes.Values)if(item.mesh)Destroy(item.mesh);
   meshes.Clear();
   foreach(var item in textures.Values)if(item)Destroy(item);
   textures.Clear();
   if(material)Destroy(material);
   if(active){NativeMethods.urui_destroy();active=false;}
   if(ReferenceEquals(ActiveHost,this))ActiveHost=null;
  }
 }
}
