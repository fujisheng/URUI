# Effect C# 使用接口

命名空间 `URUI.Effect`。本页说明预设、控制器、播放和联动；粒子/纹理表面见[渲染与资源](RENDERING_ZH.md)，CSS 类型与默认值见[参数参考](CSS_ZH.md)，完整声明见[接口参考](REFERENCE_ZH.md)。

## EffectModule

| 接口 | 参数、返回值与使用时机 |
| --- | --- |
| `new EffectModule(Document)` | Load 前注册 ui、ui-wave、surface-output 和 urui-particles |
| `new EffectModule(Document,IEffectResourceResolver)` | 同时注入额外图片和粒子资源解析 |
| `Document` | 该模块所属文档 |
| `GetController(Element)` | 取得/复用当前文档节点的 EffectController；失效或外文档节点被拒绝 |
| `TryGet(Document,out EffectModule)` | 静态查询已安装模块；不创建模块，不接管文档生命周期 |
| `PropertyDefinitions` | 静态只读样式注册描述，供 Editor schema 使用 |
| `BindSurface(Element,SurfaceEffectInput)` | 给当前节点绑定明确表面输入，返回 IDisposable；每节点只能绑定一个 |
| `Dispose()` | 取消控制器播放、释放粒子及模块订阅，清理绑定；不销毁 Document |

模块与 Document 一起存续，文档重载后重新获取节点控制器。同一个模块不会跨文档复用。纯 CSS 效果不要求手动为每个节点创建控制器。GetController 操作当前有效作者节点，进一步 Apply/Play 等要求节点仍有效且已挂载。

## 参数与预设

`EffectParameters` 是纯参数快照，不持有节点和渲染实例。纹理字段引用 Unity Texture；Clone 复制参数和 Gradient 键、模式与色彩空间，不复制纹理像素。所有纹理资源仍由调用方保持有效。

`EffectPreset : ScriptableObject` 可通过 Assets → Create → URUI → Effect Preset 创建。它保存可共享的序列化参数，接口如下：

| 接口 | 语义 |
| --- | --- |
| `preset.CreateParameters()` | 返回独立参数快照，修改不直接修改预设 |
| `preset.SetParameters(EffectParameters)` | 复制并替换资产参数 |
| `preset.Append(EffectParameters)` | 按已启用效果组规则合并，Flip 按标志合并 |
| `controller.Apply(preset,EffectApplyMode.Replace)` | 默认替换控制器预设通道，并解除该目标原有 Link |
| `controller.Apply(preset,EffectApplyMode.MergeActiveGroups)` | 合并传入的启用组，None 不清除已启用组 |
| `controller.CreateParameters()` | 取得该节点当前有效的独立参数快照 |
| `controller.SaveTo(preset)` | 将当前有效参数快照保存到传入预设对象 |
| `controller.OverridePreset(preset)` | 临时预设基础通道，返回 IDisposable；释放恢复前一个仍有效租约或原基础通道，不解除 Link/播放或改写 CSS |

运行时 SetParameters/SaveTo 修改内存对象，不等于编辑器资产已标脏并持久化；需要保存 .asset 时由编辑器工作流明确负责。不要为每帧更新反复创建预设资产。

CreateParameters/SaveTo 需要读取当前计算样式，节点须已完成布局。若刚加载或修改后必须立刻保存有效参数，先在安全调用点执行 `module.Document.SynchronizeLayout()`；普通帧中可等 LayoutUpdated 后读取。

```csharp
using URUI.Effect;
using UnityEngine;

public static class PresetExamples
{
    public static EffectPreset CreateGrayscale(float intensity)
    {
        var preset = ScriptableObject.CreateInstance<EffectPreset>();
        preset.SetParameters(new EffectParameters {
            ToneFilter = ToneFilter.Grayscale,
            ToneIntensity = intensity
        });
        return preset;
    }
}
```

调用方拥有此临时 ScriptableObject，用完按 Unity 对象规则释放。Apply 不自动启用 CSS 效果入口，作者仍需声明 `--urui-effect:ui`。

## 参数来源与优先级

预设作为默认参数通道，不改写作者 CSS。普通作者声明可覆盖预设，显式 initial 也保留作者优先级；播放通道高于普通作者值，低于 CSS animation、important 和 transition。预设、联动和播放均不通过不断重写行内样式实现。

例如，预设设置 ToneIntensity=.5，但作者 CSS 写 `--urui-fx-tone-intensity:.8`，普通渲染使用 .8；启动 Tone 播放后由播放速率驱动；若 CSS 动画也在写该属性则动画优先。调试时用 Core 的计算样式 GetOrigin/GetNumber 核对来源。

参数组包含 Tone、Color、Sampling、Transition、Target、Blend、Shadow、Gradation、Edge、Detail、Flip 与采样/网格开关。每组字段、默认值见接口参考；每个 CSS 取值见 CSS 参数参考。Gradient 与 HDR 数据优先用 EffectPreset 保存，避免用不能精确表达的 CSS 值替代。

