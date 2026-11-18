using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Rendering.Effects;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        readonly Dictionary<ulong, EffectGeometry> effectGeometryCache = new();
        readonly HashSet<ulong> usedEffectGeometries = new();
        readonly List<ulong> removedEffectGeometries = new();
        EffectGeometry FreezeEffectGeometry(ulong id)
        {
            usedEffectGeometries.Add(id);
            if (effectGeometryCache.TryGetValue(id, out var cached)) { return cached; }
            var mesh = ReadGeometry(id) ?? throw new InvalidOperationException("效果源几何已失效。");
            var positions=mesh.positions; var uv=mesh.uv; var colors=mesh.colors; var indices=mesh.indices;
            if(positions.Length!=mesh.vertexCount||indices.Length!=mesh.indexCount)
            {
                positions=positions.AsSpan(0,mesh.vertexCount).ToArray();uv=uv.AsSpan(0,mesh.vertexCount).ToArray();
                colors=colors.AsSpan(0,mesh.vertexCount).ToArray();indices=indices.AsSpan(0,mesh.indexCount).ToArray();
            }
            else {mesh.Shared=true;}
            cached = new EffectGeometry(id, positions, uv, colors, indices); effectGeometryCache.Add(id, cached); return cached;
        }
        VertexNative[] originalSourceScratch=Array.Empty<VertexNative>();
        EffectGeometry FreezeOriginalEffectGeometry(ulong id)
        {
            usedEffectGeometries.Add(id);
            if(effectGeometryCache.TryGetValue(id,out var previous))return previous;
            if(NativeMethods.urui_get_geometry_info(id,out var vertices,out var indices)!=1||vertices<=0||indices<=0)throw new InvalidOperationException("程序原始几何已失效。");
            if(originalSourceScratch.Length<vertices)originalSourceScratch=new VertexNative[vertices];
            var positions=new Vector3[vertices];var uv=new Vector2[vertices];var colors=new Color32[vertices];var triangles=new int[indices];
            if(NativeMethods.urui_copy_geometry(id,originalSourceScratch,originalSourceScratch.Length,triangles,triangles.Length)!=1)throw new InvalidOperationException("程序原始几何复制失败。");
            for(var i=0;i<vertices;i++){var vertex=originalSourceScratch[i];positions[i]=new Vector3(vertex.x,vertex.y,0);uv[i]=new Vector2(vertex.u,vertex.v);colors[i]=new Color32(vertex.r,vertex.g,vertex.b,vertex.a);}
            var result=new EffectGeometry(id,positions,uv,colors,triangles);effectGeometryCache.Add(id,result);return result;
        }
        void PruneEffectGeometries()
        {
            removedEffectGeometries.Clear();
            foreach (var id in effectGeometryCache.Keys) { if (!usedEffectGeometries.Contains(id)) { removedEffectGeometries.Add(id); } }
            foreach (var id in removedEffectGeometries) { effectGeometryCache.Remove(id); }
        }
    }
}
