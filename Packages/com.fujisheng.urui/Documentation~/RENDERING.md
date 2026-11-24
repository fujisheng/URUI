# Rendering Extensions & Media

Everyday UI uses elements, CSS, and controls. This page targets custom painters, registered effect programs, render pipeline integration, runtime rendering statistics, and video textures. See [Public API Reference](REFERENCE.md) for declarations and [URUI.Effect](../../com.fujisheng.urui.effect/README.md) for ready-made GPU shaders & particles.

## Submitting Shared `Surface` Rendering

Built-in renders automatically via camera events; SRP adapters (e.g., URP) invoke:

```text
bool Surface.SubmitRendering(ScriptableRenderContext context,
    RenderTargetIdentifier target, Rect viewport)
```

Pass valid pipeline `context`, destination target identifier (or use the `CommandBuffer` + `RenderTargetInfo` overload in Unity 6 RenderGraph `AddUnsafePass`), and positive finite pixel `viewport`. Submit **once per active `Surface`**—never once per `Document`. Returns `false` when no active host/documents are ready (not a GPU completion signal).

Each host allows up to **8** in-flight GPU submissions (including retired frames awaiting completion). Every submission retains read references on meshes, parameter textures, cached layers, and scratch/backdrop render targets until the final reader's GPU fence completes.

URP integration is self-contained inside `SampleProject`'s `UruiUrpRendererFeature` (plus optional standalone framework adapters), keeping `com.fujisheng.urui` free of direct URP package dependencies. Verify custom pipeline configurations against your target renderer asset.

### Runtime Rendering Statistics (`Surface.GetRenderingStatistics`)

Call `Surface.GetRenderingStatistics()` in both the Unity Editor and release Players for a zero-allocation `SurfaceRenderingStatistics` snapshot reporting `Ready`, `LoadedDocuments`, `RecordedGeometryDraws`, `RecordedExternalGeometryDraws`, `NativePlanCommandCount`, `NativePlanGeometryBytes`, `NativePlanFrameBytes`, `NativePlanAllocations`, `NativePlanFrees`, `NativePlanAllocatedBytes`, `GeometryUploadBytes`, `ParameterUploadBytes`, and `ParameterCopyBytes`.

### Native Plan Batching, Parameter Pages & Surface Optimizations

Normal geometry sorting, texture grouping, conservative bounds, and batch caching are emitted directly by the native render plan. Compiled static batches preserve initial local vertex positions while transforms, uniform tints, and UVs update via immutable parameter pages uploaded cleanly without regenerating vertex buffers. Where native analysis proves all draw data lives directly in vertices, URUI selects parameterless shader passes automatically.

Screen backdrop compositing declarations, same-domain opaque target merging, non-overlapping R16/pixel-aligned mask pruning, and `CopyTexture` fast paths (when destination size/format matches safely) are driven directly by the native render plan with zero user toggles, retiring temporary targets strictly via GPU fences.

## Custom Element Painters

`document.AttachPainter(Element element, string shader, Action<ElementPaintContext> paint, Action release = null)` returns a disposable binding lease activated by `--urui-decorator:shader(name)` or `--urui-mask-image:shader(name)` in CSS.

`ElementPaintContext` provides `Element`, `Size`, `IsMaskImage`, `IsClippedOut`, and `DrawMesh(mesh, material, localTransform, properties, submesh, pass)`. Keep meshes/materials valid until GPU completion (`document.RetirePainterResources(IDisposable)` queues safe retirement). Never cache `ElementPaintContext` snapshots outside the paint callback, and never mutate DOM inside `paint`. Because arbitrary painter output bounds are unknown prior to execution, consider External Geometry providers below whenever you can supply precomputed static geometry for tighter culling and batching.

## Registering Effect Programs

