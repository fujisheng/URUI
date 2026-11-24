# Installation

Current package version: `com.fujisheng.urui` **`0.8.0-preview.1`**, runtime assembly `URUI.Runtime`. Game code using custom `.asmdef` files should reference `URUI.Runtime` explicitly; `Assembly-CSharp` picks it up automatically. See [API](API.md) for namespaces.

## Install from Disk

Open **Package Manager → Add package from disk** and select `package.json` from the complete package (`Packages/com.fujisheng.urui/package.json`). You can also copy the complete package folder directly into your Unity project's `Packages/` directory as an embedded package.

For local development in `Packages/manifest.json`, use `"com.fujisheng.urui": "file:<path relative to project Packages>"`. Choose only one installation method so the same assembly is never duplicated across `Assets`, embedded `Packages`, and Package Manager.

## Install from Git

When the remote repository contains a published commit with matching native binaries and package assets, add via **Package Manager → Add package from git URL**:

```text
https://github.com/fujisheng/URUI.git?path=Packages/com.fujisheng.urui#<verified-commit-sha>
```

Replace the placeholder with an actual published commit SHA and ensure Git access credentials are configured if private. Local unpublished workspace edits should use disk installation.

## Unity Environment Requirements

| Setting | Requirement |
| --- | --- |
| Unity | `6000.3 / 22f1` declared; primary verified baseline is `6000.3.22f1` |
| Graphics | Primarily verified on macOS ARM64 / Metal; see [Limitations & Support Matrix](LIMITATIONS.md) for other targets |
| Camera | Enabled `MainCamera`; URP submits via the pipeline RendererFeature adapter |
| Input | `Input Manager (Old / Both)`, or set `Surface.Input.UseUnityInput = false` and inject events via `Surface.Input` |
| Host | One active `Surface`, with each `Document` explicitly bound |
| Fonts | Readable raw font byte assets containing all glyphs rendered by your pages |
| Images | `Texture2D` does not require Read/Write; requires resident full mip0 (disable texture streaming / active mipmap limits) |

URP integration is runnable in `SampleProject` (with 3D stage compositing, RenderGraph, and Effect shaders); HDRP, unverified graphics backends, and non-macOS devices are not automatically inferred—see [Limitations](LIMITATIONS.md). HTML does not auto-hot-reload on edit; reload the document or re-enter Play Mode after edits.

## Native Plugins & Protocol Version

The complete package bundles `Runtime/Plugins` and C++ headers under `Native~`. Current protocol versions are **ABI13 / render-plan9 / element4 / style4 / effect7 / filter3 / animation1** (172 production C exports, `URUI_DEBUG` disabled by default). Build hashes and per-platform symbol verification live in [PLUGINS.json](PLUGINS.json).

Before replacing a loaded native dynamic library in Editor or Player, close the process first, replace the complete matching package/plugin set, and relaunch. Never mix C# scripts from one package release with native libraries from another. macOS ARM64 and x86_64 are shipped as separate single-architecture plugins.

## Samples & Optional Packages

- **Standalone URP Showcase Project**: Open [SampleProject/](../../../SampleProject/) directly in Unity `6000.3.22f1` to explore 3D sanctuary compositing, radial skill cooldowns, summon astrolabe sequences, `URUI.Effect` shaders/particles, `URUI.Authoring`, and the 10,000-item armory.
- **Package Core Sample**: In Package Manager select **URUI → Samples → Solstice → Import**, then open the imported `Solstice.unity` scene (typically `Assets/Samples/URUI/<version>/Solstice`). You can also invoke **Tools → URUI → Open Solstice Sample**.

`Hello URUI`, `Element Playground`, and `Runtime Controls` provide focused topic samples. `Documentation~`, `Samples~`, `Tests~`, and `Native~` are ignored by standard Unity asset import and imported explicitly only when requested.

Optional packages [Effect](../../com.fujisheng.urui.effect/README.md) and [Authoring](../../com.fujisheng.urui.authoring/README.md) require matching versions of `com.fujisheng.urui`. For application navigation/screen binding frameworks, connect through a dedicated adapter package. Proceed to [Quick Start](QUICK_START.md).
