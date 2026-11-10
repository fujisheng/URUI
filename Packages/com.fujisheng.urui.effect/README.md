# URUI.Effect

URUI 的可选效果库，提供声明式颜色、采样、转场、渐变、阴影、粒子和纹理表面处理。当前版本 **`0.4.1-preview.1`**，依赖 **URUI Core `0.7.1-preview.1`** 与 Unity 内置粒子模块。运行程序集 `URUI.Effect` 仅引用 `URUI.Runtime`，可以独立于 FUI 使用。

## 文档导航

| 要完成的工作 | 阅读文档 |
| --- | --- |
| 安装、挂载 CSS、运行第一个效果 | [快速开始](Documentation~/QUICK_START_ZH.md) |
| 预设、控制器、动画、联动、输入策略 | [C# 使用接口](Documentation~/API_ZH.md) |
| 查每个 CSS 参数、类型、默认值与 C# 对应项 | [CSS 参数参考](Documentation~/CSS_ZH.md) |
| 粒子、纹理着色、视频表面、绘制辅助与预览 | [渲染与资源](Documentation~/RENDERING_ZH.md) |
| 查完整类型、重载及默认参数 | [接口声明参考](Documentation~/REFERENCE_ZH.md) |
| 观察模式及极值效果 | [Effect Gallery](Samples~/EffectGallery/README.md) |
| 理解宿主、事件、资源和扩展协议 | [Core 文档](../com.fujisheng.urui/README.md) |

## 声明式效果

在 Document.Load 前创建 `EffectModule` 并挂载 `/urui-effect/Effect.css`。HTML 的完整文档需链接该资源，再在作者 CSS 中声明：

```css
.reward {
  --urui-effect: ui;
  --urui-fx-tone-filter: grayscale;
  --urui-fx-tone-intensity: .5;
  --urui-fx-color-filter: additive;
  --urui-fx-color: #336699;
  --urui-fx-color-intensity: .3;
}
```

`--urui-effect:ui` 可用于图片、文字或节点子树；叶子图片可直接着色，复杂节点使用有界覆盖图。图片波形使用 `--urui-image-effect:ui-wave`。标准 CSS filter 由 Core 提供。

通过 `module.GetController(element)` 取得节点控制器，Apply 应用 EffectPreset，Play 启动独立 EffectPlayback，Link 建立参数联动。结构、尺寸和静态皮肤留在 HTML/CSS，业务代码负责数据、播放意图和资源映射。

## 生命周期和验证边界

每份 Document 创建一个模块。模块注册发生在加载前；重载后重新查询节点和获取控制器。页面结束时先卸载文档，再释放模块和调用方持有的绑定/资源。已提交 GPU 资源通过宿主 fence 安全退役，不能按固定帧数提前销毁。

效果可能增加采样、几何、绘制次数和 RenderTexture。三档 blur 分别使用 25/81/169 次核采样；Shadow/Outline/Mirror 和复杂 Blend 也有额外成本。静态缓存、CPU 同步、GC、GPU 耗时及内存应分别测量，不作全场景零分配或通用性能优势承诺。

当前协议 **ABI8 / style4 / effect7 / filter3 / animation1**。真实运行主要验证 macOS ARM64/Metal；其他平台构建与设备运行范围见 Core 的[支持矩阵](../com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)和[插件清单](../com.fujisheng.urui/Documentation~/PLUGINS.json)。

第三方署名、来源和许可独立记录在 [ThirdPartyNotices.md](ThirdPartyNotices.md)，分发时保留原许可证。
