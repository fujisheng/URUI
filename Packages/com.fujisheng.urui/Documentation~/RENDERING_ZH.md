# 渲染扩展与媒体

一般页面使用普通节点、CSS 和控件即可。本页面向需要自定义绘制、注册效果程序、接入渲染管线或处理视频的调用方。公开声明见[接口参考](REFERENCE_ZH.md)，现成效果见 [URUI.Effect](../../com.fujisheng.urui.effect/README.md)。

## 提交共享 Surface

Built-in 使用宿主相机事件；SRP 由适配层在正确的相机/目标阶段调用：

```text
bool Surface.SubmitRendering(ScriptableRenderContext context,
    RenderTargetIdentifier target, Rect viewport)
```

传入当前管线的有效 context、实际目标及像素 viewport，宽高须为正有限值。一个 Surface 每次提交包含其所有活动文档，不要对每页重复提交。返回 false 表示当前没有可提交的活动宿主/文档，不是 GPU 完成通知。管线负责帧末 Submit。

Document.SubmitRendering 的重载提交同一共享 UI。RenderCommandBuffer 只在没有必须安全退役的资源时允许借用；可释放图片、图层和 Linear 标准合成应使用受控 SubmitRendering。外借的 buffer 每次仍会清空重录，不应自行长期保存旧命令。

URP 的完整项目接入由独立 FUI 适配包的 UruiRendererFeature 提供；Core 没有直接 URP 包依赖。该项目路径已运行验证，不等于任意 URP 配置或 HDRP 均受支持。

## 节点自定义绘制

`document.AttachPainter(Element element, string shader, Action<ElementPaintContext> paint, Action release = null)` 返回绑定租约。作者 CSS 使用对应的 `--urui-decorator:shader(name)` 或 `--urui-mask-image:shader(name)` 激活；回调通过上下文 DrawMesh 录制，不自行提交 Graphics 命令。

ElementPaintContext 提供 Element、Size、IsMaskImage、IsClippedOut；`DrawMesh(mesh,material,localTransform,properties,submesh,pass)` 录制节点局部几何。mesh/material 必须在 GPU 使用结束前有效。Dispose 绑定取消绘制；release 用于资源释放，其他在途资源通过 `document.RetirePainterResources(IDisposable)` 退役。

ElementPaintContext 为 readonly struct，default 无效；所有副本仅在当前回调窗口有效，不能缓存到下一帧。效果/绘制回调中不要修改 DOM、重载页面或安排页面变更。

## 注册效果程序

| 接口 | 用途 |
| --- | --- |
| `document.Effects.Register(EffectDefinition)` | 加载使用该效果的文档前注册命名程序 |
| `EffectDefinition(name,kind,factory,programKey,colorContract,...)` | 名称、Image/Element 种类、工厂、稳定程序身份、明确颜色约定及参数 |
| `IEffectFactory.Create(EffectContext)` | 为节点创建实例 |
| `IElementEffect.Prepare(EffectFrame)` | 读取冻结帧，准备资源/参数 |
| `IElementEffect.Record(EffectRenderContext)` | 只录制绘制 |
| `EffectContext.Own<T>(resource)` | 将 IDisposable 资源交给实例上下文持有 |
| `EffectContext.Retire(resource)` | 将旧资源转入 GPU 安全退役 |
| `EffectContext.RequirePreparation()` | 请求重新准备 |
| `document.Effects.TryGetFrame(element,out frame)` | 取得当前可用帧；失败不使用 default 帧 |
| `TryGet(name,out definition)` / `Count` | 查询注册表 |

EffectFrame 是不可变值快照，包含 ElementToken、Layout、Style、Size、Time、Source、SourceVersion、SourceGeometry、SourceCaptureBounds/SourceUvBounds、ElementToDocument 及源颜色/方向标志。不要根据 Texture 对象引用相同就假定内容未变，应同时考虑 SourceVersion。

EffectRenderContext 为只读绘制窗口：DrawOriginal 绘制原输入，Draw 绘制默认几何，DrawGeometry 绘制指定几何，RecordSurface 录制表面处理，AfterSubmission 在命令实际提交后通知。DrawOriginal 的使用状态由当前窗口副本共享。回调返回后所有窗口副本永久失效；EffectFrame 快照本身不会随内部复用变化。

`--urui-image-effect:name` 选择原图效果，`--urui-effect:name` 选择节点效果。复杂节点可能先捕获子树。定义中的参数需与 CSS 注册类型匹配，UsesTime 仅在算法读取时间时开启；扩边、合成和条件式原图路径由相应重载声明。

## 子树滤镜

标准 CSS filter 程序由 Core 提供。按声明顺序执行 brightness、contrast、grayscale、saturate、opacity 等颜色运算；blur/drop-shadow 与复杂节点可能使用有界离屏目标，保留祖先裁剪和效果 halo。

`RegisterFilter(string name, Func<ILayerFilterProgram> factory, bool cacheable = false)` 注册扩展程序。ILayerFilterProgram.Prepare/Record 接收 FilterInvocation 的名称、数值、sigma、offset、颜色、区域；FilterRenderContext 通过 Texture、Draw、Copy 操作显式输入/中间/输出 buffer。仅确定输出完全由声明依赖决定的纯程序可设 cacheable:true；动态程序默认 false。

`EffectStatistics.GetTargets()` 返回 live/leased/idle 颜色字节、创建及复用统计；FilterProcessingPasses 统计实际滤镜提交，不是 GPU 毫秒。颜色字节不含驱动对齐等额外开销。

## 视频输出

`URUI.Media.VideoPlayback` 是 MonoBehaviour，使用 Unity VideoPlayer 解码，Output 为持有的 RenderTexture。它与 UI 时钟分开，由宿主管理播放策略。

| 接口 | 语义 |
| --- | --- |
| `Play(VideoClip clip,bool loop = true)` | 播放 Unity 视频资产 |
| `PlayUrl(string url,bool loop = true)` | 播放平台支持的 URL，准备后获得尺寸 |
| `Pause()` / `Resume()` / `Replay()` / `Stop()` | 暂停、恢复、重播、停止 |
| `HasPresentedFrame` | 是否已有真正呈现的帧 |
| `PresentedFrame` / `PresentedVersion` | 最近呈现帧号 / 单调内容版本 |
| `FramePresented : Action<VideoPlayback>` | 实际帧已呈现 |
| `Failed : Action<string>` | 解码/准备失败 |
| `AcquireOutput() : ImageLease` | 获取独立输出租约，交给图片解析器后由 URUI 归还 |

将 HTML 图片路径映射到 AcquireOutput 返回的租约。尚未准备好时不要在 ImageResolver 中返回 null 后期待它自动异步重试：null 会成为本代际失败缓存。应在首帧就绪后加载/切换相应图片资源，或使用宿主明确的占位与换源流程。

需要对视频增加处理时，用 Effect 的 [SurfaceEffectInput](../../com.fujisheng.urui.effect/Documentation~/RENDERING_ZH.md)，版本函数读取 PresentedVersion，ready 函数读取 HasPresentedFrame。同一解码帧不应重复处理。

## 资源释放

资源所有权和提交安全规则见[生命周期与资源](LIFECYCLE_ZH.md)。自有 Unity 对象的延后释放可使用 `GpuRetirement.DestroyOwnedObject(UnityEngine.Object)`，由当前运行模式选择 Destroy/DestroyImmediate；它不替代 GPU fence，也不能用于销毁外部共享资产。不要用固定等待几帧估计 GPU 已完成。
