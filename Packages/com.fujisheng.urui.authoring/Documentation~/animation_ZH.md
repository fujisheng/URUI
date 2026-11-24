# 动画编辑与运行 API

## 最短使用流程

1. 双击项目 Assets 下的 HTML，或打开 英文菜单 `Window/URUI/Editor`（中文为 `Window/URUI/编辑器`）→ Open Page / 打开页面。
2. 选中节点，切到“动画”，点击“添加动画”→ 弹出 / 淡入 / 淡出 / 滑入 / 按钮脉冲。
3. 拖动刻度预览；输入秒数可精确定位。点击“录制”，在 URUI Inspector 或画布调整当前节点。
4. 点击“结束录制”，播放检查，再点击“保存”。页面、CSS、序列一起保存；粒子 Prefab、Material、EffectPreset 仍在各自工作台明确 Apply。

第一次添加预设会为无 id 的节点分配唯一 id，在同目录创建 `<页面名>.animations.css` 和 `<页面名>.urui-sequence.json`，并链接 CSS。三项变更是一笔可撤销事务，保存前不创建文件。预设只创建动画，没有自动挂到 CSS `animation` 属性上；正式运行由 `SequencePlayer` 或业务自己的 CSS 动画触发。

布局模式改基础样式。动画模式未录制时只临时预览属性，停止、定位或切模式后恢复；**开启录制后**才写当前关键帧，不把预览值保存为基础样式。录制时只编辑选中轨道绑定的一个节点，选其他节点、切换源码版本、Undo、切模式或退出窗口会结束录制。第一次录制前选择轨道首轮范围内的时间；重复和往返播放复用首轮关键帧。

## 时间轴操作

| 操作 | 用法 |
| --- | --- |
| 播放 / 暂停 / 停止 | 暂停保留姿态；停止释放覆盖、回到零并恢复基础样式/粒子状态 |
| 上一帧 / 下一帧 | 默认 60 fps；“帧吸附”按该帧率量化时间 |
| 时间缩放 / 平移 | ＋ / − / 适应全部；放大后用横向滑条平移，主窗口可收起时间轴 |
| 拖入节点 | 从节点树拖到时间轴，按落点时间建立弹出轨或粒子轨；版本已变化时拒绝旧拖拽 |
| 多选关键帧 | Shift / Cmd / Ctrl 点击菱形；同一位置的多条声明一起选择 |
| 移动关键帧 | 拖动选中菱形，松手一次提交；Esc 取消；Option 拖动复制 |
| 精确移动 / 复制 | 定位时间针，再用“移动到当前时间 / 复制到当前时间” |
| 删除关键帧 | “删除关键帧”；删除整个位置，不删其他位置与无关 CSS |
| M / S / 锁 | 静音轨道、单独预览、锁定编辑；锁定与静音保存在序列，Solo 仅为临时预览 |
| 缓动 | linear / ease / ease-in / ease-out / ease-in-out，或三次贝塞尔控制点；也接受 CSS `linear(...)` |
| 显式关键帧 | 选择轨道，在“属性 / 值”输入 CSS 名和值后添加，不必开启录制 |

关键帧多选移动在同一工作集事务中进行。目标位置已占用时拒绝移动，避免静默覆盖。复制到已有位置按属性合并。共享 `@keyframes` 的轨道会同时受到源码变更影响，Inspector 会提示共享引用。内联 `<style>` 中的动画可以播放；本版可视化关键帧修改定位外部 CSS 文件，内联动画请先移到链接的 CSS 文件。

## 序列格式 v1

```json
{
  "version": 1,
  "name": "奖励弹窗",
  "duration": 1.2,
  "loop": false,
  "tracks": [
    {
      "id": "popup",
      "kind": "css",
      "target": "reward",
      "keyframes": "reward-in",
      "start": 0,
      "duration": 0.3,
      "iterations": 1,
      "alternate": false,
      "easing": "ease-out",
      "muted": false,
      "locked": false,
      "source": "",
      "seed": 1,
      "speed": 1
    }
  ]
}
```

`target` 是播放根内唯一的 HTML id，或显式 slots 字典的键；`$root` 表示传入根节点。序列不持久化编辑器节点 key 或原生句柄。不同根可各自拥有同名 id；同一根内歧义拒绝绑定。动态生成节点使用显式 slots。

