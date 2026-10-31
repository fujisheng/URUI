using System;
using System.Collections.Generic;

namespace RmlUnity {
 public enum Match3Outcome { Playing, Won, Lost }
 public enum Match3StepType { Swap, Rebound, Clear, Fall, Shuffle, Finish }

 public sealed class Match3Step {
  public Match3StepType Type { get; private set; }
  public int[] Board { get; private set; }
  public int[] Cells { get; private set; }
  // For a Fall step, SourceForCell[destination] is the previous cell index;
  // -1 denotes a newly spawned gem. Other steps leave this null.
  public int[] SourceForCell { get; private set; }
  public int From { get; private set; }
  public int To { get; private set; }
  public int Chain { get; private set; }
  public Match3Step(Match3StepType type,int[] board,int[] cells=null,int[] sourceForCell=null,int from=-1,int to=-1,int chain=0){
   Type=type;Board=(int[])board.Clone();Cells=cells==null?Array.Empty<int>():(int[])cells.Clone();SourceForCell=sourceForCell==null?null:(int[])sourceForCell.Clone();From=from;To=to;Chain=chain;
  }
 }
 public sealed class Match3Move {
  public bool Accepted { get; private set; }
  public bool Matched { get; private set; }
  public IReadOnlyList<Match3Step> Steps { get; private set; }
  public Match3Move(bool accepted,bool matched,List<Match3Step> steps){Accepted=accepted;Matched=matched;Steps=steps.AsReadOnly();}
 }

 public struct Match3Rng {
  uint state;
  public Match3Rng(uint seed){state=seed==0?0x9E3779B9u:seed;}
  public uint Next(){state^=state<<13;state^=state>>17;state^=state<<5;return state;}
  public int Range(int max){return (int)(Next()%(uint)max);}
 }

 public static class Match3Rules {
  public const int Width=8, Cells=64, GemTypes=6;
  static void Validate(int[] board){if(board==null||board.Length!=Cells)throw new ArgumentException("Expected 64 cells",nameof(board));}
  public static int[] Matches(int[] board){
   Validate(board);var mark=new bool[Cells];
   for(int y=0;y<Width;y++)for(int x=0;x<Width;){
    int value=board[y*Width+x],end=x+1;
    while(end<Width&&board[y*Width+end]==value)end++;
    if(value>=0&&end-x>=3)for(int k=x;k<end;k++)mark[y*Width+k]=true;
    x=end;
   }
   for(int x=0;x<Width;x++)for(int y=0;y<Width;){
    int value=board[y*Width+x],end=y+1;
    while(end<Width&&board[end*Width+x]==value)end++;
    if(value>=0&&end-y>=3)for(int k=y;k<end;k++)mark[k*Width+x]=true;
    y=end;
   }
   var result=new List<int>();for(int i=0;i<Cells;i++)if(mark[i])result.Add(i);return result.ToArray();
  }
  public static bool HasMatch(int[] board)=>Matches(board).Length>0;
  static bool SwapMakesMatch(int[] board,int a,int b){
   int t=board[a];board[a]=board[b];board[b]=t;
   bool matched=HasMatch(board);
   t=board[a];board[a]=board[b];board[b]=t;
   return matched;
  }
  public static bool FindLegalMove(int[] board,out int from,out int to){
   Validate(board);
   for(int y=0;y<Width;y++)for(int x=0;x<Width;x++){
    int i=y*Width+x;
    if(x+1<Width&&board[i]!=board[i+1]&&SwapMakesMatch(board,i,i+1)){from=i;to=i+1;return true;}
    if(y+1<Width&&board[i]!=board[i+Width]&&SwapMakesMatch(board,i,i+Width)){from=i;to=i+Width;return true;}
   }
   from=to=-1;return false;
  }
  public static bool HasLegalMove(int[] board){int a,b;return FindLegalMove(board,out a,out b);}
  public static int[] CreatePlayable(ref Match3Rng rng){
   for(int attempt=0;attempt<512;attempt++){
    var board=new int[Cells];
    for(int i=0;i<Cells;i++){
     int x=i%Width,y=i/Width,start=rng.Range(GemTypes),selected=-1;
     for(int n=0;n<GemTypes;n++){
      int candidate=(start+n)%GemTypes;
      if(x>=2&&board[i-1]==candidate&&board[i-2]==candidate)continue;
      if(y>=2&&board[i-Width]==candidate&&board[i-2*Width]==candidate)continue;
      selected=candidate;break;
     }
     board[i]=selected;
    }
    if(HasLegalMove(board))return board;
   }
   throw new InvalidOperationException("Unable to generate a playable board");
  }
  public static int[] ReshufflePlayable(int[] original,uint seed){
   Validate(original);var rng=new Match3Rng(seed);
   for(int attempt=0;attempt<1024;attempt++){
    var board=(int[])original.Clone();
    for(int i=Cells-1;i>0;i--){int j=rng.Range(i+1);int t=board[i];board[i]=board[j];board[j]=t;}
    if(!HasMatch(board)&&HasLegalMove(board))return board;
   }
   // Some skewed inventories cannot form a match-free grid. Recover with a
   // fresh playable board rather than trapping the game in a no-move state.
   return CreatePlayable(ref rng);
  }
 }

