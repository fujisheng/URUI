using System;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace RmlUnity {
 // RML is the sole UI renderer for this variant. Game state and random draws
 // come from Match3Game, which is also used by the uGUI variant.
 public sealed class Match3RmlView : MonoBehaviour {
  static readonly string[] Gems={"art/ruby.png","art/sapphire.png","art/emerald.png","art/amber.png","art/amethyst.png","art/star.png"};
  public RmlUnityHost host;
  public TextAsset sharedAtlasStyle;
  public bool typedTranslateScale;
  public bool spanNumericRoundtrip;
  public bool exactFixedNumeric; // Optional generic codec; does not change precision.
  public bool retainedElementReferences;
  RmlElementReference[] gemReferences,fxReferences;
  public int configuredMoves=18,configuredTarget=18,configuredGem=2;
  Match3Game game;
  public Match3Game CurrentGame=>game;
  public bool IsAnimating=>busy;
  public int DiagnosticStep {get;private set;}=-1;
  bool busy,paused;
  int selected=-1,dragStart=-1;
  uint seed=0x4D4F4F4Eu;
  const int Cell=102,BoardX=132,BoardY=506;

  void Awake(){
   if(!host)host=GetComponent<RmlUnityHost>();
   if(host){
    string roboto=Path.Combine(Application.streamingAssetsPath,"RmlUnity/Roboto-Regular.ttf");
    if(File.Exists(roboto))host.overrideFontPath=roboto;
    #if UNITY_EDITOR
    else {
     roboto=Path.Combine(Application.dataPath,"RmlUnity/UGUI/Fonts/Roboto-Regular.ttf");
     if(File.Exists(roboto))host.overrideFontPath=roboto;
    }
    #elif UNITY_STANDALONE_OSX
    else throw new FileNotFoundException("Match3 packaged Roboto font is missing",roboto);
    #endif
    string markup=CreateMarkup(Screen.width,Screen.height);
    if(sharedAtlasStyle){
     markup=markup.Replace("</style>",sharedAtlasStyle.text+"</style>");
     foreach(string name in new[]{"title","panel","button","slot","burst","ruby","sapphire","emerald","amber","amethyst","star"})
      markup=markup.Replace("src='art/"+name+".png'","sprite='"+name+"'");
    }
    host.InitialMarkup=markup;
   }
  }
  IEnumerator Start(){
   while(host&&!host.Ready)yield return null;
   if(!host)yield break;
   host.Clicked+=OnClick;host.PointerInput+=OnPointer;
   Show("main");
  }
  void OnDestroy(){ReleaseAnimationReferences();if(host){host.Clicked-=OnClick;host.PointerInput-=OnPointer;}}

  public static string CreateMarkup(int width,int height){
   float layoutScale=Mathf.Min(width/1080f,height/1920f);
   int left=Mathf.RoundToInt((width-1080f*layoutScale)*0.5f);
   int offset=Mathf.RoundToInt((height-1920f*layoutScale)*0.5f);
   var b=new StringBuilder(30000);
   b.Append("<rml><head><style>body{margin:0;color:#fff8db;font-family:Roboto;} .screen{position:absolute;left:0;top:0;width:1080px;height:1920px;} .label{font-size:46px;text-align:center;} .small{font-size:31px;text-align:center;color:#d5f7e8;} .button{font-size:55px;text-align:center;color:#fff5cd;} .gem{position:absolute;width:94px;height:94px;} .slot{position:absolute;width:100px;height:100px;border-width:3px;border-color:transparent;} .fx{position:absolute;width:110px;height:110px;display:none;} </style></head><body>");
   b.Append("<img src='art/forest.png' style='position:absolute;left:0;top:0;width:").Append(width).Append("px;height:").Append(height).Append("px;' />");
   b.Append("<div style='position:absolute;left:").Append(left).Append("px;top:").Append(offset).Append("px;width:1080px;height:1920px;transform-origin:0px 0px;transform:scale(").Append(N(layoutScale)).Append(");'>");
   b.Append("<div id='main' class='screen'><img src='art/title.png' style='position:absolute;left:190px;top:230px;width:700px;height:420px;' /><div class='label' style='position:absolute;left:190px;top:395px;width:700px;font-size:58px;'>ENCHANTED GROVE</div><div class='small' style='position:absolute;left:170px;top:810px;width:740px;'>Match emeralds to awaken the forest</div>");
   Button(b,"play",340,1110,400,150,"PLAY");
   b.Append("<div class='small' style='position:absolute;left:170px;top:1375px;width:740px;'>18 MOVES  ·  18 EMERALDS  ·  6 GEMS</div></div>");
   b.Append("<div id='game' class='screen' style='display:none;'>");
   b.Append("<img src='art/panel.png' style='position:absolute;left:72px;top:106px;width:936px;height:340px;' /><div class='label' style='position:absolute;left:186px;top:140px;width:710px;'>GROVE QUEST</div>");
   b.Append("<div id='moves' class='small' style='position:absolute;left:135px;top:245px;width:235px;'>MOVES 18</div><div id='target' class='small' style='position:absolute;left:375px;top:245px;width:345px;'>LEAVES 0/18</div><div id='score' class='small' style='position:absolute;left:730px;top:245px;width:235px;'>SCORE 0</div>");
   b.Append("<img src='art/panel.png' style='position:absolute;left:78px;top:446px;width:924px;height:968px;' />");
   for(int i=0;i<64;i++){
    int x=BoardX+(i%8)*Cell,y=BoardY+(i/8)*Cell;
    b.Append("<img id='slot").Append(i).Append("' class='slot' src='art/slot.png' style='left:").Append(x).Append("px;top:").Append(y).Append("px;' />");
    b.Append("<img id='cell").Append(i).Append("' class='gem' src='art/ruby.png' style='left:").Append(x+3).Append("px;top:").Append(y+3).Append("px;' />");
    b.Append("<img id='fx").Append(i).Append("' class='fx' src='art/burst.png' style='left:").Append(x-5).Append("px;top:").Append(y-5).Append("px;' />");
   }
   b.Append("<div id='message' class='small' style='position:absolute;left:85px;top:1460px;width:910px;'>Tap or drag adjacent gems to swap</div>");
   Button(b,"pause",180,1560,310,120,"PAUSE");Button(b,"home",590,1560,310,120,"HOME");
   b.Append("</div>");
   b.Append("<div id='pause-screen' class='screen' style='display:none;background-color:#051c2ad9;'><img src='art/panel.png' style='position:absolute;left:100px;top:520px;width:880px;height:680px;' /><div class='label' style='position:absolute;left:180px;top:650px;width:720px;font-size:80px;'>PAUSED</div>");
   Button(b,"resume",250,840,580,140,"RESUME");b.Append("</div>");
   b.Append("<div id='result' class='screen' style='display:none;'><img src='art/panel.png' style='position:absolute;left:100px;top:440px;width:880px;height:980px;' /><div id='result-title' class='label' style='position:absolute;left:190px;top:600px;width:700px;font-size:82px;'>FOREST RESTORED</div><div id='result-score' class='label' style='position:absolute;left:190px;top:790px;width:700px;'>SCORE 0</div>");
   Button(b,"replay",240,1000,600,140,"PLAY AGAIN");Button(b,"result-home",240,1190,600,140,"HOME");b.Append("</div></div></body></rml>");
   return b.ToString();
  }
  static void Button(StringBuilder b,string id,int x,int y,int width,int height,string label){
   b.Append("<div id='").Append(id).Append("' class='button' style='position:absolute;left:").Append(x).Append("px;top:").Append(y).Append("px;width:").Append(width).Append("px;height:").Append(height).Append("px;'><img src='art/button.png' style='position:absolute;left:0;top:0;width:").Append(width).Append("px;height:").Append(height).Append("px;' /><div style='position:absolute;left:0;top:").Append(height/3).Append("px;width:").Append(width).Append("px;'>").Append(label).Append("</div></div>");
  }
  void Show(string page){
   host.SetStyle("main","display",page=="main"?"block":"none");
   host.SetStyle("game","display",page=="game"?"block":"none");
   host.SetStyle("result","display",page=="result"?"block":"none");
   host.SetStyle("pause-screen","display",page=="pause"?"block":"none");
  }
  void StartGame(){ReleaseAnimationReferences();game=new Match3Game(seed,configuredMoves,configuredTarget,configuredGem);selected=-1;dragStart=-1;busy=false;paused=false;DrawBoard(game.Snapshot());for(int i=0;i<64;i++){host.SetStyle("slot"+i,"border-color","transparent");host.SetStyle("cell"+i,"opacity","1");host.SetStyle("cell"+i,"transform","none");host.SetStyle("fx"+i,"display","none");host.SetStyle("fx"+i,"opacity","1");host.SetStyle("fx"+i,"transform","none");}UpdateHud();BindAnimationReferences();Show("game");}
  void BindAnimationReferences(){
   if(!retainedElementReferences)return;int count=game.Snapshot().Length;
   gemReferences=new RmlElementReference[count];fxReferences=new RmlElementReference[count];
   try{for(int i=0;i<count;i++){gemReferences[i]=host.AcquireElementReference("cell"+i);fxReferences[i]=host.AcquireElementReference("fx"+i);if(gemReferences[i]==null||fxReferences[i]==null)throw new InvalidOperationException("Could not bind animation elements");}}
   catch{ReleaseAnimationReferences();throw;}
  }
  void ReleaseAnimationReferences(){if(gemReferences!=null)foreach(var r in gemReferences)r?.Dispose();if(fxReferences!=null)foreach(var r in fxReferences)r?.Dispose();gemReferences=null;fxReferences=null;}
  static string N(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
  static readonly Unity.Profiling.ProfilerMarker TransformPrepareMarker=new Unity.Profiling.ProfilerMarker("Match3.NumericPrepare");
  void SetAnimatedTransform(string id,float x,float y,float scale,RmlElementReference reference=null){
   if(Match3PreparedInput.Enabled){var v=Match3PreparedInput.Transform(x,y,scale);if(reference==null||!reference.SetTranslateScale2D(v.x,v.y,v.z,v.z))throw new InvalidOperationException("Prepared animation reference invalidated");return;}
   long preparation=Match3CpuProbe.Start();
   float sx=0,tx=0,ty=0;string value=null;
   using(TransformPrepareMarker.Auto()){
   if(typedTranslateScale&&spanNumericRoundtrip){if(exactFixedNumeric){sx=RmlNumericCompatibility.FormatRoundTrip(scale,"0.###",CultureInfo.InvariantCulture,true);tx=RmlNumericCompatibility.FormatRoundTrip(x,"0.###",CultureInfo.InvariantCulture,true);ty=RmlNumericCompatibility.FormatRoundTrip(y,"0.###",CultureInfo.InvariantCulture,true);}else{sx=RmlNumericCompatibility.FormatRoundTrip(scale,"0.###",CultureInfo.InvariantCulture);tx=RmlNumericCompatibility.FormatRoundTrip(x,"0.###",CultureInfo.InvariantCulture);ty=RmlNumericCompatibility.FormatRoundTrip(y,"0.###",CultureInfo.InvariantCulture);}}
   else{
    string a=N(x),b=N(y),c=N(scale);
    if(typedTranslateScale){sx=float.Parse(c,CultureInfo.InvariantCulture);tx=float.Parse(a,CultureInfo.InvariantCulture);ty=float.Parse(b,CultureInfo.InvariantCulture);}
    else value="translate("+a+"px,"+b+"px) scale("+c+")";
   }
   }
   Match3CpuProbe.EndPrepare(preparation);if(typedTranslateScale&&retainedElementReferences){if(reference==null||!reference.SetTranslateScale2D(tx,ty,sx,sx))throw new InvalidOperationException("Animation object reference invalidated");}else if(typedTranslateScale)host.SetTranslateScale2D(id,tx,ty,sx,sx);else host.SetStyle(id,"transform",value);
  }
  void SetAnimatedOpacity(string id,float value){if(Match3PreparedInput.Enabled){Match3PreparedInput.Opacity(value,out string text);host.SetStyle(id,"opacity",text);}else host.SetStyle(id,"opacity",N(value));}
  void UpdateHud(){host.SetText("moves","MOVES "+game.MovesRemaining);host.SetText("target","LEAVES "+game.Collected+"/"+game.TargetCount);host.SetText("score","SCORE "+game.Score);}
  void DrawBoard(int[] board){for(int i=0;i<64;i++)host.SetAttribute("cell"+i,sharedAtlasStyle?"sprite":"src",sharedAtlasStyle?Path.GetFileNameWithoutExtension(Gems[board[i]]):Gems[board[i]]);}
  void Select(int index){if(selected>=0)host.SetStyle("slot"+selected,"border-color","transparent");selected=index;if(index>=0)host.SetStyle("slot"+index,"border-color","#ffe67d");}
  static int ParseCell(string id){if(id==null||!id.StartsWith("cell",StringComparison.Ordinal))return -1;return int.TryParse(id.Substring(4),out int n)&&n>=0&&n<64?n:-1;}
  void OnPointer(RmlPointerEvent e){
   if(game==null||busy||paused)return;
   if(e.phase==RmlPointerPhase.Down)dragStart=ParseCell(e.hitId);
   if(e.phase==RmlPointerPhase.Cancel)dragStart=-1;
   if(e.phase==RmlPointerPhase.Up){int end=ParseCell(e.hitId);if(dragStart>=0&&end>=0&&dragStart!=end){Select(-1);Submit(dragStart,end);dragStart=-1;}else dragStart=-1;}
  }
  void OnClick(string id){
   if(id=="play"||id=="replay"){StartGame();return;}
   if(id=="home"||id=="result-home"){paused=false;busy=false;StopAllCoroutines();ReleaseAnimationReferences();Show("main");return;}
   if(id=="pause"&&game!=null){paused=true;dragStart=-1;Select(-1);Show("pause");return;}
   if(id=="resume"){paused=false;Show("game");return;}
   int cell=ParseCell(id);if(cell<0||game==null||busy||paused)return;
   if(selected<0){Select(cell);return;}
   int from=selected;Select(-1);if(from!=cell)Submit(from,cell);
  }
  void Submit(int from,int to){
   if(game==null||busy||paused)return;
   long ruleStart=Match3CpuProbe.Start();var move=game.TrySwap(from,to);Match3CpuProbe.EndRules(ruleStart);if(!move.Accepted){Select(to);return;}
   StartCoroutine(Animate(move));
  }
  IEnumerator Animate(Match3Move move){
   var routine=AnimateSteps(move);try{while(Match3CpuProbe.Step(routine))yield return routine.Current;}finally{(routine as IDisposable)?.Dispose();}
  }
  IEnumerator AnimateSteps(Match3Move move){
   busy=true;
   foreach(var step in move.Steps){
    DiagnosticStep=(int)step.Type;
    while(paused)yield return null;
    if(step.Type==Match3StepType.Swap||step.Type==Match3StepType.Rebound){
     int dx=((step.To%8)-(step.From%8))*Cell,dy=((step.To/8)-(step.From/8))*Cell;
     float elapsed=0;
     while(elapsed<Match3Motion.SwapSeconds){
      if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.Record(step,elapsed);}
      float t=Match3Motion.EaseInOut(elapsed/Match3Motion.SwapSeconds);
      float arc=Mathf.Sin(Mathf.PI*t)*14f,scale=1f+Mathf.Sin(Mathf.PI*t)*0.12f;
      SetAnimatedTransform("cell"+step.From,dx*t,dy*t-arc,scale,gemReferences?[step.From]);
      SetAnimatedTransform("cell"+step.To,-dx*t,-dy*t+arc,scale,gemReferences?[step.To]);
      yield return null;
     }
     host.SetStyle("cell"+step.From,"transform","none");host.SetStyle("cell"+step.To,"transform","none");
     DrawBoard(step.Board);
    }else if(step.Type==Match3StepType.Clear){
     foreach(int cell in step.Cells)host.SetStyle("fx"+cell,"display","block");
     float elapsed=0;
     while(elapsed<Match3Motion.ClearSeconds){
      if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.Record(step,elapsed);}
      float t=Mathf.Clamp01(elapsed/Match3Motion.ClearSeconds);
      foreach(int cell in step.Cells){
       SetAnimatedOpacity("cell"+cell,1f-0.78f*Match3Motion.EaseInOut(t));
       SetAnimatedTransform("cell"+cell,0,0,1f-0.32f*Match3Motion.EaseInOut(t),gemReferences?[cell]);
       SetAnimatedOpacity("fx"+cell,Mathf.Sin(Mathf.PI*t));
       SetAnimatedTransform("fx"+cell,0,0,0.55f+0.75f*Match3Motion.EaseOutCubic(t),fxReferences?[cell]);
      }
      yield return null;
     }
     foreach(int cell in step.Cells){host.SetStyle("fx"+cell,"display","none");host.SetStyle("fx"+cell,"transform","none");host.SetStyle("cell"+cell,"opacity","1");host.SetStyle("cell"+cell,"transform","none");}
    }else if(step.Type==Match3StepType.Fall||step.Type==Match3StepType.Shuffle){
     DrawBoard(step.Board);
     if(step.Type==Match3StepType.Fall){
      float elapsed=0;
      while(elapsed<Match3Motion.FallSeconds){
       if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.Record(step,elapsed);}
       for(int i=0;i<64;i++){
        int source=step.SourceForCell==null?-1:step.SourceForCell[i];
        float distance=Mathf.Min(56f,source<0?Cell:Mathf.Max(0,(i/8-source/8)*Cell));
        float t=Match3Motion.FallProgress(elapsed,i%8);
        SetAnimatedTransform("cell"+i,0,-distance*(1f-t),1f+0.08f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t)),gemReferences?[i]);
       }
       yield return null;
      }
      for(int i=0;i<64;i++)host.SetStyle("cell"+i,"transform","none");
     }
     yield return Delay(0.05f);
    }
   }
   UpdateHud();
   if(game.Outcome!=Match3Outcome.Playing){host.SetText("result-title",game.Outcome==Match3Outcome.Won?"FOREST RESTORED":"OUT OF MOVES");host.SetText("result-score","SCORE "+game.Score);Show("result");}
   else host.SetText("message",move.Matched?"Gems shimmer through the grove!":"Try another pair");
   busy=false;
  }
  IEnumerator Delay(float seconds){float elapsed=0;while(elapsed<seconds){if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.RecordDelay(seconds,elapsed);}yield return null;}}
 }
}
