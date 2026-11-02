# Borrowed Unity Images Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox syntax for tracking.

**Goal:** Register existing Unity Texture2D images without CPU pixel reads/native RGBA retention/second GPU textures, while keeping generated textures owned.
**Architecture:** ImageAsset remains a simple source-to-Texture2D mapping, now borrowed by default. A document-owned registry allocates monotonically unique high-bit resource IDs, deduplicates Texture2D references, and supplies textures to every renderer. Native texture protocol v1 stores borrowed metadata and named aliases only; existing ABI3 layouts and generated texture uploads remain intact. A shared shader helper handles source orientation and alpha, without generating converted textures or changing caller sampler state.
**Tech Stack:** Unity6000.3 C#, C++17 RmlUi bridge, Built-in shaders, existing seven native cross toolchains.
**Spec:** /Volumes/FData/URUI/Artifacts/ImageMemoryAudit/AUDIT.md, approved user instruction 2026-11-02 10:38:56.

## Global Constraints
- Existing main workspace authorized; no new branch, remote push, release or tag modification.
- Preserve concurrent user edits; debug default OFF; no Player/Android APK/performance matrix.
- Existing Unity texture borrowed, never Destroy/UnloadAsset/Apply by URUI; no hidden pixel conversions or duplicated GPU textures.
- Generated/font textures retain explicit owned lifetime and current retirement rules.
- All seven plugin binaries and export/capability manifest must match the same native sources.

## Review Focus
- Borrowed source destroyed/resized/reformatted while registered must fail clearly, without a copy fallback.
- Old context IDs cannot resolve to a newly registered texture after reload or deferred plan retirement.
- Multiple source aliases to one texture must share one native metadata record and Unity texture instance.
- Half-transparent colored edges, UV row orientation, sRGB/linear source interpretation and sampler state must remain correct.
- Shared/experimental renderer retirement must never destroy borrowed resources or skip owned destruction.

### Task 1: Native borrowed metadata protocol
Files: Native~/urui.h, bridge.cpp, new native protocol regression test/runner, Tools/build-native-platforms.py.
Interfaces: urui_texture_protocol_version()=1; urui_register_external_texture(source,width,height,uint64_t resource_id); resource_id high bit set, same id/size can have aliases, registration only with document unloaded; urui_get_texture_kind(id):0missing/1owned/2borrowed. Borrowed get_texture_info bytes0, copy_texture fails explicitly. Unregister removes only last alias's record. IDs originate in managed registry and never recycle.
- [x] Add native test; observe missing protocol / current duplicated pixels before code.
- [x] Implement metadata-only registration, alias accounting, explicit kind/version checks.
- [x] Build mac ARM64 and run protocol lifecycle/alias/collision/no-pixel-copy tests.

### Task 2: Managed borrowing and rendering
Files: Runtime/Resources/Images/ImageAsset.cs, Documents/Configuration, Internal/Resources/BorrowedTextureRegistry.cs, all host/parameter/frame texture paths, shared shader helper and two shaders/contracts.
Interfaces: registry.Register(ImageBinding[]) populates native metadata; TryResolve(id,out Texture2D) checks lifetime/dimensions/format; records retained by render plans until their existing retirement finishes. No owned cache stores borrowed texture. Uniform helper defines source size, borrowed flag and sRGB interpretation.
- [x] Add real Editor regression: non-readable source + aliases produces zero native pixel bytes and zero extra image Texture2Ds; watch baseline fail.
- [x] Implement registry, public mapping behavior, shader properties and ownership-specific cleanup in all paths.
- [x] Test source updates, reload/unload/destroy, non-recycled IDs, source destruction diagnostics, source sampler unchanged, owned font texture release, parameter/frame plan borrowed lifetimes.
- [x] Render UV/alpha edge comparisons and Gamma/Linear/color source tests; no blanket post-filter alpha multiplication artifacts.

### Task 3: Sample, all binaries and verification
Files: sample resource checks/import settings/docs, plugin manifest, seven binaries; targeted test tools.
- [x] Remove readable requirement from sample and its 40 imported images after checking that no code consumes pixels.
- [x] Build all seven platforms from frozen native sources with debug OFF; check architecture, all production exports, native texture protocol and Android16KiB alignment; static iOS/WebGL link checks.
- [x] Run short borrowed texture Editor checks and existing GameFlow94/element functional checks that cover touched behavior; compare source/import files.
- [x] Update ownership/API/limitations docs and validation manifest with actual evidence boundaries.
- [x] Fresh review, address real defects with regression checks, final diff/status audit. Local commit only if all required checks pass; no push.

### Task 4: User-requested batching verification

- [x] Read native/C# batch keys, uniform/material state and atlas path.
- [x] Three ordered layouts, RGBA reference and borrowed paths, batching off/on:12records/60assertions passed. Recorded UI submissions and command-buffer bytes are reported; headless GPU counters were unusable.