 public sealed class Match3Game {
  readonly int initialMoves,targetCount,targetGem;
  Match3Rng rng;
  int[] board;
  public uint Seed {get;private set;}
  public int MovesRemaining {get;private set;}
  public int Collected {get;private set;}
  public int Score {get;private set;}
  public int TargetCount=>targetCount;
  public int TargetGem=>targetGem;
  public Match3Outcome Outcome {get;private set;}
  public Match3Game(uint seed,int moves=18,int target=18,int collectGem=2){
   if(moves<=0||target<=0||collectGem<0||collectGem>=Match3Rules.GemTypes)throw new ArgumentOutOfRangeException("Invalid match-three configuration");
   initialMoves=moves;targetCount=target;targetGem=collectGem;Reset(seed);
  }
  public void Reset(uint seed){Seed=seed;rng=new Match3Rng(seed);board=Match3Rules.CreatePlayable(ref rng);MovesRemaining=initialMoves;Collected=0;Score=0;Outcome=Match3Outcome.Playing;}
  public int[] Snapshot()=> (int[])board.Clone();
  public int GemAt(int cell)=>cell>=0&&cell<64?board[cell]:-1;
  static bool Adjacent(int a,int b)=>a>=0&&b>=0&&a<64&&b<64&&((a/8==b/8&&Math.Abs(a-b)==1)||Math.Abs(a-b)==8);
  public Match3Move TrySwap(int from,int to){
   var steps=new List<Match3Step>();
   if(Outcome!=Match3Outcome.Playing||!Adjacent(from,to))return new Match3Move(false,false,steps);
   int t=board[from];board[from]=board[to];board[to]=t;
   steps.Add(new Match3Step(Match3StepType.Swap,board,from:from,to:to));
   var matched=Match3Rules.Matches(board);
   if(matched.Length==0){t=board[from];board[from]=board[to];board[to]=t;steps.Add(new Match3Step(Match3StepType.Rebound,board,from:from,to:to));return new Match3Move(true,false,steps);}
   MovesRemaining--;
   int chain=0;
   while(matched.Length>0){
    chain++;
    foreach(int cell in matched){if(board[cell]==targetGem)Collected++;board[cell]=-1;}
    Score+=matched.Length*100*chain;
    steps.Add(new Match3Step(Match3StepType.Clear,board,matched,chain:chain));
    var source=new int[64];for(int i=0;i<64;i++)source[i]=-1;
    for(int x=0;x<8;x++){
     int write=7;
     for(int y=7;y>=0;y--){int read=y*8+x;if(board[read]<0)continue;int dest=write*8+x;board[dest]=board[read];source[dest]=read;write--;}
     while(write>=0){board[write*8+x]=rng.Range(6);write--;}
    }
    steps.Add(new Match3Step(Match3StepType.Fall,board,sourceForCell:source,chain:chain));
    matched=Match3Rules.Matches(board);
    if(chain>128)throw new InvalidOperationException("Cascade safety limit reached");
   }
   if(!Match3Rules.HasLegalMove(board)){
    board=Match3Rules.ReshufflePlayable(board,rng.Next());
    steps.Add(new Match3Step(Match3StepType.Shuffle,board));
   }
   if(Collected>=targetCount)Outcome=Match3Outcome.Won;
   else if(MovesRemaining==0)Outcome=Match3Outcome.Lost;
   if(Outcome!=Match3Outcome.Playing)steps.Add(new Match3Step(Match3StepType.Finish,board));
   return new Match3Move(true,true,steps);
  }
 }
}
