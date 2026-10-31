using System;
using System.Linq;
using UnityEngine;

namespace RmlUnity.Editor {
 public static class Match3GameTests {
  static void Check(bool ok,string message){if(!ok)throw new Exception("MATCH3: "+message);}
  static bool Equal(int[] a,int[] b)=>a.SequenceEqual(b);

  public static void Run(){
   Check(Match3RmlView.CreateMarkup(720,1280).Contains("scale(0.667)"),"Rml layout scales a 1080-wide design to a 720-wide portrait viewport");
   var a=new Match3Game(0x4D4F4F4Eu);
   var b=new Match3Game(0x4D4F4F4Eu);
   Check(Equal(a.Snapshot(),b.Snapshot()),"same seed must produce same board");
   Check(a.Snapshot().Length==64&&a.Snapshot().All(x=>x>=0&&x<6),"8x8 board with six gems");
   Check(!Match3Rules.HasMatch(a.Snapshot())&&Match3Rules.HasLegalMove(a.Snapshot()),"initial board playable and match-free");

   var before=a.Snapshot();
   var invalid=a.TrySwap(0,63);
   Check(!invalid.Accepted&&Equal(before,a.Snapshot())&&a.MovesRemaining==18,"non-adjacent swap rejected");

   bool reboundFound=false;
   for(int i=0;i<64&&!reboundFound;i++)foreach(int j in new[]{i+1,i+8}){
    if(j>=64||(j==i+1&&i%8==7))continue;
    var probe=new Match3Game(0x4D4F4F4Eu);
    var move=probe.TrySwap(i,j);
    if(move.Accepted&&!move.Matched){
     Check(move.Steps.Count==2&&move.Steps[1].Type==Match3StepType.Rebound,"failed match animates swap and rebound");
     Check(probe.MovesRemaining==18&&Equal(before,probe.Snapshot()),"rebound costs no move and restores board");
     reboundFound=true;break;
    }
   }
   Check(reboundFound,"at least one adjacent non-match exists");

   int from,to;Check(Match3Rules.FindLegalMove(before,out from,out to),"a legal move exists");
   var successful=a.TrySwap(from,to);
   Check(successful.Accepted&&successful.Matched&&a.MovesRemaining==17,"match consumes exactly one move");
   Check(successful.Steps.Any(x=>x.Type==Match3StepType.Clear)&&successful.Steps.Any(x=>x.Type==Match3StepType.Fall),"clear and fall phases emitted");
   Check(!Match3Rules.HasMatch(a.Snapshot())&&Match3Rules.HasLegalMove(a.Snapshot()),"settled board has no matches and remains playable");
   var replay=b.TrySwap(from,to);
   Check(replay.Matched&&Equal(a.Snapshot(),b.Snapshot())&&a.Score==b.Score,"replay gives identical result");

   bool cascadeFound=false;
   for(uint fixtureSeed=1;fixtureSeed<=256&&!cascadeFound;fixtureSeed++){
    var fixture=new Match3Game(fixtureSeed);
    if(!Match3Rules.FindLegalMove(fixture.Snapshot(),out int first,out int second))continue;
    var move=fixture.TrySwap(first,second);
    cascadeFound=move.Steps.Any(x=>x.Type==Match3StepType.Clear&&x.Chain>1);
   }
   Check(cascadeFound,"a deterministic seed demonstrates a multi-clear cascade");

   bool winFound=false;
   for(int gem=0;gem<6&&!winFound;gem++){
    var win=new Match3Game(0x4D4F4F4Eu,18,1,gem);
    var first=win.TrySwap(from,to);
    winFound=first.Matched&&win.Outcome==Match3Outcome.Won&&win.MovesRemaining==17;
   }
   Check(winFound,"collecting the first target gem enters victory state");

   var noMove=new int[64];for(int y=0;y<8;y++)for(int x=0;x<8;x++)noMove[y*8+x]=(x+2*y)%6;
   Check(!Match3Rules.HasMatch(noMove)&&!Match3Rules.HasLegalMove(noMove),"fixture truly has no legal swap");
   var reshuffled=Match3Rules.ReshufflePlayable(noMove,0xFA11BACCu);
   Check(!Match3Rules.HasMatch(reshuffled)&&Match3Rules.HasLegalMove(reshuffled),"reshuffle restores a playable board");
   Check(Equal(noMove.OrderBy(x=>x).ToArray(),reshuffled.OrderBy(x=>x).ToArray()),"reshuffle preserves gem inventory");

   var loss=new Match3Game(0x4D4F4F4Eu,1,999,2);
   var last=loss.TrySwap(from,to);
   Check(last.Matched&&loss.Outcome==Match3Outcome.Lost&&loss.MovesRemaining==0,"zero moves ends in loss");
   Check(!loss.TrySwap(from,to).Accepted,"finished game rejects input");
   loss.Reset(0x4D4F4F4Eu);
   Check(loss.Outcome==Match3Outcome.Playing&&loss.MovesRemaining==1&&Equal(loss.Snapshot(),before),"replay reset restores deterministic start");
   Debug.Log("MATCH3_GAME_TESTS_PASS");
  }
 }
}
