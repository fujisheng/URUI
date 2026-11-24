# URUI.Effect Public API Reference

Matches package version `0.5.0-preview.1`. Lists public types and directly declared public/protected members extracted from Runtime sources for checking parameters, return types, defaults, and inheritance; omits internal/private types and inherited base members (see Unity docs for base APIs).

Declarations omit implementations; see [Documentation Home](../README.md) for usage sequence, cleanup rules, and sample components. Partial types are merged, and protected constructors are invoked by `Document` factories.

## URUI.Effect

### ArcTextLayout

Source: [ArcTextLayout.cs](../Runtime/ArcTextLayout.cs).

```csharp
// Lays out graphemes of an authored single-line text node along a circular arc using a span prototype and CSS tokens.
public sealed class ArcTextLayout : IDisposable
public ArcTextLayout(Element host, Element source, Element prototype);
public void Dispose();
```

### BlendType

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum BlendType { Custom, AlphaBlend, Multiply, Additive, SoftAdditive, MultiplyAdditive }
```

### ColorFilter

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum ColorFilter { None = 0, Multiply = 1, Additive = 2, Subtractive = 3, Replace = 4, MultiplyLuminance = 5, MultiplyAdditive = 6, HsvModifier = 7, Contrast = 8 }
```

### DetailFilter

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum DetailFilter { None = 0, Masking = 1, Multiply = 2, Additive = 3, Subtractive = 6, Replace = 4, MultiplyAdditive = 5 }
```

### EdgeMode

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum EdgeMode { None = 0, Plain, Shiny }
```

### EffectApplyMode

Source: [EffectController.cs](../Runtime/EffectController.cs).

```csharp
public enum EffectApplyMode { Replace, MergeActiveGroups }
```

### EffectController

Source: [EffectController.cs](../Runtime/EffectController.cs).

```csharp
// Preset application, playback control, and parameter linking for authored nodes without owning DOM lifetime or per-node MonoBehaviours.
public sealed class EffectController : IDisposable, IEffectParameterBinding
public Element Element { get; }
public TransitionInputPolicy InputPolicy { get; set; }
public void Apply(EffectPreset preset, EffectApplyMode mode = EffectApplyMode.Replace);
public EffectParameters CreateParameters();
public void SaveTo(EffectPreset preset);
public IDisposable OverridePreset(EffectPreset preset);
public EffectPlayback Play(EffectTweenDefinition definition, CancellationToken cancellationToken = default);
public IDisposable Link(EffectController source, bool useTargetRoot = true);
public void Dispose();
```

### EffectDirection

Source: [EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs).

```csharp
public enum EffectDirection { Forward, Reverse }
```

### EffectInput

Source: [EffectScheduler.cs](../Runtime/EffectScheduler.cs).

```csharp
// Complete processing key; time-driven shaders encode clock/frame identity in Version while video feeds PresentedVersion.
public readonly struct EffectInput : IEquatable<EffectInput>
public readonly object Source;
public readonly ulong Version, Parameters;
public readonly Vector2 Size;
public readonly string Program;
public readonly RenderTextureFormat Format;
public EffectInput(object source, ulong version, Vector2 size, ulong parameters, string program, RenderTextureFormat format);
public bool Equals(EffectInput other);
public override bool Equals(object value);
public override int GetHashCode();
```

### EffectModule

Source: [EffectModule.cs](../Runtime/EffectModule.cs).

```csharp
// Explicit per-document effect module; resolves assets through caller-supplied bindings without scanning scene objects.
public sealed class EffectModule : IDisposable
public Document Document { get; }
public static IReadOnlyList<StylePropertyDefinition> PropertyDefinitions { get; }
public static bool TryGet(Document document, out EffectModule module);
public EffectModule(Document document);
public EffectModule(Document document, IEffectResourceResolver resources);
public EffectController GetController(Element element);
public IDisposable BindSurface(Element element, SurfaceEffectInput input);
public void Dispose();
```

### EffectParameters

Source: [EffectParameters.cs](../Runtime/EffectParameters.cs).

