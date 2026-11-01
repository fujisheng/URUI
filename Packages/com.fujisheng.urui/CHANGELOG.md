# Changelog

## Unreleased

- Provide `URUI.Documents.Document`, `Element`, `ElementStyle`, `ImageAsset` and `ClickEvent` in `URUI.Runtime`.
- Support RML loading, ID queries, text and structure updates, RCSS styles, 2D transforms, image mappings and disposable click subscriptions.
- Define main-thread access, one active document, element invalidation and resource ownership.
- Separate hosting, input, rendering and native interop into internal responsibility namespaces, with the URUIBridge native library and `urui_*` exports.
- Include Hello URUI, the minimal scene menu, API guides, the support matrix and the roadmap for AI-assisted Unity UI development.
- Bundle native plugins for Android ARM64/x86_64, Windows x86_64, macOS ARM64/x86_64, iOS device ARM64 and WebGL wasm32; distinguish compilation/link checks from Unity runtime validation.
