using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RmlUnity {
 // All images, buttons, labels and their references are serialized in the
 // Match3UGUI.prefab. Runtime only binds state and animates existing objects.
 public sealed class Match3UGUIView : MonoBehaviour {
  public GameObject mainPage,gamePage,pausePage,resultPage;
  public Image[] gems,slots,fx;
  public Button[] cellButtons;
  public Sprite[] gemSprites;
  public Text movesLabel,targetLabel,scoreLabel,messageLabel,resultTitle,resultScore;
  public Button playButton,pauseButton,homeButton,resumeButton,replayButton,resultHomeButton;
  Match3Game game;
  public Match3Game CurrentGame=>game;
  public bool IsAnimating=>busy;
  public int configuredMoves=18,configuredTarget=18,configuredGem=2;
  uint seed=0x4D4F4F4Eu;
  bool busy,paused;
  int selected=-1,dragStart=-1,dragPointerId=int.MinValue,suppressClickFrame=-1;
  Vector2[] gemBase;

  void Awake(){
   if(gems==null||gems.Length!=64||slots==null||slots.Length!=64||fx==null||fx.Length!=64||cellButtons==null||cellButtons.Length!=64||gemSprites==null||gemSprites.Length!=6)
    throw new InvalidOperationException("Match3UGUI prefab requires 64 serialized cells and six sprites");
   gemBase=new Vector2[64];
   for(int i=0;i<64;i++){
    int index=i;gemBase[i]=gems[i].rectTransform.anchoredPosition;
    cellButtons[i].onClick.AddListener(()=>OnCellTap(index));
    var input=gems[i].GetComponent<Match3GemInput>();
    if(!input||input.index!=i)throw new InvalidOperationException("Match3UGUI prefab input binding mismatch at "+i);
    input.owner=this;fx[i].enabled=false;
   }
   playButton.onClick.AddListener(StartGame);replayButton.onClick.AddListener(StartGame);
   homeButton.onClick.AddListener(Home);resultHomeButton.onClick.AddListener(Home);
   pauseButton.onClick.AddListener(()=>{if(game!=null){paused=true;dragStart=-1;dragPointerId=int.MinValue;Select(-1);Show("pause");}});
   resumeButton.onClick.AddListener(()=>{paused=false;Show("game");});
   Show("main");
  }
  void Show(string page){
   mainPage.SetActive(page=="main");gamePage.SetActive(page=="game");
   pausePage.SetActive(page=="pause");resultPage.SetActive(page=="result");
  }
  void Home(){StopAllCoroutines();busy=false;paused=false;game=null;dragStart=-1;dragPointerId=int.MinValue;Show("main");}
  void StartGame(){
   StopAllCoroutines();game=new Match3Game(seed,configuredMoves,configuredTarget,configuredGem);busy=false;paused=false;selected=-1;dragStart=-1;dragPointerId=int.MinValue;
   for(int i=0;i<64;i++){
    gems[i].rectTransform.anchoredPosition=gemBase[i];gems[i].rectTransform.localScale=Vector3.one;gems[i].color=Color.white;
    slots[i].color=Color.white;fx[i].enabled=false;fx[i].rectTransform.localScale=Vector3.one;fx[i].color=Color.white;
   }
   DrawBoard(game.Snapshot());UpdateHud();Show("game");
  }
  void UpdateHud(){movesLabel.text="MOVES "+game.MovesRemaining;targetLabel.text="LEAVES "+game.Collected+"/"+game.TargetCount;scoreLabel.text="SCORE "+game.Score;}
  void DrawBoard(int[] board){for(int i=0;i<64;i++)gems[i].sprite=gemSprites[board[i]];}
  void Select(int index){if(selected>=0)slots[selected].color=Color.white;selected=index;if(index>=0)slots[index].color=new Color(1f,0.9f,0.42f,1f);}
  void OnCellTap(int cell){
   if(Time.frameCount==suppressClickFrame||dragStart>=0||game==null||busy||paused)return;
   if(selected<0){Select(cell);return;}
   int from=selected;Select(-1);if(from!=cell)Submit(from,cell);
  }
  public void OnGemDown(int index,int pointerId){if(game!=null&&!busy&&!paused&&dragStart<0){dragStart=index;dragPointerId=pointerId;}}
  public void OnGemCancel(){dragStart=-1;dragPointerId=int.MinValue;}
  public void OnGemUp(int index,PointerEventData data){
   if(game==null||busy||paused||dragStart<0||data.pointerId!=dragPointerId)return;
   int target=-1;
   for(int i=0;i<64;i++)if(RectTransformUtility.RectangleContainsScreenPoint(slots[i].rectTransform,data.position,data.pressEventCamera)){target=i;break;}
   if(target>=0&&target!=dragStart){suppressClickFrame=Time.frameCount;Select(-1);Submit(dragStart,target);}
   dragStart=-1;dragPointerId=int.MinValue;
  }
  void Submit(int from,int to){
   long ruleStart=Match3CpuProbe.Start();var move=game.TrySwap(from,to);Match3CpuProbe.EndRules(ruleStart);
   if(!move.Accepted){Select(to);return;}
   StartCoroutine(Animate(move));
  }
  IEnumerator Animate(Match3Move move){
   var routine=AnimateSteps(move);try{while(Match3CpuProbe.Step(routine))yield return routine.Current;}finally{(routine as IDisposable)?.Dispose();}
  }
  public int DiagnosticStep {get;private set;}=-1;
  IEnumerator AnimateSteps(Match3Move move){
   busy=true;
   foreach(var step in move.Steps){
    DiagnosticStep=(int)step.Type;
    while(paused)yield return null;
    if(step.Type==Match3StepType.Swap||step.Type==Match3StepType.Rebound){
     var from=gems[step.From].rectTransform;var to=gems[step.To].rectTransform;
     Vector2 a=gemBase[step.From],b=gemBase[step.To];float elapsed=0;
     while(elapsed<Match3Motion.SwapSeconds){
      if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.Record(step,elapsed);}
      float t=Match3Motion.EaseInOut(elapsed/Match3Motion.SwapSeconds);
      float arc=Mathf.Sin(Mathf.PI*t)*14f,scale=1f+Mathf.Sin(Mathf.PI*t)*0.12f;
      if(Match3PreparedInput.Enabled){int dx=(step.To%8-step.From%8)*102,dy=(step.To/8-step.From/8)*102;var va=Match3PreparedInput.Transform(dx*t,dy*t-arc,scale);var vb=Match3PreparedInput.Transform(-dx*t,-dy*t+arc,scale);from.anchoredPosition=a+new Vector2(va.x,-va.y);to.anchoredPosition=b+new Vector2(vb.x,-vb.y);from.localScale=Vector3.one*va.z;to.localScale=Vector3.one*vb.z;}else{
      from.anchoredPosition=Vector2.LerpUnclamped(a,b,t)+Vector2.up*arc;
      to.anchoredPosition=Vector2.LerpUnclamped(b,a,t)-Vector2.up*arc;
      from.localScale=to.localScale=Vector3.one*scale;}
      yield return null;
     }
     from.anchoredPosition=a;to.anchoredPosition=b;from.localScale=to.localScale=Vector3.one;DrawBoard(step.Board);
    }else if(step.Type==Match3StepType.Clear){
     foreach(int i in step.Cells)fx[i].enabled=true;
     float elapsed=0;
     while(elapsed<Match3Motion.ClearSeconds){
      if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.Record(step,elapsed);}
      float t=Mathf.Clamp01(elapsed/Match3Motion.ClearSeconds);
      foreach(int i in step.Cells){
       if(Match3PreparedInput.Enabled){float opacity=Match3PreparedInput.Opacity(1f-.78f*Match3Motion.EaseInOut(t),out _);var v=Match3PreparedInput.Transform(0,0,1f-.32f*Match3Motion.EaseInOut(t));gems[i].color=new Color(1,1,1,opacity);gems[i].rectTransform.localScale=Vector3.one*v.z;opacity=Match3PreparedInput.Opacity(Mathf.Sin(Mathf.PI*t),out _);v=Match3PreparedInput.Transform(0,0,.55f+.75f*Match3Motion.EaseOutCubic(t));fx[i].color=new Color(1,1,1,opacity);fx[i].rectTransform.localScale=Vector3.one*v.z;}else{
       gems[i].color=new Color(1,1,1,1f-0.78f*Match3Motion.EaseInOut(t));
       gems[i].rectTransform.localScale=Vector3.one*(1f-0.32f*Match3Motion.EaseInOut(t));
       fx[i].color=new Color(1,1,1,Mathf.Sin(Mathf.PI*t));
       fx[i].rectTransform.localScale=Vector3.one*(0.55f+0.75f*Match3Motion.EaseOutCubic(t));}
      }
      yield return null;
     }
     foreach(int i in step.Cells){fx[i].enabled=false;fx[i].rectTransform.localScale=Vector3.one;gems[i].rectTransform.localScale=Vector3.one;gems[i].color=Color.white;}
    }else if(step.Type==Match3StepType.Fall||step.Type==Match3StepType.Shuffle){
     DrawBoard(step.Board);
     if(step.Type==Match3StepType.Fall){
      float elapsed=0;
      while(elapsed<Match3Motion.FallSeconds){
       if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.Record(step,elapsed);}
       for(int i=0;i<64;i++){
        int source=step.SourceForCell==null?-1:step.SourceForCell[i];
        float distance=Mathf.Min(56f,source<0?102:Mathf.Max(0,(i/8-source/8)*102));
        float t=Match3Motion.FallProgress(elapsed,i%8);
        if(Match3PreparedInput.Enabled){var v=Match3PreparedInput.Transform(0,-distance*(1f-t),1f+.08f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t)));gems[i].rectTransform.anchoredPosition=gemBase[i]+new Vector2(v.x,-v.y);gems[i].rectTransform.localScale=Vector3.one*v.z;}else{
        gems[i].rectTransform.anchoredPosition=gemBase[i]+Vector2.up*(distance*(1f-t));
        gems[i].rectTransform.localScale=Vector3.one*(1f+0.08f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t)));}
       }
       yield return null;
      }
      for(int i=0;i<64;i++)gems[i].rectTransform.anchoredPosition=gemBase[i];
      for(int i=0;i<64;i++)gems[i].rectTransform.localScale=Vector3.one;
     }
     yield return Delay(0.05f);
    }
   }
   UpdateHud();
   if(game.Outcome!=Match3Outcome.Playing){resultTitle.text=game.Outcome==Match3Outcome.Won?"FOREST RESTORED":"OUT OF MOVES";resultScore.text="SCORE "+game.Score;Show("result");}
   else messageLabel.text=move.Matched?"Gems shimmer through the grove!":"Try another pair";
   busy=false;
  }
  IEnumerator Delay(float seconds){float elapsed=0;while(elapsed<seconds){if(!paused){elapsed+=Match3MotionClock.Delta;Match3MotionClock.RecordDelay(seconds,elapsed);}yield return null;}}
 }

}
