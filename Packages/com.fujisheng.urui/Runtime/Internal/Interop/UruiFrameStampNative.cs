using System.Runtime.InteropServices;

namespace URUI.Internal.Interop
{
    /// <summary>
    /// 原生计划的精确版本戳。epoch 隔离文档生命周期，commands/resources 共同决定快照是否仍有效。
    /// retained_bytes/compare_bytes 保留 C ABI 布局；关闭调试时不会收集比较开销。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UruiFrameStampNative
    {
        public ulong epoch;
        public ulong commands;
        public ulong resources;
        public ulong retained_bytes;
        public ulong compare_bytes;
        public uint count;
        public uint flags;
    }
}
