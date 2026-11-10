using UnityEngine.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        /// <summary>
        /// 当前帧已录制的 UI 命令缓冲，供外部渲染后端（如 URP RendererFeature）提交。
        /// </summary>
        internal CommandBuffer RenderBuffer
        {
            get
            {
                if (hasOwnedImages || HasLayerResources || (OwnerSurface && (OwnerSurface.HasPainters || OwnerSurface.HasEffects))) throw new System.InvalidOperationException("持有可释放资源的文档必须通过 Document.SubmitRendering 提交，不能借出未追踪的命令缓冲。");
                externalBufferExposed = true;
                return buffer;
            }
        }
    }
}
