# Styles & Animation

Uses `URUI.Elements`, `URUI.Styles`, and `URUI.Animation`. HTML/CSS owns static layout, state rules, keyframes, and curves; C# updates values, class lists, and playback handles. See [Public API Reference](REFERENCE.md) for full overloads.

## Inline Style Writes

| API | Parameters & Usage |
| --- | --- |
| `element.Style.Width/Height/... : Length` | Pixels, percent, `em`, `rem`, `auto`, or `Unset` |
| `Length.Pixels/Percent/Em/Rem(float)` | `Percent(50)` means `50%` (not `0.5`) |
| `Length.Auto` / `Length.Unset` | Auto keyword / remove inline override |
| `Style.Display/FlexDirection/AlignItems/...` | Strongly typed enum style properties |
| `Style.Opacity/FlexGrow/FlexShrink : float?` | Assign `null` to clear inline override |
| `Style.BackgroundColor : Color?` | Assign `null` to restore cascaded color |
| `Set(string property, string value)` | String entry point for full CSS expressions |
| `SetNumber/SetPixels/SetPercent/SetDensityPixels` | Typed numeric writes matching property syntax |
| `SetColor(string, Color)` | Writes typed color value |
| `SetKeyword(string, int)` | Writes registered property keyword index validated by native parser |
| `Remove(string)` | Removes inline declaration |
| `Get(string)` | Reads raw inline declaration (not cascaded computed value) |

```csharp
using URUI.Elements;
using UnityEngine;

public static class StyleExamples
{
    public static void Open(Element panel)
    {
        panel.Style.Display = DisplayStyle.Flex;
        panel.Style.FlexDirection = FlexDirection.Column;
        panel.Style.Width = Length.Percent(50);
        panel.Style.PaddingLeft = 12;
        panel.Style.Opacity = .8f;
        panel.Style.BackgroundColor = Color.blue;
        panel.Style.Width = Length.Unset;
    }
}
```

`ResolvedStyle.Get` reads cascaded values (relative units may remain unresolved strings). `ResolvedStyle.TryGetLength(name, out Length)` reads finite pixels, percent, `em`, `rem`, or `auto` with zero managed string allocation (unitless numbers resolve as pixels, and percentages are never converted to pixels automatically). Read `ResolvedStyle.Width`/`Height` for resolved border pixel dimensions, and `Layout`/`ContentRect`/`WorldBound` for positioned bounds (check `HasLayout` first).

## Custom Properties & Computed Snapshots

Register custom CSS variables with `@property` whenever you need typed C# reads, inheritance, or CSS/sequence animation:

```css
@property --progress { syntax: "<number>"; inherits: false; initial-value: 0; }
@property --accent { syntax: "<color>"; inherits: false; initial-value: white; }
.panel { --progress: 0.5; --accent: #4080ff; }
```

`document.Styles.GetComputed(element)` requires a valid element with completed layout and returns an immutable `ComputedStyleSnapshot` (reused when unchanged and never mutated in place across frames).

| Member | Return Value / Constraint |
| --- | --- |
| `GetNumber(string)` | Registered `<number>` value |
| `GetPixels(string)` | Resolved pixel length |
| `GetColor(string)` | Unity `Color` |
| `GetKeyword(string)` | Keyword token string |
| `GetImage(string)` | `ComputedImageValue` with `Kind`, `Url`, `CssText`, `SourceUrl` |
| `GetOrigin(string)` | `Initial` / `UserAgent` / `Author` / `Animation` / `Important` / `Transition` |
| `Contains(StylePropertyDefinition)` | Checks property name & registered syntax type |
| `Generation` / `Revision` | Document asset generation / snapshot value revision |
| `WithOverrides(dictionary, maximumOrigin)` | Creates a snapshot view with temporary overrides bounded by cascade origin without mutating DOM |
| `document.Styles.Subscribe(element, callback)` | Invokes callback on subsequent layout commits when values change; returns `IDisposable` |

```csharp
using System;
using URUI.Documents;
using URUI.Elements;
using URUI.Styles;

public static class ComputedStyleExamples
{
    public static IDisposable Observe(Document document, Element panel, Action<float> changed)
    {
        var definition = new StylePropertyDefinition("--progress", StyleValueType.Number);
        return document.Styles.Subscribe(panel, snapshot =>
        {
            if (snapshot.Contains(definition)) changed(snapshot.GetNumber("--progress"));
        });
    }
}
```

