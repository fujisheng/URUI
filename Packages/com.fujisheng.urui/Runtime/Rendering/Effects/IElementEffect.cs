using System;
namespace URUI.Rendering.Effects
{
    /// <summary>Prepare 可更新资源；Record 只录制现有资源，不访问或修改 DOM。</summary>
    /// <summary>可选的 Prepare 结果：节点本地坐标中的实际绘制范围，供子树滤镜/遮罩分配区域。</summary>
    public interface IEffectBounds {UnityEngine.Rect InkBounds {get;}}
    public interface IElementEffect : IDisposable
    {
        void Prepare(EffectFrame frame);
        void Record(EffectRenderContext context);
    }
}
