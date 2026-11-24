# Animation Editing & Runtime API

## Shortest Workflow

1. Double-click an HTML file under `Assets/` (or **Window → URUI → Editor → Open Page**).
2. Select a target element, switch to **Animation**, and click **Add Animation** (`popup` / `fade-in` / `fade-out` / `slide-in` / `pulse`).
3. Scrub the timeline playhead or type an exact timestamp in seconds; toggle **Record**, tweak style/transform properties in Inspector/Canvas, then stop **Record**.
4. Preview playback and click **Save**—HTML, linked CSS keyframes, and `.urui-sequence.json` save together (while `ParticleSystem` Prefabs, `Material`s, and `EffectPreset`s remain explicitly applied in their asset inspectors).

Adding the first animation preset assigns unique `id` attributes to unnamed targets, scaffolds `<page>.animations.css` + `<page>.urui-sequence.json`, and links the CSS cleanly inside one undoable transaction without touching disk until **Save**. Unrecorded tweaks in Animation mode preview transiently; recording writes keyframes rather than mutating base CSS rules.

## Timeline Controls

| Action | Behavior |
| --- | --- |
| Play / Pause / Stop | Pause holds pose; Stop clears transient animation overrides and restores base styles/particles |
| Prev / Next Frame | Steps at `60 fps` (frame snap quantizes drag positions) |
| Zoom / Pan | `+` / `-` / Fit All, horizontal scrollbar |
| Drag Node from Hierarchy | Creates CSS popup track or particle track at drop timestamp (rejects stale revisions) |
| Multi-Select Keys | `Shift` / `Cmd` / `Ctrl` click key diamonds (selects all declarations at that stop) |
| Move / Duplicate Keys | Drag diamonds (`Option`-drag duplicates; `Esc` cancels) or use **Move/Copy to Playhead** |
| Mute / Solo / Lock (`M / S / Lock`) | `Mute` and `Lock` serialize into sequence JSON; `Solo` is preview-only |
| Easing | `linear`, `ease`, `ease-in`, `ease-out`, `ease-in-out`, cubic-bezier handles, or CSS `linear(...)` |
| Explicit Keyframe Input | Add/edit CSS property/value pairs at selected track stops without toggling Record |

Multi-key moves reject overlapping destination stops cleanly rather than silently clobbering properties; explicit key copies merge declarations. External stylesheet `@keyframes` are required for visual keyframe editing (inline `<style>` animations play back cleanly, move to linked CSS to keyframe visually).

## Sequence JSON Format v1

```json
{
  "version": 1,
  "name": "RewardPopup",
  "duration": 1.2,
  "loop": false,
  "tracks": [
    {
      "id": "popup",
      "kind": "css",
      "target": "reward",
      "keyframes": "reward-in",
      "start": 0,
      "duration": 0.3,
      "iterations": 1,
      "alternate": false,
      "easing": "ease-out",
      "muted": false,
      "locked": false,
      "source": "",
      "seed": 1,
      "speed": 1
    }
  ]
}
```

`target` matches a unique HTML `id` inside the playback root (`$root` targets the root itself), or an explicit key in the runtime `slots` dictionary for spawned clone subtrees. Sequences stay 0..600 seconds with up to **256** tracks and **4096** keyframes across active CSS tracks. Sequential tracks affecting distinct CSS properties hand off cleanly while preserving prior final values; overlapping tracks modifying the exact same property on the same target are rejected at construction time. Cascade follows native Animation origin (`!important` and transitions still win; `Stop()` removes only sequence overrides cleanly).

## Core Runtime: `URUI.Animation.SequencePlayer`

Constructed and controlled strictly on the Unity main thread (`SequenceDefinition.Parse` parses strict JSON and snapshots safely into `SequencePlayer`):

```csharp
using URUI.Animation;

// Document is loaded and laid out; sequenceText comes from TextAsset.text or project resources.
var definition = SequenceDefinition.Parse(sequenceText);
using var playback = new SequencePlayer(document.Body, definition);
playback.Play();
await playback.Finished; // Completes naturally on finite sequences; Stop/Dispose cancels pending tasks.
```

