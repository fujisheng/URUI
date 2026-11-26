# Hello URUI

Import this sample via **Package Manager → URUI → Samples → Hello URUI → Import**, attach `HelloURUI` to an empty GameObject, and enter Play Mode. When `Document.Source` is unset, the controller loads `Resources/URUI/Samples/Hello.html`; or assign an editable HTML `TextAsset` and `SourceUrl` on the same `Document`. The controller configures `Surface`, shared CSS, and bundled font bytes explicitly.

After `Loaded`, the sample queries `label`, `action`, and `panel`, updates text via `Element.Text`, subscribes clicks via `RegisterCallback<ClickEvent>`, and applies CSS inline via `Style`. It waits for `Document.isActiveAndEnabled` before loading so Unity component enable order never triggers issues.

`OnDisable` disposes subscriptions and unloads the document without destroying caller-provided `TextAsset`s. For dynamic element creation, detaching, re-attaching, and `Destroy()`, import `Element Playground`—or explore the full showcase in [Tideglass Beacon](../Tideglass%20Beacon/README.md).
