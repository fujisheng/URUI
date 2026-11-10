# Effect CSS 参数参考

对应 Effect `0.4.1-preview.1`。本页的注册参数类型和初始值取自包内 [Effect.css](../Runtime/Resources/URUI.Effect/Effect.css)，C# 字段取自 [EffectParameters](../Runtime/EffectParameters.cs)。使用前挂载并链接 `/urui-effect/Effect.css`，模块在 Load 前注册，见[快速开始](QUICK_START_ZH.md)。

## 激活方式

| 声明 | 用途 |
| --- | --- |
| `--urui-effect: ui` | 图片、文字或整个节点子树的通用效果 |
| `--urui-image-effect: ui-wave` | 原图波形 |
| `--urui-image-effect: surface-output` | 显式 BindSurface 的处理输出 |
| `data-urui-effect-root="id"` | 当前页面内的坐标根；移除后回退自身，不能跨文档找根 |

仅设置 fx 参数不会自动激活 ui。参数不继承，具体作用到声明了效果的节点；对整个子树的效果可能先捕获合成。单层 border-image 可使用图片效果，图案坐标独立于九宫格采样 UV。

## 数值、图片与颜色

注册为 `<number>` 的值不带单位；布尔关键字使用 true/false；声明 px 长度时带 px。数值必须有限，范围还受运行时参数读取器约束。常用 intensity/progress/softness/range/fade 为 0..1，sampling-width 为 .5..10；SamplingScale 在计算参数中限制为 .01..100。不要把初始值当作任意输入都合法的保证。

Vector2 在 CSS 中拆成 -x/-y，EffectRange 拆成 -min/-max。纹理使用 `url(...)` 或 none；相对 URL 以实际 CSS 源文件为基址，附加图片由 IEffectResourceResolver 获取。C# 的 GradationGradient 为完整 Gradient，CSS gradation-gradient 对应 LUT 图像，不是相同的对象类型。

颜色组的 hue/saturation/value/contrast/brightness/alpha 标量默认为 auto，表示保留对应 Color 分量。HsvModifier 使用有符号色相/饱和度/明度分量，Contrast 使用对比度/亮度分量，不应把它们当作普通显示 RGB。需要 HDR 或完整 Gradient 时通过 EffectPreset 保存。

## 常用组合

```css
.disabled-icon {
  --urui-effect: ui;
  --urui-fx-tone-filter: grayscale;
  --urui-fx-tone-intensity: 1;
}
.reward {
  --urui-effect: ui;
  --urui-fx-transition-filter: dissolve;
  --urui-fx-transition-texture: url("noise.png");
  --urui-fx-transition-progress: .5;
  --urui-fx-transition-width: .2;
}
```

C# 临时数值更新可用 `element.Style.SetNumber("--urui-fx-transition-progress", value)`；长期播放通常用 EffectController.Play，预设用 Apply。优先级与覆盖规则见 [C# 接口](API_ZH.md)。

## 模式与成本

Tone 为色调，Color 为颜色混合，Target 限定受影响颜色区域，Sampling 为采样，Transition 为转场，Detail 为附加细节，Edge 为边缘，Gradation 为渐变，Shadow 为阴影/描边/镜像，Blend 为最终输出混合，Flip 为标志位。各模式的合法 CSS 名称完整列在下方 syntax。

BlurFast/Medium/Detail 使用 25/81/169 次核采样。Pixelation、RgbShift、Sobel 等的零点按各自算法定义，不保证所有组 intensity=0 都等同关闭。Shadow/Outline/Mirror 增加几何；特殊 Blend 对整个节点输出生效，复杂输入可能先捕获。父级裁剪和 mask/opacity 仍生效；allow-to-modify-mesh-shape:false 禁止原图扩边。

## Wave 参数

默认 `.wave-image` 容器提供下列值，其直接 img 子节点启用 ui-wave。网格尺寸、频率等必须满足程序有效范围，完整纹理之外的组合范围见[渲染与资源](RENDERING_ZH.md)。

| 属性 | 默认 | 含义 |
| --- | --- | --- |
| `--urui-wave-fixed-edge` | top | 固定边或中心，top/bottom/left/right/center |
| `--urui-wave-displacement` | horizontal | 位移轴，horizontal/vertical |
| `--urui-wave-columns` / `--urui-wave-rows` | 24 / 8 | 整数网格细分，columns 2..128，rows 1..64 |
| `--urui-wave-amplitude` | 18px | 非负位移幅度 |
| `--urui-wave-frequency` | 6 | 波形频率 |
| `--urui-wave-speed` | 1.3 | 时间速度 |
| `--urui-wave-secondary` | .5 | 次级波权重 |
| `--urui-wave-fixed-part` | .65 | 固定部分，0–0.95 |
| `--urui-wave-cross-influence` | .25 | 交叉影响 |
| `--urui-wave-cross-scale` | .01 | 交叉尺度 |
| `--urui-wave-time-offset` | 0 | 时间偏移 |

