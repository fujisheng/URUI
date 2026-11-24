# URUI Core (`com.fujisheng.urui`)

**English** | [中文](README_ZH.md)

Version: `0.8.0-preview.1` · Unity Baseline: `6000.3.22f1` · Native Protocol: `ABI13 / render-plan9 / element4 / 172 exports`

URUI Core embeds a native HTML/CSS layout and rendering engine (`URUIBridge`) inside Unity while keeping business C# completely text-first: HTML defines structure, CSS drives layout/theming/keyframes, and managed C# (`Document`, `Surface`, `Element`, controls) manages logic without per-element `MonoBehaviour` overhead.

![SOLSTICE Core Showcase](Documentation~/Images/solstice.gif)

## Primary & Learning Samples

| Sample | Purpose | Compatibility |
| --- | --- | --- |
| **Solstice** *(Primary)* | Modern fantasy tactical RPG HUD: 3 CSS themes, `Radial360` skills, `SequencePlayer` summon reveal, controls, 10k `ListView`, `Surface.GetRenderingStatistics()` | Built-in & URP |
| **Hello URUI** | Minimal document loading, style writes, disposable `ClickEvent` subscriptions | Built-in & URP |
| **Element Playground** | Explicit DOM node creation, reparenting, class toggles, detachment/destruction | Built-in & URP |
| **Runtime Controls** | Typed form controls, focus navigation, virtualized `ListView` | Built-in & URP |

Quick launch in Editor: **Tools → URUI → Open Solstice Sample** (imports and opens `Solstice.unity`), or open [`../../SampleProject/`](../../SampleProject) for the full URP 3D + Effects edition.

## Documentation ([EN](Documentation~/QUICK_START.md) / [中文](Documentation~/QUICK_START_ZH.md))

| Guide | English | 中文 |
| --- | --- | --- |
| Installation & Native Plugins | [INSTALL.md](Documentation~/INSTALL.md) | [INSTALL_ZH.md](Documentation~/INSTALL_ZH.md) |
| Quick Start & First Screen | [QUICK_START.md](Documentation~/QUICK_START.md) | [QUICK_START_ZH.md](Documentation~/QUICK_START_ZH.md) |
| Document & Element API | [API.md](Documentation~/API.md) | [API_ZH.md](Documentation~/API_ZH.md) |
| Controls & Virtual ListView | [CONTROLS.md](Documentation~/CONTROLS.md) | [CONTROLS_ZH.md](Documentation~/CONTROLS_ZH.md) |
| Default Control Skinning | [DEFAULT_CONTROLS.md](Documentation~/DEFAULT_CONTROLS.md) | [DEFAULT_CONTROLS_ZH.md](Documentation~/DEFAULT_CONTROLS_ZH.md) |
| Input, Focus & Events | [EVENTS.md](Documentation~/EVENTS.md) | [EVENTS_ZH.md](Documentation~/EVENTS_ZH.md) |
| Lifecycle, Ownership & GPU Leases | [LIFECYCLE.md](Documentation~/LIFECYCLE.md) | [LIFECYCLE_ZH.md](Documentation~/LIFECYCLE_ZH.md) |
| CSS Styles & Unified Animation | [STYLES_ANIMATION.md](Documentation~/STYLES_ANIMATION.md) | [STYLES_ANIMATION_ZH.md](Documentation~/STYLES_ANIMATION_ZH.md) |
| SRP Submission, Painters & Stats | [RENDERING.md](Documentation~/RENDERING.md) | [RENDERING_ZH.md](Documentation~/RENDERING_ZH.md) |
| Code Style Conventions | [CODE_STYLE.md](Documentation~/CODE_STYLE.md) | [CODE_STYLE_ZH.md](Documentation~/CODE_STYLE_ZH.md) |
| Support Matrix & Limitations | [LIMITATIONS.md](Documentation~/LIMITATIONS.md) | [LIMITATIONS_ZH.md](Documentation~/LIMITATIONS_ZH.md) |
| Roadmap | [ROADMAP.md](Documentation~/ROADMAP.md) | [ROADMAP_ZH.md](Documentation~/ROADMAP_ZH.md) |
| Complete Public C# Reference | [REFERENCE.md](Documentation~/REFERENCE.md) | [REFERENCE_ZH.md](Documentation~/REFERENCE_ZH.md) |
| Verification & Editor Fixtures | [Tests~/Editor/README.md](Tests~/Editor/README.md) | [Tests~/Editor/README_ZH.md](Tests~/Editor/README_ZH.md) |

## Machine-Readable Schemas & Notices

- CSS / API / Control Support Matrix: [`Documentation~/HtmlCssSupport.json`](Documentation~/HtmlCssSupport.json)
- Native Binary Checksums & Exports: [`Documentation~/PLUGINS.json`](Documentation~/PLUGINS.json)
- Changelog: [`CHANGELOG.md`](CHANGELOG.md) · Notices: [`Third Party Notices.md`](Third%20Party%20Notices.md) · License: [`LICENSE.md`](LICENSE.md)
