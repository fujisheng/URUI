# URUI Authoring 可视化编排工具包 (`com.fujisheng.urui.authoring`)

[English](README.md) | **中文**

当前版本：`0.1.0-preview.1` · 仅限 Editor 程序集（`URUI.Authoring.Editor`）· Unity 基线：`6000.3.22f1`

提供面向 URUI 的源码保真可视化工作台、Play 模式实时节点检查器、多轨序列动画编辑器（`*.urui-sequence.json`），以及供 AI Coding Agent 调用的本机 HTTP v1 接口。全部代码位于 Editor 程序集，零运行时包体入侵。

## 核心能力

- **源码保真编辑与 Live 实时检查**：可视化查看 DOM 节点树、读取计算样式与 Origin 来源、最小化修改 HTML 属性与 CSS 规则，并原生接入 Unity `Undo / Redo`。
- **统一动画时间轴工作流**：所见即所得编排 CSS 关键帧轨道与粒子轨，支持编辑态定帧预览、Play 态实时检视与独立资源草稿暂存（Explicit Apply/Save）。
- **本机 HTTP Agent API (`v1`)**：按需启用的本机 `127.0.0.1` JSON 服务（端点描述位于 `Library/URUI/Authoring/endpoint.json`），支持 Agent 按版本会话安全改写界面并实时抓取截图验收。

## 指南文档

| 专题 | English | 中文 |
| --- | --- | --- |
| 工作台与操作总览 | [index.md](Documentation~/index.md) | [index_ZH.md](Documentation~/index_ZH.md) |
| 统一序列动画工作流 | [animation.md](Documentation~/animation.md) | [animation_ZH.md](Documentation~/animation_ZH.md) |
| Editor C# 会话接口 | [editor-api.md](Documentation~/editor-api.md) | [editor-api_ZH.md](Documentation~/editor-api_ZH.md) |
| 本机 HTTP Agent 接口 (`v1`) | [agent-api.md](Documentation~/agent-api.md) | [agent-api_ZH.md](Documentation~/agent-api_ZH.md) |

## 示例与校验

- 包内示例：[`Samples~/Animation Demo`](Samples~/Animation%20Demo/README_ZH.md) · [`Samples~/Animation Effect Demo`](Samples~/Animation%20Effect%20Demo/README_ZH.md)
- 独立托管校验测试：`dotnet run --project Tools/Tests/Authoring/AuthoringTests.csproj`