## 完整注册参数

下表逐项列出全部 106 个 `--urui-fx-*` 注册属性。Syntax 为 CSS 注册语法，Default 为 CSS 初始值，EffectParameters 为对应 C# 字段；点号后表示 Vector2/EffectRange/Color 分量。initial 恢复相应作者属性的初始语义，不表示删除作者优先级。

### tone

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-tone-filter` | `none \| grayscale \| sepia \| negative \| retro \| posterize` | `none` | `ToneFilter` |
| `--urui-fx-tone-intensity` | `<number>` | `1` | `ToneIntensity` |

### color

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-color-filter` | `none \| multiply \| additive \| subtractive \| replace \| multiply-luminance \| multiply-additive \| hsv-modifier \| contrast` | `none` | `ColorFilter` |
| `--urui-fx-color-intensity` | `<number>` | `1` | `ColorIntensity` |
| `--urui-fx-color` | `<color>` | `white` | `Color` |
| `--urui-fx-color-glow` | `false \| true` | `false` | `ColorGlow` |
| `--urui-fx-color-hue-shift` | `<number> \| auto` | `auto` | `Color.r` |
| `--urui-fx-color-saturation-shift` | `<number> \| auto` | `auto` | `Color.g` |
| `--urui-fx-color-value-shift` | `<number> \| auto` | `auto` | `Color.b` |
| `--urui-fx-color-contrast-shift` | `<number> \| auto` | `auto` | `Color.r` |
| `--urui-fx-color-brightness-shift` | `<number> \| auto` | `auto` | `Color.g` |
| `--urui-fx-color-alpha` | `<number> \| auto` | `auto` | `Color.a` |

### target

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-target-mode` | `none \| hue \| luminance` | `none` | `TargetMode` |
| `--urui-fx-target-color` | `<color>` | `white` | `TargetColor` |
| `--urui-fx-target-range` | `<number>` | `.1` | `TargetRange` |
| `--urui-fx-target-softness` | `<number>` | `.5` | `TargetSoftness` |

### sampling

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-sampling-filter` | `none \| blur-fast \| blur-medium \| blur-detail \| pixelation \| rgb-shift \| edge-luminance \| edge-alpha` | `none` | `SamplingFilter` |
| `--urui-fx-sampling-intensity` | `<number>` | `.5` | `SamplingIntensity` |
| `--urui-fx-sampling-width` | `<number>` | `1` | `SamplingWidth` |
| `--urui-fx-sampling-scale` | `<number>` | `1` | `SamplingScale` |

