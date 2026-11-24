# Editor C# API

All types require the Unity Editor main thread. Game builds reference only `URUI.Runtime` (`URUI.Authoring.Editor` never enters Player builds). External agents should prefer the HTTP v1 API or these supported services rather than reflecting internal fields.

## Authoring Session: `URUI.Authoring.Sessions.AuthoringSession`

| Member | Purpose |
| --- | --- |
| `AuthoringSession(string projectRoot)` | Creates a workspace session scoped to `projectRoot` |
| `Open(string path)` | Reads HTML, validates syntax, builds lexical source model (no disk save) |
| `File`, `Model`, `Workspace` | Active `SourceDocument`, parsed nodes, and file workspace |
| `Id`, `Revision`, `StructureRevision` | Session ID, workspace revision, and structural identity revision |
| `Set(key, property, value, expectedRevision)` | Single-property edit command (`null` unsets override) |
| `Execute(IReadOnlyList<AuthoringEdit>, expectedRevision)` | Batch attribute edits or a single structural transaction |
| `CopyNodes(keys)` / `RemoveNodes(keys, expectedRevision)` | Duplicate or atomically remove outermost selected ancestors in source order |
| `EditFile(path, text, expectedRevision)` | Validates and replaces working copy text |
| `SetSharedRule(path, ruleStart, property, value, expectedRevision)` | Minimal surgical edit inside a targeted CSS rule |
| `Undo()`, `Redo()` | Replays source revision history without fabricating inline px from computed layout |
| `Reload(bool discardLocal)` | Checks disk stamps and reloads safely (preserves dirty local edits by default) |
| `Save()` | Commits dirty working files checking disk conflicts per target |
| `Apply(selectedChanges, sourceMap, expectedRevision)` | Translates reliably mapped Live tweaks into clean authoring edits |

`SourceDocument.Parse` and `CssSource` maintain exact UTF-16 byte/character offsets alongside lexical edits (`HtmlCssValidator` performs semantic validation). Never bypass sessions to write `File.Text` or asset text directly. `AuthoringEdit` carries `Operation` (`set`/`insert`/`remove`/`move`), `Node`, `Property`, `Value`, `Parent`, and `Index` (`-1` appends). Limit structural batches to one topology op per transaction.

## `LiveSession`

`AuthoringEnvironment.Live` owns the shared Editor runtime debug log (`new LiveSession()` constructs standalone isolated sessions):

- `Set(node, property, value, expectedRevision)` and `Execute(edits, expectedRevision)` target immutable active nodes without resurrecting stale IDs across reloads.
- `Undo()` / `Redo()` replay real runtime setters; `Reset()` restores original units/unset states where elements remain valid.
- `Changes()` returns delta tweaks vs baseline (not a full DOM dump); `Dispose()` ends the session without loading/unloading game pages.

`ElementSelectionProxy.ForLive/ForAuthor` provides unsaved Selection proxies; batch with `SetMany` / `SetProperties` under `SessionUndoBridge.Record`. Control properties expose `value`, `text`, `src`, `placeholder`, `readonly`, `maxlength`, `min`, `max`, `step`, `for` (`checked` maps Checkbox/Radio presence; `null` removes HTML boolean attrs).

## Core Editor Extension Contract (`URUI.Editor.Inspection`)

Extensible contracts live in Core Editor so optional Effect/Authoring packages stay cleanly decoupled without reverse assembly references:

- `RuntimeInspectionBridge.Inspect/Root/Pick` & `InspectedNode` expose stable identity, validity, tags, read/write attrs/computed styles, and layout without leaking native tokens.
- `RuntimeInspectionBridge.Refresh` & `CreatePreview` support live refresh and isolated preview canvases.
- `IAuthoringContext` & `IAuthoringExtension` (discovered via `TypeCache`) provide property schemas, built-in resources, preview hooks (`AttachPreview`/`AdvancePreview`), custom Inspectors, workbenches, and `Dispose`.

Extensions must attach preview leases before authoring load, avoid second runtime `Surface` instances or mutating live game pages during Play Mode, and tear down cleanly across domain reloads.

## Effect Editor Capabilities

- `EffectModule.TryGet(document, out module)` / `EffectModule.PropertyDefinitions`
- `EffectController.OverridePreset(preset)` (reversible base-preset lease)
- `ParticlePainter.RefreshSource()` / `OverrideMaterial(source, replacement)` / `SetManualSimulation(enabled, seed)` / `Seek(time, seed)` / `Step(delta)` / `SimulationTime`
- `ParticlesElement` matching controls (`Seek`, `Step`, `RefreshSource`, `OverrideSource`, `OverrideMaterial`)
- `EffectAssetSession` (isolated working copy for `EffectPreset`, `Material`, particle Prefab with explicit `Apply`/`SaveAs`/`Dispose`)

## Unified Animation & Workspace

See [Unified Animation API](animation.md#editor-authoring-api) for signatures. `SourceWorkspace.Create` / `AuthoringSession.EditWorkset` publish multi-file HTML + CSS + `.urui-sequence.json` worksets together with disk recovery journals. `AuthoringSession.NodeIdentity(key)` / `ResolveIdentity(identity)` track nodes reliably across known operations, while `AuthoringEnvironment.SetMode(WorkspaceMode)` and `PendingAssets()` / `ApplyAsset()` (`IAuthoringAssetWorkspace`) coordinate clean per-asset explicit saving.

## Editor Localization (`URUI.Editor.Localization.EditorText`)

Shared between Core, Effect Editor, and Authoring via `CurrentLanguage` / `IsChinese`, `Tr(key)`, `Format(key, args)`, `NodeName(tag, id)`, `Message(message)`, and `Title(window, key)`—translating UI chrome cleanly without altering authored IDs, paths, CSS values, or diagnostic codes.
