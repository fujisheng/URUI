# Render Pipeline & URP Integration Guide

**English** | [中文](RENDER_PIPELINE_URP_ZH.md)

URUI Core (`com.fujisheng.urui`) intentionally carries **zero direct dependency on URP or HDRP** so projects on any Unity configuration can install it without dependency conflicts.

## Built-in Render Pipeline

In **Built-in RP**, `Surface` automatically creates a managed `RenderHost` that attaches recorded `CommandBuffer` submissions directly onto `Camera.main` at `CameraEvent.AfterEverything`—no manual setup required.

## Universal Render Pipeline (URP — Unity 6 RenderGraph)

URP does not execute `Camera.AddCommandBuffer(CameraEvent.AfterEverything, ...)`. Instead, enqueue URUI cleanly inside a custom `ScriptableRendererFeature`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using URUI.Hosting;

public sealed class UruiUrpRendererFeature : ScriptableRendererFeature
{
    sealed class UruiPass : ScriptableRenderPass
    {
        static readonly List<Surface> surfaces = new();

        sealed class PassData
        {
            internal TextureHandle ColorTarget;
            internal RenderTexture RenderTexture;
            internal Rect Viewport;
            internal int TargetDisplay;
        }

        public UruiPass() => renderPassEvent = RenderPassEvent.AfterRendering + 2;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
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
            using var builder = renderGraph.AddUnsafePass<PassData>("URUI Pass", out var passData);
            passData.ColorTarget = colorTarget;
            passData.RenderTexture = cameraData.targetTexture;
            passData.Viewport = viewport;
            passData.TargetDisplay = camera.targetDisplay;

            if (colorTarget.IsValid())
            {
                builder.UseTexture(colorTarget, AccessFlags.ReadWrite);
            }
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext ctx) =>
            {
                surfaces.Clear();
                foreach (var s in Object.FindObjectsByType<Surface>(FindObjectsSortMode.None))
                {
                    if (s && s.isActiveAndEnabled && s.gameObject.activeInHierarchy)
                    {
                        surfaces.Add(s);
                    }
                }
                if (surfaces.Count == 0) return;

                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                var target = data.RenderTexture
                    ? new RenderTargetIdentifier(data.RenderTexture)
                    : data.ColorTarget.IsValid()
                        ? (RenderTargetIdentifier)data.ColorTarget
                        : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                var info = data.RenderTexture
                    ? URUI.Rendering.RenderTargetInfo.FromTexture(data.RenderTexture)
                    : URUI.Rendering.RenderTargetInfo.FromScreen(data.TargetDisplay, 1);

                for (var i = 0; i < surfaces.Count; i++)
                {
                    surfaces[i].SubmitRendering(cmd, target, data.Viewport, info);
                }
            });
        }
    }

    UruiPass pass;
    public override void Create() => pass ??= new UruiPass();
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if (pass != null && data.cameraData.cameraType == CameraType.Game && data.cameraData.camera.CompareTag("MainCamera"))
        {
            renderer.EnqueuePass(pass);
        }
    }
}
```

> **Working Reference**: See [`SampleProject/Assets/Solstice/Runtime/Rendering/UruiUrpRendererFeature.cs`](../SampleProject/Assets/Solstice/Runtime/Rendering/UruiUrpRendererFeature.cs), including `#if URP_COMPATIBILITY_MODE` fallback and `UniversalRendererData` setup in [`SolsticeProjectSetup.cs`](../SampleProject/Assets/Solstice/Editor/SolsticeProjectSetup.cs).

## Critical URP Pitfalls to Avoid

1. **Do NOT call `renderGraph.GetTextureDesc(colorTarget)` unconditionally** when `activeColorTexture` refers to the backbuffer—use `camera.pixelRect` + `RenderTargetInfo.FromScreen(...)`.
2. **Submit ONCE per `Surface` per camera**, never once per `Document`: `Surface.SubmitRendering` already renders every active visible `Document` attached to that `Surface` in proper `layer / order` sequence.
3. **Disambiguate `RenderTargetInfo`**: Both `UnityEngine.Rendering.RenderGraphModule` and `URUI.Rendering` define `RenderTargetInfo`. Use `global::URUI.Rendering.RenderTargetInfo`.
4. **Always use full base-camera `pixelRect`**: Do not pass scaled intermediate render-scale dimensions as UI viewport if targeting final screen resolution.
