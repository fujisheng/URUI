using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    // 所属类型的内部状态记录；保留嵌套访问边界与原有资源引用关系。
    internal sealed partial class RenderHost
    {
        struct TextureDependency
        {
            public Texture texture;
            public uint update;
            public int width;
            public int height;
            public FilterMode filter;
            public TextureWrapMode u;
            public TextureWrapMode v;
            public TextureWrapMode w;
            public float bias;
            public int aniso;
        }

        sealed class MeshCache
        {
            public Mesh mesh;
            public bool Shared;
            public Vector3[] positions;
            public Vector2[] uv;
            public Color32[] colors;
            public int[] indices;
            public int vertexCount, indexCount;
            public Bounds bounds;
            public int CapacityBytes => positions.Length * 24 + indices.Length * 4;
        }

    }
}
