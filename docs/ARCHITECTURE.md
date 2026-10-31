# Runtime data flow

`RML/RCSS → RmlUi DOM/style/layout → local geometry + ordered draw stream → C ABI → Unity Mesh/texture/CommandBuffer → Built-in Camera`.

The original C# renderer copies command/geometry snapshots and records Unity draws. Optional parameter-table paths separate local packed vertices from changing affine data. NativeBatch prepares ordered packed batches in C++; NativeDelta copies generation-checked CPU ranges; NativeCore prepares exact-compatible matrix/value packets. NativeFrame also owns immutable CPU frame leases and topology reuse. Unity resource creation, capability checks, fence retirement and submission stay in C#.

Managed APIs and the original renderer are retained. Node/resource handles carry identity and epochs. CPU leases, Unity references and GPU completion have separate lifetimes. Unsupported preparation retains the existing draw representation; uncertain cleanup blocks recording until explicit safe recovery.

Current validation targets Mac ARM64, Built-in/Metal, one context on its creation thread.
