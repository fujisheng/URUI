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
            Vector3[] positions; Vector2[] uv; Color32[] colors; int[] indices;
            if (meshes.TryGetValue(id, out var mesh) && mesh.positions != null)
            { positions = mesh.positions; uv = mesh.uv; colors = mesh.colors; indices = mesh.indices; }
            else
            {
                if (NativeMethods.urui_get_geometry_info(id, out var vertices, out var count) != 1 || vertices < 1 || count < 1)
                { throw new InvalidOperationException("效果的原始几何已失效。"); }
                var source = new VertexNative[vertices]; indices = new int[count];
                if (NativeMethods.urui_copy_geometry(id, source, vertices, indices, count) != 1)
                { throw new InvalidOperationException("无法冻结效果几何。"); }
                positions = new Vector3[vertices]; uv = new Vector2[vertices]; colors = new Color32[vertices];
                for (var index = 0; index < vertices; index++)
                {
                    var vertex = source[index]; positions[index] = new Vector3(vertex.x, vertex.y, 0);
                    uv[index] = new Vector2(vertex.u, vertex.v); colors[index] = new Color32(vertex.r, vertex.g, vertex.b, vertex.a);
                }
            }
            cached = new EffectGeometry(id, positions, uv, colors, indices); effectGeometryCache.Add(id, cached); return cached;
        }
        void PruneEffectGeometries()
        {
            removedEffectGeometries.Clear();
            foreach (var id in effectGeometryCache.Keys) { if (!usedEffectGeometries.Contains(id)) { removedEffectGeometries.Add(id); } }
            foreach (var id in removedEffectGeometries) { effectGeometryCache.Remove(id); }
        }
    }
}
