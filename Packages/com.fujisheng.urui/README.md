# URUI · UnityRmlUi

URUI 为 AI 辅助开发 Unity UI 提供可读的文本界面：使用 RML/RCSS 描述层级、布局和外观，使用 C# 查询与更新元素。RmlUi 处理文档与布局，Unity 提交绘制并提供输入。

uGUI 的场景、Prefab、Inspector 与对象引用需要共同还原界面关系。RML/RCSS 将更多界面信息表达为文本，便于人和 AI 阅读、修改与审查。URUI 采用 Web 风格的工作方式，实际语法与能力以 RmlUi 为准，不提供完整浏览器或 JavaScript 运行环境。

本文档对应仓库 `main` 分支。安装见[安装指南](Documentation~/INSTALL_ZH.md)。从 Package Manager 导入 **Hello URUI**，或运行 **Tools > URUI > Create Minimal UI Scene** 即可开始。

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

导入 **Runtime Controls** 样例，打开随附的 `RuntimeControls.unity` 场景进入 Play Mode，或将 `RuntimeControls` 添加到空 GameObject。样例使用包内控件基础样式，覆盖全部现有控件、只读/延后字段、键盘导航、滚动条和千行虚拟列表。API 通过 `Document.Create<T>()` / `Q<T>()`、值变化订阅、ContentContainer 与 Document.Input 连接业务，详见 Documentation~/API_ZH.md。
