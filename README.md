# URUI · UnityRmlUi

**为 AI 辅助开发而设计的 Unity UI：用 RML/RCSS 描述界面，用 C# 驱动交互。**

URUI 将 [RmlUi](https://github.com/mikke89/RmlUi) 接入 Unity。RmlUi 负责文档解析与布局，Unity 负责绘制和输入，业务代码通过 `URUI.Documents.Document` 查询与更新元素。

## 为什么做 URUI

传统 uGUI 界面的信息分散在场景层级、Prefab、Inspector 配置和对象引用中。AI 需要还原这些对象之间的关系，才能判断一段修改会影响哪里；布局问题也往往需要在编辑器中反复定位。

AI 更熟悉 Web 风格的文本结构与样式。URUI 将界面层级、布局和外观写成可直接阅读的 RML/RCSS，让人和 AI 都能在文本中理解界面、讨论修改并检查差异。C# 保留游戏状态和业务逻辑，使用局部节点查询、样式更新与点击回调连接界面。

- **易于理解**：结构和样式可集中阅读，减少对场景与 Inspector 隐含配置的依赖。
- **易于修改和审查**：界面变更体现为文本差异，便于 AI 提出局部修改，也便于开发者检查。
- **接入 Unity 业务**：`Document`、`Element` 与可释放订阅提供简洁的托管接口，无需业务代码管理原生句柄。

RML/RCSS 使用 RmlUi 的规则。URUI 不提供完整浏览器环境，也不承诺 HTML、CSS 或 JavaScript 的完整兼容；它不依赖 AI 服务，AI 辅助开发是使用方式。

## 安装与开始

在 Package Manager 选择 **Add package from git URL**，输入：

```text
https://github.com/fujisheng/URUI.git?path=Packages/com.fujisheng.urui
```

也可通过 **Add package from disk** 选择 `Packages/com.fujisheng.urui/package.json`，或将完整包目录作为嵌入包。

默认从包内 **Game Flow** 样例开始：Package Manager → URUI → Samples → **Game Flow** → Import，打开实际导入目录中的 `GameFlow.unity`。也可使用 **Tools > URUI > Open Game Flow Sample**。它展示加载、模拟登录、主界面、背包、设置和排行榜，全部必需资源随包提供；见 [Game Flow 运行说明](Packages/com.fujisheng.urui/Samples~/Game%20Flow/README.md)。

权威源码位于 `Packages/com.fujisheng.urui/Samples~/Game Flow`。Unity 标准导入到 `Assets/Samples/URUI/<包版本>/Game Flow`，项目内是导入副本；安装包不会自动导入 Samples。Hello URUI、Element Playground、Runtime Controls 和最小场景菜单保留为次要学习入口。[快速开始](Packages/com.fujisheng.urui/Documentation~/QUICK_START_ZH.md)先介绍默认样例，再说明最小 API 用法。

![Game Flow：Unity Editor 实际运行录制](Packages/com.fujisheng.urui/Documentation~/Images/game-flow.gif)

实际 Editor 流程演示，登录和排行榜为本地 mock。动图与包 README 共用一份资源。

```csharp
using URUI.Documents;
using URUI.Events;

document.LoadMarkup(rml);
var panel = document.Root.Q("panel");
var label = document.CreateElement("span");
label.Text = "Hello URUI";
panel.Add(label);
panel.Style.SetWidth(320);
var subscription = panel.RegisterCallback<ClickEvent>(evt => Debug.Log(evt.CurrentTarget.Name));
// 组件禁用时释放订阅：subscription.Dispose();
```

逐控件学习可另行导入 Runtime Controls；完整游戏 UI 流程请以 Game Flow 为入口。

## 当前支持

Unity 运行验证环境为 **6000.3.22f1 / macOS ARM64 Editor 与 ARM64 IL2CPP Player / Built-in / Metal**。包内包含同源 ABI3 / 元素协议 v1 插件：macOS ARM64/x86_64、Windows x64、Android ARM64/x86_64、iOS 设备 ARM64、WebGL wasm32；均已完成原生编译、架构与导出检查，iOS/WebGL 静态库通过完整链接检查。其他平台尚未做 Unity Player 或设备运行验证。

提供类型化控件与自定义 Element 工厂、泛型查询、内容容器、强类型 RCSS 与布局/坐标读取、同步事件传播、指针捕获、焦点键盘、文本输入与固定行高虚拟列表。一个活动文档，Unity 主线程访问；需 MainCamera、Input Manager (Old) 或 Both，图片开启 Read/Write，字体文件可读。

不包含数据绑定或 FUI 依赖；未来可通过可选适配器包装 Name/Parent、节点操作及订阅。当前不承诺其他 UI 系统的完整接口兼容；系统 IME、移动软键盘与设备验证边界见支持范围。调试宏 URUI_DEBUG 默认关闭。详见[支持范围](Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)。

## 文档

- [安装](Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md) · [快速开始](Packages/com.fujisheng.urui/Documentation~/QUICK_START_ZH.md)
- [C# API](Packages/com.fujisheng.urui/Documentation~/API_ZH.md) · [输入与事件](Packages/com.fujisheng.urui/Documentation~/EVENTS_ZH.md) · [生命周期与资源](Packages/com.fujisheng.urui/Documentation~/LIFECYCLE_ZH.md)
- [代码组织](docs/CODE_ORGANIZATION_ZH.md) · [架构](docs/ARCHITECTURE_ZH.md) · [开发与原生构建](docs/DEVELOPMENT_ZH.md) · [代码约定](Packages/com.fujisheng.urui/Documentation~/CODE_STYLE_ZH.md)

包包含 RmlUi 6.3 与 FreeType 2.13.3 源码。项目许可见 [LICENSE.md](Packages/com.fujisheng.urui/LICENSE.md)，第三方版权与许可见 [Third Party Notices](Packages/com.fujisheng.urui/Third%20Party%20Notices.md)。
