# 渲染管线与 URP 集成指南

[English](RENDER_PIPELINE_URP.md) | **中文**

URUI 核心包（`com.fujisheng.urui`）刻意保持**对 URP / HDRP 零直接包依赖**，确保在任何 Unity 管线配置中都能轻量化引入。

## Built-in 渲染管线

在 **Built-in Render Pipeline** 下，`Surface` 内部的 `RenderHost` 会自动向 `Camera.main` 挂载 `CameraEvent.AfterEverything` 渲染命令，无需编写任何胶水代码即可渲染显示。

## Universal Render Pipeline（Unity 6 URP / RenderGraph）

URP 不会执行传统相机事件 `Camera.AddCommandBuffer(CameraEvent.AfterEverything, ...)`，因此需要在 URP 的 `UniversalRendererData` 上添加一个自定义 `ScriptableRendererFeature`，在相机后阶段调用 `Surface.SubmitRendering`（详见可运行源码 [`UruiUrpRendererFeature.cs`](../SampleProject/Assets/Solstice/Runtime/Rendering/UruiUrpRendererFeature.cs)）：

```csharp
// 核心 RenderGraph 注入片段（Edit Mode 未进入 Play 时跳过提交；完整含 Compatibility Mode 兜底见 SampleProject）
if (!Application.isPlaying)
{
    return;
}
using var builder = renderGraph.AddUnsafePass<PassData>("URUI Pass", out var passData);
passData.ColorTarget = resourceData.activeColorTexture;
passData.RenderTexture = cameraData.targetTexture;
passData.Viewport = camera.pixelRect;
passData.TargetDisplay = camera.targetDisplay;
if (passData.ColorTarget.IsValid())
{
    builder.UseTexture(passData.ColorTarget, AccessFlags.ReadWrite);
}
builder.AllowPassCulling(false);
builder.SetRenderFunc(static (PassData data, UnsafeGraphContext ctx) =>
{
    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
    var target = data.RenderTexture
        ? new RenderTargetIdentifier(data.RenderTexture)
        : data.ColorTarget.IsValid()
            ? (RenderTargetIdentifier)data.ColorTarget
            : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
    var info = data.RenderTexture
        ? global::URUI.Rendering.RenderTargetInfo.FromTexture(data.RenderTexture)
        : global::URUI.Rendering.RenderTargetInfo.FromScreen(data.TargetDisplay, 1);

    foreach (var surface in activeSurfaces)
    {
        surface.SubmitRendering(cmd, target, data.Viewport, info);
    }
});
```

## URP 接入五个关键注意事项

1. **必须在 Edit Mode 下拦截未唤醒场景的 `Surface` 提交**：Unity Editor 处于未播放状态时，Game 视图重绘同样会驱动 `CameraType.Game` 执行 URP RenderGraph，而此时场景中挂载的 `Surface` 尚未执行 `Awake()`（`ownerThread == 0`）。请在 `AddRenderPasses` 与 `RecordRenderGraph` 中增加 `if (!Application.isPlaying) return;`（或仅提交显式就绪的运行中 Stage），避免主线程校验报错。
2. **避免对 Backbuffer 调用 `renderGraph.GetTextureDesc(activeColorTexture)`**：当绘制直出屏幕 Backbuffer 时 `GetTextureDesc` 可能失败；请优先使用 `camera.pixelRect` 搭配 `global::URUI.Rendering.RenderTargetInfo.FromScreen(...)`。
3. **每个 `Surface` 只提交一次，而非逐 `Document` 提交**：`Surface.SubmitRendering` 会自动按 `layer / order` 顺序一次性绘制该 `Surface` 上所有可见 `Document`。
4. **显式限定 `global::URUI.Rendering.RenderTargetInfo`**：Unity 6 的 `UnityEngine.Rendering.RenderGraphModule` 命名空间中也新增了同名结构，显式前缀可避免二义性编译错误。
5. **始终保证与项目 `GraphicsSettings` 与 `QualitySettings` 绑定同一 `UniversalRenderPipelineAsset`**：在所有质量档位均指定挂有该 Feature 的 Renderer。
