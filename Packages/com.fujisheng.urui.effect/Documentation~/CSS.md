# Effect CSS Reference

Matches Effect `0.5.0-preview.1`. Property syntaxes and defaults come from [Effect.css](../Runtime/Resources/URUI.Effect/Effect.css), mapped to [EffectParameters](../Runtime/EffectParameters.cs). Link `/urui-effect/Effect.css` in HTML and attach `EffectModule` before loading—see [Quick Start](QUICK_START.md).

## Activation Properties

| Declaration | Purpose |
| --- | --- |
| `--urui-effect: ui` | General effect shader pipeline across images, text, or element subtrees |
| `--urui-image-effect: ui-wave` | Vertex wave displacement on native image geometry |
| `--urui-image-effect: surface-output` | Displays offscreen `BindSurface` output |
| `data-urui-effect-root="id"` | Coordinate root element `id` within the same document (falls back to self if missing) |

Setting `--urui-fx-*` properties without `--urui-effect: ui` has no effect. Effect properties are non-inherited (`inherits: false`); subtree effects capture children when necessary before compositing. Single-layer `border-image` targets also participate cleanly.

## Numbers, Images & Colors

Values with `<number>` syntax take unitless scalars, booleans take `true`/`false`, and pixel properties require `px`. Common normalized intensity/progress/softness/range/fade properties span `0..1`, `sampling-width` spans `.5..10`, and `sampling-scale` clamps to `.01..100`.

`Vector2` maps to `-x`/`-y` properties and `EffectRange` maps to `-min`/`-max`. Texture slots accept `url(...)` or `none` (relative URLs resolve from CSS file location and are supplied via `IEffectResourceResolver`). Note that CSS `--urui-fx-gradation-gradient` samples a 1D LUT `Texture` rather than C# `UnityEngine.Gradient`. Color shift scalars default to `auto` (preserve color channel); author full HDR or multi-stop gradients via `EffectPreset`.

## Common Recipes

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

Use `element.Style.SetNumber("--urui-fx-transition-progress", value)` for direct single-property updates, `EffectController.Play` for C# tweens, and `Apply` for presets.

## Filter Modes & Sampling Costs

`BlurFast` / `BlurMedium` / `BlurDetail` perform **25 / 81 / 169** kernel taps respectively. `Shadow` / `Outline` / `Mirror` add copy geometry; special `Blend` modes capture complex subtrees when needed. Parent clipping, masks, and opacity always composited cleanly; set `--urui-fx-allow-to-modify-mesh-shape: false` to disable sampling expansion padding on raw textures.

## Wave Parameters (`.wave-image`)

| Property | Default | Meaning |
| --- | --- | --- |
| `--urui-wave-fixed-edge` | `top` | Anchored edge or center (`top`/`bottom`/`left`/`right`/`center`) |
| `--urui-wave-displacement` | `horizontal` | Wave axis (`horizontal`/`vertical`) |
| `--urui-wave-columns` / `--urui-wave-rows` | `24` / `8` | Mesh subdivisions (`columns` 2..128, `rows` 1..64) |
| `--urui-wave-amplitude` | `18px` | Non-negative displacement amplitude |
| `--urui-wave-frequency` | `6` | Wave frequency |
| `--urui-wave-speed` | `1.3` | Time speed multiplier |
| `--urui-wave-secondary` | `.5` | Secondary harmonic blend weight |
| `--urui-wave-fixed-part` | `.65` | Clamped anchored span (`0..0.95`) |
| `--urui-wave-cross-influence` | `.25` | Orthogonal wave coupling |
| `--urui-wave-cross-scale` | `.01` | Orthogonal frequency scale |
| `--urui-wave-time-offset` | `0` | Phase offset in seconds |

## Complete `--urui-fx-*` Registry (106 Properties)

Setting `initial` restores the registered default below without removing author cascade origin.

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
