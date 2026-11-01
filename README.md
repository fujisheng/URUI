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

导入 **Hello URUI** 样例，将 `HelloUrui` 添加到空 GameObject 后进入 Play Mode；或使用 **Tools > URUI > Create Minimal UI Scene** 创建最小文档场景。[快速开始](Packages/com.fujisheng.urui/Documentation~/QUICK_START_ZH.md)包含完整的 RML 和业务脚本。

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

## 当前支持

Unity 运行验证环境为 **6000.3.22f1 / macOS ARM64 Editor / Built-in / Metal**。包内包含同源 ABI3 插件：macOS ARM64/x86_64、Windows x64、Android ARM64/x86_64、iOS 设备 ARM64、WebGL wasm32；均已完成原生编译、架构与导出检查，iOS/WebGL 静态库通过完整链接检查。其他平台尚未做 Unity Player 或设备运行验证。

提供文档根、局部 Name/Class 查询、动态节点与层级、普通文本/属性、类名、行内 RCSS 样式、二维变换、图片映射和普通点击通知。一个活动文档，Unity 主线程访问；需 MainCamera、Input Manager (Old) 或 Both，图片开启 Read/Write，字体文件可读。

不包含数据绑定或 FUI 依赖；未来可通过可选适配器包装 Name/Parent、节点操作及订阅。当前不模拟完整 VisualElement/UQuery、浏览器或键盘/IME/表单体系。调试宏 URUI_DEBUG 默认关闭。详见[支持范围](Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)。

## 文档

- [安装](Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md) · [快速开始](Packages/com.fujisheng.urui/Documentation~/QUICK_START_ZH.md)
- [C# API](Packages/com.fujisheng.urui/Documentation~/API_ZH.md) · [点击事件](Packages/com.fujisheng.urui/Documentation~/EVENTS_ZH.md) · [生命周期与资源](Packages/com.fujisheng.urui/Documentation~/LIFECYCLE_ZH.md)
- [代码组织](docs/CODE_ORGANIZATION_ZH.md) · [架构](docs/ARCHITECTURE_ZH.md) · [开发与原生构建](docs/DEVELOPMENT_ZH.md) · [代码约定](Packages/com.fujisheng.urui/Documentation~/CODE_STYLE_ZH.md)

包包含 RmlUi 6.3 与 FreeType 2.13.3 源码。项目许可见 [LICENSE.md](Packages/com.fujisheng.urui/LICENSE.md)，第三方版权与许可见 [Third Party Notices](Packages/com.fujisheng.urui/Third%20Party%20Notices.md)。
