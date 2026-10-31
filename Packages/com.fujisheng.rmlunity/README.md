# RmlUnity UPM package

Copy this complete folder to `YourProject/Packages/com.fujisheng.rmlunity`, or use Package Manager > Add package from disk and select package.json. Do not install alongside the legacy Assets/RmlUnity classes: their assembly names and GUIDs intentionally preserve identity, so migrate/remove the old installation first.

Validated scope: Unity 6000.3.22f1, macOS ARM64, Built-in/Metal. Other platforms and SRPs are not validated; no Android/Windows/Linux binary is shipped. Keep Input Manager enabled (legacy Input is the current backend). The legacy Input API is provided by the installed engine, not a UPM inputlegacy package. The only explicit built-in module dependency is jsonserialize for optional renderer diagnostics. No uGUI, UITK comparison project, TestRunner or benchmark dependency is installed.

Use Tools > RmlUnity > Create Minimal UI Scene, or import the optional Hello Rml sample in Package Manager. User code calls RmlUnityHost.LoadMarkup, SetText, SetStyle, SetTranslateScale2D, AcquireElementReference and the Clicked / PointerInput events. Native state and pointer lifetimes remain internal. Assign a document and a MainCamera. The default font is the platform Arial; overrideFontPath can select an application-supplied font file. This bridge currently permits one live native context, not independent concurrent Hosts.

Runtime/ contains the managed framework, shaders and the Mac plugin. Editor/ contains only reusable scene and shader-contract tooling. Samples~/ is opt-in. Tests~/ and Native~/ are ignored by Unity asset compilation. NativeFrame and other experimental rendering paths remain explicit opt-ins; managed rendering is the default. Demo click mutations default off. CPU probe consumers and Match3 are excluded from the core.

## Native source and offline rebuild

Native~/bridge.cpp is the compiled implementation unit. Several algorithms are header-defined: NativeFrame (native_frame.h), prepared batching/delta (prepared_batch.h), render epochs (render_plan.h), core numeric preparation (core_frame.h), and retained element references (element_references.h). They contain complete C++ implementations, not missing cpp counterparts.

RmlUi 6.3 and FreeType 2.13.3 sources are included under Native~/ThirdParty with their licenses and content inventory. No source outside this package, registry download or FetchContent is needed. Install Apple command-line build tools and CMake >=3.21, then run `bash Native~/build-macos.sh /tmp/rmlunity-native-build` on an ARM64 Mac. The script builds in the specified fresh directory, runs four contract tests with assertions enabled, and does not replace the shipped plugin automatically. The lookup instrumentation test is included only with its optional instrumented build. Copy a rebuilt library deliberately into an embedded package if desired. Read-only PackageCache installs should be embedded before replacement.

See Documentation~/SOURCE_AUDIT.md, Native~/SOURCE_PROVENANCE.json and the verification report beside this package. The unchanged shipped dylib is the qualified previous artifact; rebuild reproducibility means complete versioned input/ABI and contract checks, not an assertion of bit-identical compiler output. Do not infer GPU/performance claims from package import checks. Framework licensing still requires the owner’s decision; third-party terms are preserved.
