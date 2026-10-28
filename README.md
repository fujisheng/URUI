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

## Android CI

[Android APK workflow](.github/workflows/android-apk.yml) | [手机操作与许可证配置](docs/ANDROID_CI.md)

- **原生 RmlUi Android arm64-v8a：已通过远端编译。** [验证运行 #40625090580](https://github.com/fujisheng/RmlUnity/actions/runs/40625090580) 中包含 `rmlunity-android-arm64-native` artifact。
- **Unity APK：尚未构建成功。** 当前远端运行停止在 `Check Unity activation secrets`，因为仓库未设置 Unity 许可证与账号 Secrets。
- 将 `UNITY_EMAIL`、`UNITY_PASSWORD` 及有效的 `UNITY_SERIAL`（或受支持的 `UNITY_LICENSE`）设置为 GitHub Actions Secrets 后，再从 Actions 手动执行工作流。不要在 Issue、代码或聊天中分享密钥。

## Validation

See [docs/VALIDATION.md](docs/VALIDATION.md) for the required build, graphics, correctness and profiling checklist.

This repo deliberately pins an external dependency; it does **not** include or vendor RmlUi, native DLLs, fonts or benchmark results. The first milestone is end-to-end execution, followed by correctness and performance optimization.
