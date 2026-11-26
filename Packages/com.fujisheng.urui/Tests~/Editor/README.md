# Editor Validation Checks

This directory is excluded from normal Unity asset import. Import explicitly into an isolated, disposable Unity **6000.3.22f1 / macOS ARM64 / Built-in / Metal** project to verify public API contracts and Editor entry points.

## Package Sample Verification

URUI's primary showcase is `Samples~/Tideglass Beacon` (with the standalone URP showcase in repository root `SampleProject/`). To validate Package Manager import into an isolated project, copy `Fixtures/Editor/PackageImport.cs` into `Assets/URUIEditorChecks/Editor` and invoke `URUI.Tests.Editor.PackageImport.ImportSamples`, which exercises `Sample.FindByPackage` / `Sample.Import(OverridePreviousImports)` and logs imported paths/bytes to `Evidence/sample-import.json`. Always verify functional behavior by opening `Tideglass.unity` or running `URUI.Tests` EditMode/PlayMode suites (including `Surface.GetRenderingStatistics()` checks).

Topic regression fixtures (`Hello URUI`, `Element Playground`, `Runtime Controls`) test isolated legacy/unit capabilities:

## Topic API & Control Checks

1. Install `com.fujisheng.urui` from disk in an isolated test project (`com.unity.modules.imageconversion: 1.0.0` enabled for PNG captures) with matching ABI13 native binaries.
2. Copy `Fixtures/AcceptanceDocuments.cs` and target runtime fixtures to `Assets/URUIEditorChecks`.
3. Copy matching Editor fixtures into `Assets/URUIEditorChecks/Editor` (avoiding fixtures referencing unimported samples).
4. Invoke headless checks outputting reports to `Evidence/`:

```sh
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.ApiValidation.Run -logFile /absolute/api.log
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.UsabilityValidation.Run -logFile /absolute/usability.log
```

`ApiValidation` checks click events, CSS pixel sizing, multi-document isolation on a shared `Surface`, thread affinity guards, malformed HTML rejection, unload/reload invalidation, duplicate subscription guards, callback-deferred reload queueing, and resource ownership. `PublicApiContract` verifies public type boundaries, 80-byte event ABI (`text` offset `72` on 64-bit), native frame struct layout, and default-disabled `URUI_DEBUG`. `ProductionSafetyContract` confirms uncertain GPU submission state reports failure cleanly without creating orphan GPU targets.

`ElementTreeValidation.Run`, `PlaygroundValidation.Run`, `ElementRuntimeValidation.Run` (`Evidence/element-runtime.json`), and `ElementRuntimeValidation.VerifyControls` (`Evidence/control-completeness.json` + macOS ARM64 IL2CPP Player builder `BuildControlsPlayer`) test structural queries, control completeness, Unicode selections, virtualized row rebinding, and strict document rules.

Standard regression runs with `URUI_DEBUG` disabled; testing diagnostic builds requires enabling `URUI_DEBUG` in both native CMake and Unity Scripting Define Symbols simultaneously (see [Limitations](../../Documentation~/LIMITATIONS.md)).
