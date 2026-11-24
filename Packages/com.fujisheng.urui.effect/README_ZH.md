# URUI Effect 特效扩展包 (`com.fujisheng.urui.effect`)

[English](README.md) | **中文**

当前版本：`0.5.0-preview.1` · 依赖 Core：`0.8.0-preview.1` · Unity 基线：`6000.3.22f1`

为 URUI 提供声明式 Shader 效果（`--urui-fx-*`）、可复用预设与时间线播放（`EffectPreset` / `EffectController`）、圆弧排版（`ArcTextLayout`）、网格水波（`WaveEffect`）、离屏材质表面（`TextureEffectSurface`）以及声明式粒子节点（`<urui-particles>`）。不依赖 FUI 或 UGUI，也无需额外原生插件。

## 核心能力

- **`--urui-effect: ui`**：提供 106 个已注册 CSS 属性，涵盖 Tone、Color、Sampling、Transition（Fade/Dissolve/Shiny/Melt/Burn）、Detail、Edge、Gradation、Shadow/Outline/Mirror、Blend 与 Flip。
- **`EffectPreset` 与 `EffectController`**：在不污染作者 CSS 与 DOM 节点的前提下，应用预设、临时叠加调试租约、快照联动或驱动高精度 Tween 动画。
- **`<urui-particles>` 与统一序列轨**：以标准 CSS 盒模型排版 Unity 粒子 prefab，并可由 `SequencePlayer` 与其它 UI 动画同步时间轴。
- **`TextureEffectSurface`**：借助 `EffectModule.BindSurface` 将自定义材质渲染结果绑定为高频复用纹理表面。

## 文档索引

| 文档 | English | 中文 |
| --- | --- | --- |
| 快速上手 | [QUICK_START.md](Documentation~/QUICK_START.md) | [QUICK_START_ZH.md](Documentation~/QUICK_START_ZH.md) |
| C# 效果接口 | [API.md](Documentation~/API.md) | [API_ZH.md](Documentation~/API_ZH.md) |
| CSS `--urui-fx-*` 与 Wave 参数表 | [CSS.md](Documentation~/CSS.md) | [CSS_ZH.md](Documentation~/CSS_ZH.md) |
| 粒子、表面纹理与渲染资源 | [RENDERING.md](Documentation~/RENDERING.md) | [RENDERING_ZH.md](Documentation~/RENDERING_ZH.md) |
| 完整公开接口声明 | [REFERENCE.md](Documentation~/REFERENCE.md) | [REFERENCE_ZH.md](Documentation~/REFERENCE_ZH.md) |

## 示例入口

- 包内画廊示例：[`Samples~/EffectGallery`](Samples~/EffectGallery/README_ZH.md)（覆盖 13 组 83 项滤镜状态）
- 完整 URP 示范工程：[`../../SampleProject/`](../../SampleProject)（包含 Effect Atelier 与盲盒粒子舞台）

第三方版权声明见 [`ThirdPartyNotices.md`](ThirdPartyNotices.md)。