Type mismatches on typed getters throw—ensure `@property` declarations match. Relative image URLs retain their source stylesheet origin rather than resolving from C# scripts. Dispose style subscriptions cleanly on unbind or reload.

## Named CSS Animations

Author states and `@keyframes` in CSS, then trigger finite animation groups in C#:

```css
@keyframes panel-enter {
  from { opacity: 0; }
  to { opacity: 1; }
}
.panel.entering { animation: panel-enter 0.2s ease-out; }
```

```csharp
using System.Threading;
using System.Threading.Tasks;
using URUI.Animation;
using URUI.Documents;
using URUI.Elements;

public static class AnimationExamples
{
    public static async Task Enter(Document document, Element panel, CancellationToken token)
    {
        var targets = AnimationGroup.Named(new[] { new AnimationTarget(panel, "panel-enter") });
        using (var playback = document.Animations.Play(panel, "entering", targets, token))
        {
            await playback.Finished;
        }
    }
}
```

| API | Semantics |
| --- | --- |
| `AnimationTarget(Element, string name)` | Target node paired with CSS `animation-name` |
| `AnimationGroup.Named(IEnumerable<AnimationTarget>)` | Explicit finite animation targets |
| `AnimationGroup.FromAuthorTargets(Func<IReadOnlyList<Element>>)` | Resolves finite targets lazily after layout prep |
| `document.Animations.Play(root, activeClass, group, token)` | Starts independent `AnimationPlayback` on a valid mounted root |
| `playback.Identity` | Monotonic playback identity for re-entrancy guards |
| `playback.Finished : Task` | Completes once all finite tracks end naturally |
| `playback.IsPlaying` | True while running |
| `Cancel()` / `Dispose()` | Cancels playback cleanly without signaling natural completion |

Starting a new playback for the same root and `activeClass` supersedes prior playbacks cleanly. Hiding, destroying, unloading, or cancellation tokens never report false completion. Use finite animations for entrance completion signals rather than infinite loops or fixed timers. Supported CSS easing includes `linear`, `cubic-bezier(...)`, standard presets, and `linear(...)` piecewise stops per `HtmlCssSupport.json`.

## UI Clocks

`IUIClock` provides `double Now` and `float Delta`. `Surface.UIClock` defaults to `UnityUIClock.Unscaled` (`UnityUIClock.Scaled` follows `Time.timeScale`) and must only be replaced while zero documents are loaded on the `Surface`.

```csharp
using URUI.Animation;

public sealed class ManualUiClock : IUIClock
{
    public double Now { get; private set; }
    public float Delta { get; private set; }
    public void Advance(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0)
            throw new System.ArgumentOutOfRangeException(nameof(seconds));
        Delta = seconds;
        Now += seconds;
    }
}
```

Advance custom clocks monotonically on the Unity main thread. `document.Effects.BindClock(element, clock)` overrides the effect clock on an individual element via a disposable lease without changing CSS animations or video decoders.

## Built-in Behavior Controllers

Link `/urui/behaviors.css` (see [behaviors.css](../Runtime/Resources/URUI/behaviors.css)) in HTML; these controllers ship in Core (`URUI.Animation`) with zero `URUI.Effect` dependency:

| Type & Signature | CSS Prerequisite & Lifecycle |
| --- | --- |
| `ScrollMotion(Element viewport, Element content)` | `.scroll-motion` / `.scroll-motion-content` pair; supports inertia, elastic/clamped bounds, and `ScrollTo(Vector2)`; dispose unbinds & clears overrides |
| `StaggeredReveal(root, container, activeClass, prepareClass, revealedClass)` | Reads `--urui-stagger-edge/span/gap` (+ optional delay/boundary) in unitless seconds, measures during prepare state, applies `revealedClass` sequentially; dispose cleans up |
| `BezierMotion(path, rotation, source, target)` | Binds curve bend/rotation/progress variables between live source and target rects (`--urui-motion-*` + `SetNumber("--urui-motion-progress", t)`); `Begin()`, `Stop()`, `Dispose()` |
| `VisibilityClass(target, activation, className)` | Mirrors effective visibility of `activation` onto `className` on `target`; dispose cleans up |

Keep behavior controllers across frames rather than recreating them inside `Update`. See `Solstice` and `Runtime Controls` samples for ready-to-copy HTML/CSS markup.
