using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using URUI.Hosting;

namespace Solstice.Rendering
{
    /// <summary>
    /// URP Renderer Feature that submits active URUI Surfaces after camera rendering
    /// using Unity 6 RenderGraph (with compatibility-mode fallback when enabled).
    /// </summary>
    public sealed class UruiUrpRendererFeature : ScriptableRendererFeature
    {
        sealed class UruiRenderPass : ScriptableRenderPass
        {
            static readonly List<Surface> activeSurfaces = new();
#if URP_COMPATIBILITY_MODE
            RenderTargetIdentifier compatDestination;
            global::URUI.Rendering.RenderTargetInfo compatTargetInfo;
            Rect compatViewport;
            RTHandle compatOutput;
#endif

            sealed class PassData
            {
                internal TextureHandle ColorTarget;
                internal RenderTexture RenderTexture;
                internal Rect Viewport;
                internal int TargetDisplay;
                internal Camera Camera;
            }

            public UruiRenderPass()
            {
                renderPassEvent = RenderPassEvent.AfterRendering + 2;
            }

            static void CollectSurfaces(Camera camera, List<Surface> results)
            {
                results.Clear();
                if (!Application.isPlaying)
                {
                    return;
                }
                var found = Object.FindObjectsByType<Surface>(FindObjectsSortMode.None);
                for (var i = 0; i < found.Length; i++)
                {
                    var surface = found[i];
                    if (surface && surface.isActiveAndEnabled && surface.gameObject.activeInHierarchy)
                    {
                        results.Add(surface);
                    }
                }
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!Application.isPlaying)
                {
                    return;
                }
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var camera = cameraData.baseCamera ? cameraData.baseCamera : cameraData.camera;
                if (!camera || camera.cameraType != CameraType.Game || !camera.CompareTag("MainCamera"))
                {
                    return;
                }

                var viewport = camera.pixelRect;
                if (viewport.width <= 0 || viewport.height <= 0)
                {
                    return;
                }

                var colorTarget = resourceData.activeColorTexture;
                using (var builder = renderGraph.AddUnsafePass<PassData>("Solstice URUI Pass", out var passData, profilingSampler))
                {
                    passData.ColorTarget = colorTarget;
                    passData.RenderTexture = cameraData.targetTexture;
                    passData.Viewport = viewport;
                    passData.TargetDisplay = camera.targetDisplay;
                    passData.Camera = camera;

                    if (colorTarget.IsValid())
                    {
                        builder.UseTexture(colorTarget, AccessFlags.ReadWrite);
                    }
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    {
                        CollectSurfaces(data.Camera, activeSurfaces);
                        if (activeSurfaces.Count == 0)
                        {
                            return;
                        }

                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        var target = data.RenderTexture
                            ? new RenderTargetIdentifier(data.RenderTexture)
                            : data.ColorTarget.IsValid()
                                ? (RenderTargetIdentifier)data.ColorTarget
                                : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                        var info = data.RenderTexture
                            ? global::URUI.Rendering.RenderTargetInfo.FromTexture(data.RenderTexture)
                            : global::URUI.Rendering.RenderTargetInfo.FromScreen(data.TargetDisplay, 1);

                        for (var i = 0; i < activeSurfaces.Count; i++)
                        {
                            activeSurfaces[i].SubmitRendering(cmd, target, data.Viewport, info);
                        }
                    });
                }
            }

#if URP_COMPATIBILITY_MODE
#pragma warning disable CS0618
#pragma warning disable CS0672
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var camera = renderingData.cameraData.baseCamera ? renderingData.cameraData.baseCamera : renderingData.cameraData.camera;
                compatViewport = camera ? camera.pixelRect : default;
                var rt = renderingData.cameraData.targetTexture;
                compatDestination = rt ? new RenderTargetIdentifier(rt) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                compatTargetInfo = rt
                    ? global::URUI.Rendering.RenderTargetInfo.FromTexture(rt)
                    : global::URUI.Rendering.RenderTargetInfo.FromScreen(camera ? camera.targetDisplay : 0, 1);
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref compatOutput, compatDestination);
                ConfigureTarget(compatOutput);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!Application.isPlaying)
                {
                    return;
                }
                var camera = renderingData.cameraData.camera;
                if (!camera || !camera.CompareTag("MainCamera") || compatViewport.width <= 0 || compatViewport.height <= 0)
                {
                    return;
                }
                CollectSurfaces(camera, activeSurfaces);
                for (var i = 0; i < activeSurfaces.Count; i++)
                {
                    activeSurfaces[i].SubmitRendering(context, compatDestination, compatViewport, compatTargetInfo);
                }
            }
#pragma warning restore CS0672
#pragma warning restore CS0618
#endif
        }

        UruiRenderPass pass;

        public override void Create()
        {
            pass ??= new UruiRenderPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || !Application.isPlaying)
            {
                return;
            }
            if (renderingData.cameraData.cameraType == CameraType.Game && renderingData.cameraData.camera.CompareTag("MainCamera"))
            {
                renderer.EnqueuePass(pass);
            }
        }
    }
}
