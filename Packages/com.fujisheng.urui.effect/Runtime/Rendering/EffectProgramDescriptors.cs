using UnityEngine.Rendering;
using URUI.Rendering;

namespace URUI.Effect.Rendering
{
    internal static class EffectProgramDescriptors
    {
        static RenderProgramDescriptor Active(RenderSampling sampling, RenderGeometry geometry)
            => new RenderProgramDescriptor(RenderTextureEncoding.TextureDeclared, RenderColorSpace.Active,
                RenderColorSpace.Active, RenderAlpha.Premultiplied, RenderBlendDomain.Active,
                BlendMode.One, BlendMode.OneMinusSrcAlpha, RenderTargetCapability.Hdr, sampling,
                geometry, RenderTimeInput.UIClock);
        internal static readonly RenderProgramDescriptor UI = Active(RenderSampling.Neighborhood, RenderGeometry.Expanded);
        internal static readonly RenderProgramDescriptor Texture = Active(RenderSampling.BilinearClamp, RenderGeometry.Local);
        internal static readonly RenderProgramDescriptor Wave = Active(RenderSampling.BilinearClamp, RenderGeometry.VertexDisplacement);
        // 表面任务执行调用者的任意材质，不能冒充内置调色程序的颜色/混合约定。
        internal static readonly RenderProgramDescriptor Surface = default;
    }
}
