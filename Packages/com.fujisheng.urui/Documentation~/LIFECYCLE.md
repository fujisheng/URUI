# Lifecycle & Resources

## Shared `Surface` & Isolated `Document`s

An active `Surface` owns the shared native context, viewport, UI clock, input dispatch, and rendering submission. Multiple `Document` instances bind explicitly to a `Surface`, each keeping isolated DOM state, style rules, event subscriptions, and resource namespaces. All URUI calls run on the Unity main thread; switch back to the main thread before feeding async loaded bytes/textures.

Authored pages use real HTML/CSS. Production UI prefers `Load(CompiledDocumentAsset)` compiled in Editor, while dynamic runtime inputs can use `Load(TextAsset asset, string sourceUrl)` or `LoadHtml(string html, string sourceUrl)`. Loads prepare and validate candidate state before publishing; failure preserves the live DOM and assets untouched. `SourceUrl` sets the resource resolution base URI rather than switching parser modes.

Candidate updates, listeners, and resource retirement queues are staged before commit. Exceptions thrown by user `Loaded` listeners are isolated and logged without marking a committed page failed. Pending initial focus retries cleanly if blocked transiently by modal state. Focus chains commit before listeners inspect state so refocusing inside callbacks is never overwritten by stale outer state.

Enabling a `Document` restores its serialized `CompiledSource` / `Source` or last requested document; disabling preserves restoration intent, whereas explicit `Unload()` clears it. Closing Document A invalidates only A's elements, events, and animations without touching Document B. Disabling `Surface` pauses all pages and re-enables them cleanly on restore. `Surface.UIClock` can only be reassigned when zero documents are loaded.

`Load`/`Unload` mutations inside `Loaded`/`Unloaded`, pointer, image, or animation callbacks queue to run safely after the native call returns; use `RunAfterEvent` for immediate post-dispatch continuation. Effect `Prepare`/`Record` callbacks consume immutable frame snapshots and must never mutate DOM or schedule page loads.

## Element Identity

`DocumentElement` represents `<html>` and `Body` represents `<body>`. Wrappers carry owning document generation + element token: duplicate IDs across separate documents never collide, cross-document `Add`/`Insert` or cycles or moving/destroying roots are rejected, and each context tracks up to **65,536** live nodes.

`Create<T>()` / `CreateElement(tag)` create unmounted nodes in the target document. `Remove`/`Clear` preserve subtrees, identities, and subscriptions for remounting; call `Destroy()` when permanently discarded. `Text`/`SetInnerHtml` structural replacements, `Destroy()`, `Unload()`, and reloads permanently invalidate affected handles—recreating an element with the same `id` produces a distinct identity. HTML `<template>` contents remain inert and clone cleanly.

`Element` does not implement `IDisposable`; instead dispose the `IDisposable` leases returned by event/style subscriptions, and document unload cleans all owned attached and detached elements.

## Asset Ownership Matrix

| Asset Type | Ownership & Release Rules |
| --- | --- |
| `TextAsset` in `Source` / `TextResources` | Owned by application; URUI never destroys `TextAsset` assets and copies/retains required native snapshots per document generation |
| `Texture2D` in `Images` | Directly borrowed GPU texture object; Read/Write not required; never destroyed by URUI |
| `ImageLease` from `ImageResolver` | Leased to URUI; released back via lease callback only after both native and all in-flight GPU submissions finish |
| Font bytes, glyphs & internal atlas textures | Deduplicated by source identity; document retains rule bindings while Surface/GPU fences govern atlas/retirement bounds |
| Elements & event subscriptions | Owned by `Document`/`Element`; cleaned on subtree `Destroy()` or `Unload()` |
| Meshes, materials, command buffers & scratch render targets | Managed by `Surface` host; retired assets stay alive until GPU completion fence signals |

## Fonts, CSS & Images

Configure `TextResources`, `Images`, and `ImageResolver` while the target `Document` is unloaded (Document B can be configured safely while Document A stays active). Relative paths resolve canonically against `SourceUrl`; register CSS, font bytes, and HTML directly:

```csharp
document.Unload();
document.TextResources = new[] {
    new TextResource("/ui/panel.css", cssAsset),
    new TextResource("/ui/fonts/panel.ttf", fontBytesAsset)
};
document.Images = new[] { new ImageAsset("icons/coin.png", texture) };
document.Load(panelHtmlAsset, "/ui/panel.html");
```

Link `<link rel="stylesheet" href="panel.css">` in HTML and reference custom fonts via standard `@font-face` + `url("fonts/panel.ttf")`. Supply `TextResource` binary fonts as `.bytes` `TextAsset` assets (using `TextAsset.GetData<byte>()` native spans without duplicating large managed arrays); never pass Unity `Font` objects or convert TTF/OTF bytes to text strings. Font family/weight/style resolve per document at 26.6 subpixel precision—ensure bundled font assets cover every Chinese/Latin glyph rendered in your UI.

Image URIs must match HTML/CSS references cleanly. Multiple URI aliases pointing at the same `Texture2D` share native metadata. While your application may update texture pixels and call `Apply()`, never `Destroy`, `UnloadAsset`, or resize a borrowed `Texture2D` while URUI or an in-flight GPU frame holds a lease. URUI performs no network downloads or disk image decoding internally.

