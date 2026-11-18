# Editor C# 接口

所有接口限 Unity Editor 主线程。业务 Player 代码继续只引用 URUI.Runtime；本包不进入 Player。Editor 自动化优先使用 HTTP 协议或这些服务，不反射 Unity/URUI 私有字段。

## 作者会话：URUI.Authoring.Sessions.AuthoringSession

| 接口 | 含义 |
| --- | --- |
| `AuthoringSession(string projectRoot)` | 创建有根目录约束的工作集 |
| `Open(string path)` | 读取 HTML、校验、构建保真模型；不保存 |
| `File`, `Model`, `Workspace` | 当前作者文件、源码节点和工作集 |
| `Id`, `Revision`, `StructureRevision` | 会话、工作集版本和结构身份版本 |
| `Set(key, property, value, expectedRevision)` | 单属性命令；null 表示移除覆盖 |
| `Execute(IReadOnlyList<AuthoringEdit>, expectedRevision)` | 批量属性或单个结构命令 |
| `CopyNodes(keys)` / `RemoveNodes(keys,expectedRevision)` | 复制/原子删除多个节点；按源码顺序，只保留选中的最外层祖先 |
| `EditFile(path,text,expectedRevision)` | 校验后替换工作副本文本 |
| `SetSharedRule(path,ruleStart,property,value,expectedRevision)` | 在确定 CSS 规则内最小修改 |
| `Undo()`, `Redo()` | 重放源码历史，不倒写计算样式 |
| `Reload(bool discardLocal)` | 校验外部版本；默认不放弃未保存内容 |
| `Save()` | 显式写入工作集；检查所有目标的磁盘版本 |
| `Apply(selectedChanges,sourceMap,expectedRevision)` | 只将可靠映射的 Live 选中修改转成作者命令 |

`SourceDocument.Parse` 保留原文，提供范围、属性、结构和最小修改方法。它是词法保真模型；语义由现有 HtmlCssValidator 判断。`CssSource.Rules/SetRuleDeclaration` 处理规则范围，`Declarations/SetDeclaration` 处理声明范围。所有偏移为 UTF-16。

`AuthoringEdit` 字段为 `Operation`、`Node`、`Property`、`Value`、`Parent`、`Index`。Operation 默认 set；结构命令为 insert/remove/move，Index 默认 -1（末尾）。每批结构命令只接受一个操作，避免解析后使用旧 NodeKey。

`SourceWorkspace` 支持多个 HTML/CSS 工作副本、内容 hash 冲突检查、Undo/Redo 和可恢复保存。不要绕过会话向 File.Text 或 Unity 资产直接写入。

## LiveSession

`new LiveSession()` 创建独立临时日志；正常 UI 使用 `AuthoringEnvironment.Live`，而非另建会话。

- `Set(node,property,value,expectedRevision)` 和 `Execute(IReadOnlyList<LiveEdit>,expectedRevision)` 修改固定真实节点。
- `Undo/Redo` 重放真实 setter；`Reset` 恢复仍有效目标上的工具覆盖，保留原单位与未设置状态。
- `Changes()` 返回当前调参相对基线的差异；并不等于完整运行 DOM 快照。
- `Dispose` 结束日志，不 Load/Unload 游戏 Document。旧节点失效不会重新按 id 查询。

`ElementSelectionProxy.ForLive/ForAuthor` 按需创建不保存的 ScriptableObject，作为原生 Selection 目标。`SetMany(targets,property,value)` 使用同一会话的批量命令；`SetProperties(IReadOnlyList<ElementSelectionProxy> targets,IReadOnlyDictionary<string,string> values)` 将多个属性应用到这些目标，形成一次 Undo。动画模式转为临时姿态或一组录制关键帧，Play 模式只修改真实节点的临时状态；自动化传入的是明确的完整属性值，不执行 Inspector 数字字段的单位推断。`SessionUndoBridge.Record` 把命令游标接入 Unity Undo；记录代理对象本身无法恢复 DOM。

控件属性包含 `value`、`text`、图片 `src`、输入框 `name/placeholder/readonly/maxlength`、范围控件 `min/max/step`、Label 的 `for`。Checkbox/Radio 的作者 value 对应 checked 属性存在性，Textarea 对应标签内文本；Live value 使用相应控件的静默 setter。HTML 布尔属性以存在性为真，移除使用 null。

## Core Editor 扩展契约

契约位于 Core Editor 的 `URUI.Editor.Inspection`，可选包只引用这个程序集，不能引用 Authoring 实现包。

