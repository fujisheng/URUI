# URUI — 面向 Unity 的文本优先 HTML/CSS UI 引擎

[English](README.md) | **中文**

URUI 将 **标准 HTML、CSS 与文本驱动的时间线动画** 引入 **Unity 6（`6000.3.22f1`）**，底层采用 C++ 原生桥接库（`URUIBridge` ABI13 / render-plan9 / element4，172 个正式导出函数）、FreeType 字形光栅化、GPU 参数页合批渲染，彻底避免为每个 UI 节点创建 `GameObject / MonoBehaviour` 的传统开销。

---

## 潮汐信标

《潮汐信标》完全由 AI 制作，涵盖界面设计、代码、原创美术、动效和音效。下方动图录自 Unity 实际运行画面。

![潮汐信标实机演示，展示引潮站、随鼠标倾转的舰员立绘、模组购买与装备、技能预演和战斗](docs/images/tideglass-beacon.gif)

在引潮站任命随航舰员、派出远航小队、签收回执并佩戴航籍称号。配装、三处信标、演练与战报共用本地存档。大厅两侧的操作键沿各自的折射玻璃排列，14 个按钮的键帽随指针起伏，命中面固定。独立工程展示实时玻璃折射、舱室转场、URP 合成和 URUI Effect 潮能波形与命中粒子。

高清截图可查看[引潮站](docs/images/tideglass-lobby.png)和[英雄详情](docs/images/tideglass-hero.png)。

---

## 核心特性

- **天然契合 AI 与文本 Diff**：界面结构、主题设计变量、交互状态与多轨动画序列全部使用可读文本（`.html` / `.css` / `.urui-sequence.json`），方便 Git 审阅、浏览器对照与 AI Agent 直接协作。
- **原生渲染计划与可控内存**：原生 C++ 端完成布局计算、有序渲染计划编译（`render-plan9`）、单批 8 纹理槽合批、不可变参数页上传及 GPU Fence 安全退役；稳态帧托管零 GC，并通过 `Surface.GetRenderingStatistics()` 在 Editor 与 Player 中实时观测渲染开销。
- **模块化三包设计**：
  - [**`com.fujisheng.urui`**（`0.8.0-preview.1`）](Packages/com.fujisheng.urui/README_ZH.md)：Core 运行时（`Surface`、`Document`、`Element`、全套类型化控件、万级虚拟列表 `ListView`、`CompiledDocumentAsset` 预编译字节码、`SequencePlayer`、`StaggeredReveal`、`BezierMotion`）。
  - [**`com.fujisheng.urui.effect`**（`0.5.0-preview.1`）](Packages/com.fujisheng.urui.effect/README_ZH.md)：CSS `--urui-fx-*` 滤镜、预设控制器 `EffectController`、声明式粒子 `<urui-particles>`、弧形文字 `ArcTextLayout`、`WaveEffect` 与离屏 `TextureEffectSurface`。
  - [**`com.fujisheng.urui.authoring`**（`0.1.0-preview.1`）](Packages/com.fujisheng.urui.authoring/README_ZH.md)：Editor 专属可视化检查器、源码树、动画时间轴与本机 HTTP Agent 接口 v1。

---

## 快速上手

用 Unity 6000.3.22f1 打开 [SampleProject](SampleProject)，选择 **Tools → URUI → Open Tideglass Beacon Sample**，然后进入 Play Mode。Built-in 工程可在 Package Manager 导入 Core 的 **Tideglass Beacon**。操作与源码位置见[样例说明](Packages/com.fujisheng.urui/Samples~/Tideglass%20Beacon/README_ZH.md)。

## 文档导航

### 全库技术指南（[English](docs/GETTING_STARTED.md) / 中文）

| 主题 | English | 中文 |
| --- | --- | --- |
| 安装与快速上手 | [GETTING_STARTED.md](docs/GETTING_STARTED.md) | [GETTING_STARTED_ZH.md](docs/GETTING_STARTED_ZH.md) |
| 整体架构与 `ABI13` 原生协议 | [ARCHITECTURE.md](docs/ARCHITECTURE.md) | [ARCHITECTURE_ZH.md](docs/ARCHITECTURE_ZH.md) |
| URP 与 Built-in 渲染管线集成 | [RENDER_PIPELINE_URP.md](docs/RENDER_PIPELINE_URP.md) | [RENDER_PIPELINE_URP_ZH.md](docs/RENDER_PIPELINE_URP_ZH.md) |
| 性能优化、合批与内存/显存防泄漏设计 | [PERFORMANCE_AND_MEMORY.md](docs/PERFORMANCE_AND_MEMORY.md) | [PERFORMANCE_AND_MEMORY_ZH.md](docs/PERFORMANCE_AND_MEMORY_ZH.md) |
| HTML 标签与 CSS 属性支持速查表 | [HTML_CSS_CHEATSHEET.md](docs/HTML_CSS_CHEATSHEET.md) | [HTML_CSS_CHEATSHEET_ZH.md](docs/HTML_CSS_CHEATSHEET_ZH.md) |
| AI 辅助工作流与实时编辑协议 | [AI_WORKFLOW.md](docs/AI_WORKFLOW.md) | [AI_WORKFLOW_ZH.md](docs/AI_WORKFLOW_ZH.md) |
| 原生构建、单元测试与研发规范 | [DEVELOPMENT.md](docs/DEVELOPMENT.md) | [DEVELOPMENT_ZH.md](docs/DEVELOPMENT_ZH.md) |
| 代码组织、目录职责与程序集边界 | [CODE_ORGANIZATION.md](docs/CODE_ORGANIZATION.md) | [CODE_ORGANIZATION_ZH.md](docs/CODE_ORGANIZATION_ZH.md) |

### 分包参考手册

- **Core 核心包 (`com.fujisheng.urui`)**：[README_ZH](Packages/com.fujisheng.urui/README_ZH.md) · [English README](Packages/com.fujisheng.urui/README.md) · [快速开始](Packages/com.fujisheng.urui/Documentation~/QUICK_START_ZH.md) · [API 参考](Packages/com.fujisheng.urui/Documentation~/REFERENCE_ZH.md)
- **Effect 特效包 (`com.fujisheng.urui.effect`)**：[README_ZH](Packages/com.fujisheng.urui.effect/README_ZH.md) · [English README](Packages/com.fujisheng.urui.effect/README.md) · [CSS 参数参考](Packages/com.fujisheng.urui.effect/Documentation~/CSS_ZH.md)
- **Authoring 编辑器包 (`com.fujisheng.urui.authoring`)**：[README_ZH](Packages/com.fujisheng.urui.authoring/README_ZH.md) · [English README](Packages/com.fujisheng.urui.authoring/README.md) · [Agent HTTP 接口](Packages/com.fujisheng.urui.authoring/Documentation~/agent-api_ZH.md)

---

## 原生平台与工具链支持

[`Packages/com.fujisheng.urui/Runtime/Plugins`](Packages/com.fujisheng.urui/Runtime/Plugins) 预置七个目标平台的同源编译产物（统一 `ABI13 / render-plan9 / element4`）：
`macOS ARM64`、`macOS x86_64`、`Windows x86_64`、`Android ARM64`、`Android x86_64`、`iOS ARM64（静态库）`、`WebGL wasm32（静态库）`。

Unity 验证基线版本为 `6000.3.22f1`。二进制清单与构建哈希见 [`PLUGINS.json`](Packages/com.fujisheng.urui/Documentation~/PLUGINS.json)，第三方开源许可说明见 [`Third Party Notices.md`](Packages/com.fujisheng.urui/Third%20Party%20Notices.md)。