### allow

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-allow-to-modify-mesh-shape` | `false \| true` | `true` | `AllowToModifyMeshShape` |

### transition

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-transition-filter` | `none \| fade \| cutoff \| dissolve \| shiny \| mask \| melt \| burn \| pattern` | `none` | `TransitionFilter` |
| `--urui-fx-transition-progress` | `<number>` | `.5` | `TransitionProgress` |
| `--urui-fx-transition-width` | `<number>` | `.2` | `TransitionWidth` |
| `--urui-fx-transition-softness` | `<number>` | `.2` | `TransitionSoftness` |
| `--urui-fx-transition-rotation` | `<number>` | `0` | `TransitionRotation` |
| `--urui-fx-transition-range-min` | `<number>` | `0` | `TransitionRange.Min` |
| `--urui-fx-transition-range-max` | `<number>` | `1` | `TransitionRange.Max` |
| `--urui-fx-transition-auto-play-speed` | `<number>` | `0` | `TransitionAutoPlaySpeed` |
| `--urui-fx-transition-reverse` | `false \| true` | `false` | `TransitionReverse` |
| `--urui-fx-transition-keep-aspect-ratio` | `false \| true` | `true` | `TransitionKeepAspectRatio` |
| `--urui-fx-transition-pattern-reverse` | `false \| true` | `false` | `TransitionPatternReverse` |
| `--urui-fx-transition-color-glow` | `false \| true` | `false` | `TransitionColorGlow` |
| `--urui-fx-transition-texture` | `<image>` | `none` | `TransitionTexture` |
| `--urui-fx-transition-texture-scale-x` | `<number>` | `1` | `TransitionTextureScale.x` |
| `--urui-fx-transition-texture-scale-y` | `<number>` | `1` | `TransitionTextureScale.y` |
| `--urui-fx-transition-texture-offset-x` | `<number>` | `0` | `TransitionTextureOffset.x` |
| `--urui-fx-transition-texture-offset-y` | `<number>` | `0` | `TransitionTextureOffset.y` |
| `--urui-fx-transition-texture-speed-x` | `<number>` | `0` | `TransitionTextureSpeed.x` |
| `--urui-fx-transition-texture-speed-y` | `<number>` | `0` | `TransitionTextureSpeed.y` |
| `--urui-fx-transition-color-filter` | `none \| multiply \| additive \| subtractive \| replace \| multiply-luminance \| multiply-additive \| hsv-modifier \| contrast` | `multiply-additive` | `TransitionColorFilter` |
| `--urui-fx-transition-color-hue-shift` | `<number> \| auto` | `auto` | `TransitionColor.r` |
| `--urui-fx-transition-color-saturation-shift` | `<number> \| auto` | `auto` | `TransitionColor.g` |
| `--urui-fx-transition-color-value-shift` | `<number> \| auto` | `auto` | `TransitionColor.b` |
| `--urui-fx-transition-color-contrast-shift` | `<number> \| auto` | `auto` | `TransitionColor.r` |
| `--urui-fx-transition-color-brightness-shift` | `<number> \| auto` | `auto` | `TransitionColor.g` |
| `--urui-fx-transition-color-alpha` | `<number> \| auto` | `auto` | `TransitionColor.a` |
| `--urui-fx-transition-color` | `<color>` | `#0080ff` | `TransitionColor` |

### detail

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-detail-intensity` | `<number>` | `1` | `DetailIntensity` |
| `--urui-fx-detail-threshold-min` | `<number>` | `0` | `DetailThreshold.Min` |
| `--urui-fx-detail-threshold-max` | `<number>` | `1` | `DetailThreshold.Max` |
| `--urui-fx-detail-texture` | `<image>` | `none` | `DetailTexture` |
| `--urui-fx-detail-texture-scale-x` | `<number>` | `1` | `DetailTextureScale.x` |
| `--urui-fx-detail-texture-scale-y` | `<number>` | `1` | `DetailTextureScale.y` |
| `--urui-fx-detail-texture-offset-x` | `<number>` | `0` | `DetailTextureOffset.x` |
| `--urui-fx-detail-texture-offset-y` | `<number>` | `0` | `DetailTextureOffset.y` |
| `--urui-fx-detail-texture-speed-x` | `<number>` | `0` | `DetailTextureSpeed.x` |
| `--urui-fx-detail-texture-speed-y` | `<number>` | `0` | `DetailTextureSpeed.y` |
| `--urui-fx-detail-color` | `<color>` | `white` | `DetailColor` |
| `--urui-fx-detail-filter` | `none \| masking \| multiply \| additive \| subtractive \| replace \| multiply-additive` | `none` | `DetailFilter` |

### edge

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-edge-width` | `<number>` | `.5` | `EdgeWidth` |
| `--urui-fx-edge-shiny-progress` | `<number>` | `.5` | `EdgeShinyProgress` |
| `--urui-fx-edge-shiny-width` | `<number>` | `.5` | `EdgeShinyWidth` |
| `--urui-fx-edge-shiny-auto-play-speed` | `<number>` | `1` | `EdgeShinyAutoPlaySpeed` |
| `--urui-fx-edge-color-glow` | `false \| true` | `false` | `EdgeColorGlow` |
| `--urui-fx-edge-color-filter` | `none \| multiply \| additive \| subtractive \| replace \| multiply-luminance \| multiply-additive \| hsv-modifier \| contrast` | `replace` | `EdgeColorFilter` |
| `--urui-fx-edge-color-hue-shift` | `<number> \| auto` | `auto` | `EdgeColor.r` |
| `--urui-fx-edge-color-saturation-shift` | `<number> \| auto` | `auto` | `EdgeColor.g` |
| `--urui-fx-edge-color-value-shift` | `<number> \| auto` | `auto` | `EdgeColor.b` |
| `--urui-fx-edge-color-contrast-shift` | `<number> \| auto` | `auto` | `EdgeColor.r` |
| `--urui-fx-edge-color-brightness-shift` | `<number> \| auto` | `auto` | `EdgeColor.g` |
| `--urui-fx-edge-color-alpha` | `<number> \| auto` | `auto` | `EdgeColor.a` |
| `--urui-fx-edge-color` | `<color>` | `white` | `EdgeColor` |
| `--urui-fx-edge-mode` | `none \| plain \| shiny` | `none` | `EdgeMode` |