## 启动和等待播放

```csharp
using System.Threading;
using System.Threading.Tasks;
using URUI.Effect;

public static class EffectPlaybackExamples
{
    public static async Task Fade(EffectController controller, CancellationToken token)
    {
        using (var playback = controller.Play(new EffectTweenDefinition {
            Channels = EffectTweenChannels.Transition,
            Duration = .35,
            WrapMode = EffectWrapMode.Once,
            UpdateMode = EffectUpdateMode.Unscaled
        }, token))
        {
            await playback.Finished;
        }
    }
}
```

播放只驱动已启用组，例中节点须已有 TransitionFilter.Fade 等转场模式。控制器新一次 Play 会取消旧播放；取消、文档卸载和节点失效不产生成功完成。

### EffectTweenDefinition

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `Channels` | Visual | 视觉通道位标志；ProgressChanged 需要额外包含 Event |
| `Direction` | Forward | Forward/Reverse |
| `Curve` | 0→1 线性曲线 | 正向曲线，构造播放时复制 |
| `SeparateReverseCurve` / `ReverseCurve` | false / 线性 | 启用独立反向曲线 |
| `Delay` | 0 | 起始延时，秒 |
| `Duration` | 1 | 主段时长，秒；归一化到至少 .001 |
| `Interval` | 0 | 循环/往返间隔，秒 |
| `WrapMode` | Loop | Once、Loop、PingPongOnce、PingPongLoop |
| `UpdateMode` | Normal | Normal、Unscaled、Manual |
| `PlayOnEnable` | Forward | None、Forward、Reverse、KeepDirection |
| `ResetTimeOnEnable` | true | 再次启用时是否重置位置 |

时间必须有限，Delay/Interval 负值归一化为零。定义与曲线在创建播放时复制，事后修改原 definition 不改变已有句柄。

### 通道与控制

| 通道 | 驱动的有效参数 |
| --- | --- |
| Tone、Color、Sampling | 对应启用组的 intensity |
| Transition | transition-progress |
| GradationOffset | -1..1 渐变偏移 |
| GradationRotation | Angle/AngleGradient 的 0..360 旋转 |
| EdgeShiny | Shiny 边缘进度 |
| Event | 进度改变时发送 ProgressChanged，不单独绘制 |

EffectPlayback 提供以下接口：

| 接口 | 语义 |
| --- | --- |
| `Pause()` / `Resume()` | 保留位置暂停/恢复；完成或取消后 Resume 不启动新运行 |
| `Stop()` | 暂停并回到时间零；不触发自然完成 |
| `Restart()` | 重启并增加 RunIdentity；已取消的句柄不能复用；已完成时产生新的 Finished Task |
| `SetTime(double seconds)` | 手动 seek，更新输出，不发出自然完成 |
| `Advance(double delta)` | 推进并判定完成；Manual 模式由调用方主动调用 |
| `Cancel()` / `Dispose()` | 取消，Finished 进入取消状态 |
| `Enabled` | 显式控制启用，遵循 PlayOnEnable/ResetTimeOnEnable |
| `Progress` / `EvaluatedProgress` | 原始归一化进度 / 经曲线计算的进度 |
| `Time` / `TotalTime` | 当前时间 / 含延时、往返与间隔的总长度 |
| `IsPlaying`、`IsPaused`、`IsCancelled`、`IsDelaying` | 当前状态 |
| `Finished : Task` / `Completed : Action` | 自然完成的任务/事件，循环模式不会自然结束 |
| `ProgressChanged : Action<float>` | 仅启用 Event 通道且进度变化时触发 |

句柄在创建线程操作；跨线程取消使用传入 CancellationToken，由后续主线程更新处理。Normal/Unscaled 使用 Unity 对应时间；调用方显式提供的自定义 Surface.UIClock 在两种自动模式中优先。Manual 不由宿主自动推进。节点隐藏不隐式切换 Enabled。

## 参数联动

`target.Link(source, bool useTargetRoot = true)` 返回可释放租约。目标保持独立参数块，不共享可变节点状态。true 时共享来源坐标根，仅限同一文档；跨文档使用 false，只共享纯参数。循环联动会被拒绝。

目标自身的作者 CSS、SamplingScale 和 AllowToModifyMeshShape 保持生效。释放 Link 或来源失效后，目标保留最后有效参数快照；不要在来源页面卸载后继续操作旧控制器。对目标 Apply 新预设会解除原 Link。

## 转场与输入

`controller.InputPolicy` 默认为 `TransitionInputPolicy.Standard`。显式设置 DisableWhenComplete 后，Fade/Cutoff/Dissolve/Melt/Burn 的实际进度到 1 时拒绝本节点命中，不改写 pointer-events。其他转场或未启用组仍使用标准输入规则。控制器 Dispose 取消其播放/联动并解绑，不销毁 DOM。
