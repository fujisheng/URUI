using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    /// <summary>跨 ABI 复制的紧凑顶点；颜色为预乘 alpha，字段顺序与原生顶点布局一致。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VertexNative
    {
        public float x;
        public float y;
        public float u;
        public float v;
        public byte r;
        public byte g;
        public byte b;
        public byte a;
    }
}
