# URUI Authoring 使用说明

## 安装与入口

本包只在 Unity Editor 编译，引用 `URUI.Runtime` 与 `URUI.Editor`。先安装 Core，再添加 Authoring 的 `package.json`；需要效果时再安装 Effect。使用本地包时可以在 `Packages/manifest.json` 配置三个独立 `file:` 路径。Git 安装应固定到实际包含这些包的已发布提交。

英文 Unity 使用 `Window/URUI/Editor` 打开作者工作台，中文 Unity 使用 `Window/URUI/编辑器`。中心工作台提供画布、布局、动画和源码，左右使用可停靠的 URUI Hierarchy 和 URUI Inspector。可用工具栏「层级和属性」重新打开两侧面板。独立画布和动画入口是 `Window/URUI/Canvas`、`Window/URUI/Animation`。Project 双击 `.html` 打开工作副本。导入 Authoring Demo 后可直接打开 `Inventory.html`。作者文件必须位于项目 Assets 或嵌入式 Packages，包缓存和符号链接不直接写入。只读包样例先通过 Package Manager Import 到 Assets。

## 编辑器语言

界面跟随 Unity 当前的 Editor 语言设置：中文使用简体中文文本，英文使用英文；其他语言回退英文。关闭 Unity 的编辑器本地化时按英文处理，不读取操作系统语言，也不增加单独的 URUI 语言设置。语言变化会刷新窗口标题、菜单、节点树、Inspector、动画和可选 Effect 窗口；语言切换不修改作者工作副本。

真实节点 id、资源名、文件路径、用户文字、CSS 属性及枚举值、动画绑定和 Agent 协议字段保留原文。未命名提示、参数标签、工具提示和操作说明才参与翻译。无需安装额外 Localization 包。Unity 内部语言读取接口不可用时安全回退英文；静态英文菜单保留为兼容后备。

## 作者模式

HTML/CSS 保存界面结构、基础样式与动画关键帧；`.urui-sequence.json` 只保存轨道绑定、时间与资源引用。编辑器不生成另一份私有 UI 树 JSON。布局模式的节点树和 Inspector 修改工作副本，动画模式未录制时属性调整只作临时预览；画布显示真实 URUI 渲染结果，Save 才写文件。

URUI Hierarchy 显示图层和样式表。URUI Editor 左侧的控件库抽屉可通过工具栏展开或收起，卡片展示标准控件的外观示例。单击卡片会把控件添加到选中的容器内，或放到选中的普通控件之后。在搜索框按回车可添加第一项。也可以拖到画布、容器内部或图层行边缘来指定位置。新控件自动选中，实际外观由页面样式决定。控件库的「项目片段」列出当前页面引用的片段。URUI Inspector 的「常用属性」按控件类型提供内容、大小与位置和外观，切换「全部属性」可编辑绑定、变换及其余 CSS。

样式表面板按文档引用顺序显示 CSS。选中规则后在右侧编辑选择器及声明，已导入的包资源以只读显示。节点 Inspector 的局部样式可提取到可写的直接引用样式表，提取前会显示声明与目标文件。计算来源显示当前 URUI 能提供的来源类别。工具栏“源码”可查看工作副本中的 HTML 和 CSS。

在 ArtOfTD 中，作者画布使用 `/URUI/` 等项目挂载点解析资源。源码保存后，Unity 的导入器会尝试更新已有编译资产。需要更新游戏资源清单时，使用 `Tools/URUI Optimization/Export Asset Manifest`。

作者预览支持 Built-in 与 SRP（包括 URP），无需在游戏 Renderer 中额外添加预览专用 Feature。`/urui/base.css`、`controls.css`、`behaviors.css` 和项目 `/URUI/...` Resources 引用保持原 URI；项目 HTML/CSS 依赖从工作副本读取，中文及空格资源名在 Unity 资产边界解析。`rel="urui-fragment"` 引用的现有模板片段可作为依赖加载，活动页面仍按完整 HTML 校验。页面或依赖加载失败时显示错误并停用该页画布，避免操作上一页的画面。

图层名称优先使用真实 HTML `id`、可见文字和 class。悬停可查看标签与完整父级路径。默认简化无名 HTML 外层和 `head/style/link/meta` 等非界面内容。「完整结构」可查看全部节点。搜索会直接列出命中项，支持 `.class` 和 `#id` 前缀，按回车选中第一项。通过画布或 Inspector 选中筛选之外的节点时，树会恢复层级并定位节点。

节点树支持选中、多选、搜索、复制粘贴、删除、重命名 id、拖动排序和改父级。`Cmd/Ctrl+D` 可在当前节点后创建副本。副本分配唯一的 HTML id，副本内部的标签和锚点引用也随之更新。引用原始 id 的业务代码和共享 CSS 规则仍指向原节点。

选中 URUI 节点时，属性在 URUI Inspector 中显示，Unity 默认 Inspector 清空。直接从 Unity 的 Hierarchy 或 Project 选择对象时，默认 Inspector 显示该对象。

