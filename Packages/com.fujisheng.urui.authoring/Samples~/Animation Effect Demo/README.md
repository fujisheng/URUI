# UI + Effect + Particle Unified Sequence Demo

Requires `com.fujisheng.urui.effect`. Import into `Assets/` via Package Manager, open `Reward.html` → **Animation** → Play: UI transforms, `--urui-fx-*` effect parameters, and `urui-particles` share one timeline clock. Particle Prefabs/Materials follow Effect's material contract and only persist to source assets on explicit **Apply**.

At runtime, instantiate `new EffectModule(document, resources)` before loading so `resources` resolves `confetti.prefab`, link `/urui-effect/Effect.css` in HTML, and start `SequencePlayer` (Core and Authoring retain zero hard dependency on Effect).
