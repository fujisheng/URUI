# Solstice — URUI Core Package Sample

**English** | [中文](README_ZH.md)

**Solstice** (*Solstice · Sanctuary*) is URUI Core's flagship self-contained sample demonstrating a modern fantasy tactical RPG HUD built with pure HTML, CSS, and managed C# (`URUI.Runtime` only—zero `URUI.Effect` and zero `URP` dependencies; works out of the box in Built-in RP and URP).

## How to Open

1. In Unity Editor, select **Window → Package Manager → URUI → Samples → Solstice → Import**,
   or run **Tools → URUI → Open Solstice Sample** (imports and opens `Solstice.unity` automatically).
2. Press **Play** to explore all 5 screens:
   - **Sanctuary Lobby**: High-contrast obsidian-indigo HUD over full-bleed sky-citadel artwork, vanguard hero switching (`Aurelia`, `Kael`, `Lyra`, `Veyra`), expedition quests, and skewed Battle CTA.
   - **Combat Encounter**: Boss HP/Shield progress bars, combat log, 1x/2x battle speed & auto battle, and 4 RPG skill buttons with live `Image.FillMode = Radial360` cooldown sweeps.
   - **Heroes Roster**: Hero card selection, stat progression upgrade, and CSS highlight states.
   - **Summon Astrolabe**: Multi-track `SequencePlayer` (CSS orbital rotation + card reveal flip) and pity counter bar.
   - **10,000-Item Virtualized Armory**: Category filters, live `TextField` search, and pooled `ListView` recycling visible rows across 10,000 relics.
   - **Settings Modal & Dev Telemetry**: Live `@property` CSS theme switching (`Obsidian Indigo`, `Ember Crimson`, `Sunlit Alabaster`), volume `Slider`, and `Surface.GetRenderingStatistics()` drawer.

## Full URP + Effects Edition

For the full URP 3D parallax stage (`UruiUrpRendererFeature`), `TextureEffectSurface` custom HLSL shader banners, `<urui-particles>` bursts, `ui-wave` vertex banners, `ArcTextLayout`, and `CompiledDocumentAsset` AOT precompilation, open repository root [`SampleProject/`](../../../../SampleProject).
