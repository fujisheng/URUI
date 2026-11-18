# Effect 渲染与资源

本页介绍资源解析、粒子、纹理效果、表面处理和绘制辅助。基础安装和模块创建见[快速开始](QUICK_START_ZH.md)，控制器见 [C# 接口](API_ZH.md)。各类型全部声明见[接口参考](REFERENCE_ZH.md)。

## IEffectResourceResolver

在 `new EffectModule(document, resolver)` 时注入以下接口：

| 方法 | 调用方责任 |
| --- | --- |
| `ImageLease AcquireImage(ComputedImageValue image)` | 同步返回效果附加纹理租约；根据 image.Kind/Url/SourceUrl 解析实际资源；租约交给效果后由其释放 |
| `GameObject ResolveParticles(string source)` | 把 urui-particles 的 src 映射到粒子 prefab/源；保持源资产有效 |

普通 img 的资源由 Document.Images/ImageResolver 处理；效果的 transition/detail/gradient 额外纹理由该 resolver 处理。两者不能只配置其中一个而期望自动互通。ComputedImageValue 保留真正的 CSS 来源 URL，相对路径不要按脚本目录猜测。

```csharp
using System.Collections.Generic;
using URUI.Effect;
using URUI.Resources;
using URUI.Styles;
using UnityEngine;

public sealed class EffectAssetMap : IEffectResourceResolver
{
    readonly IReadOnlyDictionary<string, Texture2D> images;
    readonly IReadOnlyDictionary<string, GameObject> particles;

    public EffectAssetMap(IReadOnlyDictionary<string, Texture2D> images,
        IReadOnlyDictionary<string, GameObject> particles)
    {
        this.images = images;
        this.particles = particles;
    }
    public ImageLease AcquireImage(ComputedImageValue image)
    {
        if (image.Kind != ComputedImageKind.Url) return null;
        return images.TryGetValue(image.Url, out var texture) ? new ImageLease(texture) : null;
    }
    public GameObject ResolveParticles(string source)
    {
        return particles.TryGetValue(source, out var prefab) ? prefab : null;
    }
}
```

例中图片由调用方长期持有，ImageLease 不销毁原图；若采用可卸载资源管理器，使用 release 回调归还引用，遵守 Core 的 GPU 租约规则。加载失败或换源失败应保留仍在使用的旧资源，不能提前卸载。

## 声明式粒子

```html
<urui-particles id="reward-particles" src="particles/reward"
  data-urui-play-on-load="true"
  data-urui-pause-when-hidden="true"
  data-urui-restart-on-show="true"></urui-particles>
```

```css
#reward-particles { width: 320px; height: 180px; }
```

EffectModule 在 Load 前注册 `URUI.Effect.Elements.ParticlesElement`。通过 `document.Q<ParticlesElement>("reward-particles")` 获取，节点布局/位置来自 CSS，Unity 负责 ParticleSystem 模拟，效果包烘焙并绘制。不需要每个节点一个 MonoBehaviour。

| 接口/属性 | 语义 |
| --- | --- |
| `Source` | 请求的 src，可改写 |
| `CurrentSource` | 最近成功安装的源；失败时与 Source 可能不同 |
| `ParticleCount` / `PlaybackState` | 当前粒子数 / Playing、Paused、Stopped |
| `Play()` / `Pause()` / `Stop()` / `Restart()` | 显式播放控制；有效 src 准备前调用会失败 |
| `Seek(time,randomSeed = 1)` / `SimulationTime` | 从零模拟到 0..600 秒，种子必须非零；切换为手动模拟并暂停 |
| `Step(delta)` | 推进 0..1 秒并暂停；自动模拟不会同时推进 |
| `RefreshSource()` | 源引用不变时重建隔离实例；新源准备失败保留旧源 |
| `OverrideSource(GameObject)` / `OverridePlayback()` | 临时源/播放租约；释放恢复原引用和播放状态，不写 Prefab |
| `OverrideMaterial(source,replacement)` | 仅替换当前粒子实例中匹配的主体/尾迹材质槽；不修改 Renderer/Prefab 源引用，释放时恢复 |
| `GetStatistics()` | 读取 CPU 读取、主体/尾迹烘焙、录制次数及毫秒累计 |
| `data-urui-play-on-load` | 默认 true |
| `data-urui-pause-when-hidden` | 默认 true |
| `data-urui-restart-on-show` | 默认 true |

data 属性接受 true/false，显式 Pause/Stop 不会被再次显示自动恢复。裁剪只影响绘制/烘焙，不代替暂停；原粒子时间模式保持不变。换源会先校验组件、材质和尾迹条件，失败保留旧实例与状态。移除、片段替换、文档卸载或模块释放都会回收实例，相关 GPU 数据仍等待安全退役。

需要把粒子组合到普通作者节点时使用 `new ParticlePainter(element, source, ParticlePlaybackOptions)`；PlayOnLoad/PauseWhenHidden/RestartOnShow 默认均为 true。该类提供 SetSource、SetOptions、播放控制、Source 和 GetStatistics，Dispose 释放绑定。作者节点使用 particle-paint class 及对应样式。

`ParticlePainter` 同样提供 Seek、Step、RefreshSource 和 OverridePlayback；`SetManualSimulation(bool,uint)` 显式切换模拟驱动。恢复自动驱动后沿用源的自动随机种子配置。Seek 的确定性仅针对同平台、同粒子配置，不承诺跨平台一致。所有模拟/刷新/租约操作在拥有该节点的 Unity 主线程执行，节点或绑定失效后不可继续调用。

## UI 固定参数程序

`ui` 的计算样式由原生固定布局读取，数值和共享时钟通过参数纹理送入 Shader。采样扩边、Flip、阴影距离/淡出和镜像裁切在 GPU 执行；源拓扑、阴影模式或迭代次数变化时建立模板。归一化三角流及颜色完全相同的源共享模板，模板和资源等实际提交 fence 完成后退役。

普通图像效果直接参与有序合批。子树效果保留必要的捕获与最终组混合；重叠内容、不同混合域、邻域采样的含义保持一致。附加纹理仍由资源解析器和租约管理，相对 URL 包含声明来源。

`URUI.Effect/UiEffect` 是固定参数表驱动的内部程序。已有通过 `Graphics.Blit` 设置其旧 Uniform 的调用改用 `EffectPreview`；文档中的效果继续通过 CSS、预设及控制器调用。自有 Shader 和表面处理继续使用它们自己的材质契约。

自定义固定程序通过 Core 的 `IParameterImageEffect`、`RenderParameterLayout` 提供值布局，`EffectProgramPreparation` 在回调内提供源和坐标事实。`Frame` 按需生成独立不可变快照；准备视图和 Span 不能跨回调保存。带几何的程序实现 `IParameterImageGeometry`，额外运行时行和共享资源上传分别使用 `IParameterImageRuntime`、`IParameterImageResources`。捕获子树需同时提供 `IParameterImageGeometry` 和 `IParameterEffectLayout`，在分配输入之前返回保守覆盖范围和最终混合。

## 原图材质效果与 Wave

`URUI.Effect.Rendering.TextureColorEffect` 提供：

| 方法 | 参数与用途 |
| --- | --- |
| `Define(name, authored, rateProperty = null, saturationProperty = null, brightnessProperty = null)` | 用作者 Material 建立共享不可变程序；可指定速率/饱和度/亮度参数对应关系 |
| `DefineResolved(name, resolver, keyProperty, rateProperty = null, saturationProperty = null, brightnessProperty = null)` | 从声明的资源键解析 Material，不由每个页面枚举全部材质 |

返回 EffectDefinition，在 Document.Load 前传给 `document.Effects.Register(...)`；CSS 用 `--urui-image-effect:name`。保留作者材质初值，显式 CSS 才覆盖；不应在外部共享材质上逐节点写不同参数。保持作者 Material/Shader 及相关纹理在实例使用期间有效。

`WaveEffect.Define(string name = "ui-wave")` 返回波形图片程序；EffectModule 已登记默认 ui-wave，无需重复注册。可使用 wave-image 容器及直接 img 子节点，或自行声明等效属性，见[CSS 参考](CSS_ZH.md)。波形在 GPU 顶点上位移，布局/输入仍属于原 img；当前组合范围为完整纹理，图集子区、九宫格与 filled 的组合尚未验证。

## TextureEffectSurface 与 SurfaceEffectInput

用于已有 Texture/视频帧的离屏处理。命名空间分别为 URUI.Effect 和 URUI.Effect.Rendering。

| 接口 | 使用方式 |
| --- | --- |
| `TextureEffectSurface(int width,int height,IDisposable inputLease = null)` | 创建固定像素输出；要求 ARGBHalf 支持；调用方持有表面对象 |
| `Texture` / `Output : ImageLeaseSource` | 当前输出 RT / 可获取独立租约的输出源 |
| `Prepare(Vector2 size)` | Prepare 阶段更新布局几何，不改变固定输出像素尺寸 |
| `Record(context,input,material,properties)` | 在宿主有序命令流中处理 |
| `Render(input,material,size)` | 独立显式处理入口；自动随 UI 提交时优先使用 BindSurface |
| `Clear()` | 显式清空输出 |
| `Dispose()` | 归还自身所有权，实际资源等待租约及 GPU 使用结束 |
| `GetStatistics()` | 全局 live 表面、颜色字节、处理/清空 pass 及网格重建统计 |

`SurfaceEffectInput(surface, source, material, version, ready = null, prepare = null, usesTime = false)` 绑定处理输入。version 必须代表实际内容版本，视频使用 PresentedVersion；ready 控制是否已有可处理帧。prepare 允许准备本次材质参数，usesTime 显式声明 Shader 需要 `_EffectTime`。材质参数、关键词、Shader 与相关纹理状态改变也会失效；含未知 GPU 输入时保守刷新。

```csharp
using System;
using URUI.Effect;
using URUI.Effect.Rendering;
using URUI.Elements;
using URUI.Media;
using UnityEngine;

public static class SurfaceBindingExamples
{
    public static IDisposable BindVideo(EffectModule module, Element image,
        VideoPlayback video, Material material, TextureEffectSurface surface)
    {
        if (!video.HasPresentedFrame) throw new InvalidOperationException("请先等待视频首帧。");
        var input = new SurfaceEffectInput(surface, video.Output, material,
            () => video.PresentedVersion, () => video.HasPresentedFrame);
        return module.BindSurface(image, input);
    }
}
```

完整接线顺序：先准备表面并让 Document.ImageResolver 将指定图片 URI 映射到 `surface.Output.Acquire()`；创建模块；加载含 img 的文档并立即 BindSurface；在第一次效果准备前完成绑定。作者 img 使用 `--urui-image-effect:surface-output`，src 指向表面输出 URI。视频源 RT 在绑定期间不能被重建或释放；换源/尺寸变化时重新建立相应表面与绑定，调用方可用 inputLease 持有视频输出租约。

结束时先卸载文档，再释放绑定、表面与模块，最后释放外部源/材质引用。BindSurface 返回的租约不拥有传入的 TextureEffectSurface 或源 Material。隐藏期间停止处理；相同版本与参数的静态输入不重复处理；只有实际提交后才确认处理缓存。

## 调度器与提交确认

`EffectInput(source,version,size,parameters,program,format)` 表示完整处理身份。`EffectScheduler.TryPrepare(input,out update)` 在缓存命中时返回 false；需要处理时返回独立只读 Update。只有处理命令实际提交后才调用 `update.Submitted()`。取消、重载或输入策略变化时 Invalidate 使旧确认失效。default Update 不可提交，迟到的旧世代确认不能覆盖新缓存。

## 其他绘制辅助

| 类型 | API 与作者要求 |
| --- | --- |
| `FeatherMaskPainter(Element)` | 作者使用 feather-mask / `--urui-mask-image:shader(feather-mask)`，四边 `--urui-mask-feather-left/top/right/bottom` 为 px；Dispose 解绑 |
| `StrokePainter(Element)` | SetSegments(points,segmentColors) 更新局部线段，线宽来自 `--urui-line-width` 正 px；Dispose 解绑 |
| `ArcTextLayout(host,source,prototype)` | 对作者文本与原型做弧形布局，尺寸和曲线参数来自作者样式；Dispose 清理 |
| `EffectResources(EffectContext,IEffectResourceResolver)` | 扩展效果的图片资源辅助，PrepareImage(name,value) 准备，GetImage(name) 读取；生命周期交给 EffectContext |

## 预览

Assets → Create → URUI → Effect Preset 创建预设后，可在 Inspector 点击预览，或打开 Window → URUI → Effect Preview。预览使用生产效果、采样/阴影几何及 Shader；输入为普通 Unity 直 Alpha 纹理，背景遵守 Unity 色彩空间约定。

程序化预览使用 `new EffectPreview(parameters)`、`SetParameters(parameters)` 和 `Render(Texture source,Rect content,double time,RenderTexture destination,Color background)`，调用方提供源、内容区域、明确时间及目标 RT，完成后 Dispose。预览不会创建第二个 Surface，不应把预览截图当成所有平台运行证据。

## 成本和统计

BlurFast/Medium/Detail 核采样分别为 25/81/169；阴影/描边使用共享复制拓扑和 GPU 位移，复杂 Blend/节点效果可能先捕获。EffectStatistics.GetTargets、TextureEffectSurface.GetStatistics 和 ParticlePainter.GetStatistics 的含义不同；计数与 CPU 毫秒不等于 GPU 执行时间。更多平台和资源限制见 [Core 支持范围](../../com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)。
