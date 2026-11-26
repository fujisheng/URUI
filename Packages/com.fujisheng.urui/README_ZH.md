# URUI Core 核心包 (`com.fujisheng.urui`)

[English](README.md) | **中文**

当前版本：`0.8.0-preview.1` · Unity 基线：`6000.3.22f1` · 原生协议：`ABI13 / render-plan9 / element4 / 172 个导出函数`

URUI Core 在 Unity 内嵌入高性能原生 HTML/CSS 排版与渲染核心（`URUIBridge`）：用 HTML 描述页面结构，CSS 管理样式、变量主题与动画关键帧，托管 C#（`Surface`、`Document`、`Element` 与强类型控件）驱动业务交互，全程无每节点 `MonoBehaviour`。

## 包内可导入样例

| 样例 | 用途 | 管线兼容 |
| --- | --- | --- |
| **潮汐信标** | 舰员随航、离线派遣、航籍授勋、三处信标、整备与战报。大厅展示立体键帽、实时折射和舱室转场 | Built-in 或独立 URP 工程 |
| **Hello URUI** | 最小入门：托管 `Document` 加载、样式读写与点击事件订阅释放 | Built-in & URP |
| **Element Playground** | 显式节点创建、局部查询、层级移动/重挂载、类名切换与销毁生命周期 | Built-in & URP |
| **Runtime Controls** | 全套表单控件、方向键焦点导航与可复用虚拟列表 | Built-in & URP |

快速运行：在 Unity Editor 菜单点击 **Tools → URUI → Open Tideglass Beacon Sample**（自动导入并打开 `Tideglass.unity`），或直接打开仓库根目录 [`../../SampleProject/`](../../SampleProject) 查看包含 URP 合成与特效的完整工程。

## 专题文档导航（[English](Documentation~/QUICK_START.md) / 中文）

| 专题 | English | 中文 |
| --- | --- | --- |
| 安装与原生库部署 | [INSTALL.md](Documentation~/INSTALL.md) | [INSTALL_ZH.md](Documentation~/INSTALL_ZH.md) |
| 快速开始 | [QUICK_START.md](Documentation~/QUICK_START.md) | [QUICK_START_ZH.md](Documentation~/QUICK_START_ZH.md) |
| Document 与 Element API | [API.md](Documentation~/API.md) | [API_ZH.md](Documentation~/API_ZH.md) |
| 控件与 ListView 虚拟滚动 | [CONTROLS.md](Documentation~/CONTROLS.md) | [CONTROLS_ZH.md](Documentation~/CONTROLS_ZH.md) |
| 默认控件皮肤机制 | [DEFAULT_CONTROLS.md](Documentation~/DEFAULT_CONTROLS.md) | [DEFAULT_CONTROLS_ZH.md](Documentation~/DEFAULT_CONTROLS_ZH.md) |
| 输入、焦点与同步事件流 | [EVENTS.md](Documentation~/EVENTS.md) | [EVENTS_ZH.md](Documentation~/EVENTS_ZH.md) |
| 生命周期、资产借用与 GPU 退役 | [LIFECYCLE.md](Documentation~/LIFECYCLE.md) | [LIFECYCLE_ZH.md](Documentation~/LIFECYCLE_ZH.md) |
| 样式、关键帧与序列动画 | [STYLES_ANIMATION.md](Documentation~/STYLES_ANIMATION.md) | [STYLES_ANIMATION_ZH.md](Documentation~/STYLES_ANIMATION_ZH.md) |
| SRP 提交、自定义绘制与统计指标 | [RENDERING.md](Documentation~/RENDERING.md) | [RENDERING_ZH.md](Documentation~/RENDERING_ZH.md) |
| 代码风格约定 | [CODE_STYLE.md](Documentation~/CODE_STYLE.md) | [CODE_STYLE_ZH.md](Documentation~/CODE_STYLE_ZH.md) |
| 支持范围与限制边界 | [LIMITATIONS.md](Documentation~/LIMITATIONS.md) | [LIMITATIONS_ZH.md](Documentation~/LIMITATIONS_ZH.md) |
| 演进规划 | [ROADMAP.md](Documentation~/ROADMAP.md) | [ROADMAP_ZH.md](Documentation~/ROADMAP_ZH.md) |
| 完整公开 C# 接口声明 | [REFERENCE.md](Documentation~/REFERENCE.md) | [REFERENCE_ZH.md](Documentation~/REFERENCE_ZH.md) |
| 编辑器校验与回归入口 | [Tests~/Editor/README.md](Tests~/Editor/README.md) | [Tests~/Editor/README_ZH.md](Tests~/Editor/README_ZH.md) |

## 机读规范与版本信息

- HTML/CSS 支持范围清单：[`Documentation~/HtmlCssSupport.json`](Documentation~/HtmlCssSupport.json)
- 原生插件校验与导出清单：[`Documentation~/PLUGINS.json`](Documentation~/PLUGINS.json)
- 变更日志：[`CHANGELOG.md`](CHANGELOG.md) · 第三方许可：[`Third Party Notices.md`](Third%20Party%20Notices.md) · License：[`LICENSE.md`](LICENSE.md)
