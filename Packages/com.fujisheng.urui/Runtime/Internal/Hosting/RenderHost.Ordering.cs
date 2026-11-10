using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Meshes;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        readonly List<BatchItem> pendingGeometry=new(128);
        readonly List<SegmentPlan> segmentPlans=new();
        int segmentCursor;

        readonly struct BatchState:IEquatable<BatchState>
        {
            readonly ulong texture;
            readonly int flags,x,y,width,height;
            readonly Matrix4x4 transform;
            internal BatchState(BatchItem item)
            {
                var command=item.command;texture=command.texture;flags=command.flags;
                x=command.clip_x;y=command.clip_y;width=command.clip_w;height=command.clip_h;transform=item.transform;
            }
            public bool Equals(BatchState other)=>texture==other.texture&&flags==other.flags&&x==other.x&&y==other.y&&
                width==other.width&&height==other.height&&transform.Equals(other.transform);
            public override bool Equals(object other)=>other is BatchState state&&Equals(state);
            public override int GetHashCode()=>HashCode.Combine(texture,flags,x,y,width,height,transform);
        }
        readonly struct PlanKey:IEquatable<PlanKey>
        {
            internal readonly BatchState State;
            readonly float x0,y0,x1,y1;
            internal PlanKey(BatchItem item){State=new BatchState(item);x0=item.x0;y0=item.y0;x1=item.x1;y1=item.y1;}
            public bool Equals(PlanKey other)=>State.Equals(other.State)&&x0==other.x0&&y0==other.y0&&x1==other.x1&&y1==other.y1;
        }
        sealed class SegmentPlan:IComparer<int>
        {
            struct Link { internal int Item,Next; }
            PlanKey[] keys=Array.Empty<PlanKey>();
            int[] depths=Array.Empty<int>(),states=Array.Empty<int>(),visits=Array.Empty<int>();
            internal int[] Order=Array.Empty<int>();
            int count,epoch;
            readonly Dictionary<BatchState,int> stateIds=new();
            readonly Dictionary<long,int> cells=new();
            readonly List<Link> links=new();
            readonly List<int> large=new();
            internal void Prepare(List<BatchItem> items)
            {
                var same=count==items.Count;
                for(var index=0;same&&index<count;index++)same=keys[index].Equals(new PlanKey(items[index]));
                if(same)return;
                count=items.Count;
                if(keys.Length<count)
                {
                    var capacity=Math.Max(count,keys.Length*2+16);
                    keys=new PlanKey[capacity];depths=new int[capacity];states=new int[capacity];visits=new int[capacity];Order=new int[capacity];
                }
                stateIds.Clear();cells.Clear();links.Clear();large.Clear();
                for(var index=0;index<count;index++)
                {
                    keys[index]=new PlanKey(items[index]);Order[index]=index;depths[index]=0;
                    var state=keys[index].State;
                    if(!stateIds.TryGetValue(state,out var id)){id=stateIds.Count;stateIds.Add(state,id);}
                    states[index]=id;
                }
                // 同一状态可以直接保持三角形顺序，无需构造空间依赖。
                if(stateIds.Count==1)return;
                var comparisons=0;
                for(var index=0;index<count;index++)
                {
                    if(epoch==int.MaxValue){Array.Clear(visits,0,visits.Length);epoch=0;}
                    epoch++;
                    var item=items[index];
                    var small=CellBounds(item,out var x0,out var y0,out var x1,out var y1);
                    if(!small)
                    {
                        for(var prior=0;prior<index;prior++)
                            if(!Dependency(items,index,prior,ref comparisons))return;
                    }
                    else
                    {
                        for(var y=y0;y<=y1;y++)for(var x=x0;x<=x1;x++)
                            if(cells.TryGetValue(Cell(x,y),out var link))
                                while(link>=0)
                                {
                                    var entry=links[link];
                                    if(visits[entry.Item]!=epoch)
                                    {
                                        visits[entry.Item]=epoch;
                                        if(!Dependency(items,index,entry.Item,ref comparisons))return;
                                    }
                                    link=entry.Next;
                                }
                        foreach(var prior in large)if(!Dependency(items,index,prior,ref comparisons))return;
                    }
                    if(!small){large.Add(index);continue;}
                    var needed=(x1-x0+1)*(y1-y0+1);
                    if(links.Count+needed>16384)return;
                    for(var y=y0;y<=y1;y++)for(var x=x0;x<=x1;x++)
                    {
                        var cell=Cell(x,y);var next=cells.TryGetValue(cell,out var head)?head:-1;
                        cells[cell]=links.Count;links.Add(new Link{Item=index,Next=next});
                    }
                }
                // 先满足所有相交图形的依赖，再按状态分组；同状态、同深度保持原顺序。
                Array.Sort(Order,0,count,this);
            }
            bool Dependency(List<BatchItem> items,int index,int prior,ref int comparisons)
            {
                // 复杂画面达到预算时沿用原顺序，不能为降低提交数改变透明混合。
                if(++comparisons>262144)return false;
                var a=items[index];var b=items[prior];
                if(!BatchRules.Disjoint(a.x0,a.y0,a.x1,a.y1,b.x0,b.y0,b.x1,b.y1))
                    depths[index]=Math.Max(depths[index],depths[prior]+(states[index]==states[prior]?0:1));
                return true;
            }
            static long Cell(int x,int y)=>((long)x<<32)|(uint)y;
            static bool CellBounds(BatchItem item,out int x0,out int y0,out int x1,out int y1)
            {
                x0=y0=x1=y1=0;
                if(!float.IsFinite(item.x0)||!float.IsFinite(item.y0)||!float.IsFinite(item.x1)||!float.IsFinite(item.y1)||
                    Math.Abs(item.x0)>1e7f||Math.Abs(item.y0)>1e7f||Math.Abs(item.x1)>1e7f||Math.Abs(item.y1)>1e7f)return false;
                x0=Mathf.FloorToInt(item.x0/32);y0=Mathf.FloorToInt(item.y0/32);
                x1=Mathf.FloorToInt(item.x1/32);y1=Mathf.FloorToInt(item.y1/32);
                return (long)(x1-x0+1)*(y1-y0+1)<=64;
            }
            public int Compare(int a,int b)
            {
                var order=depths[a].CompareTo(depths[b]);
                if(order==0)order=states[a].CompareTo(states[b]);
                return order!=0?order:a.CompareTo(b);
            }
        }
        void FlushGroup(ref PhaseFrame phase)
        {
            if(pendingGeometry.Count==0)return;
            using var profile = batchPhase.Auto();
            if(segmentCursor==segmentPlans.Count)segmentPlans.Add(new SegmentPlan());
            var plan=segmentPlans[segmentCursor++];plan.Prepare(pendingGeometry);
            for(var index=0;index<pendingGeometry.Count;index++)
            {
                var item=pendingGeometry[plan.Order[index]];
                if(!CanJoin(item,ref phase))SubmitBatch(ref phase);
                AddToBatch(item);
            }
            SubmitBatch(ref phase);pendingGeometry.Clear();
        }
    }
}
