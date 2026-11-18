# URUI.Effect 接口声明参考

对应包版本 `0.5.0-preview.1`。本页根据当前 Runtime 源码整理公开类型及其直接声明的公开/受保护成员，供查参数、返回类型、默认值和继承关系；不包含 internal/private 类型，也不重复继承成员。Unity 基类接口请查 Unity 文档。

声明块用于查阅，省略实现体；完整使用顺序、资源释放和可运行组件见[文档导航](../README.md)。同名的 partial 类型合并列出，受保护构造函数由 Document 工厂调用。成员注释来自源码，不替代专题说明中的约束。

## URUI.Effect

### ArcTextLayout

源码：[ArcTextLayout.cs](../Runtime/ArcTextLayout.cs)。

```csharp
// 把作者单行纯文本的字素沿圆弧排列；字素使用作者span原型，参数来自CSS。
public sealed class ArcTextLayout : IDisposable
public ArcTextLayout(Element host, Element source, Element prototype);
public void Dispose();
```

### BlendType

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum BlendType { Custom, AlphaBlend, Multiply, Additive, SoftAdditive, MultiplyAdditive }
```

### ColorFilter

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum ColorFilter { None = 0, Multiply = 1, Additive = 2, Subtractive = 3, Replace = 4, MultiplyLuminance = 5, MultiplyAdditive = 6, HsvModifier = 7, Contrast = 8 }
```

### DetailFilter

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum DetailFilter { None = 0, Masking = 1, Multiply = 2, Additive = 3, Subtractive = 6, Replace = 4, MultiplyAdditive = 5 }
```

### EdgeMode

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum EdgeMode { None = 0, Plain, Shiny }
```

### EffectApplyMode

源码：[EffectController.cs](../Runtime/EffectController.cs)。

```csharp
public enum EffectApplyMode { Replace, MergeActiveGroups }
```

### EffectController

源码：[EffectController.cs](../Runtime/EffectController.cs)。

```csharp
// 作者节点的预设、独立播放与快照联动；不拥有 DOM 或每节点 MonoBehaviour。
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

源码：[EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs)。

```csharp
public enum EffectDirection { Forward, Reverse }
```

### EffectInput

源码：[EffectScheduler.cs](../Runtime/EffectScheduler.cs)。

```csharp
// 处理输入的完整身份。时间驱动调用方显式把时间版本放进 Version；视频使用实际呈现版本。
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

源码：[EffectModule.cs](../Runtime/EffectModule.cs)。

```csharp
// 显式安装到一个文档的效果模块。项目资产和媒体输入由调用者绑定，不查找场景对象。
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

源码：[EffectParameters.cs](../Runtime/EffectParameters.cs)。

```csharp
// 独立的效果参数数据，不持有节点或渲染实例；字段默认值如下。
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
// 纹理作为共享资源引用，Gradient 数据独立复制，供文档和节点分别修改。
public EffectParameters Clone();
```

### EffectPlayback

源码：[EffectPlayback.cs](../Runtime/EffectPlayback.cs)。

```csharp
// 独立的双精度时间线。手动 seek 不触发成功完成；自然到达终点仅通知一次。
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

源码：[EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs)。

```csharp
public enum EffectPlayOnEnable { None, Forward, Reverse, KeepDirection }
```

### EffectPreset

源码：[EffectPreset.cs](../Runtime/EffectPreset.cs)。

```csharp
// 可序列化的共享参数资产；调用者取得独立快照后再应用节点覆盖。
public sealed class EffectPreset : ScriptableObject
public EffectParameters CreateParameters();
public void SetParameters(EffectParameters value);
public void Append(EffectParameters value);
```

### EffectPreview

源码：[EffectPreview.cs](../Runtime/EffectPreview.cs)。

```csharp
// 编辑器与测试共用的生产效果预览；时间显式传入，参数取独立快照。
public sealed class EffectPreview : IDisposable
public EffectPreview(EffectParameters parameters);
public void SetParameters(EffectParameters value);
public void Render(Texture source, Rect content, double time, RenderTexture destination, Color background);
public void Dispose();
```

### EffectRange

源码：[EffectParameters.cs](../Runtime/EffectParameters.cs)。

```csharp
// 效果区间；源字段的 min/max 顺序和语义保持不变。
public struct EffectRange
public float Min;
public float Max;
public EffectRange(float min, float max);
```

### EffectResources

源码：[EffectResources.cs](../Runtime/EffectResources.cs)。