```csharp
// Plain immutable/copyable effect parameters without node or renderer ownership; default values shown below.
public sealed class EffectParameters
public ToneFilter ToneFilter = ToneFilter.None;
[Range(0, 1)] public float ToneIntensity = 1;
public ColorFilter ColorFilter = ColorFilter.None;
[Range(0, 1)] public float ColorIntensity = 1;
public Color Color = Color.white;
public bool ColorGlow = false;
public SamplingFilter SamplingFilter = SamplingFilter.None;
[Range(0, 1)] public float SamplingIntensity = 0.5f;
[Range(0.5f, 10f)] public float SamplingWidth = 1;
public TransitionFilter TransitionFilter = TransitionFilter.None;
[Range(0, 1)] public float TransitionProgress = 0.5f;
public bool TransitionReverse;
public Texture TransitionTexture;
public Vector2 TransitionTextureScale = new Vector2(1, 1);
public Vector2 TransitionTextureOffset = new Vector2(0, 0);
public Vector2 TransitionTextureSpeed = new Vector2(0, 0);
[Range(0, 360)] public float TransitionRotation = 0;
public bool TransitionKeepAspectRatio = true;
[Range(0, 1)] public float TransitionWidth = 0.2f;
[Range(0, 1)] public float TransitionSoftness = 0.2f;
public EffectRange TransitionRange = new EffectRange(0, 1);
public ColorFilter TransitionColorFilter = ColorFilter.MultiplyAdditive;
public Color TransitionColor = new Color(0f, 0.5f, 1.0f, 1.0f);
public bool TransitionColorGlow = false;
public bool TransitionPatternReverse = false;
[Range(-5, 5)] public float TransitionAutoPlaySpeed = 0f;
public TargetMode TargetMode = TargetMode.None;
public Color TargetColor = Color.white;
[Range(0, 1)] public float TargetRange = 0.1f;
[Range(0, 1)] public float TargetSoftness = 0.5f;
public BlendType BlendType = BlendType.AlphaBlend;
public BlendMode SrcBlendMode = BlendMode.One;
public BlendMode DstBlendMode = BlendMode.OneMinusSrcAlpha;
public ShadowMode ShadowMode = ShadowMode.None;
public Vector2 ShadowDistance = new Vector2(1f, -1f);
[Range(1, 5)] public int ShadowIteration = 1;
[Range(0, 1)] public float ShadowFade = 0.9f;
[Range(0, 2)] public float ShadowMirrorScale = 0.5f;
[Range(0, 1)] public float ShadowBlurIntensity = 1;
public ColorFilter ShadowColorFilter = ColorFilter.Replace;
public Color ShadowColor = Color.white;
public bool ShadowColorGlow = false;
public GradationMode GradationMode = GradationMode.None;
[Range(0, 1)] public float GradationIntensity = 1;
public GradationColorFilter GradationColorFilter = GradationColorFilter.Multiply;
public Color GradationColor1 = Color.white;
public Color GradationColor2 = Color.white;
public Color GradationColor3 = Color.white;
public Color GradationColor4 = Color.white;
public Gradient GradationGradient = new Gradient();
[Range(-1, 1)] public float GradationOffset = 0;
[Range(0.01f, 10)] public float GradationScale = 1;
[Range(0, 360)] public float GradationRotation = 0;
public EdgeMode EdgeMode = EdgeMode.None;
[Range(0, 1)] public float EdgeWidth = 0.5f;
public ColorFilter EdgeColorFilter = ColorFilter.Replace;
public Color EdgeColor = Color.white;
public bool EdgeColorGlow = false;
[Range(0, 1)] public float EdgeShinyProgress = 0.5f;
[Range(0, 1)] public float EdgeShinyWidth = 0.5f;
[Range(-5, 5)] public float EdgeShinyAutoPlaySpeed = 1f;
public PatternArea PatternArea = PatternArea.Inner;
public DetailFilter DetailFilter = DetailFilter.None;
[Range(0, 1)] public float DetailIntensity = 1;
public Color DetailColor = Color.white;
public EffectRange DetailThreshold = new EffectRange(0, 1);
public Texture DetailTexture;
public Vector2 DetailTextureScale = new Vector2(1, 1);
public Vector2 DetailTextureOffset = new Vector2(0, 0);
public Vector2 DetailTextureSpeed = new Vector2(0, 0);
public Flip Flip = 0;
public float SamplingScale = 1;
public bool AllowToModifyMeshShape = true;
// References shared textures while deep-copying Gradient stops/color mode for independent document and node edits.
public EffectParameters Clone();
```

### EffectPlayback

Source: [EffectPlayback.cs](../Runtime/EffectPlayback.cs).

```csharp
// Double-precision effect timeline handle; manual seeks do not complete Finished and natural completion notifies once.
public sealed class EffectPlayback : IDisposable
public event Action<float> ProgressChanged;
public event Action Completed;
public Task Finished { get; }
public long RunIdentity { get; private set; }
public float Progress { get; }
public float EvaluatedProgress { get; }
public double Time { get; }
public bool IsPlaying { get; }
public bool IsCancelled { get; }
public bool IsPaused { get; }
public bool IsDelaying { get; }
public EffectDirection Direction { get; }
public EffectTweenChannels Channels { get; }
public EffectUpdateMode UpdateMode { get; }
public double TotalTime { get; }
public bool Enabled { get; set; }
public EffectPlayback(EffectTweenDefinition definition);
public void Advance(double delta);
public void SetTime(double seconds);
public void Pause();
public void Resume();
public void Stop();
public void Restart();
public void Cancel();
public void Dispose();
```

