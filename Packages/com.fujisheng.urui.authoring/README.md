# URUI Authoring

Unity 内的 URUI HTML/CSS 作者工具：可视化画布、独立节点树、原生 Inspector、真实 Play 页面临时调参，以及可选本机 Agent API。需要 Unity 6000.3.22f1 与 URUI Core 0.8.0-preview.1。

先安装 Core，再从磁盘添加本包 `package.json`。Effect 为可选依赖；不要求 UnityCLI、FUI、Node.js 或外部 .NET CLI。安装 Effect 后通过 Core Editor 契约发现效果/粒子面板。

1. 从 Package Manager 导入 **Authoring Demo**，双击导入的 `Inventory.html`。
2. 用 `Window → URUI → Hierarchy / Canvas` 停靠窗口，选中节点后在 Unity Inspector 编辑。
3. 作者模式修改工作副本，按 Save 或聚焦作者窗口后 Cmd/Ctrl+S 才写文件。
4. Play 中选中真实 Document，再在 URUI Hierarchy 选择节点。此时修改真实实例，退出运行丢弃未应用调整。

- [使用、布局与运行时行为](Documentation~/index.md)
- [Agent HTTP 协议及完整客户端](Documentation~/agent-api.md)
- [Editor C# 接口与扩展](Documentation~/editor-api.md)

只在 `Window → URUI → Agent → Enable Local API` 启用本机服务。连接信息位于项目 `Library/URUI/Authoring/endpoint.json`，不要提交或公开其中的 token。

本包为 Editor-only 预览版本。画布复用 URUI 生产渲染，支持的 HTML/CSS 范围仍由 Core 决定；不提供浏览器 JavaScript 环境。首批图形运行验证针对 macOS ARM64 / Metal。许可证沿用仓库授权状态，见 [LICENSE.md](LICENSE.md)。
