using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    /// <summary>
    /// 一条有序绘制命令。geometry/texture 是原生资源句柄，不是可解引用的指针。
    /// 末尾带显式 4 字节 reserved 对齐填充，与 Native~/urui.h 中的 URUICommand 完全一致。
    /// </summary>
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
        public int reserved;
    }
}