### EffectPlayOnEnable

Source: [EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs).

```csharp
public enum EffectPlayOnEnable { None, Forward, Reverse, KeepDirection }
```

### EffectPreset

Source: [EffectPreset.cs](../Runtime/EffectPreset.cs).

```csharp
// Serializable shared preset asset; callers acquire snapshots or apply non-destructive node overrides.
public sealed class EffectPreset : ScriptableObject
public EffectParameters CreateParameters();
public void SetParameters(EffectParameters value);
public void Append(EffectParameters value);
```

### EffectPreview

Source: [EffectPreview.cs](../Runtime/EffectPreview.cs).

```csharp
// Production effect preview renderer for Editor and tests using explicit clock values and parameter snapshots.
public sealed class EffectPreview : IDisposable
public EffectPreview(EffectParameters parameters);
public void SetParameters(EffectParameters value);
public void Render(Texture source, Rect content, double time, RenderTexture destination, Color background);
public void Dispose();
```

### EffectRange

Source: [EffectParameters.cs](../Runtime/EffectParameters.cs).

```csharp
// Effect min/max scalar range preserving authored bounds semantics.
public struct EffectRange
public float Min;
public float Max;
public EffectRange(float min, float max);
```

### EffectResources

Source: [EffectResources.cs](../Runtime/EffectResources.cs).

```csharp
// Auxiliary texture binding for effects; acquires new lease before retiring previous lease via owning EffectContext.
public sealed class EffectResources
public EffectResources(EffectContext context, IEffectResourceResolver resolver);
public Texture GetImage(string name);
// Returns false and preserves prior input when missing; propagates resolver errors cleanly without corrupting active input.
public bool PrepareImage(string name, ComputedImageValue value);
```

### EffectScheduler

Source: [EffectScheduler.cs](../Runtime/EffectScheduler.cs).

```csharp
// Per-instance processing scheduler tracking submission generations so stale updates never overwrite newer cache state.
public sealed class EffectScheduler
public bool TryPrepare(EffectInput input, out Update update);
public void Invalidate();
```

### EffectScheduler.Update

Source: [EffectScheduler.cs](../Runtime/EffectScheduler.cs).

```csharp
public readonly struct Update
public void Submitted();
```

### EffectTweenChannels

Source: [EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs).

```csharp
public enum EffectTweenChannels : uint { None = 0, Tone = 1, Color = 2, Sampling = 4, Transition = 8, GradationOffset = 1 << 5, GradationRotation = 1 << 6, EdgeShiny = 1 << 8, Event = 1u << 31, Visual = Tone | Color | Sampling | Transition | GradationOffset | GradationRotation | EdgeShiny }
```

### EffectTweenDefinition

Source: [EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs).

```csharp
public sealed class EffectTweenDefinition
public EffectTweenChannels Channels = EffectTweenChannels.Visual;
public EffectDirection Direction = EffectDirection.Forward;
public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);
public bool SeparateReverseCurve;
public AnimationCurve ReverseCurve = AnimationCurve.Linear(0, 0, 1, 1);
public double Delay, Duration = 1, Interval;
public EffectPlayOnEnable PlayOnEnable = EffectPlayOnEnable.Forward;
public bool ResetTimeOnEnable = true;
public EffectWrapMode WrapMode = EffectWrapMode.Loop;
public EffectUpdateMode UpdateMode = EffectUpdateMode.Normal;
```

### EffectUpdateMode

Source: [EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs).

```csharp
public enum EffectUpdateMode { Normal, Unscaled, Manual }
```

### EffectWrapMode

Source: [EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs).

```csharp
public enum EffectWrapMode { Once, Loop, PingPongOnce, PingPongLoop }
```

### FeatherMaskPainter

Source: [FeatherMaskPainter.cs](../Runtime/FeatherMaskPainter.cs).

```csharp
// Soft feathered edge mask for --urui-mask-image driven by CSS custom properties.
public sealed class FeatherMaskPainter : IDisposable
// Binds an authored shader(feather-mask) painter.
public FeatherMaskPainter(Element element);
// Releases owned drawing and GPU resources.
public void Dispose();
```