在工作台按 `Cmd/Ctrl+S` 保存。存在未保存的 HTML、CSS 或序列时，换页会提供保存并打开、放弃修改并打开、取消三个选项。

画布支持缩放、平移、矩形手柄、定位预设和分辨率。拖动期间只预览，松手提交一笔 Undo，Esc 或失去焦点取消。`W/E/R/T` 切换移动、旋转、缩放、矩形工具；`F` 聚焦选中节点；中键平移，滚轮缩放；Shift 拖动框选。Presets 提供常用分辨率和安全区参考，Safe Area 的 L/T/R/B 像素边距可编辑，只画引导线，不自动改写布局。快捷键只在作者窗口接收。Edit 用于选中和拖动；Interact 把输入发给 `Surface.Input`，实际业务事件可能因此触发。

首次打开页面时，作者预览读取 `ProjectSettings/URUIAuthoring.json` 中的 `width` 和 `height`（像素）。配置缺省时使用 1280×720。为页面选过的尺寸会优先恢复。ArtOfTD 将项目默认配置设为 2532×1170。

自由布局使用 absolute/fixed 定位。符合条件的 `auto` 偏移可以直接拖动，松手后按实际布局写入像素值。Flex 和普通流由布局系统驱动，使用 Inspector 调整间距、顺序、对齐和伸缩。选择定位预设会明确转换布局，支持九点与横/纵/双向拉伸，并保持当前矩形。CSS 包含块可以是定位祖先或视口。复杂变换或无法可靠还原的布局可通过高级 CSS 编辑。

Inspector 使用 Unity 原生字段和折叠面板：布局、变换、图像、文字、交互、控件、边距、容器排列等。图像/文字/输入参数按真实节点类型出现；输入框、勾选框、滑动条和进度条分别显示适用字段，包含纯文本的普通 div 也显示文字面板，结构化按钮可快捷选择真实文字子节点，面板只是已有属性的编辑入口，不代表添加运行时组件。尺寸约束、内外边距等次要面板默认折叠。画布工具栏可以直接切换移动、旋转、缩放和矩形工具。

数值编辑保留 `%/px/em` 等原单位；`auto`、变量和复杂表达式继续使用原始输入。简单 `translate(x,y) rotate(zdeg) scale(x,y)` 支持分量调节，保留函数顺序与其他分量；矩阵、重复函数及无法安全拆分的变换保留原始表达式。内外边距面板提供四边联动，同步同一值和单位，形成一次 Undo。多选只调整数字时，各节点保留各自单位；显式切换单位才统一单位。批量或联动操作遇到表达式、自动值或重要声明时，不做部分写入；需要替换时通过属性菜单启用原始表达式编辑，或明确粘贴完整值。属性右侧菜单支持复制、粘贴、移除覆盖与切换原始表达式编辑；“继/改/共/临/录”提示计算值、局部覆盖、共享规则、运行临时值或录制状态。悬浮可查看原始属性名和级联来源。

常用面板始终修改当前选中节点；共享规则选择移入“高级 · HTML / CSS 与共享样式”，仅影响该区域的 CSS 字段。高级 CSS 默认折叠。Inspector 区分局部声明、实际计算值和只读几何。清除覆盖应恢复未设置状态，不能把计算后的 px 当成原始值。修改共享 CSS 会影响该规则匹配的其他节点，操作前确认目标规则。现有来源诊断显示级联类别，不能据此声称知道每个胜出声明的文件和行号。

外部 Agent 或编辑器修改文件后，工具校验并刷新干净的工作副本；存在未保存修改时报告 `source_conflict`。无效外部内容保留最后有效预览。单文件保存使用替换，跨文件保存有暂存与恢复日志，新文件使用排他创建，提交时逐项检查外部改动；回滚发现外部写入时保留恢复资料而不覆盖它；跨多个文件不承诺文件系统级原子提交。

已知插入、删除、移动命令记录明确的节点对应关系，移动及其 Undo/Redo 会保留原选择；被删除节点暂时失效，Undo 可恢复。全文替换、外部重载和无法证明对应关系的结构修改会使旧选择失效。工具不会按相同 id 猜测原节点。若保存中途失败，工具尝试还原已写文件；无法完整还原时保留 `Library/URUI/Authoring/Recovery/*.journal` 与源目录的 `.urui-*.bak`，错误会给出日志位置。恢复前对比当前文件与备份，避免覆盖外部新修改。

未保存的作者文本在进入 Play、脚本重载或正常退出前写入 Library 的恢复副本，恢复前核对源文件版本；冲突时保留副本。恢复不会自动保存源文件，恢复后撤销历史从新会话开始。原生资产工作副本属于临时会话，切换模式或重编译前显式 Apply/Save As。

## Play Mode

1. 两侧 URUI 面板在进入 Play 时自动显示运行时数据。也可以从 `Window/URUI/Runtime Debugging`（中文「运行调试」）打开同一对面板。两个面板默认在 Unity 原生 Hierarchy 和 Inspector 所在的停靠区，可以拖动标签到其他位置。
2. URUI Hierarchy 显示已加载的 URUI 文档，并可通过上方菜单切换。搜索支持节点名、标签、文字、`#id` 和 `.class`，包括动态节点。
3. 选择节点后，URUI Inspector 会显示该节点的属性，Unity 默认 Inspector 清空。临时调整立即作用到 Game View，暂停时也可以修改并刷新布局。退出 Play 后，两侧面板切回作者工作副本。

