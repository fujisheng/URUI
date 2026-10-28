# MVP Architecture
RML/RCSS TextAsset -> RmlUi 6.3 C++ parse/layout/events -> RenderInterface records geometry & commands -> C ABI -> Unity C# caches Mesh/Texture2D -> CommandBuffer with premultiplied-alpha shader.
The native bridge is a main-thread singleton. Advanced transforms, decorators and masking are intentionally out of scope. This is **not** a general web-browser implementation.
