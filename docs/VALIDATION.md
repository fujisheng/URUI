# Validation of the main-sync candidate

The fixed baseline is Unity 6000.3.22f1, Mac ARM64, Built-in/Metal, IL2CPP Release. No Unity upgrade, Android execution, registry download or runner registration was performed.

## Fresh checks

- Native Release rebuilt offline from committed C++ sources with RmlUi 6.3 and FreeType 2.13.3.
- Core, NativeBatch, NativeDelta and NativeFrame C++ suites passed with ASan/UBSan. Leak detection was disabled; this is not a leak-freedom proof.
- Actual plugin C ABI: 1,000 owner-thread create/destroy contexts and 15,231 concurrent wrong-owner queries passed; stale outputs were cleared and an existing immutable CPU lease remained valid after context teardown.
- RML and uGUI Match3 samples built as ARM64 IL2CPP Release Players. A separate development regression Player used the same runtime source and native library.
- 31 legacy/NativeFrame pixel comparisons passed: zero pixels outside tolerance 2; maximum channel difference 1. Coverage included alpha/order, clipping, 2D/3D fallback, late DOM changes, reload, repeated cameras, targets, mode changes, pressure/admission, input and game states.
- NativeFrame Host stop contracts passed 17 checks in both Editor and Player: same-frame/OnEnable rejection, explicit later-frame recovery, deferred close and destruction cleanup. Four injected uncertain roots deliberately remained retained until each test process exited; they are not a normal-frame memory measurement.
- Independent review closed missing RmlUi/Containers/FreeType notices, script execution mode and overly broad fault-contract wording.

The shipped Mac dylib SHA-256 is `0c9df11f988f807de559f6407a7c6f4e59414b6fd749a8b8bb0dd88792761a8a`. Each of the three test Apps contained exactly those bytes. Runtime source files were checked against the previously qualified Phase1 snapshot; publication only trims unneeded harness/dependency files. Test-only probes were added to a separate validation copy.

## Reproduction

```sh
python3 Native/test_native_frame.py Assets/Plugins/macOS/libRmlUnityBridge.dylib
clang++ -std=c++17 -O2 -fsanitize=address,undefined -fno-omit-frame-pointer Native/native_frame_tests.cpp -o /tmp/rml-native-frame-test
ASAN_OPTIONS=detect_leaks=0 /tmp/rml-native-frame-test
```

The other standalone native suites are `core_frame_tests.cpp`, `prepared_batch_tests.cpp` and `prepared_delta_tests.cpp`. Unity sample build entries are `RmlUnity.Editor.ReleaseBuild.Rml` and `RmlUnity.Editor.ReleaseBuild.Ugui`; they preserve existing output directories.

The offline validation project resolved the exact cached uGUI 2.0.0 using a local file reference. The repository manifest uses portable registry version 2.0.0; a fresh registry installation was not tested. Initially embedding the cached package incorrectly enabled package test sources and failed for missing NUnit/TestRunner; using the previously verified local package reference resolved that validation setup issue without changing package code.

## Scope and limits

The prior 680-run atomic study and 30-run Match3 study informed the choice of explicit NativeFrame option. This publication did not rerun the full performance matrix or measure a new speed claim. Static total-frame P99 regression remains documented; physical GPU cost and unsupported IL2CPP GC counters remain unknown. Macro CPU, wall-clock waits, upload requests and physical GPU traffic must be interpreted separately.

The default C# renderer and managed API remain. Older parameter-root modes do not have NativeFrame's identical same-frame/reentrant failure gate. Other platforms, SRPs, arbitrary shaders/filters/masks and Editor SIGILL root cause remain outside this validation.

The inherited Android ABI-1 binary is disabled, preserved by its original Git blob, and must not be used with the new C# assembly. No workflows were changed. The existing Native push workflow builds/publishes Android; this sync uses `[skip ci]` to honor the no-Android boundary. An intentionally skipped workflow is not a green CI result; local validation is reported above.
