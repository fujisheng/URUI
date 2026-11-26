# URUI Authoring User Guide

## Installation & Entry Points

`com.fujisheng.urui.authoring` compiles only in the Unity Editor (`URUI.Authoring.Editor`), referencing `URUI.Runtime` and `URUI.Editor`. Install Core first, add Authoring's `package.json`, and optionally install Effect. Local development setups can point `Packages/manifest.json` via `file:` entries; Git installations should pin published commits containing matching packages.

Open the workbench via **Window → URUI → Editor** (or Chinese locale equivalent), alongside dockable **URUI Hierarchy** and **URUI Inspector** windows (re-openable via toolbar **Hierarchy & Inspector**). Standalone shortcuts are **Window → URUI → Canvas** and **Window → URUI → Animation**, or double-click any `.html` file in the Project window. After importing `Authoring Demo`, open `Inventory.html`. Authored pages must live inside `Assets/` or embedded `Packages/` (read-only package cache samples should be imported via Package Manager first).

## Editor Localization

UI chrome follows Unity's active Editor language preference (Simplified Chinese vs English, falling back to English when unsupported or when Editor localization is disabled; no separate OS check or custom toggle is needed). Switching Editor language updates titles, menus, labels, and tooltips immediately without mutating authored source files. Element `id`s, CSS properties, asset paths, animation bindings, and HTTP Agent protocol fields always remain verbatim.

## Authoring Mode

HTML/CSS stores document structure, static styling, and animation `@keyframes`, while `.urui-sequence.json` stores only track bindings, timing ranges, and asset refs—zero proprietary binary scene/tree formats are invented. Editing in Layout mode mutates the working copy directly; unscoped adjustments in Animation mode while not recording are transient previews; explicit Save commits changes cleanly to disk.

- **URUI Hierarchy & Control Drawer**: Toggleable left library drawer lets you click or drag standard controls and project fragments into containers or trees (`Enter` in search inserts the first hit). Layer labels display real `id`, visible text, and CSS classes (`Full Hierarchy` exposes wrapper nodes). Supports multi-select, `.class`/`#id` filter, duplicate (`Cmd/Ctrl+D` updates internal IDs cleanly), drag reorder, and reparenting.
- **Stylesheet & Source Inspector**: Shows rules in document cascade order (read-only package styles such as `/urui/base.css` or `/urui-effect/Effect.css` render read-only). Extract inline styles cleanly to linked stylesheets and inspect raw HTML/CSS via the **Source** button.
- **Mounting `/URUI/...` Assets & Fragment Preview**: Projects resolving `/URUI/...` paths reimport `.compiled.asset` snapshots incrementally on save. Works seamlessly under both Built-in and URP without adding dedicated preview RendererFeatures.
- **Canvas Controls**: Zoom, pan (middle drag / scroll), marquee multi-select (`Shift`), Move/Rotate/Scale/Rect tools (`W/E/R/T`), Frame Selected (`F`), custom resolution & Safe Area overlays (`Edit` vs `Interact` toggles live `Surface.Input`). Initial fallback viewport reads `ProjectSettings/URUIAuthoring.json` (`1280×720` default).
- **Rich Inspector & Transforms**: Displays typed panels per node type (Text, Image, Input, Toggle, Slider, ProgressBar, Padding/Margin 4-side linking, simple `translate`/`rotate`/`scale` component sliders, unit preservation across `px`/`%`/`em`, origin badges for computed/inline/shared/transient/recorded values). Modifying shared rules affects all matched elements cleanly.
- **External Edits & Safe Persistence**: Detects external edits from IDE or coding agents (`source_conflict` when dirty locally), keeps atomic single-file swaps, maintains staging journals in `Library/URUI/Authoring/Recovery/*.journal` + `.urui-*.bak` if multi-file writes hit partial failure, and preserves selection identities across structured edits/Undo/Redo whenever provenance is certain.

## Play Mode & Live Debugging

Open **Window → URUI → Runtime Debugging** (or auto-switch panels in Play Mode) to inspect live running URUI documents:

1. Select active Documents from the toolbar switcher; search live/dynamic nodes by tag, text, `#id`, or `.class`.
2. Inspect computed styles and tweak live properties directly in **URUI Inspector** (`Game View` updates immediately, even while paused; exiting Play Mode returns safely to authoring mode without silently writing back to source files).
3. Use **Review & Apply...** to selectively commit traceable tweaks back to mapped HTML/CSS source rules without overwriting dynamic nodes or conflicting modified files. Runtime live images resolve `Texture2D` project paths via transient preview leases.

## Effect & Particles Authoring

Installing `com.fujisheng.urui.effect` activates particle & FX inspector panels automatically (documents link `/urui-effect/Effect.css`). Edit CSS effect values, inspect/tweak isolated working copies of `EffectPreset`, `Material`, or `ParticleSystem` Prefabs, and scrub/step/replay particles (`0..600`s deterministic seek). Particle prefabs follow Effect's renderer contract (`_UruiPaintOpacity`, `_UruiClipEnabled`, `_UruiClipRect`; VFX Graph is unsupported). Original `.asset` / `.mat` / `.prefab` changes save only upon explicit **Apply / Save As**.

## Lifecycle, Boundaries & Timeline

URUI supports one active runtime `Surface`; Editor authoring pauses its isolated Preview Scene cleanly during Play Mode and resumes afterward. Live DOM hierarchy creation/deletion is read-only during Play.

For multi-track timeline workflows (`$root`, CSS presets, keyframes, particle sync, save manifests), read [Unified Animation & Runtime API](animation.md) and [Localhost Agent HTTP API](agent-api.md#animation-commands). Run authoring validation via repository `Tools/verify-authoring.py`.
