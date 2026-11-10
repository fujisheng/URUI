using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    /// <summary>一条有序绘制命令。geometry/texture 是原生资源句柄，不是可解引用的指针。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandNative
    {
        public ulong geometry;
        public ulong texture;
        public float tx;
        public float ty;
        public int flags;
        public bool ClipEnabled => (flags & 1) != 0;
        public int TextureSampling => (flags >> 1) & 3;
        public int clip_x;
        public int clip_y;
        public int clip_w;
        public int clip_h;
    }
}
