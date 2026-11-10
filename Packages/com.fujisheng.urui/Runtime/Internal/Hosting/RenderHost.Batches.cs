using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;
using URUI.Internal.Rendering.Meshes;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        int batchVertexCount,batchIndexCount;
        const int TextureBindingBudget=8;
        readonly ulong[] batchTextures=new ulong[TextureBindingBudget];
        int batchTextureCount;
        void ClearFrameScratch(){frameUsedGeometry.Clear();frameStaleGeometry.Clear();}
        void ClearBatches()
        {
            foreach(var batch in batches)if(batch.mesh)Destroy(batch.mesh);
            batches.Clear();batchGroup.Clear();batchVertexCount=batchIndexCount=batchTextureCount=0;
            pendingGeometry.Clear();segmentPlans.Clear();
        }
        static bool SameCommand(CommandNative a,CommandNative b)=>a.geometry==b.geometry&&a.texture==b.texture&&a.tx==b.tx&&a.ty==b.ty&&
            a.flags==b.flags&&a.clip_x==b.clip_x&&a.clip_y==b.clip_y&&a.clip_w==b.clip_w&&a.clip_h==b.clip_h;
        bool CanJoin(BatchItem item,ref PhaseFrame phase)
        {
            if(batchGroup.Count==0)return true;
            var first=batchGroup[0];var a=first.command;var b=item.command;
            if(a.flags!=b.flags||a.clip_x!=b.clip_x||a.clip_y!=b.clip_y||a.clip_w!=b.clip_w||a.clip_h!=b.clip_h||
                !BatchRules.EquivalentAffine2D(first.transform,item.transform)||batchVertexCount+item.geometry.vertexCount>100000||batchIndexCount+item.geometry.indexCount>300000)return false;
            if(a.texture!=b.texture&&(LoadTexture(a.texture) is RenderTexture)!=(LoadTexture(b.texture) is RenderTexture))return false;
            return TextureSlot(b.texture)>=0||batchTextureCount<TextureBindingBudget;
        }
        int TextureSlot(ulong texture)
        {
            for(var index=0;index<batchTextureCount;index++)if(batchTextures[index]==texture)return index;
            return -1;
        }
        void AddToBatch(BatchItem item)
        {
            if(TextureSlot(item.command.texture)<0)batchTextures[batchTextureCount++]=item.command.texture;
            batchGroup.Add(item);batchVertexCount+=item.geometry.vertexCount;batchIndexCount+=item.geometry.indexCount;
        }
        static void SetDrawBounds(ref BatchItem item)
        {
            var bounds=item.geometry.bounds;var offset=new Vector3(item.command.tx,item.command.ty,0);
            var a=item.transform.MultiplyPoint3x4(new Vector3(bounds.min.x,bounds.min.y,0)+offset);
            var b=item.transform.MultiplyPoint3x4(new Vector3(bounds.max.x,bounds.min.y,0)+offset);
            var c=item.transform.MultiplyPoint3x4(new Vector3(bounds.min.x,bounds.max.y,0)+offset);
            var d=item.transform.MultiplyPoint3x4(new Vector3(bounds.max.x,bounds.max.y,0)+offset);
            item.x0=Mathf.Min(Mathf.Min(a.x,b.x),Mathf.Min(c.x,d.x));item.x1=Mathf.Max(Mathf.Max(a.x,b.x),Mathf.Max(c.x,d.x));
            item.y0=Mathf.Min(Mathf.Min(a.y,b.y),Mathf.Min(c.y,d.y));item.y1=Mathf.Max(Mathf.Max(a.y,b.y),Mathf.Max(c.y,d.y));
        }
        bool FullyClipped(BatchItem item)
        {
            // 滤镜源图层可延伸到主视口外；离屏源仍可能投射屏内阴影或光晕。
            var region=Region(activeLayer);
            if(item.x1<region.xMin||item.y1<region.yMin||item.x0>region.xMax||item.y0>region.yMax)return true;
            var clip=item.command;
            return clip.ClipEnabled&&(clip.clip_w<=0||clip.clip_h<=0||item.x1<clip.clip_x||item.x0>clip.clip_x+clip.clip_w||
                item.y1<clip.clip_y||item.y0>clip.clip_y+clip.clip_h);
        }
        static Mesh UploadMesh(string name,Vector3[] positions,Vector2[] uv,Color32[] colors,int[] indices,int vertices,int count,Vector2[] sourceSlots)
        {
            var mesh=new Mesh{name=name,indexFormat=vertices>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(positions,0,vertices);mesh.SetUVs(0,uv,0,vertices);mesh.SetColors(colors,0,vertices);
            mesh.SetUVs(1,sourceSlots,0,vertices);
            mesh.SetIndices(indices,0,count,MeshTopology.Triangles,0,true);
            return mesh;
        }
        void SubmitBatch(ref PhaseFrame phase)
        {
            if(batchGroup.Count==0)return;
            if(batchGroup.Count==1)
            {
                DrawOne(batchGroup[0],ref phase);batchGroup.Clear();batchVertexCount=batchIndexCount=batchTextureCount=0;return;
            }
            if(batchCursor==batches.Count)batches.Add(new BatchCache());
            var cached=batches[batchCursor++];
            var unchanged=cached.mesh&&cached.count==batchGroup.Count;
            for(var index=0;unchanged&&index<batchGroup.Count;index++)unchanged=SameCommand(cached.items[index],batchGroup[index].command);
            if(!unchanged)
            {
                if(cached.positions.Length<batchVertexCount)
                {
                    var capacity=Math.Max(batchVertexCount,cached.positions.Length*2+16);
                    cached.positions=new Vector3[capacity];cached.uv=new Vector2[capacity];cached.colors=new Color32[capacity];cached.sourceSlots=new Vector2[capacity];
                }
                if(cached.indices.Length<batchIndexCount)cached.indices=new int[Math.Max(batchIndexCount,cached.indices.Length*2+24)];
                if(cached.items.Length<batchGroup.Count)cached.items=new CommandNative[Math.Max(batchGroup.Count,cached.items.Length*2+16)];
                var vertex=0;var triangle=0;
                foreach(var item in batchGroup)
                {
                    var geometry=item.geometry;var command=item.command;
                    var slot=TextureSlot(command.texture);
                    if(slot<0)throw new InvalidOperationException("URUI 批次缺少纹理绑定。");
                    for(var index=0;index<geometry.vertexCount;index++)
                    {
                        var point=geometry.positions[index];point.x+=command.tx;point.y+=command.ty;
                        cached.positions[vertex+index]=point;cached.uv[vertex+index]=geometry.uv[index];cached.colors[vertex+index]=geometry.colors[index];
                        cached.sourceSlots[vertex+index]=new Vector2(slot,0);
                    }
                    for(var index=0;index<geometry.indexCount;index++)cached.indices[triangle+index]=vertex+geometry.indices[index];
                    vertex+=geometry.vertexCount;triangle+=geometry.indexCount;
                }
                // 每个GPU版本保持不可变；更新只复用CPU staging，避免改写已排队但尚未Submit的Mesh。
                var replacement=UploadMesh("URUI batch",cached.positions,cached.uv,cached.colors,cached.indices,batchVertexCount,batchIndexCount,cached.sourceSlots);
                if(cached.mesh)Destroy(cached.mesh);cached.mesh=replacement;
                for(var index=0;index<batchGroup.Count;index++)cached.items[index]=batchGroup[index].command;
                cached.count=batchGroup.Count;
            }
            DrawBatch(cached,ref phase);batchGroup.Clear();batchVertexCount=batchIndexCount=batchTextureCount=0;
        }
    }
}
