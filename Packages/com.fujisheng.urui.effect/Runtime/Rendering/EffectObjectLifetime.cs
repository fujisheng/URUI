using UnityEngine;
namespace URUI.Effect.Rendering
{
    internal static class EffectObjectLifetime
    {
        // 只释放自有对象；GPU 对象须先由 EffectContext/GpuRetirement 确认退役。
        internal static void Destroy(Object value)
        {
            if (!value)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}
