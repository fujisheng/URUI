# URUI Authoring

Unity 内的 URUI HTML/CSS 作者工具：控件库、节点树、可视化画布、样式表和内置 Inspector、真实 Play 页面临时调参，以及可选本机 Agent API。需要 Unity 6000.3.22f1 与 URUI Core 0.8.0-preview.1。

先安装 Core，再从磁盘添加本包 `package.json`。Effect 为可选依赖；不要求 UnityCLI、FUI、Node.js 或外部 .NET CLI。安装 Effect 后通过 Core Editor 契约发现效果/粒子面板。

1. 从 Package Manager 导入 **Authoring Demo**，双击导入的 `Inventory.html`。
2. 在 URUI Editor 工具栏点击「控件库」展开或收起左侧抽屉。单击带外观预览的卡片即可添加，也可以拖到画布或图层定位。选中控件后可在 URUI Inspector 修改常用属性。
3. 作者模式修改工作副本，按 Save 或聚焦作者窗口后 Cmd/Ctrl+S 才写文件。
4. 左右两侧可停靠的 URUI 层级和属性面板随 Edit Mode 和 Play Mode 自动切换。Play 中选择文档和节点即可修改真实实例，退出运行后切回作者工作副本。

- [使用、布局与运行时行为](Documentation~/index.md)
- [Agent HTTP 协议及完整客户端](Documentation~/agent-api.md)
- [Editor C# 接口与扩展](Documentation~/editor-api.md)

只在 `Window → URUI → Agent → Enable Local API` 启用本机服务。连接信息位于项目 `Library/URUI/Authoring/endpoint.json`，不要提交或公开其中的 token。

本包为 Editor-only 预览版本。画布复用 URUI 生产渲染，支持的 HTML/CSS 范围仍由 Core 决定；不提供浏览器 JavaScript 环境。首批图形运行验证针对 macOS ARM64 / Metal。许可证沿用仓库授权状态，见 [LICENSE.md](LICENSE.md)。
