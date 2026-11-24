# 代码组织与程序集边界

[English](CODE_ORGANIZATION.md) | **中文**

本仓库包含三个职责清晰、单向依赖的 UPM 包：Core（`com.fujisheng.urui`）、可选效果扩展（`com.fujisheng.urui.effect`）以及纯编辑器工作台（`com.fujisheng.urui.authoring`）。

## Core 核心包（`Packages/com.fujisheng.urui`）

| 相对路径 | 职责边界 |
| --- | --- |
| `Runtime/Hosting` | `Surface`、`SurfaceInput`、`SurfaceRenderingStatistics` 与跨管线渲染提交 |
| `Runtime/Documents` | `Document` 生命周期、`CompiledDocumentAsset`/HTML 加载、节点工厂与资源装配 |
| `Runtime/Elements` | 节点代际身份、深度优先局部查询、层级操作、行内样式覆盖与空间坐标转换 |
| `Runtime/Controls` | 类型化控件与固定行高虚拟列表 `ListView` |
| `Runtime/Events` | 同步三阶段事件传播、指针捕获、焦点、文本输入与布局变化事件 |
| `Runtime/Resources` | `TextResource`、`ImageAsset`、资源租约与 GPU 安全退役帮助类 |
| `Runtime/Styles`、`Runtime/Animation` | 计算样式快照、单调 `IUIClock`、`SequencePlayer`、`StaggeredReveal`、`BezierMotion` |
| `Runtime/Media`、`Runtime/Rendering` | 视频纹理输出及自定义 Painter / 参数化 Shader / SRP 渲染契约 |
| `Runtime/Internal` | `RenderHost`、C ABI13 原生互操作、`render-plan9` 合批、图层缓存与参数纹理页 |
| `Runtime/Debug` | 受 `URUI_DEBUG` 宏严格隔离的诊断实现（默认不编译进正式版） |
| `Runtime/Resources/URUI`、`Runtime/Plugins` | 包内内置 CSS/Shader/默认字体及七平台原生 `URUIBridge` 二进制 |
| `Editor` | 作者资产导入器、AOT `CompiledDocumentCompiler`、构建校验与示例入口菜单 |
| `Native~/Engine`、`Native~/ThirdParty` | 原生 C++ 核心源码与第三方基础库（`RmlUi`、`FreeType`） |

## Effect 扩展包（`Packages/com.fujisheng.urui.effect`）

- `Runtime/` — `EffectModule`、`EffectController`、`EffectPreset`、`EffectPlayback` 与完整 `EffectParameters`
- `Runtime/Elements` — `<urui-particles>` 声明式粒子节点
- `Runtime/Rendering` — `WaveEffect`、`TextureColorEffect`、`SurfaceEffect` 与 `TextureEffectSurface`
- `Editor/` — 预设资产编辑与编辑器预览拓展

## Authoring 工具包（`Packages/com.fujisheng.urui.authoring`）

全部代码位于 `URUI.Authoring.Editor` 仅编辑器程序集，包含源码保真 AST、可视化工作台窗口、时间轴编辑器以及本机 Agent HTTP 服务，永远不会打入游戏运行时 Player 包体；Core 与 Effect 也保持对 Authoring 零反向依赖。

详见 [Core 公开接口参考](../Packages/com.fujisheng.urui/Documentation~/REFERENCE_ZH.md) 与 [代码风格约定](../Packages/com.fujisheng.urui/Documentation~/CODE_STYLE_ZH.md)。
