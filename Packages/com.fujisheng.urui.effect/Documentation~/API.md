# Effect C# API

Namespace `URUI.Effect`. Covers `EffectModule`, `EffectPreset`, `EffectController`, tween playback, parameter linking, and input policies. See [Rendering & Resources](RENDERING.md) for particles/`TextureEffectSurface`, [CSS Reference](CSS.md) for property defaults, and [Public API Reference](REFERENCE.md) for complete signatures.

## `EffectModule`

| API | Parameters, Return & Lifecycle |
| --- | --- |
| `new EffectModule(Document)` | Registers `ui`, `ui-wave`, `surface-output`, and `urui-particles` before `Load` |
| `new EffectModule(Document, IEffectResourceResolver)` | Also supplies auxiliary textures and particle prefab lookup |
| `Document` | Owning document |
| `GetController(Element)` | Returns/reuses `EffectController` for a valid element in this document |
| `TryGet(Document, out EffectModule)` | Queries an attached module without constructing one |
| `PropertyDefinitions` | Static read-only property descriptors used by Editor authoring schema |
| `BindSurface(Element, SurfaceEffectInput)` | Binds an explicit offscreen surface input to a node (returns `IDisposable`, max 1 per node) |
| `Dispose()` | Cancels playback, destroys owned particle instances, clears links/bindings (never destroys `Document`) |

Bind one module for the lifetime of its `Document`, re-fetching controllers after reloads. Plain CSS-only effect nodes do not require manual `GetController(...)` calls.

## Parameters & Presets

`EffectParameters` is a pure value/texture parameter snapshot holding no DOM or GPU instances (`Clone()` copies Gradient keys/mode/colorspace without duplicating texture pixels; callers keep `Texture` assets alive).

`EffectPreset : ScriptableObject` (**Assets → Create → URUI → Effect Preset**) stores reusable authored configs:

| API | Semantics |
| --- | --- |
| `preset.CreateParameters()` | Returns an isolated mutable `EffectParameters` copy |
| `preset.SetParameters(EffectParameters)` | Replaces preset fields from snapshot |
| `preset.Append(EffectParameters)` | Merges enabled groups (merging `Flip` bit flags) |
| `controller.Apply(preset, EffectApplyMode.Replace)` | Replaces controller preset layer and clears any active `Link` |
| `controller.Apply(preset, EffectApplyMode.MergeActiveGroups)` | Merges active groups while leaving `None` groups intact |
| `controller.CreateParameters()` | Reads current effective resolved parameters |
| `controller.SaveTo(preset)` | Writes effective parameters into a preset object |
| `controller.OverridePreset(preset)` | Temporary preset lease (`IDisposable`) restoring previous override/base on dispose without clearing active links or playback |

Runtime writes mutate in-memory ScriptableObjects without marking Editor `.asset` files dirty automatically. `CreateParameters()` / `SaveTo()` require completed layout (`module.Document.SynchronizeLayout()` if called immediately after load).

```csharp
using URUI.Effect;
using UnityEngine;

public static class PresetExamples
{
    public static EffectPreset CreateGrayscale(float intensity)
    {
        var preset = ScriptableObject.CreateInstance<EffectPreset>();
        preset.SetParameters(new EffectParameters {
            ToneFilter = ToneFilter.Grayscale,
            ToneIntensity = intensity
        });
        return preset;
    }
}
```

Applying a preset does not implicitly add `--urui-effect: ui`; ensure CSS declares it explicitly.

## Cascade Origin & Priority

Presets act as default base values without mutating author CSS. Author rules (including explicit `initial`) override presets; active controller playback overrides normal author rules while CSS `@keyframes`, `!important`, and `transition` still take highest CSS precedence. Use `DocumentStyles.GetComputed(element)` (`GetOrigin`/`GetNumber`) to inspect winning origins.

## Playing & Awaiting Tweens

```csharp
using System.Threading;
using System.Threading.Tasks;
using URUI.Effect;

public static class EffectPlaybackExamples
{
    public static async Task Fade(EffectController controller, CancellationToken token)
    {
        using (var playback = controller.Play(new EffectTweenDefinition {
            Channels = EffectTweenChannels.Transition,
            Duration = .35,
            WrapMode = EffectWrapMode.Once,
            UpdateMode = EffectUpdateMode.Unscaled
        }, token))
        {
            await playback.Finished;
        }
    }
}
```

