# Code Organization & Assembly Boundaries

**English** | [中文](CODE_ORGANIZATION_ZH.md)

The repository hosts three loosely coupled Unity packages cleanly separated into Runtime and Editor assemblies.

## Core (`Packages/com.fujisheng.urui`)

| Relative Path | Responsibility |
| --- | --- |
| `Runtime/Hosting` | `Surface`, `SurfaceInput`, `SurfaceRenderingStatistics`, and frame submission |
| `Runtime/Documents` | `Document` lifecycle, loading (`CompiledDocumentAsset` / HTML), node factories, events |
| `Runtime/Elements` | `Element` identity, scoped queries, hierarchy, style overrides, coordinates |
| `Runtime/Controls` | Typed HTML-mapped controls and reusable composited behaviors (`ListView`, `Dropdown`, `Slider`) |
| `Runtime/Events` | Synchronous event propagation, pointer capture, focus, text input, layout notifications |
| `Runtime/Resources` | `TextResource`, `ImageAsset`, `ImageLease`, `GpuRetirement` |
| `Runtime/Styles`, `Runtime/Animation` | Computed styles, monotonic `IUIClock`, `SequencePlayer`, `StaggeredReveal`, `BezierMotion` |
| `Runtime/Media`, `Runtime/Rendering` | Video playback output and custom painter / shader effect / SRP contracts |
| `Runtime/Internal` | Internal `RenderHost`, C ABI13 P/Invoke interop, `render-plan9` batching & parameter pages |
| `Runtime/Debug` | `URUI_DEBUG`-guarded profiling diagnostics (compiled out in default releases) |
| `Runtime/Resources/URUI`, `Runtime/Plugins` | Built-in base CSS/shaders/fonts and 7-platform native `URUIBridge` binaries |
| `Editor` | HTML/CSS importers, `CompiledDocumentCompiler`, validation guards, sample menus |
| `Native~/Engine`, `Native~/ThirdParty` | Embedded native engine C++ source and third-party dependencies (`RmlUi`, `FreeType`) |

## Effect (`Packages/com.fujisheng.urui.effect`)

- `Runtime/` — `EffectModule`, `EffectController`, `EffectPreset`, `EffectPlayback`, `EffectParameters` (`106` properties)
- `Runtime/Elements` — Declarative `<urui-particles>` (`ParticlesElement`)
- `Runtime/Rendering` — `WaveEffect`, `TextureColorEffect`, `SurfaceEffect`, `TextureEffectSurface` shaders & pools
- `Editor/` — Effect preset/asset authoring and preview inspectors

## Authoring (`Packages/com.fujisheng.urui.authoring`)

Strictly Editor-only (`URUI.Authoring.Editor`): visual source AST, docked hierarchy/inspector/animation windows, and localhost Agent HTTP v1 endpoint. Core and Effect never depend back on Authoring.

See also: [Core API Reference](../Packages/com.fujisheng.urui/Documentation~/REFERENCE.md) · [Code Style Conventions](../Packages/com.fujisheng.urui/Documentation~/CODE_STYLE.md).