| API | Semantics |
| --- | --- |
| `SequenceDefinition.Parse(text)` | Strict JSON validation rejecting unknown fields, duplicate keys, bad versions, invalid ranges |
| `definition.Validate()` / `ToJson()` | Validates / formats metadata cleanly (CSS keyframe bodies live in CSS stylesheets) |
| `new SequencePlayer(root, definition, factory = null, slots = null)` | Binds valid laid-out root, freezes base state, acquires track drivers |
| `Play()` / `Pause()` | Resumes or pauses holding current pose (replaying completed sequence restarts at `0`) |
| `Seek(seconds)` / `Sample(seconds)` | Scrubs deterministically (`Seek` pauses without completing `Finished`; finite clamps, loop wraps) |
| `Stop()` / `Dispose()` | Clears animation overrides (`Stop` resets time to `0`; `Dispose` unregisters from document without destroying `Document`) |
| `Time`, `Duration`, `Name`, `IsPlaying`, `Finished` | Live status and completion task |
| `SequenceTiming.At(track, time)` | Pure helper returning `(Active, Progress, LocalTime, Iteration)` including fill-forwards holds |

Runtime auto-playback advances smoothly with `Surface.UIClock` prior to native layout update; detaching/destroying bound targets cancels pending completion cleanly.

## Optional Effect & Particle Tracks

Installing `URUI.Effect` automatically registers the `"particles"` `ISequenceTrackFactory` (`URUI.Effect.SequenceTrackFactory`) per `EffectModule` without creating hard coupling in Core or Authoring:

- CSS effect parameters (`--urui-fx-tone-intensity`, etc.) animate as regular `css` tracks when the page links `/urui-effect/Effect.css`.
- Particle tracks target `<urui-particles>` with resolvable `source`, nonzero `uint seed`, `speed` in `(0, 4]` (`duration * speed <= 600`s), and linear timing—providing deterministic timeline scrubbing on matching platform configs and cleanly restoring idle state on `Stop()`. `ParticlesElement.SequenceControlled` reports whether a sequence currently owns simulation.
- Custom track extensions can register via `ISequenceTrackFactory` / `ISequenceTrackDriver` and `SequenceTrackRegistry.Register(document, factory)`.

## Editor Authoring API

Workspace APIs (`AnimationEnvironment.Workspace`, `new AnimationWorkspace(author)`) validate `expectedRevision` on every mutation and support `SessionUndoBridge.Record(...)` for native Unity Undo integration:

| API | Semantics |
| --- | --- |
| `Open(path)` / `Create(revision)` / `Close()` | Opens `.urui-sequence.json` / scaffolds matching sequence / closes selection |
| `Definition`, `Path`, `Revision` | Current workspace sequence snapshot, file path, revision |
| `AddPreset(sourceKey, preset, start, duration, revision)` | Adds `popup` / `fade-in` / `fade-out` / `slide-in` / `pulse` / `empty` CSS track |
| `AddParticles(sourceKey, source, start, duration, revision)` | Adds particle track targeting valid `<urui-particles>` |
| `SetTrack(id, Action<SequenceTrack>, revision)` | Edits track timing/binding/flags (rejects locked tracks) |
| `SetLocked(id, locked, revision)` / `RemoveTrack(id, revision)` | Toggles lock / deletes track preserving CSS `@keyframes` |
| `SetSequence(name, duration, loop, revision)` | Edits top-level sequence metadata |
| `Keys(id)` / `SetKey(id, position, property, value, revision)` | Reads/edits normalized `0..1` keyframe declarations in CSS |
| `TransformKeys(selection, seconds, copy, remove, revision)` | Multi-track atomic keyframe move/duplicate/delete transaction |
| `RemoveKeys` / `MoveKeys` / `KeyframeFile(track)` / `IsShared(id)` | Convenience helpers & stylesheet sharing checks |

Preview owner methods (`AnimationEnvironment.SelectTrack/Seek/Play/Pause/Stop`), recording hooks (`BeginRecording`/`EndRecording`/`Record`), canvas gesture helpers (`CanvasGesture`), and layout presets (`LayoutCommands.Preset(key, "row"|"column"|"center"|"fill")`) share one transactional source workspace cleanly.