```csharp
// 实例的附加图像输入；先取得新租约再替换，旧输入通过所属 Context 的 GPU 退役释放。
public sealed class EffectResources
public EffectResources(EffectContext context, IEffectResourceResolver resolver);
public Texture GetImage(string name);
// 缺失的新输入返回 false 并保留旧输入；解析器异常向调用者报告，同样不破坏旧输入。
public bool PrepareImage(string name, ComputedImageValue value);
```

### EffectScheduler

源码：[EffectScheduler.cs](../Runtime/EffectScheduler.cs)。

```csharp
// 按实例调度；记录并不等于提交，旧世代和迟到的提交不能覆盖新缓存。
public sealed class EffectScheduler
public bool TryPrepare(EffectInput input, out Update update);
public void Invalidate();
```

### EffectScheduler.Update

源码：[EffectScheduler.cs](../Runtime/EffectScheduler.cs)。

```csharp
public readonly struct Update
public void Submitted();
```

### EffectTweenChannels

源码：[EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs)。

```csharp
public enum EffectTweenChannels : uint { None = 0, Tone = 1, Color = 2, Sampling = 4, Transition = 8, GradationOffset = 1 << 5, GradationRotation = 1 << 6, EdgeShiny = 1 << 8, Event = 1u << 31, Visual = Tone | Color | Sampling | Transition | GradationOffset | GradationRotation | EdgeShiny }
```

### EffectTweenDefinition

源码：[EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs)。

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

源码：[EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs)。

```csharp
public enum EffectUpdateMode { Normal, Unscaled, Manual }
```

### EffectWrapMode

源码：[EffectTweenDefinition.cs](../Runtime/EffectTweenDefinition.cs)。

```csharp
public enum EffectWrapMode { Once, Loop, PingPongOnce, PingPongLoop }
```

### FeatherMaskPainter

源码：[FeatherMaskPainter.cs](../Runtime/FeatherMaskPainter.cs)。

```csharp
// 普通节点 --urui-mask-image 的羽化遮罩，参数由作者 CSS 自定义属性提供。
public sealed class FeatherMaskPainter : IDisposable
// 连接作者声明的 shader(feather-mask)。
public FeatherMaskPainter(Element element);
// 释放所持的绘制资源。
public void Dispose();
```

### Flip

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum Flip { Horizontal = 1 << 0, Vertical = 1 << 1, Effect = 1 << 2, Shadow = 1 << 3 }
```

### GradationColorFilter

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum GradationColorFilter { None = (int)ColorFilter.None, Multiply = (int)ColorFilter.Multiply, Additive = (int)ColorFilter.Additive, Subtractive = (int)ColorFilter.Subtractive, Replace = (int)ColorFilter.Replace, MultiplyLuminance = (int)ColorFilter.MultiplyLuminance, MultiplyAdditive = (int)ColorFilter.MultiplyAdditive }
```

### GradationMode

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum GradationMode { None = 0, Horizontal = 1, HorizontalGradient = 2, Vertical = 3, VerticalGradient = 4, RadialFast = 5, RadialDetail = 6, Diagonal = 11, DiagonalToRightBottom = 7, DiagonalToLeftBottom = 8, Angle = 9, AngleGradient = 10 }
```

### IEffectResourceResolver

源码：[IEffectResourceResolver.cs](../Runtime/IEffectResourceResolver.cs)。

```csharp
// 调用者提供的图像与粒子资源入口；效果包不查找场景对象或游戏目录。
public interface IEffectResourceResolver
ImageLease AcquireImage(ComputedImageValue image);
GameObject ResolveParticles(string source);
```

### ParticlePainter

源码：[ParticlePainter.cs](../Runtime/ParticlePainter.cs)。

```csharp
// 持久注册一个作者节点；粒子源独立准备、原子替换并经宿主 GPU fence 退役。
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

源码：[ParticlePlaybackOptions.cs](../Runtime/ParticlePlaybackOptions.cs)。

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

源码：[ParticlePlaybackOptions.cs](../Runtime/ParticlePlaybackOptions.cs)。

```csharp
public sealed class ParticlePlaybackOptions
public bool PlayOnLoad = true;
public bool PauseWhenHidden = true;
public bool RestartOnShow = true;
```

### ParticlePlaybackState

源码：[ParticlePlaybackOptions.cs](../Runtime/ParticlePlaybackOptions.cs)。

```csharp
public enum ParticlePlaybackState { Playing, Paused, Stopped }
```

