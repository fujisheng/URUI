# URUI · UnityRmlUi

URUI 为 AI 辅助开发 Unity UI 提供可读的文本界面：使用 RML/RCSS 描述层级、布局和外观，使用 C# 查询与更新元素。RmlUi 处理文档与布局，Unity 提交绘制并提供输入。

uGUI 的场景、Prefab、Inspector 与对象引用需要共同还原界面关系。RML/RCSS 将更多界面信息表达为文本，便于人和 AI 阅读、修改与审查。URUI 采用 Web 风格的工作方式，实际语法与能力以 RmlUi 为准，不提供完整浏览器或 JavaScript 运行环境。

本文档对应仓库 `main` 分支。安装见[安装指南](Documentation~/INSTALL_ZH.md)。默认推荐从 **Game Flow** 开始：在 Package Manager 选择 URUI，在 Samples 中导入 **Game Flow**，打开导入目录中的 `GameFlow.unity` 进入 Play Mode。也可使用 **Tools > URUI > Open Game Flow Sample** 导入并打开；安装包本身不会自动导入样例。

权威源码在包内 `Samples~/Game Flow`。Unity 标准导入到 `Assets/Samples/URUI/<包版本>/Game Flow`，以实际返回路径为准；项目内 Assets 是导入副本。完整 RML/RCSS、图片、字体、脚本、场景和程序集配置随样例提供，不需要开发工作区或 Artifacts。见 [Game Flow 运行说明](Samples~/Game%20Flow/README.md)。

Hello URUI、Element Playground、Runtime Controls 和 **Create Minimal UI Scene** 保留为次要学习入口。

![Game Flow：Unity Editor 实际运行录制](Documentation~/Images/game-flow.gif)

动图由 Unity Editor 的实际运行画面连续采帧生成，展示加载、登录、主界面、物品选择/装备、设置、排行榜和返回。登录及排行使用本地 mock；960×540、约25秒，无 Player 构建。

业务代码引用程序集 `URUI.Runtime`，按职责使用 `URUI.Documents`、`URUI.Elements`、`URUI.Controls` 和 `URUI.Events`：通过 `Document` 加载文档，查询或创建有类型节点，再更新文本、结构、样式和事件订阅。

- [快速开始](Documentation~/QUICK_START_ZH.md)
- [C# API](Documentation~/API_ZH.md)
- [事件与解绑](Documentation~/EVENTS_ZH.md)
- [资源和生命周期](Documentation~/LIFECYCLE_ZH.md)
- [支持矩阵与限制](Documentation~/LIMITATIONS_ZH.md)
- [路线图](Documentation~/ROADMAP_ZH.md)
- [代码约定](Documentation~/CODE_STYLE_ZH.md)

当前验证目标：**Unity 6000.3.22f1 / macOS ARM64 / Built-in / Metal**。运行需要单个活动文档、Unity 主线程、`MainCamera`、旧 Input Manager、可读取的字体文件与开启 Read/Write 的图片。其他目标的原生插件已编译并随包提供，Unity 运行验证仍待完成；已提供控件、键盘、表单与组合串接口；真实系统 IME、默认移动软键盘接入、SRP 和设备验证范围见支持矩阵。

项目许可见 [LICENSE.md](LICENSE.md)，第三方许可见 [Third Party Notices](Third%20Party%20Notices.md) 与 `Documentation~/Licenses/`。

## 类型化控件

主要样例 **Game Flow** 展示加载、模拟登录、主界面、背包、设置和排行榜的组合流程，使用独立 RML/RCSS 资源。需要逐项学习控件时，再导入 **Runtime Controls** 并打开 `RuntimeControls.unity`。API 通过 `Document.Create<T>()` / `Q<T>()`、值变化订阅、ContentContainer 与 Document.Input 连接业务，详见 Documentation~/API_ZH.md。
