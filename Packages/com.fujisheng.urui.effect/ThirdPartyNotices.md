# Third-party notices

UIEffect 5.8.4 — Copyright 2017–2024 mob-sakai.

Fixed upstream commit: `3ff52e4735481da36428d361afe28b85eca96e2d`.
Algorithm and shader migration is derived from this MIT-licensed source; the runtime does not depend on Coffee or UGUI. Full license: [UIEffect/LICENSE.md](ThirdParty/UIEffect/LICENSE.md). The exact source manifest and per-file hashes are recorded by the migration inventory.

Adapted data definitions: `Runtime/EffectModes.cs` and `Runtime/EffectParameters.cs` derive from upstream `Runtime/Enums.cs` and `Runtime/UIEffectPreset.cs`.

Adapted color/target algorithms: `Runtime/Resources/URUI.Effect/ColorOps.cginc` derives from upstream `Shaders/UIEffect.cginc`; parameter ranges in `Runtime/Rendering/EffectColorProperties.cs` follow upstream `Runtime/UIEffect.cs`.

Adapted sampling algorithms and expansion: `Runtime/Resources/URUI.Effect/Sampling.cginc`, the sampling wrapper in `UiEffect.shader`, and `Runtime/Rendering/EffectSampling.cs` derive from upstream `Shaders/UIEffect.cginc`, `Runtime/UIEffectContext.cs`, and `Runtime/Utilities/UIVertexUtil.cs`.

Adapted transition/detail/edge algorithms: `Runtime/Resources/URUI.Effect/{Transition,Detail,Edge}.cginc` and `Runtime/Rendering/EffectVisualProperties.cs` derive from upstream `Shaders/UIEffect.cginc`, `Runtime/UIEffectContext.cs`, and `Runtime/UIEffect.cs`. Test-only reference wrappers retain the original shader and include; their explicit clock uniform replaces only the time input.

Adapted gradient, shadow, mirror and blend definitions: `Runtime/Rendering/{EffectGradation,EffectGradientCache,EffectGeometry,EffectShadowProperties,EffectBlendState}.cs` and `Runtime/Resources/URUI.Effect/{Gradation,Shadow}.cginc` derive from upstream `Runtime/UIEffectContext.cs`, `Runtime/Utilities/{ShadowUtil,UIVertexUtil}.cs`, `Runtime/Enums.cs`, and `Shaders/UIEffect.cginc`.
