# URUI Authoring (`com.fujisheng.urui.authoring`)

**English** | [中文](README_ZH.md)

Version: `0.1.0-preview.1` · Editor-Only Assembly (`URUI.Authoring.Editor`) · Unity Baseline: `6000.3.22f1`

Source-first visual authoring workbench, live Play Mode Inspector, timeline animation editor (`*.urui-sequence.json`), and localhost HTTP v1 Agent Automation API for URUI. Everything stays strictly Editor-only (`includePlatforms: ["Editor"]`) and adds zero runtime bloat to player builds.

## Highlights

- **Authoring Workbench & Live Inspection**: Inspect HTML/CSS DOM hierarchy (`HierarchyWindow`), edit properties & rules with lexical source fidelity and Unity Undo support (`SourceDocument`, `CssSource`, `SessionUndoBridge`).
- **Animation Timeline Mode**: Author multi-track `.urui-sequence.json` definitions combining CSS keyframes and optional `urui-particles` tracks, scrub/preview deterministically without mutating scene clocks.
- **Local HTTP Agent Protocol (`v1`)**: Opt-in localhost API (`Library/URUI/Authoring/endpoint.json`) enabling AI coding agents to open documents, query layout bounds, edit CSS/HTML safely with revision tokens, trigger captures (`preview.capture`), and save atomically.

## Documentation

| Guide | English | 中文 |
| --- | --- | --- |
| Workbench Overview & User Guide | [index.md](Documentation~/index.md) | [index_ZH.md](Documentation~/index_ZH.md) |
| Unified Sequence & Timeline Animation | [animation.md](Documentation~/animation.md) | [animation_ZH.md](Documentation~/animation_ZH.md) |
| Editor C# API (`AuthoringSession`) | [editor-api.md](Documentation~/editor-api.md) | [editor-api_ZH.md](Documentation~/editor-api_ZH.md) |
| Localhost Agent HTTP API (`v1`) | [agent-api.md](Documentation~/agent-api.md) | [agent-api_ZH.md](Documentation~/agent-api_ZH.md) |

## Samples & Verification

- Package Samples: [`Samples~/Animation Demo`](Samples~/Animation%20Demo/README.md) · [`Samples~/Animation Effect Demo`](Samples~/Animation%20Effect%20Demo/README.md)
- Standalone managed authoring test suite: `dotnet run --project Tools/Tests/Authoring/AuthoringTests.csproj`
