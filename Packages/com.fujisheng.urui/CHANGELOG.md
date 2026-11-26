# Changelog

## 0.8.0-preview.1 — Native ABI13, rendering statistics and Tideglass Beacon

- Upgraded native bridge to **ABI13 / render-plan9 / element4 / 172 production exports** across all 7 target platforms with unified memory safety fixes across native layout, GPU resource fences, `UnityUIClock`, and cached render layers.
- Added public `Surface.GetRenderingStatistics()` returning `SurfaceRenderingStatistics` in both Unity Editor and release Players (`Ready`, `LoadedDocuments`, `RecordedGeometryDraws`, `NativePlanCommandCount`, `NativePlanGeometryBytes`, `NativePlanFrameBytes`, `GeometryUploadBytes`, `ParameterUploadBytes`, `ParameterCopyBytes`).
- Added AOT `CompiledDocumentAsset` compilation (`CompiledDocumentCompiler.CompileAndSave`), template cloning (`<template>` + `CloneContentTo`), and compiled query snapshots.
- Shipped **Tideglass Beacon** as the Core package sample and in the standalone URP project. Open it with **Tools → URUI → Open Tideglass Beacon Sample** to try crew selection, station management, equipment and combat. The bilingual project homepage includes a recording of the sample running in Unity.

## 0.7.1-preview.1 — Visual Fixes

- Preserved HTML `hidden` state in shared text layout; unified standard `border-image` rendering with image effects without extra overlays.
- Improved glyph `dilate` color preservation and subpixel `px`/`em` outlines, plus source dimension validation diagnostics.

## 0.7.0-preview.1 — Profiler-Driven Runtime Optimizations

- Converted paint callback windows and `EffectFrame` to generation-guarded readonly structs; removed per-call deferred closures and duplicated native sync calls.
- Introduced native frame memcmp stability tracking, `UnityUIClock` monotonic sub-microsecond rounding guard, and strict GPU-fence resource retirement.

## 0.6.1-preview.1 & 0.6.0-preview.1 — Default Control Skins & 8-Texture Batching

- Unified 8-texture slot shader batching across images and glyphs, spatial overlap-aware sorting, and UGUI-compatible neutral default control skins with `@supports` preview selection.

## 0.4.0-preview.1 — 0.2.0-preview.1 — Core HTML/CSS Engine & Controls

- Established shared `Surface` + multi-`Document` architecture, `ImageAsset` zero-copy GPU borrowing, typed UI controls, virtualized `ListView`, synchronous event propagation, and multi-platform plugin pipeline.
