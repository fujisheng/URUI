# Quick Start

Matches Core `0.8.0-preview.1`. Complete the environment prerequisites in [Installation](INSTALL.md) (Unity `6000.3.22f1`, Camera, matching native plugin, and `Input Manager (Old / Both)`).

## Run Solstice First

- **Full URP Showcase (Recommended)**: Open [SampleProject/](../../../SampleProject/) directly in Unity `6000.3.22f1`, load `Assets/Solstice/Scenes/Solstice.unity`, and press Play to experience the 3D parallax sanctuary backdrop, Boss encounter with radial skill cooldowns, Effect shaders/particles, astrolabe summon sequences, 10,000-item virtualized Armory, and Authoring preview.
- **Standalone Core Package Sample**: In Package Manager open **URUI → Samples → Solstice → Import**, open the imported `Solstice.unity`, and enter Play Mode—or run **Tools → URUI → Open Solstice Sample**. Imported assets copy into `Assets/Samples/URUI/<version>/Solstice`; package `Samples~` stays read-only. See [Solstice README](../Samples~/Solstice/README.md).

## Create a Minimal Authored Document

Create `Panel.html`:

```html
<!doctype html>
<html><head>
<link rel="stylesheet" href="/urui/base.css">
<link rel="stylesheet" href="/urui/controls.css">
<link rel="stylesheet" href="panel.css">
</head><body>
<div id="panel">
  <label id="caption">Hello URUI</label>
  <button id="action" type="button">Click</button>
</div>
</body></html>
```

Create `panel.css`:

```css
body { margin: 24px; color: white; font-family: sans-serif; font-size: 24px; }
#panel { display: block; width: 360px; padding: 20px; background-color: #204080; }
#caption { display: block; }
#action { width: 220px; height: 48px; margin-top: 20px; }
```

Use standard supported HTML/CSS syntax with explicit closing tags on non-void elements. You can inspect `Panel.html` in any browser to check basic layout; pixel-accurate font comparison requires supplying the same font bytes.

## Minimal MonoBehaviour Host Component

Ensure your scene contains an enabled `Surface` and a `MainCamera`. Editor importers precompile HTML/CSS and dependency closures into a `CompiledDocumentAsset` via `CompiledDocumentCompiler.CompileAndSave` (see [Lifecycle & Resources](LIFECYCLE.md) for canonical URL mapping, or use **Tools → URUI → Create Minimal UI Scene**). Create a GameObject, attach `PanelController`, and assign `surface` and `compiled` in the Inspector:

```csharp
using System;
using URUI.Controls;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using UnityEngine;

public sealed class PanelController : MonoBehaviour
{
    [SerializeField] Surface surface;
    [SerializeField] CompiledDocumentAsset compiled;
    Document document;
    Button button;
    Action clicked;

    void OnEnable()
    {
        if (!surface || !compiled)
            throw new InvalidOperationException("Assign Surface and CompiledDocumentAsset.");
        var page = new GameObject("Panel Document");
        page.SetActive(false);
        page.transform.SetParent(transform, false);
        document = page.AddComponent<Document>();
        try
        {
            document.Surface = surface;
            document.Loaded += Bind;
            document.Unloaded += Unbind;
            page.SetActive(true);
            document.Load(compiled);
        }
        catch
        {
            ReleasePage();
            throw;
        }
    }

    void Bind(Document loaded)
    {
        Unbind(loaded);
        var caption = loaded.Q<Label>("caption");
        button = loaded.Q<Button>("action");
        clicked = () => caption.Text = "Clicked";
        button.Clicked += clicked;
    }

    void Unbind(Document owner)
    {
        if (button != null && button.IsValid) button.Clicked -= clicked;
        button = null;
        clicked = null;
    }

    void OnDisable() => ReleasePage();

    void ReleasePage()
    {
        var previous = document;
        document = null;
        if (!previous) return;
        Unbind(previous);
        previous.Loaded -= Bind;
        previous.Unloaded -= Unbind;
        try { previous.Unload(); }
        finally { Destroy(previous.gameObject); }
    }
}
```

This controller creates and owns `Document`, unloading and destroying it on disable. If using `Document.CompiledSource` auto-load in the Inspector, check `document.IsLoaded` immediately after subscribing to `Loaded` so component enable order never drops initialization.

## Dynamic Nodes and Input

Create nodes dynamically with `document.Create<Label>()` and mount them via `document.Body.Add(label)`. `RemoveFromHierarchy()` preserves identity and subscriptions so a node can be re-added later; `Destroy()` permanently destroys it. Pass element `id` strings without `#` to `Q<Button>("action")` (queries match IDs/tags/classes rather than arbitrary CSS selector chains).

For custom input backends, set `document.Surface.Input.UseUnityInput = false` and inject events via `Surface.Input`—see [Input & Events](EVENTS.md). Continue with [Lifecycle & Resources](LIFECYCLE.md), [Controls](CONTROLS.md), and [Document & Element API](API.md).
