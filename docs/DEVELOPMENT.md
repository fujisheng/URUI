# Development, Native Builds & Verification

**English** | [中文](DEVELOPMENT_ZH.md)

Target Unity version: **`6000.3.22f1`**. Current native bridge contract: **`ABI13 / render-plan9 / element4 / 172 exports`**.

## 1. Repository Layout

- `Packages/com.fujisheng.urui/` — Core runtime (`URUI.Runtime`), Editor tools (`URUI.Editor`), prebuilt native plugins (`Runtime/Plugins`), C++ source (`Native~`), and verification suites (`Tests/`, `Tests~/`).
- `Packages/com.fujisheng.urui.effect/` — Visual filters, presets, `WaveEffect`, `TextureEffectSurface`, particles (`URUI.Effect`).
- `Packages/com.fujisheng.urui.authoring/` — Editor-only visual inspector, timeline, and localhost Agent HTTP v1 (`URUI.Authoring.Editor`).
- `SampleProject/` — Standalone open-and-play Unity `6000.3.22f1` URP showcase (**SOLSTICE**).
- `Tools/` — Native platform builder, static doc/link validators, Authoring managed suite, and regression checks.

## 2. Rebuilding Native Binaries (`URUIBridge`)

Rebuild all 7 supported platforms (`macOS ARM64/x86_64`, `Windows x86_64`, `Android ARM64/x86_64`, `iOS ARM64`, `WebGL wasm32`) from source using:

```sh
python3 Tools/build-native-platforms.py
```

Key environment/platform requirements:
- Configure Unity Playback Engines or Android NDK / Emscripten environment variables as needed (`URUI_UNITY_PLAYBACK_ENGINES` or platform SDK roots).
- Updates `Runtime/Plugins/**` and regenerates checksums/export lists inside `Packages/com.fujisheng.urui/Documentation~/PLUGINS.json`.
- Always verify ABI alignment: **172 production `urui_` exports**, `required_abi_version == 13`, and clean `PublicApiContract` layout validation in Unity.

## 3. Running Verification Suites

### Authoring Standalone Managed Suite
```sh
dotnet run --project Tools/Tests/Authoring/AuthoringTests.csproj
```

### Repository Python Tests & Documentation Hygiene Check
```sh
python3 Tools/check-docs.py
python3 Tools/check-static-links.py
python3 -m unittest discover -s Tools/Tests -p "test_*.py"
```

### Unity EditMode & PlayMode Suites (`com.fujisheng.urui` & `Solstice`)
- Run `URUI.Editor.Tests`, `URUI.Runtime.Tests.PlayMode`, and `URUI.Effect.Tests` in a clean Built-in Render Pipeline Unity `6000.3.22f1` project (with `com.unity.test-framework` enabled and `Samples~/EffectGallery` staged as automated in `Tools/Effects/validate.py`).
- Run `Solstice.Tests.SolsticeVisualCaptureTests` in `SampleProject/` (URP `17.3.0`) to verify 3D RenderGraph compositing, particles, sequences, and multi-screen layouts.
- Ensure external `TMPDIR` is set when working on external SSD storage, and run GPU rendering validations on real GPU hardware (do **not** pass `-nographics` to GPU/ReadPixels tests).