### Flip

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum Flip { Horizontal = 1 << 0, Vertical = 1 << 1, Effect = 1 << 2, Shadow = 1 << 3 }
```

### GradationColorFilter

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum GradationColorFilter { None = (int)ColorFilter.None, Multiply = (int)ColorFilter.Multiply, Additive = (int)ColorFilter.Additive, Subtractive = (int)ColorFilter.Subtractive, Replace = (int)ColorFilter.Replace, MultiplyLuminance = (int)ColorFilter.MultiplyLuminance, MultiplyAdditive = (int)ColorFilter.MultiplyAdditive }
```

### GradationMode

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum GradationMode { None = 0, Horizontal = 1, HorizontalGradient = 2, Vertical = 3, VerticalGradient = 4, RadialFast = 5, RadialDetail = 6, Diagonal = 11, DiagonalToRightBottom = 7, DiagonalToLeftBottom = 8, Angle = 9, AngleGradient = 10 }
```

### IEffectResourceResolver

Source: [IEffectResourceResolver.cs](../Runtime/IEffectResourceResolver.cs).

```csharp
// Caller-implemented resolver for auxiliary effect textures and particle prefabs.
public interface IEffectResourceResolver
ImageLease AcquireImage(ComputedImageValue image);
GameObject ResolveParticles(string source);
```

### ParticlePainter

Source: [ParticlePainter.cs](../Runtime/ParticlePainter.cs).

```csharp
// Attaches managed Unity ParticleSystem baking to an element; source swaps retire old resources behind host GPU fences.
public sealed class ParticlePainter : IDisposable
public ParticlePlaybackState PlaybackState { get; private set; }
public int ParticleCount { get; private set; }
public GameObject Source { get; }
public float SimulationTime { get; private set; }
public bool ManualSimulation { get; }
public ParticlePainter(Element element, GameObject source, bool restartOnShow = true);
public ParticlePainter(Element element, GameObject source, ParticlePlaybackOptions options);
public void SetSource(GameObject value);
public void RefreshSource();
public void SetManualSimulation(bool enabled, uint randomSeed = 1);
public void Seek(float time, uint randomSeed = 1);
public void Step(float delta);
public IDisposable OverridePlayback();
public IDisposable OverrideMaterial(Material source, Material replacement);
public void SetOptions(ParticlePlaybackOptions value);
public void Play();
public void Pause();
public void Stop();
public void Restart();
public ParticlePainterStatistics GetStatistics();
public void Dispose();
```

### ParticlePainterStatistics

Source: [ParticlePlaybackOptions.cs](../Runtime/ParticlePlaybackOptions.cs).

```csharp
public readonly struct ParticlePainterStatistics
public long BodyBakes { get; }
public long TrailBakes { get; }
public long ParticleReads { get; }
public long DrawCalls { get; }
public double ReadMilliseconds { get; }
public double BodyBakeMilliseconds { get; }
public double TrailBakeMilliseconds { get; }
public double RecordMilliseconds { get; }
```

### ParticlePlaybackOptions

Source: [ParticlePlaybackOptions.cs](../Runtime/ParticlePlaybackOptions.cs).

```csharp
public sealed class ParticlePlaybackOptions
public bool PlayOnLoad = true;
public bool PauseWhenHidden = true;
public bool RestartOnShow = true;
```

### ParticlePlaybackState

Source: [ParticlePlaybackOptions.cs](../Runtime/ParticlePlaybackOptions.cs).

```csharp
public enum ParticlePlaybackState { Playing, Paused, Stopped }
```

### PatternArea

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum PatternArea { All = 0, Inner = 1, Edge = 2 }
```

### SamplingFilter

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum SamplingFilter { None = 0, BlurFast = 1, BlurMedium = 2, BlurDetail = 3, Pixelation = 4, RgbShift = 5, EdgeLuminance = 6, EdgeAlpha = 7 }
```

### ShadowMode

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum ShadowMode { None = 0, Shadow, Shadow3, Outline, Outline8, Mirror }
```

### StrokePainter

Source: [StrokePainter.cs](../Runtime/StrokePainter.cs).

```csharp
// Draws anti-aliased local-space line segments sized by --urui-line-width CSS property.
public sealed class StrokePainter : IDisposable
public StrokePainter(Element element);
// Supplies segment endpoints in pairs matching per-segment colors; an empty array clears segments.
public void SetSegments(IReadOnlyList<Vector2> points, IReadOnlyList<Color> segmentColors);
public void Dispose();
```

