using Coffee.UIEffects;
using UnityEngine;

namespace URUI.Effect.Reference
{
    /// <summary>测试专用：只替换时钟输入，全部参数、网格和算法仍由锁定源插件执行。</summary>
    public sealed class TimedReferenceEffect : UIEffect
    {
        public float ReferenceTime;
        public override void ApplyContextToMaterial(Material material)
        {
            base.ApplyContextToMaterial(material);
            context.SetEnablePreview(true, material);
            material.SetVector("_ReferenceTime", new Vector4(ReferenceTime / 20, ReferenceTime, ReferenceTime * 2, ReferenceTime * 3));
        }
    }
}