### PatternArea

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum PatternArea { All = 0, Inner = 1, Edge = 2 }
```

### SamplingFilter

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum SamplingFilter { None = 0, BlurFast = 1, BlurMedium = 2, BlurDetail = 3, Pixelation = 4, RgbShift = 5, EdgeLuminance = 6, EdgeAlpha = 7 }
```

### ShadowMode

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum ShadowMode { None = 0, Shadow, Shadow3, Outline, Outline8, Mirror }
```

### StrokePainter

源码：[StrokePainter.cs](../Runtime/StrokePainter.cs)。

```csharp
// 通过公开绘制接口绘制局部坐标线段；线宽来自外置 --urui-line-width 样式。
public sealed class StrokePainter : IDisposable
public StrokePainter(Element element);
// 端点按起点、终点成对提供，每段对应一个颜色；空数组清除线段。
public void SetSegments(IReadOnlyList<Vector2> points, IReadOnlyList<Color> segmentColors);
public void Dispose();
```

### TargetMode

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum TargetMode { None = 0, Hue = 1, Luminance = 2 }
```

### TextureEffectSurface

源码：[TextureEffectSurface.cs](../Runtime/TextureEffectSurface.cs)。

```csharp
// 用作者材质处理 GPU 纹理，以租约发布结果，不创建 Canvas 或读取像素。
public sealed class TextureEffectSurface : IDisposable
public static Statistics GetStatistics();
public ImageLeaseSource Output { get; }
public RenderTexture Texture { get; }
public void Clear();
public TextureEffectSurface(int width, int height, IDisposable inputLease = null);
// 在效果 Prepare 阶段更新网格；录制时不分配或修改网格。
public void Prepare(Vector2 size);
// 只录制准备好的资源；处理计数与缓存版本在实际提交后更新。
public void Record(EffectRenderContext context, Texture input, Material material, MaterialPropertyBlock properties);
public void Render(Texture input, Material material, Vector2 size);
public void Dispose();
```

### TextureEffectSurface.Statistics

源码：[TextureEffectSurface.cs](../Runtime/TextureEffectSurface.cs)。

```csharp
// GPU 表面及处理工作量的只读计数；颜色字节不包含驱动对齐或 Unity 对象开销。
public readonly struct Statistics
public int LiveSurfaces { get; }
public long LiveColorBytes { get; }
public long ProcessingPasses { get; }
public long ClearPasses { get; }
public long MeshRebuilds { get; }
```

### ToneFilter

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum ToneFilter { None = 0, Grayscale = 1, Sepia = 2, Negative = 3, Retro = 4, Posterize = 5 }
```

### TransitionFilter

源码：[EffectModes.cs](../Runtime/EffectModes.cs)。

```csharp
public enum TransitionFilter { None = 0, Fade = 1, Cutoff = 2, Dissolve = 3, Shiny = 4, Mask = 5, Melt = 6, Burn = 7, Pattern = 8 }
```

### TransitionInputPolicy

源码：[EffectController.cs](../Runtime/EffectController.cs)。

```csharp
public enum TransitionInputPolicy { Standard, DisableWhenComplete }
```

## URUI.Effect.Elements

### ParticlesElement

源码：[ParticlesElement.cs](../Runtime/Elements/ParticlesElement.cs)。

```csharp
// 声明式 Unity 粒子节点；HTML 的 src 由模块资源解析器映射，模拟仍由 Unity 驱动。
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

源码：[SurfaceEffect.cs](../Runtime/Rendering/SurfaceEffect.cs)。

```csharp
// 一个明确绑定的源/表面处理任务；布局和帧版本来自调用者，不扫描其他节点。
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

源码：[TextureColorEffect.cs](../Runtime/Rendering/TextureColorEffect.cs)。

```csharp
// 单图调色/纹理/扫光共用不可变程序，节点尺寸、进度和时钟保存在独立属性块中。
public static class TextureColorEffect
// 一个程序按不继承的资源键解析作者材质；项目只提供资源，不逐文档注册每份材质。
public static EffectDefinition DefineResolved(string name, Func<string, Material> resolver, string keyProperty, string rateProperty = null, string saturationProperty = null, string brightnessProperty = null);
public static EffectDefinition Define(string name, Material authored, string rateProperty = null, string saturationProperty = null, string brightnessProperty = null);
```

### WaveEffect

源码：[WaveEffect.cs](../Runtime/Rendering/WaveEffect.cs)。

```csharp
// 原图网格变形。共享归一化网格只在细分改变时取得，时间与尺寸全部走实例属性块。
public static class WaveEffect
public static EffectDefinition Define(string name = "ui-wave");
```
