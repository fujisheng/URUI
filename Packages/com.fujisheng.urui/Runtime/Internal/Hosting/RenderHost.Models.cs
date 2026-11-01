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
            public Vector3[] positions;
            public Vector2[] uv;
            public Color32[] colors;
            public int[] indices;
            public int lastFrame;
        }

        struct BatchItem
        {
            public CommandNative command;
            public MeshCache geometry;
            public Matrix4x4 transform;
            public bool transformed;
            public float x0;
            public float y0;
            public float x1;
            public float y1;
        }

        sealed class BatchCache
        {
            public Mesh mesh;
            public BatchItem[] items;
        }
    }
}
