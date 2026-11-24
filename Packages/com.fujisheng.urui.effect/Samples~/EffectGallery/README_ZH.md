# URUI Effect Gallery 示例图库

[English](README.md)

独立着色器、转场、粒子与 `SurfaceEffect` 效果展示样例，对应 `com.fujisheng.urui.effect` `0.5.0-preview.1` 与 `com.fujisheng.urui` `0.8.0-preview.1`（原生协议 `ABI13 / render-plan9 / element4 / 纹理协议 v1`）。

> 如需体验结合 URP 3D 场景合成、Blind Box 多轨序列粒子与交互滑杆滑块的完整多模块示例，可直接用 Unity `6000.3.22f1` 打开仓库根目录的 [`SampleProject/`](../../../../SampleProject)。

## 导入与运行

1. 在 **Package Manager → URUI Effect → Samples → Declarative Effect Gallery → Import** 导入样例。
2. 在场景中确认存在启用的 `Main Camera`，创建空物体并挂载 `EffectGallery` 组件，在 Inspector 中绑定以下资源：

| 字段 | 样例资源 |
| --- | --- |
| `html` / `style` | `Gallery.html` / `Gallery.css` |
| `white` / `shader` | `white.png` / `Gallery.shader` |
| `source` / `pattern` / `gradient` | `input.png` / `pattern.png` / `gradient-lut.png` |
| `particlePrefab` | 可选纯 `ParticleSystem` prefab；留空时自动创建固定种子粒子源 |

3. 进入 Play Mode。全部效果节点由 `EffectModule`、`EffectController` 与 CSS `--urui-fx-*` 变量驱动，无每节点 `MonoBehaviour`。

## 覆盖能力

- 覆盖 13 组滤镜/视觉模式共 83 个枚举项（`default / extreme / disabled` 三态对照，`GalleryCoverage.json`）。
- 演示 `EffectController.Apply` 预设、`Link` 联动、`Play/Pause/Seek` 控制与 `<urui-particles>` 的 `data-urui-*` 生命周期策略。
- 支持 Built-in RP 与 URP（配合在相机渲染后调用 `Surface.SubmitRendering` 的 RendererFeature）。

更多说明见 [C# 接口](../../Documentation~/API_ZH.md)、[CSS 参数参考](../../Documentation~/CSS_ZH.md)、[渲染与资源](../../Documentation~/RENDERING_ZH.md) 及 [ThirdPartyNotices](../../ThirdPartyNotices.md)。
