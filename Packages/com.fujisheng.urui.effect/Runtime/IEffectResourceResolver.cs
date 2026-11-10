using UnityEngine;
using URUI.Resources;
using URUI.Styles;

namespace URUI.Effect
{
    /// <summary>调用者提供的图像与粒子资源入口；效果包不查找场景对象或游戏目录。</summary>
    public interface IEffectResourceResolver
    {
        ImageLease AcquireImage(ComputedImageValue image);
        GameObject ResolveParticles(string source);
    }
}
