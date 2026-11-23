using UnityEngine.Rendering;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        /// <summary>
        /// 当前帧 URUI 已录制的命令缓冲。为空表示文档未加载或宿主尚未就绪。
        /// 仅在不持有需退役的 GPU 资源时可借用；可释放图片及图层须使用 SubmitRendering。
        /// </summary>
        public CommandBuffer RenderCommandBuffer
        {
            get
            {
                CheckThread();
                return IsLoaded && host != null && host.Ready ? host.RenderBuffer : null;
            }
        }

        /// <summary>
        /// 向当前 SRP context 排列 UI 绘制和资源 fence。
        /// </summary>
        /// <remarks>
        /// 支持可释放 ImageLease。调用方负责相机/目标配置及管线帧末 Submit；方法不提交调用者已排队的其他命令。失败状态不确定时保留资源并诊断。
        /// </remarks>
        public bool SubmitRendering(ScriptableRenderContext context)
            => SubmitRendering(context, BuiltinRenderTextureType.CameraTarget, new UnityEngine.Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height));

        /// <summary>
        /// 向调用方持有的 SRP 颜色目标排列 UI、图层合成与资源 fence，由管线完成 Submit。
        /// </summary>
        /// <remarks>
        /// 目标须在 GPU 提交完成前保持有效。viewport 使用目标像素坐标，支持管线缩放分辨率；文档布局仍使用逻辑屏幕尺寸。
        /// </remarks>
        public bool SubmitRendering(ScriptableRenderContext context, RenderTargetIdentifier target, UnityEngine.Rect viewport)
            => SubmitRendering(context, target, viewport, default);
        /// <summary>
        /// 提交到真实颜色纹理，自动携带格式与采样信息。
        /// </summary>
        public bool SubmitRendering(ScriptableRenderContext context, UnityEngine.RenderTexture target, UnityEngine.Rect viewport)
        {
            CheckThread();
            return SubmitRendering(context, new RenderTargetIdentifier(target), viewport, URUI.Rendering.RenderTargetInfo.FromTexture(target));
        }
        /// <summary>
        /// 向管线指定的实际输出提交，并保留其颜色格式和采样信息。
        /// </summary>
        public bool SubmitRendering(ScriptableRenderContext context, RenderTargetIdentifier target, UnityEngine.Rect viewport, URUI.Rendering.RenderTargetInfo targetInfo)
        {
            CheckThread();
            if (!float.IsFinite(viewport.x) || !float.IsFinite(viewport.y) || !float.IsFinite(viewport.width) || !float.IsFinite(viewport.height) || viewport.width <= 0 || viewport.height <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(viewport), "提交视口必须为有限且宽高为正的目标像素范围。");
            }
            if (!IsLoaded)
            {
                return false;
            }
            BeginControlUpdate();
            try
            {
                return host.SubmitRendering(context, target, viewport, targetInfo);
            }
            finally
            {
                EndControlUpdate();
            }
        }
    }
}
