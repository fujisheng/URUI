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
            if (!RestoreSceneCamera())
            {
                return false;
            }
            var completed = false;
            effectSubmitted.Clear();
            OwnerSurface.BeginDispatch();
            try
            {
                CommitAnimationCompletionClasses();
                DrawFrameCore(ref phase);
                geometryRecordingPending = false;
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    InvalidateRecording(true);
                }
                OwnerSurface.EndDispatch();
                OwnerSurface.FinishNativeCall();
            }
            return true;
        }

        void CommitAnimationCompletionClasses()
        {
            var changed = false;
            foreach (var document in OwnerSurface.DocumentsSnapshot())
            {
                if (document)
                {
                    changed |= document.CommitAnimationCompletionClasses();
                }
            }
            if (!changed)
            {
                return;
            }
            // 原生动画轨道已结束。录制前同步终态类，避免先绘制退场前的样式。
            using (nativeUpdatePhase.Auto())
            {
                NativeMethods.urui_surface_update(SurfaceHandle);
            }
            OwnerSurface.RefreshDocuments(true);
        }

        Mesh UploadMesh(string name, Vector3[] positions, Vector2[] uv, Color32[] colors, int[] indices, int vertices, int count, Vector2[] sourceSlots)
        {
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(positions, 0, vertices);
            mesh.SetUVs(0, uv, 0, vertices);
            mesh.SetColors(colors, 0, vertices);
            mesh.SetUVs(1, sourceSlots, 0, vertices);
            mesh.SetIndices(indices, 0, count, MeshTopology.Triangles, 0, true);
            GeometryUploadBytes += (long)vertices * 32 + (long)count * sizeof(int);
            return mesh;
        }

        unsafe MeshCache ReadGeometry(ulong id)
        {
            if (meshes.TryGetValue(id, out var existing))
            {
                return existing;
            }
            var view = planInputLease.Geometry(id);
            var vertices = checked((int)view.VertexCount);
            var indices = checked((int)view.IndexCount);
            if (vertices <= 0 || indices <= 0)
            {
                return null;
            }
            MeshCache value = null;
            for (var index = idleGeometry.Count - 1; index >= 0; index--)
            {
                if (idleGeometry[index].positions.Length >= vertices && idleGeometry[index].indices.Length >= indices)
                {
                    value = idleGeometry[index];
                    idleGeometry.RemoveAt(index);
                    pooledGeometryBytes -= value.CapacityBytes;
                    break;
                }
            }
            value ??= new MeshCache
            {
                positions = new Vector3[vertices],
                uv = new Vector2[vertices],
                colors = new Color32[vertices],
                indices = new int[indices]
            };
            var sourceVertices = new ReadOnlySpan<RenderPlanVertex>((void*)view.Vertices, vertices);
            new ReadOnlySpan<int>((void*)view.Indices, indices).CopyTo(value.indices);
            value.vertexCount = vertices;
            value.indexCount = indices;
            for (var index = 0; index < vertices; index++)
            {
                var source = sourceVertices[index];
                value.positions[index] = source.Position;
                value.uv[index] = source.Uv;
                value.colors[index] = source.Color;
            }
            meshes.Add(id, value);
            return value;
        }

        void RecycleGeometry(MeshCache value)
        {
            if (value.mesh)
            {
                DestroyRenderObject(value.mesh);
            }
            value.mesh = null;
            // 只复用私有CPU数组；已进入GPU命令的Mesh与公开效果几何均不改写。
            if (value.Shared)
            {
                return;
            }
            if (idleGeometry.Count >= 2048 || pooledGeometryBytes + value.CapacityBytes > 16 * 1024 * 1024)
            {
                return;
            }
            idleGeometry.Add(value);
            pooledGeometryBytes += value.CapacityBytes;
        }

        Mesh SourceMesh(MeshCache value)
        {
            if (!value.mesh)
            {
                if (zeroTextureSlots.Length < value.vertexCount)
                {
                    zeroTextureSlots = new Vector2[Math.Max(value.vertexCount, zeroTextureSlots.Length * 2 + 16)];
                }
                value.mesh = UploadMesh("URUI geometry", value.positions, value.uv, value.colors, value.indices, value.vertexCount, value.indexCount, zeroTextureSlots);
            }
            return value.mesh;
        }
    }
}
