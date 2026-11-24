# Getting Started with URUI

**English** | [中文](GETTING_STARTED_ZH.md)

Target Unity version: **`6000.3.22f1`**.

## 1. Try SOLSTICE Immediately (`SampleProject/`)

The quickest way to inspect URUI across all modules is the standalone URP showcase project included in this repository:

1. Launch **Unity Hub** and open `/SampleProject` using Unity **`6000.3.22f1`**.
2. Open scene `Assets/Solstice/Scenes/Solstice.unity`.
3. Press **Play** to explore Sanctuary Lobby, Boss Combat with Radial Cooldowns, Heroes & Effect Shaders, Summon Astrolabe, 10,000-row Armory, and live Render Stats HUD.

## 2. Install URUI Packages into Your Own Project

Add local packages via **Window → Package Manager → + → Add package from disk...**:

1. Required Core runtime: `Packages/com.fujisheng.urui/package.json` (`0.8.0-preview.1`)
2. Optional visual FX / particles package: `Packages/com.fujisheng.urui.effect/package.json` (`0.5.0-preview.1`)
3. Optional Editor Workbench & Agent HTTP v1: `Packages/com.fujisheng.urui.authoring/package.json` (`0.1.0-preview.1`)

Then import the primary package sample:
- Click **Tools → URUI → Open Solstice Sample** (automatically imports `Samples~/Solstice` and opens `Solstice.unity`), or import **Hello URUI** / **Element Playground** / **Runtime Controls** via Package Manager.

## 3. Minimal Hello World Scene in 30 Seconds

1. Ensure an enabled **Main Camera** exists in your scene (`Camera.main != null`).
2. Under **URP**, register a `ScriptableRendererFeature` calling `surface.SubmitRendering(cmd, target, viewport, info)` (see [URP Render Pipeline Guide](RENDER_PIPELINE_URP.md)). Under **Built-in RP**, `RenderHost` attaches automatically.
3. Attach `Surface` and `Document` (or a small controller `MonoBehaviour`) and load clean HTML + CSS:

```csharp
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Controls;
using URUI.Events;

public sealed class QuickGreeting : MonoBehaviour
{
    void Start()
    {
        var surface = gameObject.AddComponent<Surface>();
        var doc = gameObject.AddComponent<Document>();
        doc.Surface = surface;
        doc.LoadHtml(@"<!doctype html>
<html>
<head>
  <link rel='stylesheet' href='/urui/base.css'>
  <style>
    body { margin: 32px; background: #f6f1e7; font-family: sans-serif; }
    #btn { width: 180px; height: 40px; background: #ef5a36; color: white; border-radius: 999px; }
  </style>
</head>
<body>
  <h2 id='title'>Hello URUI!</h2>
  <button id='btn' type='button'>Tap Me</button>
</body>
</html>", "/ui/greeting.html");

        doc.Q<Button>("btn")?.RegisterCallback<ClickEvent>(_ =>
        {
            doc.Q("title").Text = "Clicked cleanly!";
        });
    }
}
```

## 4. Next Steps

- Understand how `Surface`, `Document`, and `ABI13` work together: [Architecture Overview](ARCHITECTURE.md).
- Configure URP RenderGraph integration: [Render Pipeline & URP](RENDER_PIPELINE_URP.md).
- Check supported CSS properties & HTML elements: [HTML/CSS Cheatsheet](HTML_CSS_CHEATSHEET.md).
