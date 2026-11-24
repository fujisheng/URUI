# URUI 核心架构说明

[English](ARCHITECTURE.md) | **中文**

URUI 将 **作者文本表示（HTML/CSS）**、**原生排版与渲染计划编译（`URUIBridge`）** 以及 **Unity 托管调度（`Surface`、`Document`、`Element`、`RenderHost`）** 明确分层，兼顾文本可读性与原生级性能。

## 分层结构

```text
+-------------------------------------------------------------------+
|  作者文本资产：HTML5 / CSS3 / .urui-sequence.json / 字体 / 贴图     |
+-------------------------------------------------------------------+
             |                         |
             v（AOT 预编译 / 运行时加载）   v（仅限 Editor）
+---------------------------+  +------------------------------------+
| com.fujisheng.urui        |  | com.fujisheng.urui.authoring       |
| - Surface（共享上下文宿主）|  | - 源码保真 AST（HTML / CSS）         |
| - Document（多文档层叠）   |  | - 可视化层级 / 检查器 / 动画时间轴    |
| - 类型化控件与统一动画      |  | - 本机 Agent HTTP v1 协议           |
| - RenderHost（GPU 合批）   |  +------------------------------------+
+---------------------------+
             |
             v（C ABI13 · 172 个正式导出 · iOS/WebGL 使用 __Internal）
+-------------------------------------------------------------------+
| URUIBridge 原生 C++ 引擎                                           |
| - 裁剪派生自 RmlUi 的严格 DOM / Flexbox / CSS 层叠核心              |
| - FreeType 2.13.3 亚像素字形光栅化                                 |
| - render-plan9 有序渲染计划与参数纹理编译                           |
+-------------------------------------------------------------------+
             |
             v（可选效果扩展层）
+-------------------------------------------------------------------+
| com.fujisheng.urui.effect                                         |
| - 106 个 --urui-fx-* 参数属性                                     |
| - EffectController / WaveEffect / TextureEffectSurface / 粒子节点   |
+-------------------------------------------------------------------+
```

## 关键架构概念

### 1. 共享 `Surface` 与多 `Document` 分层架构
- 一个 `Surface`（`MonoBehaviour`）管理一个共享原生 UI 上下文、逻辑 CSS 像素视口（`Surface.Viewport`）、单调 UI 时钟（`Surface.UIClock`）、统一输入路由（`Surface.Input`）、GPU 资源池及提交通道。
- 多个 `Document` 可安全共享同一个 `Surface`：顶部导航 Shell、业务页面、模态详情窗（Modal）、Toast 与性能监控 HUD 各自独立维护 DOM 与资源生命周期，并通过 `Document.SetPresentation(layer, order, visible, modal)` 声明式切换层序与模态阻断。

### 2. 原生协议版本契约
当前七平台预编译二进制与托管层共同锁定（见 `PLUGINS.json`）：
- **桥接 ABI**：`13`（172 个 `urui_` 前缀导出函数）
- **渲染计划协议**：`9`（`render-plan9`）
- **元素状态协议**：`4`（`element4`）
- **借用纹理协议**：`v1`（零拷贝借用 Unity `Texture2D`，不触发 Read/Write 限制）
- **统一动画序列**：`v2`（支持 CSS `@keyframes` 与扩展粒子轨道工厂）

### 3. `render-plan9` 合批与显存提交链路
1. 原生引擎按单调时钟推进动画并计算增量布局。
2. `render-plan9` 基于空间重叠关系进行有序合批，内置 Shader 单批最多绑定 8 张纹理槽（混批图片与字形）；静态几何坐标首次生成后，位移/变色/UV 调整直接通过不可变 GPU 参数页纹理行更新。
3. `RenderHost` 录制有序绘制命令与轻量参数上传，所有共享资源由真实 GPU fence 守卫退役（每宿主最多保留 8 帧在途队列）。
4. `Surface.GetRenderingStatistics()` 允许开发者在 Editor 与正式 Player 中直接读取绘制数、原生指令数、几何字节与上传字节。

延伸阅读：[代码组织](CODE_ORGANIZATION_ZH.md) · [性能优化与内存控制](PERFORMANCE_AND_MEMORY_ZH.md) · [URP 渲染管线接入](RENDER_PIPELINE_URP_ZH.md)。
