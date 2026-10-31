# RmlUnity

RML/RCSS game UI for Unity. RmlUi 6.3 handles DOM, style, layout and geometry in C++; C# exposes managed APIs and owns Unity resources and rendering submission.

The verified baseline is **Unity 6000.3.22f1, macOS ARM64, Built-in/Metal, IL2CPP Release**. This update includes a matching ABI-2 Mac plugin and RML/uGUI Match3 samples. Other platforms require rebuilding and separate validation. The inherited Android ABI-1 binary is disabled in its importer; legacy Android workflows are preserved and were not run for this update.

## Open and run

1. Open the repository root with Unity **6000.3.22f1**, Built-in and legacy Input Manager (or Both).
2. Open `Assets/RmlUnity/Demo/Match3RmlAtlas.unity` or `Match3UGUIAtlas.unity`, select a 720×1280 Game view, and Play.
3. Build a Mac ARM64 IL2CPP Player with **Tools → RmlUnity → Build Mac ARM64**. Existing output directories are preserved; move the previous output before rebuilding.
4. The minimal example is `RmlUnityDemo.unity`; **Tools → RmlUnity → Create MVP Demo Scene** recreates it.

The RML sample retains C# rendering by default and uses typed transforms with reusable node references. The portable package manifest specifies `com.unity.ugui` 2.0.0. Roboto and its license are included.

## Managed API

```csharp
host.Clicked += id => Debug.Log(id);
host.SetText("score", score.ToString());
host.SetStyle("panel", "display", "block");

// Acquire after Ready and hold across updates.
RmlElementReference icon = host.AcquireElementReference("icon");
if (icon != null && icon.IsValid)
    icon.SetTranslateScale2D(x, y, scaleX, scaleY);
// Dispose on owner shutdown; reacquire after node/document replacement.
icon?.Dispose();
```

References bind to their original native context/thread and node identity. A new element with the same ID does not inherit an old reference. Business APIs remain managed; internal pointer/NativeArray views use bounded synchronous leases.

## Renderer choices

Existing C# rendering remains the default. Ordered C# batching, C++ NativeBatch, NativeDelta, NativeCore and NativeFrame remain explicit options. **NativeFrame is opt-in**: verified Match3 main-thread CPU improved, while static total-frame P99 regressed. It is not a universal winner.

See [setup and lifetime contracts](docs/NATIVE_RENDERERS.md) for the required configuration. Setting a boolean does not make an unsupported shader, camera, SRP or platform eligible. NativeFrame fallback is allowed only after command/resource safety is established; its uncertain cleanup blocks recording.

The whole-world paint cache is excluded from this release. Its static benefit and dynamic limitations remain in the [mechanism ledger](docs/MECHANISM_LEDGER.md); local source and experiment products are preserved. Conditional mechanisms can be combined, but unverified combinations are not enabled here.

## Native build

The included Mac library is rebuilt from this native source. With CMake and a C++17 toolchain:

```sh
./Native/build-unix.sh -DCMAKE_OSX_ARCHITECTURES=arm64
```

CMake pins RmlUi `6.3` and FreeType `VER-2-13-3`. Offline builds can pass `-DFETCHCONTENT_FULLY_DISCONNECTED=ON`, `-DFETCHCONTENT_SOURCE_DIR_RMLUI=/your/RmlUi-6.3` and `-DFETCHCONTENT_SOURCE_DIR_FREETYPE=/your/freetype-VER-2-13-3`. Windows/Linux scripts are source support, not validated by these Mac results. Do not mix the new C# assembly with the inherited Android library.

Third-party terms and binary attribution are included in [StreamingAssets notices](Assets/StreamingAssets/RmlUnity/Notices/NOTICE.txt).

See [validation](docs/VALIDATION.md) and [architecture](docs/ARCHITECTURE.md). Current scope is one context on its creation thread, Built-in rendering, RmlUi-specific markup and supported shader operations. General HTML compatibility, URP/HDRP, IME/shaping and arbitrary filters/masks are not provided.
