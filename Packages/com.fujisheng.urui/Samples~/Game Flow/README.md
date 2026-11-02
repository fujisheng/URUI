# North / 07 · Game Flow

可导入的横屏游戏 UI 流程：加载 → 登录 → 主界面 → 背包 / 设置 / 排行榜 → 返回。主界面还包括出发准备、玩家资料弹窗。登录和排名是明确的本地 mock，没有真实账号、网络后端或战斗场景。

## 导入和运行

1. 使用 Unity 6000.3.22f1 / Built-in；本轮实际验证为 macOS ARM64 Editor。包目前需要 MainCamera、旧 Input Manager/Both，或自行接入公开 Document.Input。
2. Package Manager → URUI → Samples → **Game Flow** → Import，完整导入目录并保留 Resources 和 .meta。这是包的主要推荐样例，安装包不会自动导入。
3. 打开导入目录的 GameFlow.unity，进入 Play Mode。场景没有相机时，样例创建自己拥有的相机和音频对象。
4. 输入任意 2–18 字符呼号，演示码 **demo**。Online/Offline 只模拟服务状态，不改系统网络。

权威源码为包内 `Samples~/Game Flow`。Unity 标准导入目录为 `Assets/Samples/URUI/<包版本>/Game Flow`，以实际导入路径为准；打开其中的 `GameFlow.unity`。也可使用包菜单 **Tools > URUI > Open Game Flow Sample** 导入并打开。项目内 Assets 是副本，不作第二份源码维护；移动完整导入目录仍可运行。

## 六页功能

- 加载：真实资源检查、实际初始化阶段/进度、失败反馈与重试。Inspector 的 Simulate Startup Failure Once 可演示服务初始化失败。
- 登录：呼号和公开演示码校验、离线失败、异步忙碌和恢复。
- 主界面：独立场景/人物资源、玩家等级/经验/货币 HUD、四个功能入口和主要 Deploy 行动。两个弹窗会隔离背景焦点，关闭后恢复。
- 背包：40格容量、10件原创物品、分类页签与下拉、搜索/清除、选中详情、装备切换/战力变化、消耗品使用。材料只读，没有合成或战斗。
- 设置：Audio / Display / Connection 分类，确认音开关/音量/试听、密度、动作对比度、模拟离线。Apply 写入样例专用 `URUI.GameFlow.Settings.v1` PlayerPrefs；离开丢弃未保存草稿。不会修改全局游戏音量或网络。
- 排行：本地异步快照、刷新、Friends 空状态、Offline 错误/重试和固定个人排名。切页取消请求，旧结果不能写回新页面。

## 设计、切图和真实控件

本轮先生成完整 Home/Inventory 设计稿，用户确认方向后才切图、重建独立资源并还原。其余四页也先生成同系统整稿。完整设计图仅作视觉基准，不在运行时显示完整 UI，更没有整屏图加透明按钮。

包内 **40张独立PNG** 包含纯场景、透明人物、十件物品、头像/功能图标、空槽/选中框，以及面板九切片和按钮三切片。切片保留边角，公开尺寸API按实际布局分配各片尺寸；全部文字、输入、按钮、列表和进度都是实际 RML/RCSS 控件。普通文字和按钮标题未烘焙到图片。

`Resources/URUI/GameFlow/Rml/*.rml.txt` 是六个独立上游格式页面，`Game.rcss.txt` 是共享外部样式，`Catalog.json.txt` 是数据。`.txt` 后缀用于 Unity TextAsset 导入。C# 只负责导航、公开控件交互、数据、取消与必要的图片切片组合，没有内嵌页面模板、data binding、FUI、internal 或 DEBUG 接口。

资源清单、尺寸/哈希、视觉框/切图来源、Alpha及处理方式见 `asset-manifest.json.txt`；实际 imageGen 提示词及来源见 `Images/SOURCE_DESIGN_FIRST.txt`。原生透明图保留原始输出另存，生产图执行了明确记录的 Alpha bbox 裁边/contain/留边；微小图标的确定性后处理单独记录，没有声称原生图未经处理。字体为随包分发的 OFL LatoLatin，许可证在 THIRD_PARTY_NOTICES.txt。

开发工作区保留完整整稿、Source Crop、mask、编辑目标、原生生成图、1:1 bbox检查页和处理报告：`Artifacts/GameFlow/DesignFirst/20261102` 与 `FUI-CLI/GameFlowDesign`。这些是可审阅的制作记录，运行和导入均不依赖它们。流程参考 fui-cli 的 asset-generation-workflow、bbox-review、process-ui-assets；URUI 使用真实 Editor 组合和布局证据替代 FUI Web→UGUI prefab交付，未宣称执行其整套UGUI流程或逐框用户确认。

## 生命周期与当前边界

每次切换取消旧异步任务、解绑页面事件并卸载旧文档。禁用时清理本次独占字体/RCSS缓存与自有相机/音频。页面淡入、局部相对位移、按钮 hover/press、进度过渡和装备反馈可中断，不使用会触发缺失 clip-mask 的根变换。

`GameFlowResources` 将字体和RCSS放入本次独占 temporaryCachePath，并为当前 native demo.rml 基址解析真实外部样式链接。图像用公开 Document.Images 显式映射。它是有限的样例适配器，不是通用虚拟文件系统；未验证所有平台的缓存文件访问。

缺少基本字体、RCSS、Loading页、场景或引导框切片时，组件记录 StartupError，完整重导入后从组件菜单 Retry Game Flow startup 重试。进入加载页后的物品、共享按钮/图标和目录缺失会给出加载页重试。

布局、材质和图像按确认稿还原，但不是逐像素复制：生成图字形使用真实Lato文字代替，表单/下拉和部分次级状态采用实际公开控件。变换祖先裁剪、圆角溢出裁剪、高级filter/layer、通用字体回退和资源解析等组件缺口仍存在；完整清单见 Source/docs/COMPONENT_COMPLETENESS_ZH.md。英文示例不代表中文字体、系统IME、移动软键盘、手柄或无障碍已完成。

## 验证

在 Source 运行 `python3 Tools/Tests/GameFlow/verify_assets.py` 与 `dotnet run --project Tools/Tests/GameFlow/GameFlowTests.csproj`。`python3 Tools/verify-game-flow.py` 将整个Sample导入独立目录，运行现有工程短Editor验收，输出本地截图、布局JSON和报告到 Artifacts/GameFlow。当前为94项Editor断言、18项模型断言通过。没有新增Player、设备或性能验收，也没有自动发布。
