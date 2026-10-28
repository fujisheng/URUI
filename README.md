# RmlUnity — Native Web-style UI for Unity (MVP)

> **Status: source prototype. NOT yet compiled against RmlUi 6.3 or tested in Unity.** No performance advantage is claimed.

The goal is to let AI-authored RML/RCSS interfaces render using Unity's native graphics commands without creating a uGUI GameObject for every element and without embedding Chromium/WebView.

## Architecture

```
RML/RCSS (TextAsset)
  -> RmlUi 6.3 C++ (parse, CSS, layout, events, FreeType)
  -> RenderInterface (geometry handles, font textures, ordered draw commands)
  -> C ABI / PInvoke
  -> Unity Mesh + CommandBuffer (Built-in Render Pipeline)
```

## Scope

- Implemented in source: C++ bridge, generated texture copying, geometry caching, C# P/Invoke, Mouse hover/click/wheel, Unity event callback, dynamic text, demo UI and editor menu.
- **Not implemented:** true GPU scissor/overflow clipping, advanced transforms, external image textures, complex filters/blur, standalone UXML/HTML compiler, Tailwind/React import, Unicode shaping/IME, virtual lists, benchmark suite, URP/HDRP support.
- **Limitations:** single context, single camera, main-thread native calls, no platform binaries included, demo CSS is RmlUi-specific RCSS.

## Build and run (Windows x64)

1. Install Unity **2022.3 LTS**, CMake, Visual Studio with C++ Desktop toolchain, and FreeType development headers/libs.
2. Open PowerShell, enter `Native/` and run `./build-windows.ps1`. CMake fetches RmlUi tag `6.3`. If dependencies are not found, set `CMAKE_PREFIX_PATH` or `CMAKE_TOOLCHAIN_FILE`.
3. Ensure `Assets/Plugins/x86_64/RmlUnityBridge.dll` exists (and that any dynamically linked FreeType dependency is also accessible).
4. Open the repo root using Unity Hub. Use Built-in Render Pipeline and legacy Input Manager (or Both).
5. Select **Tools → RmlUnity → Create MVP Demo Scene**, then Play.
6. Click `CLAIM +50 GOLD` and check the counter. For hover/click accuracy, first test a 1280×800 Game view.

## C# API example

```csharp
var host = FindObjectOfType<RmlUnity.RmlUnityHost>();
host.Clicked += id => Debug.Log("Clicked: " + id);
host.SetText("status", "Updated by game C# code");
```

## Validation

See [docs/VALIDATION.md](docs/VALIDATION.md) for the required build, graphics, correctness and profiling checklist.

This repo deliberately pins an external dependency; it does **not** include or vendor RmlUi, native DLLs, fonts or benchmark results. The first milestone is end-to-end execution, followed by correctness and performance optimization.
