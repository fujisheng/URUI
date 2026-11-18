# URUI

URUI 使用 HTML/CSS 描述 Unity 游戏界面的结构、布局、皮肤和动画，通过 C# 操作类型化节点、处理事件并连接游戏业务。当前提供三个 Unity Package：

| 包 | 版本 | 文档 |
| --- | --- | --- |
| `com.fujisheng.urui` | `0.8.0-preview.1` | [Core 使用文档](Packages/com.fujisheng.urui/README.md) |
| `com.fujisheng.urui.effect` | `0.5.0-preview.1` | [Effect 使用文档](Packages/com.fujisheng.urui.effect/README.md) |
| `com.fujisheng.urui.authoring` | `0.1.0-preview.1` | [可视化编辑器与运行时 Inspector](Packages/com.fujisheng.urui.authoring/README.md) |

## 安装

在 Unity Package Manager 中依次选择 Add package from git URL，先安装 Core，再按需安装 Effect 与 Authoring：

```text
https://github.com/fujisheng/URUI.git?path=Packages/com.fujisheng.urui#main
https://github.com/fujisheng/URUI.git?path=Packages/com.fujisheng.urui.effect#main
https://github.com/fujisheng/URUI.git?path=Packages/com.fujisheng.urui.authoring#main
```

可用经过验证的提交 SHA 替换 main 固定版本。也可从磁盘选择各包 package.json。Effect 依赖 Core 与 Unity 内置粒子模块，不依赖 FUI。Core 的业务绑定/导航由独立适配包承担。

安装 Authoring 后，从 **Window → URUI → Editor** 打开编辑器。启用 Effect 后，Authoring 会发现效果编辑扩展。

正式部署可将 HTML、CSS 和模板在编辑阶段编译为 `CompiledDocumentAsset`，保留源码供编辑和浏览器预览。多个独立 Document 可共享同一个 Surface。

## 快速开始

Package Manager → URUI → Samples → Game Flow → Import，打开导入的 GameFlow.unity 并进入 Play Mode。样例展示加载、模拟登录、主页、背包、设置和排行榜，资源随包提供，登录和排行榜为本地 mock。

![Game Flow：Unity Editor 实际运行录制](Packages/com.fujisheng.urui/Documentation~/Images/game-flow.gif)

此动图为 Editor 录制，不能替代 Player 验收。一个活动 Surface 管理共享视口、时钟、输入与绘制，多个独立 Document 分别拥有 DOM 和资源。节点通过 Document.Create<T>/Q<T> 操作，原始输入入口为 document.Surface.Input。

## 使用文档

- [安装](Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md) · [Core 快速开始](Packages/com.fujisheng.urui/Documentation~/QUICK_START_ZH.md) · [Effect 快速开始](Packages/com.fujisheng.urui.effect/Documentation~/QUICK_START_ZH.md)
- [Document/Element](Packages/com.fujisheng.urui/Documentation~/API_ZH.md) · [控件](Packages/com.fujisheng.urui/Documentation~/CONTROLS_ZH.md) · [输入与事件](Packages/com.fujisheng.urui/Documentation~/EVENTS_ZH.md)
- [样式与动画](Packages/com.fujisheng.urui/Documentation~/STYLES_ANIMATION_ZH.md) · [资源生命周期](Packages/com.fujisheng.urui/Documentation~/LIFECYCLE_ZH.md) · [渲染和视频](Packages/com.fujisheng.urui/Documentation~/RENDERING_ZH.md)
- [Effect C# API](Packages/com.fujisheng.urui.effect/Documentation~/API_ZH.md) · [Effect CSS 参数](Packages/com.fujisheng.urui.effect/Documentation~/CSS_ZH.md) · [粒子与表面处理](Packages/com.fujisheng.urui.effect/Documentation~/RENDERING_ZH.md)
- [Core 接口声明](Packages/com.fujisheng.urui/Documentation~/REFERENCE_ZH.md) · [Effect 接口声明](Packages/com.fujisheng.urui.effect/Documentation~/REFERENCE_ZH.md)
- [可视化作者工具](Packages/com.fujisheng.urui.authoring/Documentation~/index.md) · [独立 Agent API](Packages/com.fujisheng.urui.authoring/Documentation~/agent-api.md)
- [架构](docs/ARCHITECTURE_ZH.md) · [代码组织](docs/CODE_ORGANIZATION_ZH.md) · [开发与原生构建](docs/DEVELOPMENT_ZH.md)

## 支持范围

主要运行验证环境为 Unity 6000.3.22f1 / macOS ARM64 / Metal，包含 Built-in、项目 URP 接入和相应 IL2CPP 回归 Player。当前协议为 ABI11 / render-plan8 / element3 / style4 / effect7 / filter3 / animation1。其他平台的构建、静态审计和设备运行范围分别记录在[支持矩阵](Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)与[插件清单](Packages/com.fujisheng.urui/Documentation~/PLUGINS.json)。

HTML/CSS 是明确的支持子集；没有完整浏览器或 JavaScript 环境。系统 IME、移动软键盘和无障碍边界以支持矩阵为准。项目与第三方许可见 [Core LICENSE](Packages/com.fujisheng.urui/LICENSE.md)、[Core 第三方许可](Packages/com.fujisheng.urui/Third%20Party%20Notices.md)和 [Effect 第三方许可](Packages/com.fujisheng.urui.effect/ThirdPartyNotices.md)。