Borrowed color textures use bilinear clamping at resident `mip0` without altering original importer sampler flags (texture streaming and active mipmap limits must be disabled on borrowed UI textures). Alpha compositing respects Unity's project color space (Linear vs Gamma), delegating sRGB texture decode to Unity while honoring render target color formats.

## Synchronizing Geometry & Draw Prep

`Document.SynchronizeGeometry()` updates styles, Yoga layout, and world transforms without synthesizing ordinary element draw batches—ideal during screen opening/adapters, ensuring `Layout` and `WorldBound` are readable upon return (elements carrying custom painters or external geometry providers still prepare). Subsequent frames, cameras, or controlled SRP submission prepare remaining commands right before rendering, or call `SynchronizeLayout()` explicitly outside callbacks when immediate full command generation is needed.

Using `BeginUpdate()` + `RequestGeometry(scope.Root)` coalesces layout and narrows immediate painter preparation to the targeted subtree during batched UI construction while keeping full document layout accurate. Reading geometry inside a batch flushes pending work cleanly. Never invoke `SynchronizeGeometry()` / `SynchronizeLayout()` from inside element `OnUpdate` or active layout/paint callbacks.

Note that element status refresh optimizations require element protocol v4 (`element4`): unchanged trees reuse structural attachments and relative layout proofs (adding only 16 bytes per document proof without allocating extra snapshots or GPU buffers), immediately invalidating proofs upon context/token changes while continuing to invoke control `OnUpdate` cleanly.

## Synchronous On-Demand Images & Submission

```csharp
document.ImageResolver = source => {
    var texture = ResolveApplicationTexture(source);
    return texture ? new ImageLease(texture) : null;
};
```

Static `Document.Images` entries take priority over `ImageResolver`. Callbacks run synchronously on the Unity main thread and return an independent `ImageLease` (do not use `null` for async pending retries, because `null` is cached as a failed lookup for the current document generation). When supplying `new ImageLease(texture, release)`, let URUI invoke `release` after native + GPU fences complete rather than disposing textures manually early.

Closing a document retires its namespace immediately while keeping shared fonts/textures alive for sibling active pages and up to **8** in-flight GPU frames. When the last native lease clears and GPU fences complete, external owners can safely unload idle textures.

For SRP (such as URP), submit once per active `Surface` via `Surface.SubmitRendering(context, target, viewport)` (or via `CommandBuffer` overload with `RenderTargetInfo` inside RenderGraph unsafe passes as demonstrated in `SampleProject`'s `UruiUrpRendererFeature`). When full-screen backdrop color compositing is required, the native render plan allocates bounded work targets with GPU-fenced pooling (`LDR` = 4 B/px, `HDR` = 8 B/px) automatically—never manual toggle flags. Unmodified layers reuse prior targets safely under `R16` / aligned mask optimizations until fence retirement.

## Resource API Quick Reference

| API | Usage & Sequence |
| --- | --- |
| `TextResource(string source, TextAsset asset)` | Canonical URI paired with `TextAsset` bytes (CSS / font bytes / fragments); register before loading |
| `ImageAsset(string source, Texture2D texture)` | Borrowed texture (unique URI per document) |
| `ImageAsset(string source, Texture2D texture, SpriteRegion[] sprites)` | Named atlas regions (`SpriteRegion` array cloned, pixels not copied) |
| `SpriteRegion(string name, Rect region, float displayScale = 1)` | Top-left pixel rectangle selected via `Image.SpriteName` |
| `ImageLease(Texture texture, Action release = null)` | Independent texture lease returned to URUI |
| `ImageLeaseSource(Texture texture, Action release)` | Ref-counted source issuing multiple independent `ImageLease` instances |
| `ImageLeaseSource.Acquire()` | Acquires one consumer lease |
| `ImageLeaseSource.Dispose()` | Releases source's own hold while active leases stay valid until their consumers finish |
| `Document.TryLoadImage(string source, out Vector2Int dimensions)` | Synchronously resolves/preloads an image on a loaded document and reads pixel dimensions |
| `Document.ReleaseImage(string source)` | Retires the cached image binding for that URI in this document |
| `Document.AddStyleSheet(TextAsset asset, string sourceUrl, bool beforeAuthor = false)` | Appends or prepends (`beforeAuthor = true`) an extra CSS asset on an active document |

## Named Sprite Region Example

```csharp
using URUI.Documents;
using URUI.Resources;
using UnityEngine;

public static class AtlasExamples
{
    public static void Configure(Document document, Texture2D atlas)
    {
        document.Unload();
        document.Images = new[] {
            new ImageAsset("atlas.png", atlas, new[] {
                new SpriteRegion("coin", new Rect(0, 0, 32, 32)),
                new SpriteRegion("gem", new Rect(32, 0, 32, 32))
            })
        };
    }
}
```

Then set `<img src="atlas.png">` and assign `image.SpriteName = "coin"` or `"gem"`. Regions must fit within texture bounds while the caller preserves `atlas` lifetime. See [Public API Reference](REFERENCE.md) and [Rendering & Media](RENDERING.md) for all resource and video APIs.