`Window/URUI/Hierarchy` 可单独打开共用的 URUI 层级面板。

Play 修改默认不写 HTML/CSS、Prefab、Material、EffectPreset 或 Scene。关闭工具不提前撤销本次调参，也不卸载业务页面；退出 Play 或显式重置结束临时会话。节点销毁/页面重载使旧身份失效，不会凭相同 id 找新节点。

清理按属性独立进行；例如游戏已为纯文本节点加入结构化子节点时，工具保留游戏结构并报告该文字不能安全还原，仍继续清理其他属性和图片租约。C# 可读取 `LiveSession.RestoreDiagnostics`。

业务代码、绑定、动画、important 或 transition 仍按 Runtime 规则生效。游戏下一帧回写属性时显示实际结果，不做隐藏的强制覆盖。控件值 setter 默认静默；点击/键盘预览用于触发实际交互。

“审阅并应用…”只保存明确选择且有可靠作者映射的改动，显示目标文件。动态新建、模板克隆或内部节点缺少直接来源时可以调试和复制值，不能导出整个运行时 DOM 覆盖 HTML。源文件已变化时拒绝应用。焦点、捕获、当前粒子数量等运行状态不保存。

Live 图片通过 `image` 属性接收 Texture2D 资产路径，使用临时解析租约；不会重载游戏页面或改写 Document 的持久化资源配置。保存到源码后的路径仍需项目自己的 Runtime 资源解析器支持。

## Effect 与粒子

安装 Effect 后扩展自动出现，无需 Authoring 引用 Effect 程序集。页面需要引用对应的 `/urui-effect/Effect.css`；它提供参数注册、粒子装饰器和样式。Editor 预览解析此内置资源与 `/urui/base.css`。

Inspector 可编辑效果 CSS 参数，或打开 EffectPreset、Material、ParticleSystem Prefab 的隔离副本。复杂粒子模块复用 Unity 公开 Inspector，常用模块也有稳定 Agent 参数。原始资产只有 Apply/Save As 才更新；Prefab 的材质参数按独立 Material 保存，临时材质不能序列化进 Prefab。

编辑粒子材质时选中实际使用该材质的粒子节点，再打开其原始 Material；预览替换该节点中匹配的主体/尾迹槽，Discard 恢复引用。未使用该材质的节点会报告绑定错误。原生 Undo/Redo 同步更新画面、工作副本 dirty 状态与 Agent 的 assetRevision。

“特效与粒子 Effect”使用折叠面板，粒子节点默认展开，普通节点按需展开。粒子支持播放、暂停、重播、单步、时间定位和同源刷新。时间定位固定随机种子并从起点模拟，范围 0..600 秒；单步 0..1 秒。确定性只验收同平台同配置。自定义 simulation space 和不满足 URUI 材质契约的源会被拒绝；首版不包括 VFX Graph。

粒子 Prefab 仅允许 Transform、ParticleSystem、ParticleSystemRenderer；材质必须包含 `_UruiPaintOpacity`、`_UruiClipEnabled`、`_UruiClipRect`，尾迹还需要第二材质。EffectPreset 工作台使用生产 EffectPreview；页面画布用于检查最终布局、裁剪和合成。

## 生命周期与边界

URUI 当前只支持一个活动 Surface。编辑期预览拥有自己的 Preview Scene、相机和 RenderTexture；进入 Play 前释放，运行中附着游戏 Surface，退出后恢复作者预览。Preview Scene 不隔离原生静态上下文。

Play 节点结构首版只读；作者模式可编辑结构。运行时通过节点树选择，首版不拦截 Unity Game View 私有鼠标实现。Live 截图复用实际游戏相机，当前限 Built-in 渲染管线，包含该相机的实际场景和 UI。远程 Player、协同编辑和完整 RectTransform 兼容不在本版范围。

验证入口见仓库 `Tools/verify-authoring.py`，默认使用独立 Unity 工程。源代码编译、Editor 图形运行和 Player/设备证据应分别判断。

## 统一动画

轨道标题显示真实目标绑定 id，Inspector 的动画属性显示当前采样姿态，可通过菱形按钮加入当前轨道（包括 `$root` 根轨道）；时间轴属性菜单提供常用参数，CSS 按钮保留原始属性名输入。

详见 [动画编辑与运行 API](animation.md)，包括预设、录制、多选关键帧、缓动、序列 schema、粒子同步与资源归属。Agent 调用见 [本机 API](agent-api.md#动画命令)。动画预览和运行使用相同的原生 CSS 插值器；任意前后定位不会触发自然完成事件。

统一保存清单会显示脏源文件和未应用的原生资产。资产默认不选中，需逐项 Apply；Play 临时属性仍从运行调试对比界面明确应用，保存按钮不会自动导出运行时 DOM。
