using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Rendering.Parameters;

namespace URUI.Internal.Rendering.Frames
{
    // 所属类型的内部状态记录；保留嵌套访问边界与原有资源引用关系。
    internal sealed partial class NativeFrameRoot
    {
        sealed class MeshEntry
        {
            internal ulong id;
            internal Mesh mesh;
            internal int refs;
            internal long bytes;
        }

        sealed class TextureEntry
        {
            internal (ulong, ulong) id;
            internal Texture2D texture;
            internal bool owned = true;
            internal int refs;
            internal long bytes;
        }

        sealed class DrawBinding
        {
            internal NativePrepared.Draw state;
            internal MeshEntry mesh;
            internal Texture texture;
        }

        sealed class Plan
        {
            internal ulong epoch;
            internal ulong id;
            internal readonly List<DrawBinding> draws = new List<DrawBinding>();
            internal readonly List<TextureEntry> textures = new List<TextureEntry>();
            internal readonly List<Mesh> failedMeshes = new List<Mesh>(1);
        }

        sealed class Retirement
        {
            internal Plan plan;
            internal GraphicsBuffer buffer;
            internal CommandBuffer barrier;
            internal GraphicsFence fence;
            internal bool uncertain;
        }
    }
}