| API | Purpose |
| --- | --- |
| `document.Effects.Register(EffectDefinition)` | Registers a named effect program before loading documents that reference it |
| `EffectDefinition(name, kind, factory, programKey, colorContract, ...)` | Declares image vs subtree kind, factory, stable identity key, color contract, and CSS parameters |
| `IEffectFactory.Create(EffectContext)` | Instantiates per-element effect runtime state |
| `IElementEffect.Prepare(EffectFrame)` | Reads frozen immutable frame snapshot and prepares GPU resources |
| `IElementEffect.Record(EffectRenderContext)` | Records draws only |
| `EffectContext.Own<T>(resource)` / `Retire(resource)` | Owns `IDisposable` objects and retires replaced resources safely via GPU fence |
| `EffectContext.RequirePreparation()` | Requests preparation on next pass |
| `document.Effects.TryGetFrame(element, out frame)` | Fetches latest effect frame snapshot safely |

`EffectFrame` is an immutable snapshot (`ElementToken`, `Layout`, `Style`, `Size`, `Time`, `Source`, `SourceVersion`, `SourceGeometry`, `SourceCaptureBounds`, `SourceUvBounds`, `ElementToDocument`). Always track `SourceVersion` alongside texture references so dynamic render texture updates never falsely skip refreshes. Activate via `--urui-image-effect:name` (texture-level) or `--urui-effect:name` (element/subtree-level).

For fixed-layout high-frequency effects (such as `URUI.Effect`), use `RenderProgramDescriptor` + `RenderParameterLayout` with `IParameterImageEffect.PrepareProgram`, `IParameterImageGeometry`, `IParameterEffectLayout`, `IParameterImageRuntime`, and `EffectRegistry.BindProgramParameters` so native builds dense float4 parameter tables once and shaders sample directly without per-element material cloning.

## External Geometry Providers

`Document.AttachGeometryProvider` registers pure geometry callbacks isolated cleanly from arbitrary `AttachPainter` boundaries. `ExternalGeometryContext.Draw` takes `ExternalGeometryInput`, `ExternalGeometryProgram`, and local matrix. Source `Mesh` objects must remain CPU-readable with triangle submeshes; increment `GeometryVersion` on vertex/index/layout edits and `ResourceVersion` whenever external buffers/textures mutate outside shader reflection. Native merges adjacent compatible providers when material, clipping, and texture budgets allow; `URUI.Effect` particle rendering builds directly on this layer.

## Subtree Filters

Built-in CSS `filter` chains (`brightness`, `contrast`, `grayscale`, `saturate`, `opacity`, `blur`, `drop-shadow`) run natively in declaration order, allocating bounded offscreen targets only when blur/drop-shadow/group semantics require it. Register custom filters via `RegisterFilter(string name, Func<ILayerFilterProgram> factory, bool cacheable = false)` (`cacheable: true` only for pure deterministic filters depending strictly on declared inputs). Inspect filter buffer residency via `EffectStatistics.GetTargets()`.

## Video Output

`URUI.Media.VideoPlayback` wraps Unity `VideoPlayer` to decode frames into a managed `RenderTexture` `Output` decoupled cleanly from `Surface.UIClock`:

| Member | Behavior |
| --- | --- |
| `Play(VideoClip clip, bool loop = true)` / `PlayUrl(string url, bool loop = true)` | Starts playback cleanly |
| `Pause()` / `Resume()` / `Replay()` / `Stop()` | Playback controls |
| `HasPresentedFrame`, `PresentedFrame`, `PresentedVersion` | Ready flag, frame index, and monotonic content version |
| `FramePresented : Action<VideoPlayback>` / `Failed : Action<string>` | Events |
| `AcquireOutput() : ImageLease` | Issues a ref-counted lease to pass into `ImageResolver` |

Never return `null` from `ImageResolver` expecting async retry once a video warms up—wire placeholder assets or swap URIs after `FramePresented`. For live video post-processing shaders, pair with `URUI.Effect`'s [`SurfaceEffectInput`](../../com.fujisheng.urui.effect/Documentation~/RENDERING.md) driven by `PresentedVersion` and `HasPresentedFrame`.

## Releasing Owned Unity Objects

For custom owned `UnityEngine.Object` instances outside GPU fence tracking, consult [Lifecycle & Resources](LIFECYCLE.md) and call `GpuRetirement.DestroyOwnedObject(UnityEngine.Object)` (selects `Destroy` vs `DestroyImmediate` by mode) rather than fixed frame delays—and never use it to destroy shared external project assets or bypass GPU fences.