`start` 和单轮 `duration` 单位为秒，结束为 `start + duration * iterations`。序列时长 0..600 秒、最多 256 轨；循环序列时长必须大于零，活动 CSS 轨道合计最多 4096 个关键帧，单个命名动画也受此上限约束。零时长轨在开始时立即取终值。有限轨结束后保持最后姿态，序列停止才释放覆盖。

同一时段的同节点同属性轨道冲突会拒绝创建播放器。顺序轨道允许：较晚开始的轨道只接替它实际控制的属性，其他属性继续保留之前轨道的终态。隐式 from/to 固定取创建播放器时的基础值，不使用上一次拖动采样的姿态。需要连续衔接时显式写端点。

覆盖遵循原生 Animation 级联优先级；important 与 transition 仍可以优先。停止只移除自有覆盖，不回写旧 inline，不丢弃游戏后来写入的样式。

## Core 运行时：URUI.Animation

所有 Element/Document/播放器调用在 Unity 主线程。定义可序列化为开放文本；播放器创建时复制定义，后续修改原对象不影响已有播放。

```csharp
using URUI.Animation;

// document 已加载且完成布局；sequenceText 是 TextAsset.text 或项目自己的文本加载结果。
var definition = SequenceDefinition.Parse(sequenceText);
using var playback = new SequencePlayer(document.Body, definition);
playback.Play();
await playback.Finished; // 自然有限播放成功；Stop/Dispose 取消未完成播放。
```

跨多帧持有播放器时由业务对象保存引用，在页面/业务生命周期结束时 Dispose。上例的 using 生命周期跨 await；无限循环需要外部 Stop/Dispose，不能等待自然完成。

| API | 行为 |
| --- | --- |
| `SequenceDefinition.Parse(text)` | 严格 JSON，拒绝未知字段、重复键、不支持版本与非法范围 |
| `definition.Validate()` / `ToJson()` | 校验 / 可读 JSON；CSS 值不复制到 JSON |
| `new SequencePlayer(root, definition, factory = null, slots = null)` | root 有效、挂载且已布局；解析绑定、冻结基础、取得自有样式与扩展租约 |
| `Play()` | 从当前位置继续；自然完成后重播从零开始 |
| `Pause()` | 停止自动推进，保留当前姿态 |
| `Seek(seconds)` | 定位并暂停；不发送自然完成或业务回调 |
| `Sample(seconds)` | 任意前后定位，不改变播放开关；有限时长钳制，循环取模 |
| `Stop()` | 恢复覆盖、时间归零；未完成的 Finished 取消，可重新 Play |
| `Dispose()` | Stop 并解除文档注册；不销毁 Document / 业务源资产 |
| `Time / Duration / Name / IsPlaying / Finished` | 当前时间、元数据、自动推进状态与本次播放完成任务 |
| `SequenceTiming.At(track,time)` | 纯函数，返回 Active、Progress、LocalTime、Iteration；Active 包含 forwards 保持 |

自动播放随 Document 的正常生产时钟推进，先采样再更新原生布局。编辑器单独 Sample 不调用游戏 Update，也不回拨全局时钟。根失效、隐藏、文档卸载会停止或释放播放；绑定的子节点销毁或移出播放根会取消 Finished 并释放其余覆盖，暂停状态也检查有效性。不能重绑到同名新节点。采样失败尝试恢复上一姿态，恢复失败会合并错误报告。

## 可选 Effect / 粒子

Effect 安装到文档时自动注册 `particles` 工厂，Core 与 Authoring 不引用 Effect。没有扩展时保留序列文本，播放明确报“未安装动画轨道扩展”。

- CSS 效果参数使用普通 css 轨，例如 `--urui-fx-tone-intensity`。页面先链接 `/urui-effect/Effect.css` 注册类型，再在关键帧里写参数。
- 粒子轨绑定 `urui-particles`，`source` 是资源解析器可解析的 Prefab 引用；空字符串沿用节点已有 src。Editor 使用页面相对路径或 `/Assets/...`。
- `seed` 为非零 uint；`speed` 在 `(0,4]`，单轮实际模拟 `duration * speed` 不超过 600 秒；粒子轨 easing 为 linear。
- 同一主时间按延迟、循环、往返计算粒子局部时间，固定种子从起点模拟。拖回之前时间可重现同平台同配置的结果。
- 接管期间暂停独立自动模拟，停止恢复原 Source、手动/自动模式、种子、时间和播放状态。粒子资产副本与标准材质约束沿用 Effect 文档，不支持 VFX Graph。

