# Limitations & Support Matrix

## Environment & Platforms

- Unity `6000.3.22f1`; primary verified runtime host is **macOS ARM64 / Metal** (tested across Built-in and URP `SampleProject`).
- One active `Surface` hosts multiple isolated `Document` pages on the Unity main thread and requires an enabled `MainCamera`.
- Built-in input polls `Input Manager (Old / Both)`; disable `UseUnityInput` and feed `Surface.Input` explicitly for custom backends or Unity's new Input System.
- Textures borrow directly via `ImageAsset` or `ImageResolver` without requiring CPU Read/Write (resident full mip0 without texture streaming or active mipmap limits); fonts require readable raw TTF/OTF bytes covering rendered glyphs.
- Native plugin protocol versions: **ABI13 / render-plan9 / element4** (172 production C exports) alongside `style4 / effect7 / filter3 / animation1` version guards. See [PLUGINS.json](PLUGINS.json) for build hashes and export checks.

| Target | Static / Link Checks | Unity Runtime Scope |
| --- | --- | --- |
| macOS ARM64 | Architecture, all 172 exports, protocol contracts | Verified on Metal Editor & ARM64 IL2CPP Players; browser parity verified for supported HTML/CSS subsets |
| macOS x86_64 | Architecture & all 172 exports | Static only (not runtime-executed in this release) |
| Windows x64 | DLL architecture, Universal CRT, all 172 exports | Static only (not runtime-executed in this release) |
| Android ARM64 / x86_64 | ELF architecture, API 23+, 16 KiB page alignment, static libc++, all 172 exports | Static only (device execution pending) |
| iOS ARM64 (Device) | iphoneos SDK compile, static archive & symbol check, all 172 exports | Static only (device execution pending) |
| WebGL wasm32 | Emscripten 32-bit archive & all 172 exports | Static archive & smoke verified separately from Unity WebGL Player |

Linux, iOS Simulator, HDRP, non-macOS GPUs, and non-`6000.3.22f1` Unity versions are not validated in this matrix. macOS ARM64 and x86_64 ship as separate single-architecture plugins (restart Unity Editor before loading replacement native libraries).

## Elements & Controls

- Supports typed DOM factories, custom elements, ordered `Q`/`Query`, strongly typed `Style`, layout/bounds getters, and controls: `Label`, `Image`, `Button`, `Toggle`, `RadioButton`, `Slider`, `IntegerSlider`, `TextField`, `IntegerField`, `FloatField`, `Dropdown`, `ScrollView`, `Foldout`, `ProgressBar`, and fixed-height `ListView`.
- HTML/CSS follows the supported subset documented in `HtmlCssSupport.json` (Flexbox-first—CSS Grid is not implemented; authored tags require explicit closing on non-void elements).
- URUI uses its own C# API—it is not API/binary compatible with Unity UIElements, UXML, UI Builder, or embedded JavaScript execution.
- `Element` instances cannot be constructed outside a `Document` or moved between documents; bypasses mutating composite control internals are unsupported.
- Queries return fresh snapshots without zero-allocation guarantees; `ResolvedStyle` preserves relative units (use `Layout`/`ContentRect`/`WorldBound` for pixel rects).
- `ListView` supports fixed row height, single selection, and keyboard navigation (dispose item handlers cleanly in `UnbindItem`); dynamic row heights, multi-select, and drag reorder are out of scope.
- `IntegerSlider` backs bounds with float range sliders and does not guarantee exact 32-bit step fidelity across extreme `int32` bounds; `IntegerField` saturates safely across full `int32`.
- Core ships zero built-in ViewModel binding framework or network asset downloader; HTML/CSS edits require document reload (or Play re-entry) unless driven via `URUI.Authoring`.

## Input & Events

- Supports deterministic capture/target/bubble propagation, pointer capture/cancel, multi-touch pointer IDs, keyboard modifiers, focus/`Tab` traversal, Unicode selection, clipboard integration, and IME composition APIs.
- Automated suites verify IME preview/commit/cancel, Unicode selection bounds, dual-slider multi-touch, disabled-subtree pointer cleanup, callback-safe reloads, and virtualized row rebinding; native OS IME & mobile soft-keyboard UX still require manual target-device sign-off. Accessibility trees and gamepad focus graphs remain future work.

## Debugging & Rendering

- `URUI_DEBUG` is disabled by default in both Editor and Development builds (diagnostic native libraries require matching debug compilation flags).
- Validate runtime performance against target Player builds and real workloads using Unity Profiler, `Surface.GetRenderingStatistics()`, and native plan counters rather than single debug samples.
- Texture borrowing avoids duplicate native RGBA pixel copies and redundant draw textures, while required font atlases, intermediate offscreen/mask buffers, and GPU submission ring resources still follow hardware driver alignment and fence rules.
