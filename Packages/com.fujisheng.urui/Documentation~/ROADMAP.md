# Roadmap

URUI aims to make Unity UI legible, inspectable, and fast to iterate for both engineers and AI coding agents through standard HTML/CSS paired with crisp C# boundaries. For current platform/feature coverage see [Limitations & Support Matrix](LIMITATIONS.md); items below outline planned directions without fixed release commitments.

## Current Foundation

Core provides shared `Surface`, multi-document isolation, typed DOM controls, synchronous input propagation, CSS styling/animations/sequences, GPU-fenced resource leases, and runtime rendering statistics. Optional `URUI.Effect` adds modular GPU shaders/particles and offscreen processing, while `URUI.Authoring` adds Editor visual layout/timeline authoring and an AI Agent HTTP v1 protocol, demonstrated end-to-end in [Tideglass Beacon](../../com.fujisheng.urui/Samples~/Tideglass Beacon/README.md) and `SampleProject`.

## Planned Directions

| Area | Next Milestones |
| --- | --- |
| Platform Validation | Complete Unity Player & device runtime verification across packaged Android, iOS, Windows, and WebGL binaries; evaluate Linux & iOS Simulator |
| Framework Adapters | Keep tightening resource, focus, navigation, and binding boundaries in external UI framework adapters with zero framework coupling in Core |
| Render Pipelines | Broaden URP camera/multi-target configuration coverage and evaluate HDRP / alternate graphics APIs |
| Input & Controls | Complete native OS IME, mobile soft-keyboard, touch gesture, controller navigation, and accessibility support |
| Docs & Tooling | Maintain EN/ZH bilingual parity, API signature checks, and asset diagnostics |
| Authoring Experience | Expand diff-friendly hot reload workflows, richer timeline presets, and visual debugging tools |
| Performance & QA | Build reproducible Player performance regression suites across colorspaces, shader presets, and dense virtualized screens |

Full web browsers and arbitrary JavaScript VMs are intentionally out of scope. Static archive checks, Editor validation, and real Player/device metrics are tracked separately.
