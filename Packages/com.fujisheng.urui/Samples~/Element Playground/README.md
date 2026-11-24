# Element Playground

After importing this sample via Package Manager, create a scene with **Tools → URUI → Create Minimal UI Scene**, assign `Playground.html` to `Document.Source`, set `SourceUrl` to `samples/element-playground/Playground.html`, attach `ElementPlayground`, and enter Play Mode. The menu helper creates `Surface`, `/urui/base.css`, and font registrations (multiple documents can share the same `Surface`).

Desktop browsers can preview `Playground.html` directly when serving `Packages/com.fujisheng.urui/Runtime/Resources/URUI` under `/urui`. Interactive buttons trigger real DOM operations from C#: `Create`, select, `RemoveFromHierarchy`, `Insert` at index, `Destroy`, and `Clear`. Visual styles update via CSS class toggles and explicit status label text without hidden data bindings.

`Clear()` and `RemoveFromHierarchy()` retain elements and subscriptions until re-attached, explicitly destroyed with `Destroy()`, or cleaned on `Document.Unload()`. Dispose of unused subtrees cleanly via `Destroy()`.
