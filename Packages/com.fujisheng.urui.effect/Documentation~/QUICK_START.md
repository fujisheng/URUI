# Effect Quick Start

Current package version: `0.5.0-preview.1` (requires Core `0.8.0-preview.1`). Complete [Core Installation](../../com.fujisheng.urui/Documentation~/INSTALL.md) for Unity, Camera, input, and matching native libraries first.

## Installation & Assemblies

Install via **Package Manager → Add package from disk** pointing at `Packages/com.fujisheng.urui.effect/package.json` (or copy as an embedded package). Dependencies are `com.fujisheng.urui` and `com.unity.modules.particlesystem`. Custom `.asmdef` targets should reference `URUI.Runtime` and `URUI.Effect` (zero extra native binaries or framework dependencies).

## Prepare Authored Markup & CSS

Create `EffectPanel.html`:

```html
<!doctype html>
<html><head>
<link rel="stylesheet" href="/urui/base.css">
<link rel="stylesheet" href="/urui/controls.css">
<link rel="stylesheet" href="/urui-effect/Effect.css">
<link rel="stylesheet" href="panel.css">
</head><body>
<img id="reward" src="icon.png" class="reward">
</body></html>
```

Create `panel.css`:

```css
body { margin: 24px; }
.reward {
  width: 160px;
  height: 160px;
  --urui-effect: ui;
  --urui-fx-tone-filter: grayscale;
  --urui-fx-tone-intensity: .5;
  --urui-fx-transition-filter: fade;
  --urui-fx-transition-progress: 0;
}
```

Register `Runtime/Resources/URUI.Effect/Effect.css` under `/urui-effect/Effect.css` alongside your page CSS in `Document.TextResources`. Standard HTML/CSS layout stays inspectable in desktop browsers while `--urui-*` shaders execute inside Unity URUI.

## Minimal MonoBehaviour Host

With an active `Surface` and `MainCamera` in your scene, configure `EffectPanelHost` with your assets:

```csharp
using System;
using URUI.Documents;
using URUI.Effect;
using URUI.Hosting;
using URUI.Resources;
using UnityEngine;

public sealed class EffectPanelHost : MonoBehaviour
{
    [SerializeField] Surface surface;
    [SerializeField] TextAsset html;
    [SerializeField] TextAsset panelCss;
    [SerializeField] TextAsset effectCss;
    [SerializeField] Texture2D icon;
    Document document;
    EffectModule module;

    void OnEnable()
    {
        if (!surface || !html || !panelCss || !effectCss || !icon)
            throw new InvalidOperationException("Assign Surface and page resources first.");
        var page = new GameObject("Effect Panel");
        page.SetActive(false);
        page.transform.SetParent(transform, false);
        document = page.AddComponent<Document>();
        try
        {
            document.Surface = surface;
            document.TextResources = new[] {
                new TextResource("/demo/panel.css", panelCss),
                new TextResource("/urui-effect/Effect.css", effectCss)
            };
            document.Images = new[] { new ImageAsset("icon.png", icon) };
            module = new EffectModule(document);
            page.SetActive(true);
            document.Load(html, "/demo/panel.html");
        }
        catch
        {
            ReleasePage();
            throw;
        }
    }

    void OnDisable() => ReleasePage();

    void ReleasePage()
    {
        var previous = document;
        document = null;
        try { if (previous) previous.Unload(); }
        finally
        {
            module?.Dispose();
            module = null;
            if (previous) Destroy(previous.gameObject);
        }
    }
}
```

Required setup sequence: assign `Document` resources → construct `new EffectModule(document)` → activate GameObject and call `Load(...)` → query nodes and controllers. Never attach two `EffectModule` instances to the same `Document`.

## Driving Effects from C#

Fetch a node controller via `module.GetController(document.Q("reward"))`. With `fade` enabled in CSS, trigger playback via `controller.Play(new EffectTweenDefinition { Channels = EffectTweenChannels.Transition, Duration = .35, WrapMode = EffectWrapMode.Once })`. Active CSS animations, `!important` rules, or transitions take CSS cascade priority over playback channels—see [C# API](API.md). For auxiliary LUT/dissolve textures and particle prefabs, supply `IEffectResourceResolver` per [Rendering & Resources](RENDERING.md).

## Troubleshooting

| Symptom | What to Check |
| --- | --- |
| `unknown effect` or unknown `urui-particles` tag | Construct `EffectModule(document)` **before** `document.Load(...)` |
| Unknown `--urui-fx-*` properties | Map matching `Effect.css` to `/urui-effect/Effect.css` and link it in HTML |
| Preset `Apply` produces no visual change | Element declares `--urui-effect: ui` and author CSS isn't overriding preset properties |
| Tween never completes `Finished` | Default `WrapMode` is `Loop`; use `Once` or `PingPongOnce` when awaiting completion |
| Controller throws invalid after reload | Re-query elements and re-fetch controllers from `document.Loaded` |
| Missing particle or transition textures | Keys returned from HTML/CSS URLs match `IEffectResourceResolver` cleanly |
