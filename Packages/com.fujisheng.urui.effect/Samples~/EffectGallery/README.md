# URUI Effect Gallery

[中文说明](README_ZH.md)

Standalone shader, transition, particle, and `SurfaceEffect` gallery package sample (`com.fujisheng.urui.effect` `0.5.0-preview.1`, compatible with `com.fujisheng.urui` `0.8.0-preview.1`, native protocol `ABI13 / render-plan9 / element4 / texture protocol v1`).

> Looking for the full interactive URP multi-module demo including Effect Atelier + Blind Box particles + 3D backdrop compositing? Open [`SampleProject/`](../../../../SampleProject) directly in Unity `6000.3.22f1`.

## Import & Run

1. Open **Package Manager → URUI Effect → Samples → Declarative Effect Gallery → Import**.
2. Create an active `Main Camera` and attach `EffectGallery` (`MonoBehaviour`) to a scene GameObject.
3. Assign the sample assets in the Inspector:

| Field | Sample Asset |
| --- | --- |
| `html` / `style` | `Gallery.html` / `Gallery.css` |
| `white` / `shader` | `white.png` / `Gallery.shader` |
| `source` / `pattern` / `gradient` | `input.png` / `pattern.png` / `gradient-lut.png` |
| `particlePrefab` | Optional pure `ParticleSystem` prefab (falls back to built-in seeded particle source) |

4. Enter Play Mode. Every effect node is driven through `EffectModule` + `EffectController` + CSS `--urui-fx-*` properties with zero per-element `MonoBehaviour` instances.

## Coverage

- Covers 13 filter/effect groups and 83 enum presets in `default / extreme / disabled` states (`GalleryCoverage.json`).
- Demonstrates `EffectController.Apply`, `EffectController.Link`, `EffectController.Play`, manual `Seek`, and `<urui-particles>` with `data-urui-*` visibility policies.
- Works identically under Built-in RP and URP (when rendered via a `ScriptableRendererFeature` calling `Surface.SubmitRendering`).

See [C# API](../../Documentation~/API.md), [CSS Parameters](../../Documentation~/CSS.md), [Rendering & Resources](../../Documentation~/RENDERING.md), and [ThirdPartyNotices](../../ThirdPartyNotices.md).