扩展 API：`ISequenceTrackFactory.Kind/Create(Element,IReadOnlyList<SequenceTrack>)`，创建一个节点/类型组的 `ISequenceTrackDriver.Sample(track,localTime,active)` / `Dispose()`。构造验证全部资源；before-start 为 inactive、track=null。可通过 `SequenceTrackRegistry.Register(document,factory)` 注册，返回可释放的文档级注册租约；CSS 类型保留给 Core。当前 v1 schema 仅接收 css / particles。

Effect 提供 `URUI.Effect.SequenceTrackFactory(resources)`；通常直接让 `EffectModule` 自动注册。`ParticlesElement.SequenceControlled` 供其他调试器判断是否被统一序列接管。

## Editor 作者接口

`AnimationEnvironment.Workspace` 是当前页面共享的 `AnimationWorkspace`；独立工作副本可 `new AnimationWorkspace(author)`。所有写方法最后一个参数是当前 `workspace.Revision`，过期拒绝；需要原生 Unity Undo 时使用 `SessionUndoBridge.Record(author, () => ...)`。

| API | 参数 / 结果 |
| --- | --- |
| `Open(path)` / `Create(revision)` / `Close()` | 打开明确后缀的序列 / 创建页面同名序列 / 解除活动序列选择 |
| `Definition / Path / Revision` | 从当前工作副本读取快照、路径与工作集版本 |
| `AddPreset(sourceKey,preset,start,duration,revision)` | 返回轨道 id；preset 为 popup/fade-in/fade-out/slide-in/pulse/empty |
| `AddParticles(sourceKey,source,start,duration,revision)` | 创建粒子轨；目标必须是粒子节点且有可解析 src |
| `SetTrack(id,Action<SequenceTrack>,revision)` | 修改时间、绑定、资源、曲线、静音等；锁定拒绝 |
| `SetLocked(id,locked,revision)` / `RemoveTrack(id,revision)` | 明确解锁 / 删除轨道，关键帧 CSS 保留 |
| `SetSequence(name,duration,loop,revision)` | 修改序列元数据 |
| `Keys(id)` / `SetKey(id,position,property,value,revision)` | 位置归一化 0..1；读取/编辑外部 CSS 关键帧 |
| `TransformKeys(selection,seconds,copy,remove,revision)` | 多轨多选一次事务；selection 为 KeySelection(track,position) |
| `RemoveKeys / MoveKeys` | 单轨便捷方法；MoveKeys 的 delta 为归一化位置差 |
| `KeyframeFile(track)` / `IsShared(id)` | 定位工作副本文件 / 是否被多轨共享 |

`AnimationEnvironment.SelectTrack/Seek/Play/Pause/Stop` 与人工/Agent 共用预览所有者；`BeginRecording(proxy)/EndRecording/Record(proxy,edits)` 共用录制入口。`CanvasGesture.Preview/Commit/Dispose` 分别临时预览、一笔提交、取消。`LayoutCommands.Preset(key,"row"|"column"|"center"|"fill")` 返回普通 CSS 作者命令。

主窗口、Inspector 和快捷键共用保存入口。有脏原生资产时先显示清单，资产默认不勾选，逐项明确 Apply；没有脏资产时直接保存源工作集。只读或尚未命名的资产先在工作台 Save As。

保存由 `AuthoringSession.Save()` 统一写页面、CSS、序列，并检查关联序列的绑定、关键帧和活动属性冲突。`SourceWorkspace.Create/EditBatch` 支持新文件工作副本，Undo/Redo 可撤销尚未落盘文件；保存阶段检查外部冲突、暂存并在失败时回滚，恢复日志保留在 Library。运行预览不会保存原生资产。

跨页面的旧写请求会因作者版本变化而拒绝；关键帧修改只查当前页面实际加载的 CSS/内联依赖，不能写到曾打开的其他页面。新文件以排他创建发布，保存逐项复核磁盘版本；回滚只还原仍属于本事务的内容，检测到外部改动时保留恢复日志和备份。文件系统不提供跨文件整体原子性，原生资产也逐项独立 Apply。
