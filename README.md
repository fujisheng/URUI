# RmlUnity — standard Unity Package Manager package

The reusable package is [Packages/com.fujisheng.rmlunity](Packages/com.fujisheng.rmlunity). Copy that complete folder into your project's `Packages/` directory, or select its `package.json` with Package Manager > Add package from disk. Do not install it beside the previous `Assets/RmlUnity` assembly: remove/migrate that installation first to avoid duplicate types, assemblies and asset GUIDs.

Validated scope: Unity **6000.3.22f1**, **macOS ARM64**, **Built-in/Metal**. The public managed `RmlUnityHost` API remains; native memory and pointer lifetimes remain internal. NativeFrame stays an explicit option. No Android/Windows/Linux binary or unverified SRP support is implied.

Package layout: Runtime (managed API, shaders and qualified Mac plugin), Editor (reusable tools), optional Samples~ (Hello Rml), Tests~ (non-default test/measurement sources), and Native~ (complete framework C++ and versioned third-party sources). No uGUI, Match3 or benchmark package is a runtime dependency. No .unitypackage is needed. The only explicit engine module dependency is the built-in jsonserialize module.

Implementation is in [Native~/bridge.cpp](Packages/com.fujisheng.rmlunity/Native~/bridge.cpp) and the complete template/inline implementations in its headers. RmlUi 6.3 and FreeType 2.13.3 source, license notices, provenance inventory and offline CMake entry are included. An empty-directory rebuild from this package produced exactly the qualified dylib bytes; this is source-completeness evidence, not just a claim that a binary exists.

See [installation and source guide](Packages/com.fujisheng.rmlunity/Documentation~/INSTALL_ZH.md), [validation](Packages/com.fujisheng.rmlunity/Documentation~/VALIDATION.md), and [migration](docs/UPM_MIGRATION.md). Local-file and embedded installs, optional sample compilation, Editor and ARM64 IL2CPP functional smoke, and the Editor tool's file-preservation checks passed. These checks are not new performance measurements.

The previous complete sample project remains recoverable at commit [a29a15ebf55b1b26fcfb47bf73fc6105b1d0b563](https://github.com/fujisheng/RmlUnity/tree/a29a15ebf55b1b26fcfb47bf73fc6105b1d0b563). Its active Assets/Native/ProjectSettings layout is removed from this main tree to keep this distribution a single UPM installation. Existing Android workflows are preserved unchanged as historical configuration; they are not validated package build entry points. This migration uses `[skip ci]` to avoid triggering Android work and does not claim a green CI result.
