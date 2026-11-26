# URUI — Text-First HTML/CSS UI Engine for Unity

**English** | [中文](README_ZH.md)

URUI brings **standard HTML, CSS, and text-driven sequences** into **Unity 6 (`6000.3.22f1`)** on top of a native C++ bridge (`URUIBridge` ABI13 / render-plan9 / element4, 172 C exports), FreeType glyph rasterization, GPU-parameterized batching, and zero-per-element `MonoBehaviour` overhead.

---

## Tideglass Beacon

Tideglass Beacon was made entirely by AI, from interface design and code to original art, animation, and sound. This recording shows the sample running in Unity.

![Tideglass Beacon in Unity, from the orbital station and tilting crew portrait to module purchases, equipment, skill previews and combat](docs/images/tideglass-beacon.gif)

Run a station watch. Appoint a combat escort, send a different officer on an offline survey, and wear earned titles on the bridge. Three beacon routes, loadouts, drills and flight reports share one local save. Each bank of switches sits on one refracting pane. All 14 controls have a pointer-responsive cap and a fixed hit area. The standalone project demonstrates live glass refraction, instrument transitions, URP submission and URUI Effect wave gauges and particles.

Full-resolution stills of the [orbital station](docs/images/tideglass-lobby.png) and [pilot detail](docs/images/tideglass-hero.png).

---

## Why URUI?

- **Built for AI + Text Diffs**: Screens, components, design tokens, and multi-track keyframe timelines live in clean `.html`, `.css`, and `.urui-sequence.json` text files—friendly to Git diffs, browser layout checks, and agentic authoring.
- **Native Performance Without Prefab Sprawl**: Native C++ compiles ordered render plans (`render-plan9`), 8-texture batching, parameter page uploads, and persistent native/GPU memory fences—keeping steady-state managed GC allocations near zero and without creating one `GameObject` per visual node.
- **Standalone Multi-Module Architecture**:
  - [**`com.fujisheng.urui`** (`0.8.0-preview.1`)](Packages/com.fujisheng.urui/README.md): Core runtime (`Surface`, `Document`, `Element`, typed controls, virtualized `ListView`, `CompiledDocumentAsset` AOT bytecode, `SequencePlayer`, `StaggeredReveal`, `BezierMotion`, `Surface.GetRenderingStatistics()`).
  - [**`com.fujisheng.urui.effect`** (`0.5.0-preview.1`)](Packages/com.fujisheng.urui.effect/README.md): CSS `--urui-fx-*` visual filters, `EffectController` presets/tweens, `<urui-particles>`, `ArcTextLayout`, `WaveEffect`, and `TextureEffectSurface`.
  - [**`com.fujisheng.urui.authoring`** (`0.1.0-preview.1`)](Packages/com.fujisheng.urui.authoring/README.md): Editor-only visual inspector, source hierarchy, timeline animation editor, and localhost Agent HTTP v1 protocol.

---

## Quick Start

Open [SampleProject](SampleProject) with Unity 6000.3.22f1. Select **Tools → URUI → Open Tideglass Beacon Sample** and enter Play Mode. For a Built-in project, import **Tideglass Beacon** under Core's Package Manager samples. See the [sample guide](Packages/com.fujisheng.urui/Samples~/Tideglass%20Beacon/README.md) for controls and sources.

## Documentation Index

### Repository Guides ([EN](docs/GETTING_STARTED.md) / [中文](docs/GETTING_STARTED_ZH.md))

| Topic | English | 中文 |
| --- | --- | --- |
| Getting Started & Installation | [GETTING_STARTED.md](docs/GETTING_STARTED.md) | [GETTING_STARTED_ZH.md](docs/GETTING_STARTED_ZH.md) |
| Architecture & Native Protocol (`ABI13`) | [ARCHITECTURE.md](docs/ARCHITECTURE.md) | [ARCHITECTURE_ZH.md](docs/ARCHITECTURE_ZH.md) |
| URP & Built-in Render Pipeline Integration | [RENDER_PIPELINE_URP.md](docs/RENDER_PIPELINE_URP.md) | [RENDER_PIPELINE_URP_ZH.md](docs/RENDER_PIPELINE_URP_ZH.md) |
| Performance, Batching & Memory Safety | [PERFORMANCE_AND_MEMORY.md](docs/PERFORMANCE_AND_MEMORY.md) | [PERFORMANCE_AND_MEMORY_ZH.md](docs/PERFORMANCE_AND_MEMORY_ZH.md) |
| HTML & CSS Support Cheatsheet | [HTML_CSS_CHEATSHEET.md](docs/HTML_CSS_CHEATSHEET.md) | [HTML_CSS_CHEATSHEET_ZH.md](docs/HTML_CSS_CHEATSHEET_ZH.md) |
| AI-Assisted UI Workflow & Live Authoring | [AI_WORKFLOW.md](docs/AI_WORKFLOW.md) | [AI_WORKFLOW_ZH.md](docs/AI_WORKFLOW_ZH.md) |
| Native Build, Tests & Contribution Guide | [DEVELOPMENT.md](docs/DEVELOPMENT.md) | [DEVELOPMENT_ZH.md](docs/DEVELOPMENT_ZH.md) |
| Code Organization & Assembly Boundaries | [CODE_ORGANIZATION.md](docs/CODE_ORGANIZATION.md) | [CODE_ORGANIZATION_ZH.md](docs/CODE_ORGANIZATION_ZH.md) |

### Package Manuals

- **Core (`com.fujisheng.urui`)**: [README](Packages/com.fujisheng.urui/README.md) · [中文 README](Packages/com.fujisheng.urui/README_ZH.md) · [Quick Start](Packages/com.fujisheng.urui/Documentation~/QUICK_START.md) · [API Reference](Packages/com.fujisheng.urui/Documentation~/REFERENCE.md)
- **Effect (`com.fujisheng.urui.effect`)**: [README](Packages/com.fujisheng.urui.effect/README.md) · [中文 README](Packages/com.fujisheng.urui.effect/README_ZH.md) · [CSS Reference](Packages/com.fujisheng.urui.effect/Documentation~/CSS.md)
- **Authoring (`com.fujisheng.urui.authoring`)**: [README](Packages/com.fujisheng.urui.authoring/README.md) · [中文 README](Packages/com.fujisheng.urui.authoring/README_ZH.md) · [Agent HTTP API](Packages/com.fujisheng.urui.authoring/Documentation~/agent-api.md)

---

## Supported Platforms & Toolchains

Prebuilt native plugins under [`Packages/com.fujisheng.urui/Runtime/Plugins`](Packages/com.fujisheng.urui/Runtime/Plugins) target 7 architectures using ABI13 / render-plan9 / element4:
`macOS ARM64`, `macOS x86_64`, `Windows x86_64`, `Android ARM64`, `Android x86_64`, `iOS ARM64 (static)`, and `WebGL wasm32 (static)`.

Verification baseline: Unity `6000.3.22f1`. See [`PLUGINS.json`](Packages/com.fujisheng.urui/Documentation~/PLUGINS.json) for build metadata and third-party notices in [`Third Party Notices.md`](Packages/com.fujisheng.urui/Third%20Party%20Notices.md).
