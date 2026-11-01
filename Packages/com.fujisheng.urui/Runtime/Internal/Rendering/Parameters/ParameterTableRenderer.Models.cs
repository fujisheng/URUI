using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering.Parameters
{
    // 所属类型的内部状态记录；保留嵌套访问边界与原有资源引用关系。
    internal sealed partial class ParameterTableRenderer
    {
        // 空载体保留内部调用签名；统计字段只存在于 URUI_DEBUG 构建。
        internal partial struct Stats
        {
        }

        internal struct Ticket
        {
            internal Packet packet;
            internal ulong serial;
        }

        internal enum Result
        {
            Success,
            Unsupported,
            InvalidResource,
            Backpressure,
            Disposed
        }

        internal enum State
        {
            Free,
            Recorded,
            Submitted,
            Uncertain
        }

        internal sealed class Packet
        {
            internal CommandBuffer commands = new CommandBuffer
            {
                name = "URUI parameter prototype"
            };
            internal GraphicsBuffer parameters;
            internal int capacity;
            internal int lastCount;
            internal AffineParameter[] last = Array.Empty<AffineParameter>();
            internal bool hasData;
            internal ulong serial;
            internal Plan plan;
            internal GraphicsFence fence;
            internal State state;
#if URUI_DEBUG
            internal Stats stats;
#endif
        }

        internal sealed class Plan
        {
            internal ParameterTableRenderer owner;
            internal ulong epoch;
            internal ulong resources;
            internal long textureBytes;
            internal long geometryBytes;
            internal long combinedBytes;
            internal List<SharedBatch> sharedBatches;
            internal List<SharedMesh> sharedMeshes;
            internal List<SharedTexture> sharedTextures;
            internal List<NativeMesh> nativeMeshes;
            internal ParameterKey[] keys;
            internal int references;
            internal bool retired;
            internal readonly Dictionary<ulong, Mesh> source = new Dictionary<ulong, Mesh>();
            internal readonly Dictionary<ulong, Texture2D> textures = new Dictionary<ulong, Texture2D>();
            internal readonly List<Batch> batches = new List<Batch>();
        }

        internal sealed class Batch
        {
            internal Mesh mesh;
            internal bool parameter;
            internal int slot;
            internal int count;
            internal ParameterKey key;
            internal SharedBatch shared;
            internal NativeMesh nativeShared;
        }

        internal sealed class SharedMesh
        {
            internal ParameterTableRenderer rendererOwner;
            internal (ulong epoch, ulong id) key;
            internal Mesh value;
            internal bool gpuCreated;
            internal int references;
            internal int vertices;
            internal int indices;
            internal Vector3[] positions;
            internal Vector2[] uv;
            internal Color32[] colors;
            internal int[] topology;
        }

        internal sealed class SharedTexture
        {
            internal (ulong epoch, ulong id) key;
            internal Texture2D value;
            internal int references;
            internal int bytes;
        }
    }
}
