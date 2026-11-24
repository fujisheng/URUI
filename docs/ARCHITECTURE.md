# URUI Architecture Overview

**English** | [中文](ARCHITECTURE_ZH.md)

URUI separates **authoring representation (HTML/CSS)**, **native layout & render-plan compilation (`URUIBridge`)**, and **managed Unity execution (`Surface`, `Document`, `Element`, `RenderHost`)**.

## Layered System Design

```text
+-------------------------------------------------------------------+
|  Author Files: HTML5 / CSS3 / .urui-sequence.json / Fonts / Images |
+-------------------------------------------------------------------+
             |                         |
             v (AOT or Load)           v (Editor Only)
+---------------------------+  +------------------------------------+
| com.fujisheng.urui        |  | com.fujisheng.urui.authoring       |
| - Surface (shared host)   |  | - SourceDocument & CssSource AST   |
| - Document (multi-layer)  |  | - Hierarchy / Inspector / Timeline |
| - Controls & Animations   |  | - Localhost Agent HTTP API v1      |
| - RenderHost (GPU batch)  |  +------------------------------------+
+---------------------------+
             |
             v (P/Invoke C ABI13 / 172 exports / __Internal iOS+WebGL)
+-------------------------------------------------------------------+
| URUIBridge Native C++ Engine                                       |
| - Embedded RmlUi + Yoga-style Flexbox & CSS cascade               |
| - FreeType 2.13.3 subpixel glyph rasterization                    |
| - render-plan9 command & layer compiler                           |
+-------------------------------------------------------------------+
             |
             v (Extension Layer)
+-------------------------------------------------------------------+
| com.fujisheng.urui.effect                                         |
| - 106 CSS --urui-fx-* parameter properties                        |
| - EffectController, WaveEffect, TextureEffectSurface, Particles   |
+-------------------------------------------------------------------+
```

## Core Runtime Abstractions

### 1. Shared `Surface` & Multi-`Document` Stack
- A `Surface` (`MonoBehaviour`) owns one native UI context, logical CSS viewport (`Surface.Viewport`), monotonic clock (`Surface.UIClock`), input router (`Surface.Input`), GPU mesh/texture caches (`RenderHost`), and submission fence tracking.
- Multiple `Document` instances share one `Surface`, each isolating its own DOM tree, stylesheets, images, templates, and cleanup lifecycle.
- Ordering & modal blocking across concurrent pages (such as a shell HUD bar + content pages + detail modals + telemetry overlays) are controlled declaratively via `Document.SetPresentation(int layer, int order, bool visible, bool modal)`.

### 2. Native Contract Versions
Currently verified across all 7 shipped binary platforms (`PLUGINS.json`):
- **Bridge ABI**: `13` (`172` production C exports with `urui_` prefix)
- **Render Plan Protocol**: `9` (`render-plan9`)
- **Element State Protocol**: `4` (`element4`)
- **Texture Borrow Protocol**: `v1` (zero-copy `Texture2D` metadata referencing)
- **Sequence API**: `v2` (`SequenceDefinition`, CSS keyframes + extensible track factories)

### 3. Rendering & Batching Pipeline (`render-plan9`)
1. Native engine updates active clocks, layout, and dirty geometry.
2. `render-plan9` compiles spatially-sorted draws, texture groups (up to 8 bound textures per batch with shader texture-slot selection), immutable GPU parameter pages, and minimal off-screen layer captures.
3. `RenderHost` uploads only dirty geometry/parameter spans and records `CommandBuffer` draws guarded by explicit GPU retirement fences (capped at 8 in-flight frames per host).
4. Live diagnostics (`Surface.GetRenderingStatistics()`) expose draw counts, native command counts, geometry bytes, frame bytes, and upload counters cleanly in both Editor and Players.

See also: [Code Organization](CODE_ORGANIZATION.md) · [Performance & Memory](PERFORMANCE_AND_MEMORY.md) · [URP Integration](RENDER_PIPELINE_URP.md).
