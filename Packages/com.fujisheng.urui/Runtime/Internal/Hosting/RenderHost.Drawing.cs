using System;
using UnityEngine;
using URUI.Internal.Interop;
using UnityEngine.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        bool DrawFrame(ref PhaseFrame phase)
        {
            if(!RestoreSceneCamera())return false;
            // 原生pass的上下文归实际SRP回调所有，不能在LateUpdate录制后跨回调重放。
            // 这里只失效记录；真实提交统一完成准备和录制，不再额外录制一份旧后端命令。
            if(!recordingForContext&&CanUseColorPass())
            {geometryRecordingPending=true;return true;}
            var completed=false;
            effectSubmitted.Clear();
            OwnerSurface.BeginDispatch();
            try{DrawFrameCore(ref phase);geometryRecordingPending=false;completed=true;}
            finally
            {
                if(!completed)InvalidateRecording(true);
                OwnerSurface.EndDispatch();OwnerSurface.FinishNativeCall();
            }
            return true;
        }

        Mesh UploadMesh(string name,Vector3[] positions,Vector2[] uv,Color32[] colors,int[] indices,int vertices,int count,Vector2[] sourceSlots)
        {
            var mesh=new Mesh{name=name,indexFormat=vertices>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(positions,0,vertices);mesh.SetUVs(0,uv,0,vertices);mesh.SetColors(colors,0,vertices);
            mesh.SetUVs(1,sourceSlots,0,vertices);
            mesh.SetIndices(indices,0,count,MeshTopology.Triangles,0,true);
            GeometryUploadBytes += (long)vertices * 32 + (long)count * sizeof(int);
            return mesh;
        }
        unsafe MeshCache ReadGeometry(ulong id)
        {
            if(meshes.TryGetValue(id,out var existing))return existing;
            var view = planInputLease.Geometry(id);
            var vertices = checked((int)view.VertexCount); var indices = checked((int)view.IndexCount);
            if(vertices<=0||indices<=0)return null;
            MeshCache value=null;
            for(var index=idleGeometry.Count-1;index>=0;index--)
                if(idleGeometry[index].positions.Length>=vertices&&idleGeometry[index].indices.Length>=indices)
                {
                    value=idleGeometry[index];idleGeometry.RemoveAt(index);pooledGeometryBytes-=value.CapacityBytes;break;
                }
            value??=new MeshCache{positions=new Vector3[vertices],uv=new Vector2[vertices],colors=new Color32[vertices],indices=new int[indices]};
            var sourceVertices = new ReadOnlySpan<RenderPlanVertex>((void*)view.Vertices, vertices);
            new ReadOnlySpan<int>((void*)view.Indices, indices).CopyTo(value.indices);
            value.vertexCount=vertices;value.indexCount=indices;
            var minimum=new Vector3(float.PositiveInfinity,float.PositiveInfinity,0);
            var maximum=new Vector3(float.NegativeInfinity,float.NegativeInfinity,0);
            for(var index=0;index<vertices;index++)
            {
                var source=sourceVertices[index];var position=source.Position;
                value.positions[index]=position;value.uv[index]=source.Uv;
                value.colors[index]=source.Color;
                minimum=Vector3.Min(minimum,position);maximum=Vector3.Max(maximum,position);
            }
            value.bounds=new Bounds((minimum+maximum)*.5f,maximum-minimum);
            meshes.Add(id,value);return value;
        }

        void RecycleGeometry(MeshCache value)
        {
            if(value.mesh)DestroyRenderObject(value.mesh);value.mesh=null;
            // 只复用私有CPU数组；已进入GPU命令的Mesh与公开效果几何均不改写。
            if(value.Shared)return;
            if(idleGeometry.Count>=2048||pooledGeometryBytes+value.CapacityBytes>16*1024*1024)return;
            idleGeometry.Add(value);pooledGeometryBytes+=value.CapacityBytes;
        }
        Mesh SourceMesh(MeshCache value)
        {
            if(!value.mesh)
            {
                if(zeroTextureSlots.Length<value.vertexCount)zeroTextureSlots=new Vector2[Math.Max(value.vertexCount,zeroTextureSlots.Length*2+16)];
                value.mesh=UploadMesh("URUI geometry",value.positions,value.uv,value.colors,value.indices,value.vertexCount,value.indexCount,zeroTextureSlots);
            }
            return value.mesh;
        }
    }
}
