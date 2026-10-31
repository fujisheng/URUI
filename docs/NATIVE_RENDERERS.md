# Renderer setup and ownership

The default is the existing C# path. Enable the parameter path only when the integration exclusively owns its Host Camera commands, meshes, material and textures. Assign the supplied BuiltinShaderPropertyContract and ParameterShaderCapability assets.

```csharp
host.propertyContract = builtinShaderPropertyContract;
host.parameterCapability = parameterShaderCapability;
host.useShaderPropertyIds = true;
host.useFrameScratchCollections = true;
host.reusePropertyBlock = true;
host.batchDisjointCommands = true;
host.batchEquivalentTransforms = true;
host.batchOrderedOverlap = true;
host.retainUnchangedCommands = false;
host.useParameterTable = true;
host.shareParameterResources = true;
host.reuseParameterBatches = true;
host.reuseParameterBatchScratch = true;
host.SetRecordingOwnership(RmlRecordingOwnership.HostExclusive);

// Select a deliberate configuration; false leaves the managed parameter path.
host.useNativePreparedBatches = true; // NativeBatch
// host.useNativeIncrementalPayloads = true; // CPU delta extension of NativeBatch
// host.useNativeFinalFramePacket = true; // NativeCore preparation
// host.useBorrowedNativeFrame = true; // Alternative complete NativeFrame lease
```

NativeFrame takes precedence over older preparation toggles. Check actual activation through BorrowedNativeFrameActive, NativePreparedActive and ParameterRootActive. Eligibility includes audited shaders/capabilities, HostExclusive ownership, Built-in/Metal, fences, SM4.5, 32-bit indices and the additional camera/resource checks in code.

NativeFrame leases immutable geometry, ordered draw metadata, affine values and fallback matrices. C# validates count/stride/budget, borrows arrays only during synchronous Unity calls, and releases CPU leases in finally. Unity resources retire separately behind GPU fences. Budgets/backpressure reject unsupported work. Unknown matrix cases preserve the existing representation and painter order.

NativeFrame disable, mode switch, exception and destruction must establish clear camera recording before fallback. Its uncertain cleanup retains resources and blocks same-frame/reentrant recording; OnEnable cannot erase that state. Older parameter-root modes retain uncertain resources and clear the legacy recording before drawing, but have not passed the same fault-gate contract and do not promise identical same-frame rejection. Do not release borrowed pointers or NativeArrays in user code.

Historical five-process Match3 results compared NativeFrame with NativeBatch: fixed-input main-thread CPU 118.736 → 109.262 µs; real-input 108.454 → 98.609 µs. uGUI was 86.820 and 79.408 µs. Static total-frame P99 increased about 4.6–5.6%; gameplay-tail intervals crossed zero. These scoped results do not guarantee performance for this build/device. CPU copy volume, Unity upload requests and physical GPU traffic are different; physical GPU cost remains unmeasured.
