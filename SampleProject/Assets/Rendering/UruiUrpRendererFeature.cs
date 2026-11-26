using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using URUI.Hosting;
using RenderTargetInfo = URUI.Rendering.RenderTargetInfo;

namespace URUI.Showcase
{
    /// <summary>Submits the showcase Surface above the main URP game camera.</summary>
    public sealed class UruiUrpRendererFeature : ScriptableRendererFeature
    {
        sealed class UiPass : ScriptableRenderPass
        {
            sealed class PassData
            {
                public TextureHandle Target;
                public RenderTexture RenderTexture;
                public Surface Surface;
                public Rect Viewport;
                public int Display;
            }

            Surface surface;

            public UiPass() => renderPassEvent = RenderPassEvent.AfterRendering + 2;

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var cameraData = frame.Get<UniversalCameraData>();
                Camera camera = cameraData.camera;
                if (!surface)
                    surface = Object.FindFirstObjectByType<Surface>();

                if (!surface || !surface.isActiveAndEnabled || camera.pixelWidth <= 0 || camera.pixelHeight <= 0)
                    return;

                var target = frame.Get<UniversalResourceData>().activeColorTexture;
                using var pass = graph.AddUnsafePass<PassData>("URUI Showcase", out var data, profilingSampler);
                data.Target = target;
                data.RenderTexture = cameraData.targetTexture;
                data.Surface = surface;
                data.Viewport = camera.pixelRect;
                data.Display = camera.targetDisplay;
                if (target.IsValid())
                    pass.UseTexture(target, AccessFlags.ReadWrite);
                pass.AllowPassCulling(false);
                pass.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    var output = data.RenderTexture
                        ? new RenderTargetIdentifier(data.RenderTexture)
                        : data.Target.IsValid() ? (RenderTargetIdentifier)data.Target
                        : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                    var info = data.RenderTexture
                        ? RenderTargetInfo.FromTexture(data.RenderTexture)
                        : RenderTargetInfo.FromScreen(data.Display, 1);
                    data.Surface.SubmitRendering(command, output, data.Viewport, info);
                });
            }
        }

        UiPass pass;
        public override void Create() => pass = new UiPass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (Application.isPlaying && camera.cameraType == CameraType.Game && camera.CompareTag("MainCamera"))
                renderer.EnqueuePass(pass);
        }
    }
}