- `RuntimeInspectionBridge.Inspect/Root/Pick` 返回受控 InspectedNode；不暴露 native handle/token。
- `InspectedNode` 提供固定 Id、IsValid、Tag、Name、Parent、Child、Read/Write、Computed/Origin 和布局；只读状态不可强行写入。
- `RuntimeInspectionBridge.Refresh` 受控刷新真实页面；`CreatePreview` 只用于自有编辑预览。
- `IAuthoringContext` 传递会话、模式、选中节点、版本、Set、Refresh 和诊断入口。
- `IAuthoringExtension` 提供属性 schema、内置文本资源、AttachPreview、AdvancePreview、Inspector、工作台、命令与 Dispose。通过 TypeCache 发现无参具体实现。

AttachPreview 发生在作者文档加载前，应返回可释放租约。扩展不得在 Live 模式创建第二 Surface、重载业务页面或直接修改共享资产。程序集重载与 Play 会话结束须清理临时绑定。

## Effect 新增运行能力

- `EffectModule.TryGet(document,out module)`：附着已安装模块，不创建第二模块。
- `EffectModule.PropertyDefinitions`：实际注册的样式参数，供编辑 schema 使用。
- `EffectController.OverridePreset(preset)`：临时基础预设租约，保留原 CSS 级联和播放逻辑。
- `ParticlePainter.RefreshSource()`：同引用源改动后原子重建并退役旧副本。
- `ParticlePainter.OverrideMaterial(source,replacement)`：只替换该实例中引用 source 的材质槽；释放恢复，replacement 的所有权仍归调用方。
- `ParticlePainter.SetManualSimulation(enabled,seed)`、`Seek(time,seed)`、`Step(delta)`、`SimulationTime`：显式时间驱动。
- `ParticlesElement` 提供对应的 Seek/Step/RefreshSource 与临时 OverrideSource；不修改源 Prefab。
- `ParticlesElement.OverrideMaterial` 将源材质映射到工作副本，不改写 Renderer/Prefab 的持久化引用。
- `EffectAssetSession` 只编辑 Working 副本，使用 Apply/SaveAs/Dispose 明确资源保存归属。

## 统一动画与布局

具体签名、所有权、序列格式与失败规则见 [动画 API](animation.md#editor-作者接口)。

新增 `SourceWorkspace.Create(path,text,expectedRevision)`；`SourceChange(path,text,fileRevision,create:true)` 可将新 CSS/序列与已有 HTML 合为一次 EditBatch。`AuthoringSession.EditWorkset(changes,expectedRevision)` 校验并发布同一工作集。文件限制扩展为 HTML、CSS 与明确的 `.urui-sequence.json`，未保存新文件也进入 SourceRecovery。

`AuthoringSession.NodeIdentity(key)` / `ResolveIdentity(identity)` 提供本会话稳定身份。只对已知命令和其历史提供明确对应关系；全文替换/重载重新分配，不能用于持久化业务绑定。普通属性修改可直接 patch 预览；结构与依赖变化重载。

`AuthoringEnvironment.SetMode(WorkspaceMode)` 统一布局/动画/运行调试上下文。`AuthoringEnvironment.Save()` 是人工保存入口，存在脏资产时打开清单；`PendingAssets()` 返回扩展资产 id、path、kind、revision，`ApplyAsset(change)` 只应用明确选定版本。Core Editor 的可选 `IAuthoringAssetWorkspace.PendingAssets/ApplyAsset` 不要求 Authoring 引用 Effect。

## Editor 本地化

`URUI.Editor.Localization.EditorText` 位于 Core Editor，Authoring 与可选 Effect Editor 共用，无反向依赖。

- `CurrentLanguage` / `IsChinese`：Unity 有效编辑器语言；非中文显示英文，关闭 Editor 本地化时为英文。
- `Tr(key)` / `Format(key,params object[])`：本包文本表中的显示文本及带参数文本。只用于编辑器标签，不能把节点 id、文件路径或 CSS 值作为翻译键。
- `NodeName(tag,id)`：有 id 时原样返回，只本地化缺失名称的提示。
- `Message(message)`：本包已知诊断文本的显示翻译，保留错误代码和附加目标名称。
- `LanguageChanged`：更新视图缓存，不触发源文件命令。`Title(window,key)` 同步窗口标题。

文本表与显式 `Translate(key,bool chinese)` 可在不更改 Unity 全局语言设置的情况下验证两种语言。Unity 原生语言设置只读；内部接口不可用时使用英文。
