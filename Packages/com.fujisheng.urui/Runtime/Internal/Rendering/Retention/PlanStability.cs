using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Retention
{
    /// <summary>
    /// 同一原生计划连续出现后才允许捕获 Unity 状态，减少变化帧上的无效工作。
    /// 文档 epoch 或任一版本变化都会重新观察；不缓存原生元素指针。
    /// </summary>
    internal struct PlanStability
    {
        bool seen;
        UruiFrameStampNative previous;

        public static bool Valid(UruiFrameStampNative s) => s.flags == 1 && s.count <= int.MaxValue && s.epoch != 0 && s.commands != 0 && s.resources != 0;
        public static bool Same(UruiFrameStampNative a, UruiFrameStampNative b) => a.epoch == b.epoch && a.commands == b.commands && a.resources == b.resources && a.count == b.count;

        public bool Observe(UruiFrameStampNative current)
        {
            bool valid = Valid(current);
            bool repeated = valid && seen && Same(previous, current);
            seen = valid;
            previous = current;
            return repeated;
        }

        public void Reset()
        {
            seen = false;
            previous = default;
        }
    }
}
