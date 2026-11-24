# URUI Effect (`com.fujisheng.urui.effect`)

**English** | [中文](README_ZH.md)

Version: `0.5.0-preview.1` · Requires Core: `0.8.0-preview.1` · Unity Baseline: `6000.3.22f1`

Declarative shaders, UI visual filters (`--urui-fx-*`), preset playback controllers, curved text layout (`ArcTextLayout`), vertex wave distortion (`WaveEffect`), off-screen texture surfaces (`TextureEffectSurface`), and declarative Unity particles (`<urui-particles>`) for URUI. No FUI or UGUI dependencies, and no separate native plugin binary.

## Quick Features

- **`--urui-effect: ui`**: 106 CSS custom properties across Tone, Color, Target, Sampling, Transition (Fade/Dissolve/Shiny/Melt/Burn), Detail, Edge, Gradation, Shadow/Outline/Mirror, Blend, and Flip.
- **`EffectController` & `EffectPreset`**: Author reusable `ScriptableObject` presets, stack `OverridePreset` inspection leases, `Link` snapshots across nodes, or run double-precision `Play(EffectTweenDefinition)` animations.
- **`<urui-particles>` & `SequenceTrackFactory`**: Embed Unity `ParticleSystem` prefabs styled by CSS layout and scrubbed deterministically by `SequencePlayer` timelines.
- **`TextureEffectSurface` & `SurfaceEffect`**: Render animated shader materials into GPU half-float textures and bind them with `EffectModule.BindSurface`.

## Documentation

| Guide | English | 中文 |
| --- | --- | --- |
| Quick Start | [QUICK_START.md](Documentation~/QUICK_START.md) | [QUICK_START_ZH.md](Documentation~/QUICK_START_ZH.md) |
| C# Effect API | [API.md](Documentation~/API.md) | [API_ZH.md](Documentation~/API_ZH.md) |
| CSS `--urui-fx-*` & Wave Reference | [CSS.md](Documentation~/CSS.md) | [CSS_ZH.md](Documentation~/CSS_ZH.md) |
| Rendering, Particles & Surfaces | [RENDERING.md](Documentation~/RENDERING.md) | [RENDERING_ZH.md](Documentation~/RENDERING_ZH.md) |
| Complete Public Declarations | [REFERENCE.md](Documentation~/REFERENCE.md) | [REFERENCE_ZH.md](Documentation~/REFERENCE_ZH.md) |

## Samples

- Package Sample: [`Samples~/EffectGallery`](Samples~/EffectGallery/README.md) (83 preset coverage states, tweens & particles)
- Full URP Showcase: [`../../SampleProject/`](../../SampleProject) (*Effect Atelier* + *Blind Box* modules)

License & Notices: [`ThirdPartyNotices.md`](ThirdPartyNotices.md).