Playback animates enabled channels (for instance `TransitionFilter.Fade`). Starting a new `Play` cancels the previous handle; cancellation, reload, or invalidation cancels `Finished` safely.

### `EffectTweenDefinition`

| Field | Default | Meaning |
| --- | --- | --- |
| `Channels` | `Visual` | Visual channel bitmask (include `Event` to receive `ProgressChanged`) |
| `Direction` | `Forward` | `Forward` / `Reverse` |
| `Curve` | Linear `0→1` | Forward easing curve copied at launch |
| `SeparateReverseCurve` / `ReverseCurve` | `false` / Linear | Independent curve for reverse phase |
| `Delay` | `0` | Initial delay in seconds (negative clamped to 0) |
| `Duration` | `1` | Main duration in seconds (min `0.001`) |
| `Interval` | `0` | Interval pause between loops / ping-pongs |
| `WrapMode` | `Loop` | `Once`, `Loop`, `PingPongOnce`, `PingPongLoop` |
| `UpdateMode` | `Normal` | `Normal`, `Unscaled`, `Manual` |
| `PlayOnEnable` | `Forward` | `None`, `Forward`, `Reverse`, `KeepDirection` |
| `ResetTimeOnEnable` | `true` | Resets playback timer when re-enabled |

### Channels & Playback Controls

| Channel | Driven Parameter |
| --- | --- |
| `Tone`, `Color`, `Sampling` | Respective enabled group's intensity |
| `Transition` | `--urui-fx-transition-progress` |
| `GradationOffset` | `-1..1` gradation offset |
| `GradationRotation` | `0..360` rotation for angle gradients |
| `EdgeShiny` | Shiny edge progress |
| `Event` | Fires `ProgressChanged` without altering visual geometry |

| `EffectPlayback` Member | Semantics |
| --- | --- |
| `Pause()` / `Resume()` | Preserves position; completed/cancelled handles do not restart on `Resume()` |
| `Stop()` | Pauses and resets time to zero without completing `Finished` |
| `Restart()` | Restarts with fresh `RunIdentity` and new `Finished` task (disallowed after `Cancel()`) |
| `SetTime(double seconds)` | Deterministic seek without natural completion |
| `Advance(double delta)` | Advances time and checks completion (`Manual` mode caller-driven) |
| `Cancel()` / `Dispose()` | Cancels handle and marks `Finished` canceled |
| `Enabled` | Toggles enabled state respecting `PlayOnEnable` / `ResetTimeOnEnable` |
| `Progress` / `EvaluatedProgress` | Raw normalized progress vs curve-evaluated progress |
| `Time` / `TotalTime` | Current elapsed time vs full clip duration |
| `IsPlaying`, `IsPaused`, `IsCancelled`, `IsDelaying` | State flags |
| `Finished : Task` / `Completed : Action` | Natural completion signals (never fire for `Loop`/`PingPongLoop`) |
| `ProgressChanged : Action<float>` | Emitted when progress changes with `Event` channel active |

Operate handles on the creating thread (or pass a `CancellationToken` for cross-thread cancel handled next main-thread tick). `Surface.UIClock` takes priority when explicitly configured on `Normal`/`Unscaled` modes; hiding an element never alters `Enabled` implicitly.

## Linking Controllers

`target.Link(source, bool useTargetRoot = true)` binds parameters from `source` to `target` via a disposable lease without sharing mutable DOM state (`useTargetRoot: true` shares coordinate roots within the same document; pass `false` across documents for pure parameters; cycles are rejected). Disposing the lease or losing `source` keeps the last valid parameter snapshot on `target`. Calling `target.Apply(...)` detaches existing links cleanly.

## Transition Input Policy

`controller.InputPolicy` defaults to `TransitionInputPolicy.Standard`. Setting `DisableWhenComplete` rejects hit testing on that element once `Fade` / `Cutoff` / `Dissolve` / `Melt` / `Burn` reaches effective progress `1.0` without mutating CSS `pointer-events`. Disposing `EffectController` cancels its tweens and unlinks without removing DOM nodes.
