# Document & Element API

Matches URUI `0.8.0-preview.1`. This guide covers document lifecycle, element creation, queries, and DOM manipulation patterns; see [Public API Reference](REFERENCE.md) for full type declarations and [Installation](INSTALL.md) / [Quick Start](QUICK_START.md) first.

## Namespaces & Entry Points

| Namespace | Common APIs | Purpose |
| --- | --- | --- |
| `URUI.Hosting` | `Surface`, `SurfaceInput`, `SurfaceRenderingStatistics` | Shared viewport, UI clock, input, submission, stats |
| `URUI.Documents` | `Document` | Document loading, node factory, layer presentation |
| `URUI.Elements` | `Element`, `ElementStyle`, `Length` | Hierarchy, queries, styles, coordinates |
| `URUI.Controls` | `Button`, `TextField`, `ListView`, etc. | [Controls](CONTROLS.md) |
| `URUI.Events` | `EventBase`, `ChangeEvent<T>`, etc. | [Input & Events](EVENTS.md) |
| `URUI.Resources` | `TextResource`, `ImageAsset`, `ImageLease` | [Lifecycle & Resources](LIFECYCLE.md) |
| `URUI.Styles`, `URUI.Animation` | `DocumentStyles`, `AnimationPlayback`, `SequencePlayer` | [Styles & Animation](STYLES_ANIMATION.md) |
| `URUI.Rendering`, `URUI.Rendering.Effects` | Paint callbacks, custom effects, frames | [Rendering & Media](RENDERING.md) |
| `URUI.Media` | `VideoPlayback` | [Video Output](RENDERING.md#video-output) |

Internal interop and rendering plumbing are not public business APIs. Typed elements are created by `Document` factories—never instantiate via `new Button()`.

## Surface & Document Setup

Both are MonoBehaviours. Place one active `Surface` in the scene and explicitly assign `document.Surface` for every page (on the same or separate GameObjects; multiple `Document` instances share one `Surface`).

| Member | Usage & Constraints |
| --- | --- |
| `Surface.Viewport : Vector2Int` | Logical CSS pixel dimensions; `Vector2Int.zero` tracks screen size, otherwise both components must be positive |
| `Surface.UIClock : IUIClock` | Defaults to unscaled clock; cannot be changed while any document is loaded |
| `Surface.Input : SurfaceInput` | Single raw input entry point dispatched across active documents on the `Surface` |
| `Document.Surface` | Shared host; configure before loading |
| `Document.CompiledSource : CompiledDocumentAsset` | Recommended serialized build source; instantiates precompiled plans on enable |
| `Document.Source : TextAsset` | Runtime raw HTML `TextAsset` source (prefer `CompiledSource` for shipping builds) |
| `Document.SourceUrl : string` | Canonical resource base URI, e.g. `/ui/panel.html` |
| `Document.TextResources`, `Images`, `ImageResolver` | Resource bindings configured while `Document` is unloaded |
| `Document.IsLoaded` | DOM is loaded and queryable (does not guarantee same-frame layout/draw has completed) |

When spawning dynamically from script, keep the page GameObject inactive while assigning `Surface` and resources, then activate and call `Load(...)` so behavior never depends on Unity `OnEnable` execution order.

## Loading, Reloading & Notifications

| Member | Effect |
| --- | --- |
| `Load(CompiledDocumentAsset asset)` | Instantiates Editor-compiled HTML/CSS, templates, and query snapshots |
| `Load(TextAsset asset, string sourceUrl)` | Synchronously parses and loads full HTML from a `TextAsset` |
| `LoadHtml(string html, string sourceUrl)` | Synchronously parses and loads an in-memory HTML string |
| `Unload()` | Unloads the active page and clears automatic re-load recovery state |
| `Loaded : Action<Document>` | Fired after a successful load; query elements and wire subscriptions here |
| `Unloaded : Action<Document>` | Fired once unloaded; previous element wrappers and subscriptions are invalid |
| `LayoutUpdated : Action<Document>` | Fired after native layout refresh; layout rects and computed styles are readable |
| `SynchronizeLayout()` | Explicit immediate synchronization when measurements are required right now; avoid calling every `Update` |
| `RunAfterEvent(Action)` | Runs immediately after the current event/native callback returns, or immediately when idle |
| `BeginUpdate()` | Scopes batched geometry updates; sync flushes when the outermost scope disposes |
| `RequestGeometry(Element root)` | Queues subtree preparation inside an update batch (or prepares immediately if unbatched) |
| `FlushPendingGeometry()` | Commits pending layout before geometry/bounding reads in adapters |

Loads validate and prepare candidate state first; failure keeps the existing DOM and resources intact. Reloading invalidates previous `Element` wrappers permanently (matching `id` attributes do not resurrect old handles). Lifecycle calls inside callbacks are queued cleanly until the native call exits. Always handle both `Document.Loaded` and already-loaded states when attaching controllers.

In Editor pipelines, `URUI.Editor.Compilation.CompiledDocumentCompiler.CompileAndSave(html, sourceUrl, destination, dependencies)` emits GUID-stable `CompiledDocumentAsset` assets by resolving the dependency closure of HTML/CSS/template imports; stale sources or broken assets fail Editor import/build validation.

Use `GetCompiledTemplate(uri)` for document-scoped precompiled templates and inject via `element.SetInnerHtml(template)` (templates cannot cross parent document compiled plans). `CreateCompiledQuerySnapshot(root)` indexes a subtree once so `TryQuery(".class", out result)` / `TryQuery("#id", out result)` run fast; any structural or matching-name mutation invalidates it.

In-page `<template id="RowTemplate">` nodes remain inert until instantiated: query via `document.Q<URUI.Controls.Template>("RowTemplate")` and call `CloneContentTo(destination)` into an empty container in the same document. Native interop uses **ABI13**.

Reading `HasLayout`, `Layout`, bounds, or computed styles inside an update batch flushes pending geometry, except inside active layout/paint callbacks which read the current callback snapshot to prevent re-entrant layout recursion. Explicit `SynchronizeGeometry()` / `SynchronizeLayout()` run immediately when needed.

`SetPresentation(int layer, int order, bool visible, bool modal)` configures top-level document sorting layer, order within layer, visibility, and modal blocking. `Focus()` requests page focus (returns `false` if hidden or blocked by a modal layer). `FocusedElement` returns `null` when no node holds focus; `Pick(Vector2)` hit-tests at document coordinates.

## Queries & Element Factories

| API | Semantics |
| --- | --- |
| `DocumentElement` / `Body` | Root `<html>` / `<body>` wrappers (may be `null` before load) |
| `Q<T>(string name = null, string className = null)` | First descendant (or root) matching type, `id`, and class name; returns `null` if absent |
| `Query<T>(...)` | Query builder returning an ordered snapshot when evaluated |
| Non-generic `Q(...)` / `Query(...)` | First matching `Element` / ordered read-only element list |
| `Create<T>() where T : Element` | Creates an unmounted element belonging to the document using registered factories |
| `CreateElement(string tag = "div")` | Creates a standard or registered tag element |
| `RegisterElementType<T>(string tag, Func<ElementContext, T> factory)` | Registers a custom tag factory before loading HTML that uses `<tag>` |

`name` matches the HTML `id` attribute without `#`, and `className` matches a single class token without `.` (not arbitrary CSS selector expressions). `id` strings can repeat across documents or subtrees; unique identity comes from document generation + element token, and queries never cross document boundaries.

```csharp
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

public static class NodeExamples
{
    public static Label AddCaption(Document document, string title)
    {
        var label = document.Create<Label>();
        label.Name = "caption";
        label.Text = title;
        document.Body.Add(label);
        return label;
    }

    public static void ResetSettings(Element root)
    {
        root.Query<Toggle>(className: "setting")
            .Where(field => field.EnabledInHierarchy)
            .ForEach(field => field.SetValueWithoutNotify(false));
    }
}
```

`ElementQuery<T>` also provides `Children()`, `First()`, `Last()`, `AtIndex(int)`, and `ToList()`. `Children()` scopes to direct children; evaluation takes a fresh ordered snapshot and does not guarantee zero allocation.

## Hierarchy, Cloning & Content

| Member | When to Use |
| --- | --- |
| `Parent` | Actual DOM parent element |
| `ContentContainer` | Exposed business slot inside composite controls (`this` on plain elements) |
| `ChildCount`, `this[int]`, `Children()` | Direct children inside `ContentContainer` |
| `Add(Element)`, `Insert(int, Element)` | Mount same-document nodes into `ContentContainer` |
| `Remove(Element)`, `RemoveFromHierarchy()`, `Clear()` | Detach while keeping identity, subtree, and event subscriptions intact |
| `Destroy()` | Permanently destroy the node and its subtree |
| `Hierarchy` | Explicit access to the raw DOM including internal composite-control structure |
| `Clone()` | Deep structural clone yielding fresh identities in the same document; re-subscribe handlers |
| `Contains(Element)`, `GetFirstAncestorOfType<T>()` | Containment checks and ancestor traversal |
| `BringToFront()`, `SendToBack()` | Sibling z-order changes |

Never reparent across documents, form cycles, move, or destroy document roots, and do not bypass composite controls to mutate their internal children. `Element` does not implement `IDisposable`; `Document` owns attached and detached nodes until `Destroy()` or `Unload()`.

`Text` writes plain text (overwriting children on normal nodes and throwing if reading complex structured markup). `SetInnerHtml(string html, string sourceUrl)` parses strictly and replaces fragments transactionally (keeping the previous subtree on error); `GetInnerHtml()` serializes current markup. In event callbacks requiring synchronous fragment instantiation, wrap with `RunAfterEvent`.

`GetAttribute` returns `null` when absent; mutate attributes with `SetAttribute`/`RemoveAttribute`. Toggle class lists via `AddToClassList`, `RemoveFromClassList`, `ClassListContains`, and `EnableInClassList`. `UserData` stores arbitrary user objects (you remain responsible for releasing external resources stored inside).

## State, Focus & Coordinates

| Member | Semantics |
| --- | --- |
| `IsValid` / `IsAttached` | Valid within active document generation / connected under document root (`Remove` detaches without invalidating) |
| `SetEnabled(bool)`, `EnabledSelf`, `EnabledInHierarchy` | Interaction state; disabling clears focus and active pointer captures inside the subtree |
| `Visible` | Hides rendering while preserving layout space |
| `Style.Display = DisplayStyle.None` | Removes the node from layout calculation entirely |
| `PickingMode` | Hit-testing behavior (`Position` / `Ignore`) |
| `Focusable`, `TabIndex`, `Focus()`, `Blur()`, `Focused` | Focus state and keyboard tab traversal (positive `TabIndex` before `0`, then DOM order) |
| `HasLayout` | True once at least one layout pass has completed; check before reading rects |
| `Layout` | Border box relative to layout parent |
| `ContentRect` / `ViewportRect` | Local content rect / inner viewport excluding scrollbars & padding |
| `WorldBound` | Axis-aligned bounding box in document coordinates including cumulative transforms |
| `LocalToWorld`, `WorldToLocal`, `TryWorldToLocal` | Coordinate transforms (`TryWorldToLocal` handles singular matrices cleanly) |
| `SetTransform(...)` | 2D/3D transforms including explicit `originPercent` overloads |
| `ScrollOffset` / `ScrollSize` | Active scroll offset and total scrollable content dimensions |

Property getters read the most recently committed layout snapshot without forcing full-page relayout. Listen to `GeometryChangedEvent` for size/position changes, and avoid feedback loops that mutate layout unconditionally inside geometry callbacks. See [Styles & Animation](STYLES_ANIMATION.md) for styling APIs.

## Custom Elements

Accept `ElementContext` in the constructor, build internal structure in `OnInitialize()`, run post-layout logic in `OnUpdate()`, and clean managed subscriptions only in `OnInvalidated()` (never access native elements during invalidation). Composite controls override `ContentContainer` and wire internal children via `Hierarchy`.

```csharp
using URUI.Documents;
using URUI.Elements;

public sealed class CounterElement : Element
{
    int count;
    public CounterElement(ElementContext context) : base(context) { }
    public void Increment()
    {
        count++;
        Text = count.ToString();
    }
    public static void Register(Document document)
    {
        document.RegisterElementType("urui-counter", context => new CounterElement(context));
    }
}
```

Call `Register` before loading HTML containing `<urui-counter></urui-counter>`, then query via `Q<CounterElement>()` or create via `Create<CounterElement>()`. Factories never rely on reflection instantiation.
