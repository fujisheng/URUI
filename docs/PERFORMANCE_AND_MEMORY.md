# Performance, Batching & Memory Safety

**English** | [中文](PERFORMANCE_AND_MEMORY_ZH.md)

URUI is engineered so UI richness does not degrade frame time or trigger hidden Editor/Runtime memory growth.

## 1. Zero-GC & Minimal Managed-Native Crossing

- **No per-element `MonoBehaviour`**: A 500-node DOM screen runs inside one `Surface` + one `Document` component without 500 `GameObject` transforms or Canvas Rebuild cascades.
- **Persistent Buffers & Pooled Snapshots**: Native UTF-8 reads reuse bounded thread buffers; `DocumentsSnapshot()` updates only when membership changes; deferred action queues store value records rather than closure chains.
- **Monotonic `UnityUIClock`**: Absorbs sub-microsecond floating-point jitter across `FixedUpdate` / frame boundaries so CSS keyframes and native timers never stall or throw on monotonic clamping.

## 2. `render-plan9` GPU Batching & Parameter Paging

- **8-Texture Batching Slots**: Built-in URUI shaders bind up to 8 textures simultaneously with per-vertex texture slot indices, batching sprites, borrowed icons, and FreeType glyph atlases together without manual sprite-atlas repacking.
- **Immutable GPU Parameter Pages**: Once base vertex coordinates are compiled, transform deltas, colors, opacity, and UV updates travel inside small parameter texture rows instead of rewriting full CPU/GPU vertex buffers every frame.
- **Bounded Mesh Cache Pool**: Under dynamic vertex count churn (animated counters, streaming text, changing lists), `RenderPlanResources` evicts LRU/idle GPU `Mesh` buckets beyond bounded limits instead of growing unboundedly.

## 3. GPU Fence & Native Memory Leak Prevention

Every GPU resource that changes size, gets unloaded, or switches source must survive until all in-flight GPU commands referencing it finish:
- **In-flight Submission Fence Cap (`8`)**: Each `RenderHost` tracks up to 8 pending GPU submissions via `GraphicsFence` and retires temporary `RenderTexture` targets, meshes, borrowed image pins, and effect materials cleanly only once their last reader fence clears.
- **Native Allocator & Plan Hygiene**: Unused intermediate capture targets and off-screen masks are pruned statically when inputs produce empty coverage or zero external consumers.

## 4. Runtime Telemetry API (`Surface.GetRenderingStatistics`)

Monitor live render health cleanly in both Unity Editor and release Player builds without `URUI_DEBUG` compiler flags:

```csharp
SurfaceRenderingStatistics stats = surface.GetRenderingStatistics();
if (stats.Ready)
{
    // stats.LoadedDocuments, stats.RecordedGeometryDraws, stats.NativePlanCommandCount,
    // stats.NativePlanFrameBytes, stats.NativePlanGeometryBytes,
    // stats.GeometryUploadBytes, stats.ParameterUploadBytes, stats.ParameterCopyBytes
}
```

## 5. Authoring Best Practices for Smooth 60+ FPS

1. **Use `CompiledDocumentAsset` in Production**: Pre-compile static UI screens with `CompiledDocumentCompiler.CompileAndSave` (`SOLSTICE` Armory demonstrates AOT bytecode instant load for 10,000 items via `Backpack.compiled.asset`).
2. **Use Fixed-Height `ListView` for Large Catalogs**: Only visible + buffer rows are realized (`RealizedItemCount` stays ~14–18 even over 10,000 items).
3. **Switch Themes via `@property` Color Tokens**: Toggle root classes (`theme-obsidian` / `theme-crimson` / `theme-alabaster`) cleanly instead of unloading/reloading entire HTML trees.
4. **Subset Large CJK Fonts**: Bundle only required glyphs (for example, the `SOLSTICE` subset reduced `NotoSansSC-Bold` from ~10 MB down to ~200 KB) and disable texture mipmaps/streaming on UI textures.