### TargetMode

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum TargetMode { None = 0, Hue = 1, Luminance = 2 }
```

### TextureEffectSurface

Source: [TextureEffectSurface.cs](../Runtime/TextureEffectSurface.cs).

```csharp
// Processes GPU textures with an authored Material and yields ref-counted ImageLeases without Canvas setup or CPU readback.
public sealed class TextureEffectSurface : IDisposable
public static Statistics GetStatistics();
public ImageLeaseSource Output { get; }
public RenderTexture Texture { get; }
public void Clear();
public TextureEffectSurface(int width, int height, IDisposable inputLease = null);
// Updates geometry during effect Prepare phase; never allocates or mutates meshes during Record.
public void Prepare(Vector2 size);
// Records only prepared resources; pass counters and cache revisions commit strictly after GPU submission.
public void Record(EffectRenderContext context, Texture input, Material material, MaterialPropertyBlock properties);
public void Render(Texture input, Material material, Vector2 size);
public void Dispose();
```

### TextureEffectSurface.Statistics

Source: [TextureEffectSurface.cs](../Runtime/TextureEffectSurface.cs).

```csharp
// Read-only counters for GPU surfaces and passes; color bytes exclude driver alignment and Unity object overhead.
public readonly struct Statistics
public int LiveSurfaces { get; }
public long LiveColorBytes { get; }
public long ProcessingPasses { get; }
public long ClearPasses { get; }
public long MeshRebuilds { get; }
```

### ToneFilter

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum ToneFilter { None = 0, Grayscale = 1, Sepia = 2, Negative = 3, Retro = 4, Posterize = 5 }
```

### TransitionFilter

Source: [EffectModes.cs](../Runtime/EffectModes.cs).

```csharp
public enum TransitionFilter { None = 0, Fade = 1, Cutoff = 2, Dissolve = 3, Shiny = 4, Mask = 5, Melt = 6, Burn = 7, Pattern = 8 }
```

### TransitionInputPolicy

Source: [EffectController.cs](../Runtime/EffectController.cs).

```csharp
public enum TransitionInputPolicy { Standard, DisableWhenComplete }
```

## URUI.Effect.Elements

### ParticlesElement

Source: [ParticlesElement.cs](../Runtime/Elements/ParticlesElement.cs).

```csharp
// Declarative Unity particle element; HTML src resolves via module IEffectResourceResolver while Unity simulates particles.
public sealed class ParticlesElement : Element
public string Source { get; set; }
public string CurrentSource { get; }
public int ParticleCount { get; }
public ParticlePlaybackState PlaybackState { get; }
public float SimulationTime { get; }
public ParticlePainterStatistics GetStatistics();
public void Play();
public void Pause();
public void Stop();
public void Restart();
public void Seek(float time, uint randomSeed = 1);
public void Step(float delta);
public void RefreshSource();
public IDisposable OverridePlayback();
public IDisposable OverrideSource(GameObject value);
public IDisposable OverrideMaterial(Material source, Material replacement);
protected override void OnInitialize();
protected override void OnAttributeChanged(string name);
protected override void OnUpdate();
protected override void OnInvalidated();
```

## URUI.Effect.Rendering

### SurfaceEffectInput

Source: [SurfaceEffect.cs](../Runtime/Rendering/SurfaceEffect.cs).

```csharp
// Explicitly bound offscreen surface processing input; layout and content version come from caller without scanning sibling nodes.
public sealed class SurfaceEffectInput
public TextureEffectSurface Surface { get; }
public Texture Source { get; }
public Material Material { get; }
public Func<ulong> Version { get; }
public Func<bool> Ready { get; }
public Action<EffectFrame> Prepare { get; }
public bool UsesTime { get; }
public SurfaceEffectInput(TextureEffectSurface surface, Texture source, Material material, Func<ulong> version, Func<bool> ready = null, Action<EffectFrame> prepare = null, bool usesTime = false);
```

### TextureColorEffect

Source: [TextureColorEffect.cs](../Runtime/Rendering/TextureColorEffect.cs).

```csharp
// Immutable program for single-image color grading, detail textures, and sheen; per-node bounds, progress, and clock live in isolated blocks.
public static class TextureColorEffect
// Resolves authored shaders/materials by non-inherited resource key so projects supply assets without per-document material registration.
public static EffectDefinition DefineResolved(string name, Func<string, Material> resolver, string keyProperty, string rateProperty = null, string saturationProperty = null, string brightnessProperty = null);
public static EffectDefinition Define(string name, Material authored, string rateProperty = null, string saturationProperty = null, string brightnessProperty = null);
```

### WaveEffect

Source: [WaveEffect.cs](../Runtime/Rendering/WaveEffect.cs).

```csharp
// Vertex mesh wave displacement; shared normalized grids rebuild only on subdivision changes while timing/bounds pass per-instance.
public static class WaveEffect
public static EffectDefinition Define(string name = "ui-wave");
```