### pattern

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-pattern-area` | `all \| inner \| edge` | `inner` | `PatternArea` |

### gradation

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-gradation-mode` | `none \| horizontal \| horizontal-gradient \| vertical \| vertical-gradient \| radial-fast \| radial-detail \| diagonal-to-right-bottom \| diagonal-to-left-bottom \| angle \| angle-gradient \| diagonal` | `none` | `GradationMode` |
| `--urui-fx-gradation-color-filter` | `none \| multiply \| additive \| subtractive \| replace \| multiply-luminance \| multiply-additive` | `multiply` | `GradationColorFilter` |
| `--urui-fx-gradation-intensity` | `<number>` | `1` | `GradationIntensity` |
| `--urui-fx-gradation-offset` | `<number>` | `0` | `GradationOffset` |
| `--urui-fx-gradation-scale` | `<number>` | `1` | `GradationScale` |
| `--urui-fx-gradation-rotation` | `<number>` | `0` | `GradationRotation` |
| `--urui-fx-gradation-gradient` | `<image>` | `none` | `GradationGradient (LUT)` |
| `--urui-fx-gradation-color1` | `<color>` | `white` | `GradationColor1` |
| `--urui-fx-gradation-color2` | `<color>` | `white` | `GradationColor2` |
| `--urui-fx-gradation-color3` | `<color>` | `white` | `GradationColor3` |
| `--urui-fx-gradation-color4` | `<color>` | `white` | `GradationColor4` |

### shadow

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-shadow-mode` | `none \| shadow \| shadow3 \| outline \| outline8 \| mirror` | `none` | `ShadowMode` |
| `--urui-fx-shadow-distance-x` | `<number>` | `1` | `ShadowDistance.x` |
| `--urui-fx-shadow-distance-y` | `<number>` | `-1` | `ShadowDistance.y` |
| `--urui-fx-shadow-iteration` | `<number>` | `1` | `ShadowIteration` |
| `--urui-fx-shadow-fade` | `<number>` | `.9` | `ShadowFade` |
| `--urui-fx-shadow-mirror-scale` | `<number>` | `.5` | `ShadowMirrorScale` |
| `--urui-fx-shadow-blur-intensity` | `<number>` | `1` | `ShadowBlurIntensity` |
| `--urui-fx-shadow-color-filter` | `none \| multiply \| additive \| subtractive \| replace \| multiply-luminance \| multiply-additive \| hsv-modifier \| contrast` | `replace` | `ShadowColorFilter` |
| `--urui-fx-shadow-color` | `<color>` | `white` | `ShadowColor` |
| `--urui-fx-shadow-color-glow` | `false \| true` | `false` | `ShadowColorGlow` |
| `--urui-fx-shadow-color-hue-shift` | `<number> \| auto` | `auto` | `ShadowColor.r` |
| `--urui-fx-shadow-color-saturation-shift` | `<number> \| auto` | `auto` | `ShadowColor.g` |
| `--urui-fx-shadow-color-value-shift` | `<number> \| auto` | `auto` | `ShadowColor.b` |
| `--urui-fx-shadow-color-contrast-shift` | `<number> \| auto` | `auto` | `ShadowColor.r` |
| `--urui-fx-shadow-color-brightness-shift` | `<number> \| auto` | `auto` | `ShadowColor.g` |
| `--urui-fx-shadow-color-alpha` | `<number> \| auto` | `auto` | `ShadowColor.a` |

### flip

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-flip` | `<number>` | `0` | `Flip` |

### blend

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-blend-type` | `custom \| alpha-blend \| multiply \| additive \| soft-additive \| multiply-additive` | `alpha-blend` | `BlendType` |

### src

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-src-blend-mode` | `zero \| one \| dst-color \| src-color \| one-minus-dst-color \| src-alpha \| one-minus-src-color \| dst-alpha \| one-minus-dst-alpha \| src-alpha-saturate \| one-minus-src-alpha` | `one` | `SrcBlendMode` |

### dst

| CSS | Syntax | Default | EffectParameters |
| --- | --- | --- | --- |
| `--urui-fx-dst-blend-mode` | `zero \| one \| dst-color \| src-color \| one-minus-dst-color \| src-alpha \| one-minus-src-color \| dst-alpha \| one-minus-dst-alpha \| src-alpha-saturate \| one-minus-src-alpha` | `one-minus-src-alpha` | `DstBlendMode` |
