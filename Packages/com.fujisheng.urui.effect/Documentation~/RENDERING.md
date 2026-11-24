# Effect Rendering & Resources

Covers `IEffectResourceResolver`, declarative UI particles (`ParticlesElement`, `ParticlePainter`), fixed-parameter shaders, custom `TextureColorEffect` / `WaveEffect`, offscreen `TextureEffectSurface`, auxiliary painters, previews, and metrics. See [Quick Start](QUICK_START.md), [C# API](API.md), and [Public API Reference](REFERENCE.md).

## `IEffectResourceResolver`

Pass to `new EffectModule(document, resolver)`:

| Method | Caller Responsibility |
| --- | --- |
| `ImageLease AcquireImage(ComputedImageValue image)` | Synchronously resolves transition/detail/gradient textures by `image.Kind`/`Url`/`SourceUrl` and returns an `ImageLease` |
| `GameObject ResolveParticles(string source)` | Maps `urui-particles` `src` strings to valid particle Prefabs kept alive by caller |

Plain `<img>` tags resolve through `Document.Images` / `ImageResolver`, whereas effect textures/particles resolve through `IEffectResourceResolver`. Keep textures/prefabs valid until active GPU leases release.

```csharp
using System.Collections.Generic;
using URUI.Effect;
using URUI.Resources;
using URUI.Styles;
using UnityEngine;

public sealed class EffectAssetMap : IEffectResourceResolver
{
    readonly IReadOnlyDictionary<string, Texture2D> images;
    readonly IReadOnlyDictionary<string, GameObject> particles;

    public EffectAssetMap(IReadOnlyDictionary<string, Texture2D> images,
        IReadOnlyDictionary<string, GameObject> particles)
    {
        this.images = images;
        this.particles = particles;
    }
    public ImageLease AcquireImage(ComputedImageValue image)
    {
        if (image.Kind != ComputedImageKind.Url) return null;
        return images.TryGetValue(image.Url, out var texture) ? new ImageLease(texture) : null;
    }
    public GameObject ResolveParticles(string source)
    {
        return particles.TryGetValue(source, out var prefab) ? prefab : null;
    }
}
```

## Declarative Particles

```html
<urui-particles id="reward-particles" src="particles/reward"
  data-urui-play-on-load="true"
  data-urui-pause-when-hidden="true"
  data-urui-restart-on-show="true"></urui-particles>
```

```css
#reward-particles { width: 320px; height: 180px; }
```

`EffectModule` registers `URUI.Effect.Elements.ParticlesElement` automatically before load (`document.Q<ParticlesElement>("reward-particles")`). Layout and clipping follow CSS while Unity `ParticleSystem` simulates and URUI bakes/submits mesh geometry with zero per-node MonoBehaviours.

| API / Attribute | Semantics |
| --- | --- |
| `Source` / `CurrentSource` | Requested `src` vs active validated prefab source |
| `ParticleCount` / `PlaybackState` | Live count and `Playing` / `Paused` / `Stopped` |
| `Play()` / `Pause()` / `Stop()` / `Restart()` | Playback control |
| `Seek(time, randomSeed = 1)` / `SimulationTime` | Deterministic scrub `0..600` seconds (nonzero seed, enables manual mode) |
| `Step(delta)` | Manual step `0..1` seconds |
| `RefreshSource()` / `OverrideSource(GameObject)` / `OverridePlayback()` | Atomic live refresh & non-destructive Editor/debug overrides |
| `OverrideMaterial(source, replacement)` | Swaps matching main/trail material slots safely at runtime without mutating Prefab assets |
| `GetStatistics()` | Reports bake passes, trail bakes, draw calls, and cumulative ms |
| `data-urui-play-on-load`, `data-urui-pause-when-hidden`, `data-urui-restart-on-show` | Boolean lifecycle flags (all default `true`) |

Attach particles to any regular element via `new ParticlePainter(element, source, options)` (`Dispose()` to detach). Note deterministic `Seek` guarantees parity within the same platform and particle configuration.

## UI Fixed-Parameter Pipeline

Builtin `ui` reads fixed parameter blocks from computed styles and writes dense parameter rows synced once with `Surface.UIClock`. Geometries re-tessellate only on topology/shadow/iteration changes and share cached templates retired by GPU fence. Plain `<img>` effects batch seamlessly inline; subtree effects capture cleanly respecting parent clips, blending domains, and sampling padding.

Register custom fixed-parameter image programs via Core `IParameterImageEffect` and `RenderParameterLayout` (`EffectProgramPreparation`), implementing `IParameterImageGeometry` / `IParameterEffectLayout` / `IParameterImageRuntime` / `IParameterImageResources` as needed.

## Custom Material Shaders & Wave (`TextureColorEffect` / `WaveEffect`)

Use `URUI.Effect.Rendering.TextureColorEffect`:
- `Define(name, authored, rateProperty, saturationProperty, brightnessProperty)` for immutable authored material programs registered via `document.Effects.Register(...)` (`--urui-image-effect: name`).
- `DefineResolved(name, resolver, keyProperty, ...)` to look up shared materials dynamically by resource key.

`WaveEffect.Define(string name = "ui-wave")` provides vertex-displaced undulating mesh images (already registered automatically by `EffectModule`; link `.wave-image` styles per [CSS Reference](CSS.md)). Layout/hit-testing stays anchored to the original image rect; note `ui-wave` currently supports full uncropped textures cleanly rather than atlas sub-rects or 9-slice buttons.

## `TextureEffectSurface` & `SurfaceEffectInput`

For offscreen shader processing of existing textures or decoded `VideoPlayback` frames (`URUI.Effect` / `URUI.Effect.Rendering`):

| Member | Purpose |
| --- | --- |
| `TextureEffectSurface(int width, int height, IDisposable inputLease = null)` | Fixed-size offscreen render surface (requires `ARGBHalf` support) |
| `Texture` / `Output : ImageLeaseSource` | Output RT and ref-counted lease producer |
| `Prepare(Vector2 size)` / `Record(context, input, material, properties)` | Ordered RenderHost integration |
| `Render(input, material, size)` / `Clear()` / `Dispose()` | Explicit standalone processing and safe disposal |
| `GetStatistics()` | Live surface counts, color bytes, passes, and mesh rebuilds |

Wire video processing cleanly before `document.Load(...)`:

```csharp
using System;
using URUI.Effect;
using URUI.Effect.Rendering;
using URUI.Elements;
using URUI.Media;
using UnityEngine;

public static class SurfaceBindingExamples
{
    public static IDisposable BindVideo(EffectModule module, Element image,
        VideoPlayback video, Material material, TextureEffectSurface surface)
    {
        if (!video.HasPresentedFrame) throw new InvalidOperationException("Wait for first video frame.");
        var input = new SurfaceEffectInput(surface, video.Output, material,
            () => video.PresentedVersion, () => video.HasPresentedFrame);
        return module.BindSurface(image, input);
    }
}
```

Unchanged `PresentedVersion` + static material state skips redundant re-processing; always unload documents before disposing bindings, surfaces, `EffectModule`, and external materials.

## Auxiliary Painters & Editor Previews

- `FeatherMaskPainter(Element)` — soft feathered edges via `--urui-mask-image: shader(feather-mask)` and `--urui-mask-feather-left/top/right/bottom` (`px`).
- `StrokePainter(Element)` — anti-aliased multi-segment polylines (`SetSegments`, width via `--urui-line-width`).
- `ArcTextLayout(host, source, prototype)` — curved text layout along radius/arc CSS tokens.
- `EffectResources(EffectContext, IEffectResourceResolver)` — helper for preparing/reading named images tied to `EffectContext`.
- **Preview**: Inspect presets directly in Unity Inspector or **Window → URUI → Effect Preview**, or render offscreen via `EffectPreview(parameters)` -> `Render(source, content, time, destination, background)` (`Dispose()` when done).

## Costs & Statistics

Kernel taps scale `25` (`BlurFast`), `81` (`BlurMedium`), `169` (`BlurDetail`). Inspect runtime metrics cleanly with `EffectStatistics.GetTargets()`, `TextureEffectSurface.GetStatistics()`, and `ParticlePainter.GetStatistics()` (note CPU draw/pass counts differ from hardware GPU frame time). See [Core Limitations](../../com.fujisheng.urui/Documentation~/LIMITATIONS.md) for platform constraints.
