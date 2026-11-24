# URUI Core Public API Reference

Matches package version `0.8.0-preview.1`. Lists public types and directly declared public/protected members extracted from Runtime sources for inspecting parameters, return types, defaults, and inheritance; omits internal/private types and inherited base members (see Unity docs for base APIs).

Declarations omit implementations; see [Document & Element API](API.md) for usage ordering, resource rules, and complete MonoBehaviour components. Partial types are merged and protected constructors are called by `Document` factories.

## URUI.Animation

### AnimationController

Source: [AnimationController.cs](../Runtime/Animation/AnimationController.cs).

```csharp
// Launches named CSS animation group via activeClass; newest handle owns active slot.
public sealed class AnimationController
public AnimationPlayback Play(Element root, string activeClass, AnimationGroup animationNames, CancellationToken token = default);
// Switches element to completionClass once finite animation ends until cleared or replayed.
public AnimationPlayback Play(Element root, string activeClass, AnimationGroup animationNames, CancellationToken token, string completionClass);
```

### AnimationGroup

Source: [AnimationGroup.cs](../Runtime/Animation/AnimationGroup.cs).

```csharp
// Collection of finite animation targets resolved statically or lazily after layout.
public sealed class AnimationGroup
public static AnimationGroup Named(IEnumerable<AnimationTarget> targets);
// Awaits resolved targets starting finite CSS animations cleanly.
public static AnimationGroup FromAuthorTargets(Func<IReadOnlyList<Element>> targets);
```

### AnimationPlayback

Source: [AnimationPlayback.cs](../Runtime/Animation/AnimationPlayback.cs).

```csharp
// Scoped CSS animation playback handle ensuring canceled/stale handles never false-complete.
public sealed class AnimationPlayback : IDisposable
public long Identity { get; }
public Task Finished { get; }
public bool IsPlaying { get; }
public void Cancel();
public void Dispose();
```

### AnimationTarget

Source: [AnimationGroup.cs](../Runtime/Animation/AnimationGroup.cs).

```csharp
// Named CSS animation targeting a concrete element; completes once all property tracks finish.
public sealed class AnimationTarget
public Element Element { get; }
public string Name { get; }
public AnimationTarget(Element element, string name);
```

### BezierMotion

Source: [BezierMotion.cs](../Runtime/Animation/BezierMotion.cs).

```csharp
// Animates element along CSS-driven quadratic Bezier arc between live endpoint bounds.
public sealed class BezierMotion : IDisposable
public BezierMotion(Element path, Element rotation, Element source, Element target);
// Begins quadratic Bezier flight once endpoints have layout.
public void Begin();
// Stops flight and settles onto target endpoint.
public void Stop();
public void Dispose();
```

### IUIClock

Source: [IUIClock.cs](../Runtime/Animation/IUIClock.cs).

```csharp
// UI clock abstraction for animations and effects.
public interface IUIClock
double Now { get; }
float Delta { get; }
```

### StaggeredReveal

Source: [StaggeredReveal.cs](../Runtime/Animation/StaggeredReveal.cs).

```csharp
// Triggers sequential CSS item entrance across laid-out children.
public sealed class StaggeredReveal : IDisposable
// Reads stagger timing in unitless seconds via --urui-stagger-edge, --urui-stagger-span, and --urui-stagger-gap.
public StaggeredReveal(Element root, Element container, string activeClass, string prepareClass, string revealedClass);
// Stops stagger reveal and restores original style overrides/classes.
public void Dispose();
```

### UnityUIClock

Source: [UnityUIClock.cs](../Runtime/Animation/UnityUIClock.cs).

```csharp
// Unity scaled or unscaled UI clock; does not alter video decoder timing.
public sealed class UnityUIClock : IUIClock
public static IUIClock Scaled { get; }
public static IUIClock Unscaled { get; }
public double Now { get; }
public float Delta { get; }
```

### VisibilityClass

Source: [VisibilityClass.cs](../Runtime/Animation/VisibilityClass.cs).

```csharp
// Mirrors effective visibility onto CSS class state cleanly.
public sealed class VisibilityClass : IDisposable
public VisibilityClass(Element target, Element activation, string className);
public void Dispose();
```

## URUI.Controls

### BaseField<T>

Source: [BaseField.cs](../Runtime/Controls/BaseField.cs).

```csharp
// Shared form-field validation, silent update, and typed ChangeEvent dispatch.
public abstract class BaseField<T> : Element, INotifyValueChanged<T>
// Element factory construction context.
protected BaseField(ElementContext context);
// Parses native string into typed control value.
protected abstract T ParseValue(string text);
// Formats typed value as native string.
protected abstract string FormatValue(T value);
// Clamps/validates candidate field value.
protected virtual T NormalizeValue(T input);
// True for immediate live updates; false for delayed commit.
protected virtual bool CommitOnInput { get; }
// Syncs initial native state on initialization.
protected override void OnInitialize();
// Committed control value (unchanged during inline IME composition previews).
public T Value { get; set; }
// Updates native state and value silently.
public void SetValueWithoutNotify(T input);
// Subscribes value changes returning an IDisposable lease.
public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<T>> callback);
// Unregisters typed value change callback.
public void UnregisterValueChangedCallback(Action<ChangeEvent<T>> callback);
// Coalesces internal option updates into a single net value event.
protected void ChangeStructure(Action action);
// Commits native draft text (ignores invalid or non-finite inputs cleanly).
protected bool CommitInput();
// Internal hook called after model syncs (silent writes update visuals cleanly here).
protected virtual void OnValueSynchronized();
// Bridges native change notifications to ChangeEvent<T>.
protected override void OnEvent(EventBase evt);
```

### Button

Source: [Button.cs](../Runtime/Controls/Button.cs).

```csharp
// Push button activated by pointer click, Enter, or Space.
public class Button : Element
// Expires transient Space/Enter keyboard press state using Surface UIClock.
protected override void OnUpdate();
// Disabling cancels held keyboard activation cleanly without restoring later.
protected override void OnAttributeChanged(string name);
// Element factory construction context.
protected internal Button(ElementContext context);
// Activation callback for pointer/Space/Enter (ignored when disabled).
public event Action Clicked { add; remove; }
// Focusable by default.
protected override void OnInitialize();
// Fires on click and Space/Enter activation.
protected override void OnEvent(EventBase evt);
// Drops business delegates when element invalidates.
protected override void OnInvalidated();
```

### CheckableField

Source: [CheckableField.cs](../Runtime/Controls/CheckableField.cs).

```csharp
// Tracks Space/Enter activation state on checkable controls.
public abstract class CheckableField : BaseField<bool>
// Element factory construction context.
protected CheckableField(ElementContext context);
// Reads native checked boolean.
protected override bool ParseValue(string value);
// Sets native checked state.
protected override string FormatValue(bool value);
// Selects choice upon keyboard Space/Enter completion.
protected abstract void Activate();
// Ignores OS key-repeat triggers and clears held press on blur/cancel.
protected override void OnEvent(EventBase evt);
```

### Dropdown

Source: [Dropdown.cs](../Runtime/Controls/Dropdown.cs).

```csharp
// Native HTML `<select>` dropdown where Value tracks chosen `<option>` value.
public class Dropdown : BaseField<string>
// Keeps DropdownVisual synced across scripted and native selections.
protected override void OnValueSynchronized();
// Keeps standalone trigger enabled state synced with Dropdown.
protected override void OnAttributeChanged(string name);
// Releases authored skin bindings upon element invalidation.
protected override void OnInvalidated();
// Element factory construction context.
protected internal Dropdown(ElementContext context);
// Reads native select value.
protected override string ParseValue(string value);
// Retains selected choice option value string.
protected override string FormatValue(string value);
// Ordered choices list (writing rebuilds internal option nodes cleanly).
public IReadOnlyList<string> Choices { get; set; }
// Selected option index (-1 when unmatched).
public int SelectedIndex { get; set; }
```

### DropdownVisual

Source: [DropdownVisual.cs](../Runtime/Controls/DropdownVisual.cs).

```csharp
// Connects native `<select>` state onto custom authored button/popup templates.
public sealed class DropdownVisual : INotifyValueChanged<string>, IDisposable
// Standard `<select>` dropdown.
public Dropdown Control { get; }
// True while popup dropdown menu is open.
public bool IsOpen { get; }
// Binds authored trigger/label/popup/prototype nodes in one document; disposes generated option instances on cleanup.
public DropdownVisual(Dropdown control, Button trigger, Element label, Element popup, Button optionPrototype);
// Sets Dropdown value cleanly and notifies listeners.
public string Value { get; set; }
// Sets control state and authored skin silently.
public void SetValueWithoutNotify(string value);
// Re-synchronizes choices after mutating option child elements directly.
public void Refresh();
// Disposes generated popup rows and cleans listeners while retaining authored template.
public void Dispose();
```

### FloatField

Source: [FloatField.cs](../Runtime/Controls/FloatField.cs).

```csharp
// Typed floating-point field.
public class FloatField : NumericField<float>
// Element factory construction context.
protected internal FloatField(ElementContext context);
// Reads latest native control value.
protected override float ParseValue(string value);
// Parses/formats using invariant native representation.
protected override string FormatValue(float value);
// Rejects NaN and Infinity.
protected override float NormalizeValue(float value);
// Converts value into clamped step units.
protected override double ToNumber(float value);
// Saturates within finite float limits.
protected override float FromNumber(double value);
```

### Foldout

Source: [Foldout.cs](../Runtime/Controls/Foldout.cs).

```csharp
// Collapsible toggle section container.
public class Foldout : Element, INotifyValueChanged<bool>
// Element factory construction context.
protected internal Foldout(ElementContext context);
// Child content lives inside the collapsible container.
public override Element ContentContainer { get; }
// Foldout header caption text.
public override string Text { get; set; }
// True when Foldout is expanded cleanly without destroying children.
public bool Value { get; set; }
// Sets expansion silently.
public void SetValueWithoutNotify(bool value);
// Registers Foldout expanded listener.
public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<bool>> callback);
// Unregisters foldout expansion listener.
public void UnregisterValueChangedCallback(Action<ChangeEvent<bool>> callback);
// Reuses authored toggle/label/content nodes or creates defaults on empty elements.
protected override void OnInitialize();
// Shares backing state with typed Value without firing user change notifications on attribute edits.
protected override void OnAttributeChanged(string name);
// Expands/collapses via Left/Right arrows while header is focused.
protected override void OnEvent(EventBase evt);
```

### Image

Source: [Image.cs](../Runtime/Controls/Image.cs).

```csharp
// Image display node bound to Document images or resolved leases.
public class Image : Element
// Element factory construction context.
protected internal Image(ElementContext context);
// Image URI string matching ImageAsset.Source.
public string Source { get; set; }
// Chooses named SpriteRegion inside bound atlas (clear to restore full image).
public string SpriteName { get; set; }
// Scales intrinsic image sizing cleanly by Surface density.
public bool DensityScaledNaturalSize { get; set; }
// Standard CSS object-fit scaling behavior.
public ImageFit Fit { get; set; }
// Image fill pattern controlled cleanly via CSS --urui-image-fill-mode.
public ImageFillMode FillMode { get; set; }
// Controls 9-slice center patch rendering (ignored on un-sliced images).
public bool FillCenter { get; set; }
// Fill origin index (`0..1` linear, `0..3` radial).
public int FillOrigin { get; set; }
// Clockwise radial fill flag.
public bool FillClockwise { get; set; }
// Fill ratio clamped to [0, 1]; 0 emits zero geometry.
public float FillAmount { get; set; }
```

### ImageFillMode

Source: [Image.cs](../Runtime/Controls/Image.cs).

```csharp
// Standard image fill mode alongside FillOrigin and FillClockwise.
public enum ImageFillMode { None, Horizontal, Vertical, Radial90, Radial180, Radial360 }
```

### ImageFit

Source: [Image.cs](../Runtime/Controls/Image.cs).

```csharp
// Image scaling/cropping fit inside authored box without changing hit bounds.
public enum ImageFit { Fill, Contain, Cover }
```

### INotifyValueChanged<T>

Source: [BaseField.cs](../Runtime/Controls/BaseField.cs).

```csharp
// Unified value-field interface (main-thread only).
public interface INotifyValueChanged<T>
// Committed field value; unchanged writes suppress ChangeEvent.
T Value { get; set; }
// Updates value and visuals without emitting ChangeEvent.
void SetValueWithoutNotify(T value);
```

### IntegerField

Source: [IntegerField.cs](../Runtime/Controls/IntegerField.cs).

```csharp
// Typed integer field.
public class IntegerField : NumericField<int>
// Element factory construction context.
protected internal IntegerField(ElementContext context);
// Reads latest native control value.
protected override int ParseValue(string value);
// Parses/formats using invariant native representation.
protected override string FormatValue(int value);
// Exact int-to-double stepping precision.
protected override double ToNumber(int value);
// Saturates cleanly at Int32 min/max bounds.
protected override int FromNumber(double value);
```

### IntegerSlider

Source: [IntegerSlider.cs](../Runtime/Controls/IntegerSlider.cs).

```csharp
// Integer slider supporting pointer drag capture and arrow stepping.
public class IntegerSlider : RangeField<int>
// Element factory construction context.
protected internal IntegerSlider(ElementContext context);
// Reads latest native scalar value.
protected override int ParseValue(string value);
// Writes invariant unitless CSS number.
protected override string FormatValue(int value);
// Maps value to slider range float.
protected override float ToNumber(int value);
// Maps scalar back to typed value.
protected override int FromNumber(float value);
```

### IntegerSliderVisual

Source: [IntegerSliderVisual.cs](../Runtime/Controls/IntegerSliderVisual.cs).

```csharp
// Authored fill/thumb skin coordinator for IntegerSlider.
public sealed class IntegerSliderVisual : INotifyValueChanged<int>, IDisposable
// Integer ranged slider field.
public IntegerSlider Control { get; }
// Binds authored fill and thumb elements inside the same document.
public IntegerSliderVisual(IntegerSlider control, Element fill, Element thumb);
// Updates range/value/visuals while firing normal ChangeEvent.
public int Value { get; set; }
// Updates value and visuals without firing change notifications.
public void SetValueWithoutNotify(int value);
// Releases skin hooks safely keeping authored nodes.
public void Dispose();
```

### Label

Source: [Label.cs](../Runtime/Controls/Label.cs).

```csharp
// Plain text `<label>` node (with optional `for` input activation).
public class Label : Element
// Element factory construction context.
protected internal Label(ElementContext context);
// Associated input element id; clicking this label forwards activation natively.
public string For { get; set; }
```

### ListView

Source: [ListView.cs](../Runtime/Controls/ListView.cs).

```csharp
// Fixed-row-height virtualized list pooling visible + overscan rows inside this document.
public class ListView : ScrollView
// Element factory construction context.
protected internal ListView(ElementContext context);
// Backing IList data source (assigning rebuilds rows; call RefreshItems on in-place edits).
public IList ItemsSource { get; set; }
// Row factory returning fresh unmounted same-document elements for pooling.
public Func<Element> MakeItem { get; set; }
// Binds data item index onto pooled row element (pair cleanup with UnbindItem).
public Action<Element, int> BindItem { get; set; }
// Unbinds row before recycling or retirement (dispose user subscriptions only).
public Action<Element, int> UnbindItem { get; set; }
// Fixed row height in CSS pixels (finite > 0).
public float FixedItemHeight { get; set; }
// Number of currently pooled row instances.
public int RealizedItemCount { get; }
// Selected item index (-1 clears selection).
public int SelectedIndex { get; set; }
// Fired on selection index updates or shrinkage invalidation.
public event Action<int> SelectionChanged;
// Fired when Enter activates the selected item in a focused ListView.
public event Action<int> ItemChosen;
// Rebinds currently realized rows in place.
public void RefreshItems();
// Destroys pooled rows and invokes MakeItem cleanly.
public void Rebuild();
// Scrolls specified list index into visible viewport.
public void ScrollToItem(int index);
// Updates realized visible rows cleanly on scroll or resize.
protected override void OnUpdate();
// Detaches pooled row bindings safely (do not access DOM if element invalidated).
protected override void OnInvalidated();
// Navigates list items on focus without intercepting edit keys targeting row inputs.
protected override void OnEvent(EventBase evt);
```

### NumericField<T>

Source: [NumericField.cs](../Runtime/Controls/NumericField.cs).

```csharp
// Base numeric input adding keyboard stepping cleanly atop TextInputField.
public abstract class NumericField<T> : TextInputField<T>
// Element factory construction context.
protected NumericField(ElementContext context);
// Converts slider values cleanly across integer/float precision boundaries.
protected abstract double ToNumber(T value);
// Clamps float value safely into numeric target range.
protected abstract T FromNumber(double value);
// Preserves initial authored step anchor when min attribute is absent.
protected override void OnInitialize();
// Authored value attribute writes reset HTML step validation base.
protected override void OnAttributeChanged(string name);
// Up/Down arrow key step (must be finite positive; invalid author step or step=any defaults to 1).
public T Step { get; set; }
// Handles up/down arrow stepping while letting readonly selections and copy pass.
protected override void OnEvent(EventBase evt);
```

### ProgressBar

Source: [ProgressBar.cs](../Runtime/Controls/ProgressBar.cs).

```csharp
// Configurable 0..100 progress display preserving authored internal children.
public class ProgressBar : Element, INotifyValueChanged<float>
// Element factory construction context.
protected internal ProgressBar(ElementContext context);
// Exposed slot where user child content attaches.
public override Element ContentContainer { get; }
// Current progress clamped to [LowValue, HighValue].
public float Value { get; set; }
// Sets ProgressBar caption text.
public override string Text { get; set; }
// Inclusive minimum value (<= HighValue).
public float LowValue { get; set; }
// Inclusive maximum value (>= LowValue).
public float HighValue { get; set; }
// Registers ProgressBar value listener.
public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<float>> callback);
// Unregisters progress change callback.
public void UnregisterValueChangedCallback(Action<ChangeEvent<float>> callback);
// Syncs progress visuals silently.
public void SetValueWithoutNotify(float value);
// Reuses authored fill/label/content children or generates default child elements when empty.
protected override void OnInitialize();
// Syncs range/value/title silently after attribute updates.
protected override void OnAttributeChanged(string name);
```

### ProgressIndicator

Source: [ProgressIndicator.cs](../Runtime/Controls/ProgressIndicator.cs).

```csharp
// Drives authored progress fill nodes without generating custom UI markup.
public sealed class ProgressIndicator : INotifyValueChanged<float>
public ProgressIndicator(Element host, Element fill, float minimum = 0, float maximum = 1);
public float Value { get; set; }
public void SetValueWithoutNotify(float value);
```

### RadioButton

Source: [RadioButton.cs](../Runtime/Controls/RadioButton.cs).

```csharp
// Typed mutually exclusive radio button.
public class RadioButton : CheckableField
// Element factory construction context.
protected internal RadioButton(ElementContext context);
// Activating a RadioButton selects without unchecking itself.
protected override void Activate();
// Radio mutual-exclusion group name scoped per document/form.
public string GroupName { get; set; }
// Arrow keys cycle within radio group skipping disabled/hidden items.
protected override void OnEvent(EventBase evt);
```

### RangeField<T>

Source: [RangeField.cs](../Runtime/Controls/RangeField.cs).

```csharp
// Ranged slider supporting drag capture and keyboard step navigation.
public abstract class RangeField<T> : BaseField<T>
// Refreshes authored skin visuals after range/direction mutations.
protected override void OnAttributeChanged(string name);
// Syncs authored control skin state across edits.
protected override void OnValueSynchronized();
// Drops helper refs once node expires.
protected override void OnInvalidated();
// Element factory construction context.
protected RangeField(ElementContext context);
// Converts typed value to float.
protected abstract float ToNumber(T value);
// Converts float slider position back to typed control value.
protected abstract T FromNumber(float value);
// Physical layout axis guiding pointer drag and keyboard stepping.
public SliderDirection Direction { get; set; }
// Reverses physical slider orientation without altering committed numeric value.
public bool Inverted { get; set; }
// Maps logical slider bounds against physical layout direction and Inverted flag.
protected float MapNativeValue(float value);
// Inclusive minimum value (<= HighValue).
public float LowValue { get; set; }
// Inclusive maximum value (>= LowValue).
public float HighValue { get; set; }
// Arrow key step size (must be finite > 0).
public float Step { get; set; }
// Native input validation mapping inverted sliders cleanly.
protected override T NormalizeValue(T input);
// Tracks active pointer per slider for independent multi-touch dragging.
protected override void OnEvent(EventBase evt);
```

### ReferenceCanvas

Source: [ReferenceCanvas.cs](../Runtime/Controls/ReferenceCanvas.cs).

```csharp
// Applies responsive reference-canvas scaling onto an authored root element.
public sealed class ReferenceCanvas : IDisposable
public float Scale { get; private set; }
public Vector2 LogicalSize { get; private set; }
public ReferenceCanvas(Element root, Vector2 referenceSize, float match, Element safeContent = null, SafeAreaPolicy safeAreaPolicy = SafeAreaPolicy.Physical);
// Input matching physical pixel transform cleanly across scaled controls.
public void Apply(Rect display, Rect safe, float maximumScale = float.PositiveInfinity);
public void Dispose();
```

### RowContentSize

Source: [RowContentSize.cs](../Runtime/Controls/RowContentSize.cs).

```csharp
// Syncs horizontal content bounds cleanly across variable authored row items.
public sealed class RowContentSize : IDisposable
// Tracks total width of horizontal row items automatically.
public RowContentSize(Element content);
// Stops measuring and restores prior inline sizing.
public void Dispose();
```

### SafeAreaPolicy

Source: [ReferenceCanvas.cs](../Runtime/Controls/ReferenceCanvas.cs).

```csharp
// Device safe-area inset policy (`Physical` asymmetric vs `SymmetricHorizontal`).
public enum SafeAreaPolicy { Physical, SymmetricHorizontal }
```

### ScrollbarVisual

Source: [ScrollbarVisual.cs](../Runtime/Controls/ScrollbarVisual.cs).

```csharp
// Coordinates custom scrollbar visuals with an overflow scroll container.
public sealed class ScrollbarVisual : IDisposable
// All three elements must belong to the same document; thumb must be a direct child of track outside viewport.
public ScrollbarVisual(Element viewport, Element track, Element thumb);
// Syncs scrollbar position, extent, minimum thumb size, and visibility cleanly.
public void Refresh();
// Unbinds drag/layout hooks safely keeping authored elements.
public void Dispose();
```

### ScrollMotion

Source: [ScrollMotion.cs](../Runtime/Controls/ScrollMotion.cs).

```csharp
// Pointer/touch inertial scrolling and bounce bounds for CSS overflow containers.
public sealed class ScrollMotion : IDisposable
// CSS drives config while content must remain a direct child of viewport.
public ScrollMotion(Element viewport, Element content);
// Scrolls directly to pixel offset, canceling active drag/inertia cleanly.
public void ScrollTo(Vector2 offset);
// Cleans motion leases and restores CSS overrides.
public void Dispose();
```

### ScrollView

Source: [ScrollView.cs](../Runtime/Controls/ScrollView.cs).

```csharp
// Scrollable container supporting wheel and touch/pointer dragging.
public class ScrollView : Element
// Element factory construction context.
protected internal ScrollView(ElementContext context);
// Routes child additions into scrollable ContentContainer.
public override Element ContentContainer { get; }
// Uses authored content slot or generates default container on empty elements.
protected override void OnInitialize();
// Scrolls descendant inside ContentContainer into view cleanly when off-screen.
public void ScrollTo(Element child);
// Sizes content to viewport width post-layout cleanly.
protected override void OnUpdate();
// Captures drag past threshold and cancels accidental tap click on release.
protected override void OnEvent(EventBase evt);
```

### Slider

Source: [Slider.cs](../Runtime/Controls/Slider.cs).

```csharp
// Float slider supporting pointer drag capture and arrow stepping.
public class Slider : RangeField<float>
// Element factory construction context.
protected internal Slider(ElementContext context);
// Reads latest native scalar value.
protected override float ParseValue(string value);
// Writes invariant unitless CSS number.
protected override string FormatValue(float value);
// Maps value to slider range float.
protected override float ToNumber(float value);
// Maps scalar back to typed value.
protected override float FromNumber(float value);
```

### SliderDirection

Source: [RangeField.cs](../Runtime/Controls/RangeField.cs).

```csharp
// Physical slider orientation.
public enum SliderDirection { Horizontal, Vertical }
```

### SliderVisual

Source: [SliderVisual.cs](../Runtime/Controls/SliderVisual.cs).

```csharp
// Drives authored fill/thumb visuals smoothly in horizontal or vertical slider skins.
public sealed class SliderVisual : INotifyValueChanged<float>, IDisposable
// Floating-point ranged slider field.
public Slider Control { get; }
// Binds authored fill and thumb elements inside the same document.
public SliderVisual(Slider control, Element fill, Element thumb);
// Updates native state and visuals with normal notification.
public float Value { get; set; }
// Updates value and visuals without firing change notifications.
public void SetValueWithoutNotify(float value);
// Releases skin hooks safely keeping authored nodes.
public void Dispose();
```

### TextField

Source: [TextField.cs](../Runtime/Controls/TextField.cs).

```csharp
// Text input supporting Unicode selection, clipboard shortcuts, and IME composition.
public class TextField : TextInputField<string>
// Element factory construction context.
protected internal TextField(ElementContext context);
// Disallows null strings and embedded NUL bytes.
protected override string NormalizeValue(string value);
// Reads native string text.
protected override string ParseValue(string value);
// Retains original text string value.
protected override string FormatValue(string value);
// Synchronizes button label text cleanly without altering children.
public override string Text { get; set; }
// Enables multiline editing (note HTML <input> stays single-line; use <textarea> or C# Create<TextField>()).
public bool Multiline { get; set; }
// Preserves Enter newlines in multiline mode; submits when single-line.
protected override bool SubmitOnEnter { get; }
// Suppresses newlines in single-line mode while delegating changes to base field.
protected override void OnEvent(EventBase evt);
```

### TextFit

Source: [TextFit.cs](../Runtime/Controls/TextFit.cs).

```csharp
// Fits text inside host box between authored CSS min/max font sizes cleanly.
public sealed class TextFit : IDisposable
// Outer host element keeping authored dimensions and alignment.
public Element Host { get; }
// Direct child text wrapper maintaining normal layout flow and natural wrapping height.
public Element Content { get; }
// True when text fit cleanly at or above authored minimum font size.
public bool Fits { get; private set; }
// Starts layout tracking and takes font-size override lease cleanly until Dispose.
public TextFit(Element host, Element content);
// Marks fit pending immediately when updated/recycled so stale glyph scale never flashes.
public void Refresh();
// Stops tracking and restores font-size override and pending classes cleanly.
public void Dispose();
```

### TextInputField<T>

Source: [TextInputField.cs](../Runtime/Controls/TextInputField.cs).

```csharp
// Shared text/numeric input base handling selection, readonly, delayed commit, and Submit events.
public abstract class TextInputField<T> : BaseField<T>, ITextInputControl
// Element factory construction context.
protected TextInputField(ElementContext context);
// Prevents interactive user editing while keeping focus, selection, copy, and scripted assignment.
public bool IsReadOnly { get; set; }
// Delays value commit until Enter, blur, or CommitValue.
public bool IsDelayed { get; set; }
// Commits edits live unless IsDelayed is set.
protected override bool CommitOnInput { get; }
// Single-line fields submit on Enter; multiline inputs may override.
protected virtual bool SubmitOnEnter { get; }
// Fired on Enter key submission (not fired again on blur).
public event Action<T> Submitted { add; remove; }
// Live editable string text representation.
public override string Text { get; set; }
// Empty field placeholder string.
public string Placeholder { get; set; }
// Max Unicode character limit (0 = unlimited).
public int MaxLength { get; set; }
// Caret blink rate in Hz (0 keeps solid caret; also configurable via CSS --urui-caret-blink-rate).
public float CaretBlinkRate { get; set; }
// Caret thickness in reference pixels (or CSS --urui-caret-width).
public int CaretWidth { get; set; }
// Selects Unicode scalar range cleanly while focusing the field.
public void SelectRange(int start, int end);
// Active Unicode character selection range (requires focus).
public Vector2Int Selection { get; }
// Validates/commits draft text cleanly or reverts invalid text returning false.
public bool CommitValue();
// Replaces active draft text natively without forcing delayed commit.
protected void ReplaceEditingText(string text);
// Blocks interactive edits in readonly mode while preserving Enter/blur commit rules.
protected override void OnEvent(EventBase evt);
// Cleans up submit hook.
protected override void OnInvalidated();
```

### TextLayout

Source: [TextLayout.cs](../Runtime/Controls/TextLayout.cs).

```csharp
// Positions a naturally wrapped child text node within host bounds using 9-anchor alignments.
public sealed class TextLayout
public TextLayout(Element host, Element content, TextAnchor alignment = TextAnchor.UpperLeft);
public Element Host { get; }
public Element Content { get; }
public TextAnchor Alignment { get; set; }
```

### Toggle

Source: [Toggle.cs](../Runtime/Controls/Toggle.cs).

```csharp
// Typed boolean toggle/checkbox control.
public class Toggle : CheckableField
// Element factory construction context.
protected internal Toggle(ElementContext context);
// Toggles boolean value upon full keyboard press-and-release.
protected override void Activate();
```

### ToggleVisual

Source: [ToggleVisual.cs](../Runtime/Controls/ToggleVisual.cs).

```csharp
// Projects native Toggle checked state onto authored element via .checked class.
public sealed class ToggleVisual : INotifyValueChanged<bool>, IDisposable
// Standard checkbox/toggle field.
public Toggle Control { get; }
// Mirrors checked state cleanly via `.checked` class.
public ToggleVisual(Toggle control, Element host);
// Updates value and authored skin state with normal notification.
public bool Value { get; set; }
// Updates state and visuals cleanly without notifying.
public void SetValueWithoutNotify(bool value);
// Unbinds helper safely without destroying authored nodes.
public void Dispose();
```

## URUI.Documents

### Document

Source: [Document.Elements.cs](../Runtime/Documents/Document.Elements.cs), [Document.Styles.cs](../Runtime/Documents/Document.Styles.cs), [Document.Content.cs](../Runtime/Documents/Document.Content.cs), [Document.Templates.cs](../Runtime/Documents/Document.Templates.cs), [Document.Lifecycle.cs](../Runtime/Documents/Document.Lifecycle.cs), [Document.Configuration.cs](../Runtime/Documents/Document.Configuration.cs), [Document.Animations.cs](../Runtime/Documents/Document.Animations.cs), [Document.RenderCommandBuffer.cs](../Runtime/Documents/Document.RenderCommandBuffer.cs), [Document.ThreadSafety.cs](../Runtime/Documents/Document.ThreadSafety.cs), [Document.Loading.cs](../Runtime/Documents/Document.Loading.cs), [Document.Input.cs](../Runtime/Documents/Document.Input.cs), [Document.Effects.cs](../Runtime/Documents/Document.Effects.cs), [Document.Painting.cs](../Runtime/Documents/Document.Painting.cs), [Document.cs](../Runtime/Documents/Document.cs), [Document.Layout.cs](../Runtime/Documents/Document.Layout.cs), [Document.Events.cs](../Runtime/Documents/Document.Events.cs).

```csharp
// Loads an HTML document in Unity scenes associated with one shared Surface; unloads GPU resources when disabled.
public sealed partial class Document : MonoBehaviour
// Registers custom tag factory before loading markup using `<tag>`.
public void RegisterElementType<T>(string tag, Func<ElementContext, T> factory) where T : Element;
// Root `<html>` element (null before load; main-thread only).
public Element DocumentElement { get; }
// Root `<body>` element (null before load).
public Element Body { get; }
// Instantiates a detached document-owned typed element.
public T Create<T>() where T : Element;
// Creates a detached node by HTML tag name owned by this Document.
public Element CreateElement(string tag = "div");
// Queries first descendant matching typed criteria.
public T Q<T>(string name = null, string className = null) where T : Element;
// Builds typed query on loaded document.
public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element;
// Returns first match in subtree (null when absent).
public Element Q(string name = null, string className = null);
// Untyped depth-first element snapshot across root and descendants.
public IReadOnlyList<Element> Query(string name = null, string className = null);
// Typed computed style snapshots and subscriptions for this document.
public DocumentStyles Styles { get; }
// Top-level document host presentation layer, order, visibility, and modal blocking.
public void SetPresentation(int layer, int order, bool visible, bool modal);
// Releases cached URI resolution cleanly so future draws re-query ImageResolver.
public void ReleaseImage(string source);
// Appends stylesheet asset (or prepends before author rules when beforeAuthor = true).
public void AddStyleSheet(TextAsset asset, string sourceUrl, bool beforeAuthor = false);
// Shared scene Surface required prior to loading.
public URUI.Hosting.Surface Surface { get; set; }
// Canonical document base URL retained across disable/re-enable reloads.
public string SourceUrl { get; set; }
// Synchronously preloads an image into the document cache and reads dimensions without DOM edits.
public bool TryLoadImage(string source, out Vector2Int dimensions);
// External text/binary assets (CSS, fonts, fragments) for next load; arrays clone while reading native TextAsset bytes zero-copy.
public TextResource[] TextResources { get; set; }
// Main-thread fallback image lookup invoked on URI cache misses.
public Func<string, ImageLease> ImageResolver { get; set; }
// Optional serialized HTML TextAsset loaded on enable.
public TextAsset Source { get; set; }
// Editor-precompiled HTML/CSS/template bundle.
public CompiledDocumentAsset CompiledSource { get; set; }
// Static borrowed image mappings for next load; Texture2D assets do not require CPU Read/Write.
public ImageAsset[] Images { get; set; }
public AnimationController Animations { get; }
// Recorded command buffer when ready (use SubmitRendering instead whenever retirable images, layers, or Linear blending apply).
public CommandBuffer RenderCommandBuffer { get; }
// Enqueues shared UI draws and GPU completion fences into the active SRP ScriptableRenderContext.
public bool SubmitRendering(ScriptableRenderContext context);
// Records shared UI draws, layer composites, and GPU fences against a pipeline RenderTargetIdentifier.
public bool SubmitRendering(ScriptableRenderContext context, RenderTargetIdentifier target, UnityEngine.Rect viewport);
// Loads full HTML from a caller-owned TextAsset, preserving existing document on error.
public void Load(TextAsset asset, string sourceUrl);
// Instantiates compiled document plan, retaining prior page on failure.
public void Load(CompiledDocumentAsset asset);
// Strictly parses and loads complete HTML; invalidates prior nodes/subscriptions only upon success.
public void LoadHtml(string html, string sourceUrl);
// Unloads this document cleanly without disrupting sibling documents on the shared Surface.
public void Unload();
// Requests page keyboard focus cleanly (returns false if hidden or modal-blocked).
public bool Focus();
// Currently focused element, or null.
public Element FocusedElement { get; }
// Performs hit test at document coordinates returning top leaf.
public Element Pick(Vector2 position);
public EffectRegistry Effects { get; }
// Retires painter resources behind host GPU fences cleanly while preserving registration.
public void RetirePainterResources(IDisposable resource);
// Attaches custom painter callback invoked by `--urui-decorator:shader(...)` or `--urui-mask-image:shader(...)`.
public IDisposable AttachPainter(Element element, string shader, Action<ElementPaintContext> paint, Action release = null);
// Geometry context valid only during synchronous Prepare window.
public IDisposable AttachGeometryProvider(Element element, string shader, Action<ExternalGeometryContext> prepare, Action<int> onRecorded = null, Action release = null, RenderParameterLayout parameters = null);
// True while DOM is loaded and safe to query.
public bool IsLoaded { get; }
// Fired after each load; query elements and wire handlers safely here.
public event Action<Document> Loaded;
// Fired after document unloads and prior element handles/subscriptions invalidate.
public event Action<Document> Unloaded;
// Fired after native layout commits across active elements.
public event Action<Document> LayoutUpdated { add; remove; }
// Synchronizes layout and spatial transforms fast without emitting general element draw commands.
public void SynchronizeGeometry();
// Commits document layout while eagerly preparing painters inside the requested attached subtree.
public void SynchronizeGeometry(Element root);
// Synchronizes styles, layout, and transforms immediately without polling input or emitting document LayoutUpdated.
public void SynchronizeLayout();
public IDisposable BeginUpdate();
public void RequestGeometry(Element root);
public bool FlushPendingGeometry();
public CompiledTemplate GetCompiledTemplate(string uri);
public CompiledQuerySnapshot CreateCompiledQuerySnapshot(Element root);
// Runs delegate right after active native dispatch exits (immediately if idle).
public void RunAfterEvent(Action action);
```

### CompiledQuerySnapshot

Source: [Document.CompiledQueries.cs](../Runtime/Documents/Document.CompiledQueries.cs).

```csharp
public sealed class CompiledQuerySnapshot
public bool TryQuery(string selector, out CompiledQueryResult result);
public readonly struct CompiledQueryResult
public int Count { get; }
public Enumerator GetEnumerator();
public struct Enumerator
public Element Current { get; }
public bool MoveNext();
```

## URUI.Elements

### Align

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS Align enum override (Unset clears).
public enum Align { Unset, Auto, FlexStart, FlexEnd, Center, Stretch, Baseline }
```

### DisplayStyle

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS DisplayStyle enum override (Unset clears).
public enum DisplayStyle { Unset, Block, Inline, InlineBlock, Flex, InlineFlex, None }
```

### Element

Source: [Element.Events.cs](../Runtime/Elements/Element.Events.cs), [Element.cs](../Runtime/Elements/Element.cs), [Element.Hierarchy.cs](../Runtime/Elements/Element.Hierarchy.cs), [Element.State.cs](../Runtime/Elements/Element.State.cs), [Element.Geometry.cs](../Runtime/Elements/Element.Geometry.cs).

```csharp
public partial class Element
// Registers synchronous event listener idempotently per phase; dispose to unsubscribe.
public IDisposable RegisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where TEvent : EventBase;
// Removes specific callback handler safely even after invalidation.
public void UnregisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where TEvent : EventBase;
// Dispatches a synthetic event synchronously targeting this element.
public void SendEvent(EventBase value);
// Captures pointer so moves/releases outside bounds continue routing here.
public void CapturePointer(int pointerId);
// Releases pointer capture held by this element.
public void ReleasePointer(int pointerId);
// True if pointerId is currently captured here.
public bool HasPointerCapture(int pointerId);
// Protected internal event hook invoked after standard listeners.
protected virtual void OnEvent(EventBase value);
// HTML id attribute; duplicates allowed across nodes (Q matches first, Query matches all).
public string Name { get; set; }
// Native element tag name (or #text for text nodes).
public string TagName { get; }
// Plain text accessor (replaces children on write; throws on structured children when reading).
public virtual string Text { get; set; }
// Inline style mutation API backed by native engine.
public ElementStyle Style { get; }
// True while node and owning document generation stay alive.
public bool IsValid { get; }
// True while attached under document root (false if detached).
public bool IsAttached { get; }
// Case-sensitive DOM attribute getter (null if absent, empty string if present without value).
public string GetAttribute(string name);
// True if DOM attribute is present (including empty value).
public bool HasAttribute(string name);
// Sets DOM attribute (`id` syncs with `Name`; `class` syncs with class helpers).
public void SetAttribute(string name, string value);
// Removes DOM attribute safely.
public void RemoveAttribute(string name);
// Re-syncs control visuals after DOM attribute mutations.
protected virtual void OnAttributeChanged(string name);
// Strictly parses and atomically replaces HTML fragment content, preserving prior subtree on failure.
public void SetInnerHtml(string html, string sourceUrl);
public void SetInnerHtml(CompiledTemplate template);
// Serializes child markup respecting void tags and explicit closing tags.
public string GetInnerHtml();
// Applies finite 2D translation/scale transform.
public void SetTransform(Vector2 translation, Vector2 scale);
// Updates runtime transform preserving authored transform-origin units and cascade origin.
public void SetTransform(Vector3 translation, Quaternion rotation, Vector3 scale);
// Updates runtime 2D/3D transform and percentage origin natively without generating CSS strings.
public void SetTransform(Vector3 translation, Quaternion rotation, Vector3 scale, Vector2 originPercent);
// Case-sensitive check for a single CSS class token.
public bool ClassListContains(string className);
// Adds single CSS class token idempotently.
public void AddToClassList(string className);
// Removes single CSS class token idempotently.
public void RemoveFromClassList(string className);
// Toggles a single CSS class token.
public void EnableInClassList(string className, bool enabled);
// Instantiates template subtree as unmounted nodes owned by the same Document.
public Element Clone();
// Raw DOM parent (null for roots or detached nodes).
public Element Parent { get; }
// Direct DOM child count excluding internal decorator nodes.
public int ChildCount { get; }
// Indexed direct child access within [0, ChildCount).
public Element this[int index] { get; }
// Immutable ordered snapshot of direct children.
public IReadOnlyList<Element> Children();
// Finds first matching element by exact id/class filters (null acts as wildcard).
public Element Q(string name = null, string className = null);
// Depth-first ordered snapshot matching id/class literally.
public IReadOnlyList<Element> Query(string name = null, string className = null);
// Appends a same-document element or detached subtree cleanly.
public void Add(Element child);
// Inserts child at index (`ChildCount` appends).
public void Insert(int index, Element child);
// Removes direct child while preserving identity and handlers.
public void Remove(Element child);
// Detaches from parent preserving identity and listeners (invalid on roots).
public void RemoveFromHierarchy();
// Detaches direct children cleanly without destroying identities or listeners.
public void Clear();
// Permanently destroys node subtree and invalidates wrappers (disallowed on document roots).
public void Destroy();
// Protected constructor invoked solely by Document element factories.
protected internal Element(ElementContext context);
// Owning Document.
public Document Document { get; }
// Container hosting child content (overridden by composite controls).
public virtual Element ContentContainer { get; }
// Accesses raw DOM children directly bypassing ContentContainer redirection.
public ElementHierarchy Hierarchy { get; }
// Caller-managed opaque user object.
public object UserData { get; set; }
// Local enabled flag on this element.
public bool EnabledSelf { get; }
// True when element and all ancestors are enabled.
public bool EnabledInHierarchy { get; }
// Sets enabled flag cleanly canceling interactions when disabled.
public void SetEnabled(bool value);
// Visibility flag (false hides rendering while preserving layout box).
public bool IsVisibleInHierarchy { get; }
public bool Visible { get; set; }
// True if focusable.
public bool Focusable { get; set; }
// HTML tabindex; negative retains scripted focus while skipping sequential Tab traversal, positive sorts ahead of zero.
public int TabIndex { get; set; }
// True while this element holds keyboard focus.
public bool Focused { get; }
// Requests keyboard focus on this element.
public bool Focus();
// Removes focus cleanly.
public void Blur();
// Pointer hit-test picking mode.
public PickingMode PickingMode { get; set; }
// Lifecycle hook run after element wrapping completes for building child structure.
protected virtual void OnInitialize();
// Internal post-layout update hook.
protected virtual void OnUpdate();
// Hook run on invalidation to drop managed refs (never touch native element here).
protected virtual void OnInvalidated();
// True after at least one layout pass completed.
public bool HasLayout { get; }
// Returns first descendant matching criteria or null.
public T Q<T>(string name = null, string className = null) where T : Element;
// Builds a typed query rooted at this element.
public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element;
// True if target equals this element or a descendant.
public bool Contains(Element element);
// Finds nearest strict ancestor of type T.
public T GetFirstAncestorOfType<T>() where T : Element;
// Moves element behind siblings.
public void BringToFront();
// Moves element before siblings.
public void SendToBack();
// Latest layout border rect relative to layout parent.
public Rect Layout { get; }
// Local content box excluding padding/borders.
public Rect ContentRect { get; }
// Axis-aligned world bounds in document coordinates.
public Rect WorldBound { get; }
// Inner content viewport minus borders, padding, and active scrollbars.
public Rect ViewportRect { get; }
// Extracts clean local TRS/rotation primitives when not authored with raw matrix().
public bool TryGetLocalTransform(out Matrix4x4 localTransform, out Quaternion logicalRotation);
// Computed style and resolved metric reader for latest layout.
public ElementResolvedStyle ResolvedStyle { get; }
// Current scroll offset clamped to content dimensions.
public Vector2 ScrollOffset { get; set; }
// Full scrollable content size in pixels.
public Vector2 ScrollSize { get; }
// Converts local border-box coordinates into document space.
public Vector2 LocalToWorld(Vector2 point);
// Projects document-space point into local border coordinates (throws on singular transforms).
public Vector2 WorldToLocal(Vector2 point);
// Safe local-projection helper returning false cleanly on singular transforms.
public bool TryWorldToLocal(Vector2 point, out Vector2 localPoint);
// Hit-tests point in local element coordinates.
public bool ContainsPoint(Vector2 point);
```

### ElementContext

Source: [ElementContext.cs](../Runtime/Elements/ElementContext.cs).

```csharp
// Lightweight opaque element construction token.
public sealed class ElementContext
// Document owning the queried root.
public Document Document { get; }
```

### ElementHierarchy

Source: [ElementHierarchy.cs](../Runtime/Elements/ElementHierarchy.cs).

```csharp
// Raw DOM hierarchy view including composite control internals.
public readonly struct ElementHierarchy
// Direct DOM child count.
public int ChildCount { get; }
// Ordered snapshot of direct DOM children.
public IReadOnlyList<Element> Children();
// Appends child into ContentContainer.
public void Add(Element child);
// Inserts element into ContentContainer at index.
public void Insert(int index, Element child);
// Detaches all raw DOM children while retaining Document ownership.
public void Clear();
```

### ElementQuery<T>

Source: [ElementQuery.cs](../Runtime/Elements/ElementQuery.cs).

```csharp
// Reusable typed query evaluating fresh depth-first ordered snapshots.
public sealed class ElementQuery<T> where T : Element
// Appends LINQ-style predicate applied on query execution.
public ElementQuery<T> Where(Func<T, bool> predicate);
// Scopes query strictly to direct ContentContainer children.
public ElementQuery<T> Children();
// Materializes an ordered snapshot list of matched elements.
public List<T> ToList();
// Returns first match or null.
public T First();
// Returns last match or null.
public T Last();
// Returns match at index or null.
public T AtIndex(int index);
// Executes callback across ordered query results.
public void ForEach(Action<T> action);
```

### ElementResolvedStyle

Source: [Element.Geometry.cs](../Runtime/Elements/Element.Geometry.cs).

```csharp
// Main-thread computed style reader bound to latest layout pass.
public sealed class ElementResolvedStyle
// Zero-allocation reader for finite lengths or auto from computed CSS.
public bool TryGetLength(string property, out Length length);
// Final resolved border-box width in px.
public float Width { get; }
// Final resolved border-box height in px.
public float Height { get; }
// Reads resolved CSS expression strings (read border px dimensions via Width/Height).
public string Get(string property);
```

### ElementStyle

Source: [ElementStyle.cs](../Runtime/Elements/ElementStyle.cs), [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// Inline style overrides (inspect computed results via ResolvedStyle or Document.Styles).
public sealed partial class ElementStyle
// Sets inline CSS property string, e.g. Set("position", "absolute").
public void Set(string property, string value);
// Sets pixel-unit length property, rejecting NaN and Infinity.
public void SetPixels(string property, float value);
// Fast unitless scalar write.
public void SetNumber(string property, float value);
// Fast percentage inline override.
public void SetPercent(string property, float value);
// Writes density-scaled pixel override (`dp`).
public void SetDensityPixels(string property, float value);
// Sets registered CSS keyword index directly validated natively without building CSS text.
public void SetKeyword(string property, int value);
// Fast typed color write.
public void SetColor(string property, Color value);
// Sets pixel width.
public void SetWidth(float value);
// Sets pixel height.
public void SetHeight(float value);
// Sets left offset in px.
public void SetLeft(float value);
// Sets top offset in px.
public void SetTop(float value);
// Sets inline opacity (`0..1`, null clears).
public void SetOpacity(float value);
// Toggles display between block and none (use Style.Display or Set to restore flex/inline).
public void SetDisplay(bool visible);
// Sets inline background-color using Color32 RGBA bytes.
public void SetBackgroundColor(Color value);
// Removes inline override so stylesheet cascade resumes.
public void Remove(string property);
// Reads inline override only (null if unset by inline attribute).
public string Get(string property);
// Inline length override for width.
public Length Width { get; set; }
// Inline length override for height.
public Length Height { get; set; }
// Inline length override for min-width.
public Length MinWidth { get; set; }
// Inline length override for min-height.
public Length MinHeight { get; set; }
// Inline length override for max-width.
public Length MaxWidth { get; set; }
// Inline length override for max-height.
public Length MaxHeight { get; set; }
// Inline length override for left.
public Length Left { get; set; }
// Inline length override for top.
public Length Top { get; set; }
// Inline length override for right.
public Length Right { get; set; }
// Inline length override for bottom.
public Length Bottom { get; set; }
// Inline length override for margin-left.
public Length MarginLeft { get; set; }
// Inline length override for margin-right.
public Length MarginRight { get; set; }
// Inline length override for margin-top.
public Length MarginTop { get; set; }
// Inline length override for margin-bottom.
public Length MarginBottom { get; set; }
// Inline length override for padding-left.
public Length PaddingLeft { get; set; }
// Inline length override for padding-right.
public Length PaddingRight { get; set; }
// Inline length override for padding-top.
public Length PaddingTop { get; set; }
// Inline length override for padding-bottom.
public Length PaddingBottom { get; set; }
// Inline length override for font-size.
public Length FontSize { get; set; }
// Inline length override for flex-basis.
public Length FlexBasis { get; set; }
// Inline length override for border-left-width.
public Length BorderLeftWidth { get; set; }
// Inline length override for border-right-width.
public Length BorderRightWidth { get; set; }
// Inline length override for border-top-width.
public Length BorderTopWidth { get; set; }
// Inline length override for border-bottom-width.
public Length BorderBottomWidth { get; set; }
// Inline enum override for display.
public DisplayStyle Display { get; set; }
// Inline enum override for position.
public Position Position { get; set; }
// Inline enum override for flex-direction.
public FlexDirection FlexDirection { get; set; }
// Inline enum override for flex-wrap.
public FlexWrap FlexWrap { get; set; }
// Inline enum override for align-items.
public Align AlignItems { get; set; }
// Inline enum override for align-self.
public Align AlignSelf { get; set; }
// Inline enum override for justify-content.
public Justify JustifyContent { get; set; }
// Inline enum override for overflow.
public Overflow Overflow { get; set; }
// Inline override for overflow-x.
public Overflow OverflowX { get; set; }
// Inline override for overflow-y.
public Overflow OverflowY { get; set; }
// Inline numeric value for opacity; assign null to unset.
public float? Opacity { get; set; }
// Inline numeric value for flex-grow; assign null to unset.
public float? FlexGrow { get; set; }
// Inline numeric value for flex-shrink; assign null to unset.
public float? FlexShrink { get; set; }
// Inline background-color override (null clears).
public Color? BackgroundColor { get; set; }
```

### FlexDirection

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS FlexDirection enum override (Unset clears).
public enum FlexDirection { Unset, Row, RowReverse, Column, ColumnReverse }
```

### FlexWrap

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS FlexWrap enum override (Unset clears).
public enum FlexWrap { Unset, Nowrap, Wrap, WrapReverse }
```

### Justify

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS Justify enum override (Unset clears).
public enum Justify { Unset, FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }
```

### Length

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// Styled CSS length preserving authored units; values must be finite.
public readonly struct Length
// Numeric length scalar.
public float Value { get; }
// CSS length unit.
public LengthUnit Unit { get; }
// Clears inline style override.
public static Length Unset { get; }
// Automatic CSS `auto` length.
public static Length Auto { get; }
// Pixel length (`px`).
public static Length Pixels(float value);
// Percentage CSS length (`100` = 100%).
public static Length Percent(float value);
// CSS `em` length relative to element font-size.
public static Length Em(float value);
// CSS `rem` length relative to root font-size.
public static Length Rem(float value);
// Implicit conversion from scalar float to pixels Length.
public static implicit operator Length(float value);
// Formats length preserving original unit string.
public override string ToString();
```

### LengthUnit

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS length unit; Unset indicates no inline override.
public enum LengthUnit { Unset, Pixel, Percent, Em, Rem, Auto }
```

### Overflow

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS Overflow enum override (Unset clears).
public enum Overflow { Unset, Visible, Hidden, Auto, Scroll }
```

### PickingMode

Source: [Element.State.cs](../Runtime/Elements/Element.State.cs).

```csharp
// Hit-testing mode; Ignore skips the element itself while leaving children pickable.
public enum PickingMode { Position, Ignore }
```

### Position

Source: [ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs).

```csharp
// CSS Position enum override (Unset clears).
public enum Position { Unset, Static, Relative, Absolute }
```

## URUI.Events

### AnimationEndEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired when an authored CSS keyframe animation completes per property.
public sealed class AnimationEndEvent : EventBase
public string PropertyName { get; internal set; }
public string AnimationName { get; internal set; }
public ulong Identity { get; internal set; }
```

### AttachToDocumentEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired on tree attachment.
public sealed class AttachToDocumentEvent : EventBase
```

### BlurEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired when element loses keyboard focus.
public sealed class BlurEvent : EventBase
```

### ChangeEvent<T>

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Generic value-change event (suppressed on equal/silent writes).
public sealed class ChangeEvent<T> : EventBase
// Value before change.
public T PreviousValue { get; internal set; }
// Value after change.
public T NewValue { get; internal set; }
```

### ClickEvent

Source: [ClickEvent.cs](../Runtime/Events/ClickEvent.cs).

```csharp
// Native click target element carrying pointer metadata and propagation control.
public sealed class ClickEvent : PointerEventBase
```

### DetachFromDocumentEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired when detached from document tree (element remains valid).
public sealed class DetachFromDocumentEvent : EventBase
```

### EventBase

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Synchronous main-thread UI event sharing dispatch propagation state.
public abstract class EventBase
// Original hit target element.
public Element Target { get; internal set; }
// Element currently handling the callback.
public Element CurrentTarget { get; internal set; }
// Current dispatch phase.
public PropagationPhase PropagationPhase { get; internal set; }
// True if propagation to subsequent targets was halted.
public bool IsPropagationStopped { get; private set; }
// True if remaining listeners on CurrentTarget were halted.
public bool IsImmediatePropagationStopped { get; private set; }
// True when default control action was prevented.
public bool IsDefaultPrevented { get; private set; }
// Suppresses interruptible default control action.
public void PreventDefault();
// True for bubbling events.
public virtual bool Bubbles { get; }
// Stops bubbling/trickling to subsequent targets after finishing handlers on CurrentTarget.
public void StopPropagation();
// Immediately stops all further handlers on this event.
public void StopImmediatePropagation();
```

### FocusEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired when element receives keyboard focus.
public sealed class FocusEvent : EventBase
```

### GeometryChangedEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Element layout box changed.
public sealed class GeometryChangedEvent : EventBase
// Layout bounds from the most recent completed pass.
public Rect OldRect { get; internal set; }
// Current resolved layout border rectangle.
public Rect NewRect { get; internal set; }
```

### InputModifiers

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Keyboard modifiers active at event time.
public enum InputModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Command = 8 }
```

### KeyboardEventBase

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Keyboard event payload.
public abstract class KeyboardEventBase : EventBase
// Unity KeyCode.
public KeyCode KeyCode { get; internal set; }
// Active keyboard modifiers.
public InputModifiers Modifiers { get; internal set; }
```

### KeyDownEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Key press (including OS repeat events).
public sealed class KeyDownEvent : KeyboardEventBase
```

### KeyUpEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Key release event.
public sealed class KeyUpEvent : KeyboardEventBase
```

### PointerCancelEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Pointer gesture canceled by host/focus change.
public sealed class PointerCancelEvent : PointerEventBase
```

### PointerCaptureOutEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired when element loses pointer capture.
public sealed class PointerCaptureOutEvent : PointerEventBase
```

### PointerDownEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Pointer press event.
public sealed class PointerDownEvent : PointerEventBase
```

### PointerEnterEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Pointer entered element bounds.
public sealed class PointerEnterEvent : PointerEventBase
```

### PointerEventBase

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Pointer event data in top-left document pixel coordinates.
public abstract class PointerEventBase : EventBase
// Unique non-negative pointer identifier per contact.
public int PointerId { get; internal set; }
// Document-space position vector.
public Vector2 Position { get; internal set; }
// Pointer coordinates local to CurrentTarget.
public Vector2 LocalPosition { get; }
// Mouse button index causing this event (0 for touch).
public int Button { get; internal set; }
// Bitmask of currently pressed pointer buttons.
public int PressedButtons { get; internal set; }
// Active keyboard modifiers.
public InputModifiers Modifiers { get; internal set; }
// True when event originated from touch.
public bool IsTouch { get; internal set; }
```

### PointerLeaveEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Pointer exited element bounds.
public sealed class PointerLeaveEvent : PointerEventBase
```

### PointerMoveEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Pointer move or hover event.
public sealed class PointerMoveEvent : PointerEventBase
```

### PointerPhase

Source: [Document.Input.cs](../Runtime/Documents/Document.Input.cs).

```csharp
// Normalized pointer event phase.
public enum PointerPhase { Down, Move, Up, Cancel, Wheel }
```

### PointerUpEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Pointer release event.
public sealed class PointerUpEvent : PointerEventBase
```

### PropagationPhase

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Active dispatch phase on this handler.
public enum PropagationPhase { None, TrickleDown = 1, AtTarget = 2, BubbleUp = 4 }
```

### ScrollEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired on scroll position changes.
public sealed class ScrollEvent : EventBase
```

### TextInputEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Committed text input event.
public sealed class TextInputEvent : EventBase
// Managed UTF-16 string.
public string Text { get; internal set; }
```

### TransitionEndEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Fired when a CSS transition property finishes.
public sealed class TransitionEndEvent : EventBase
public string PropertyName { get; internal set; }
```

### TrickleDown

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Propagation phase filter for event callbacks.
public enum TrickleDown { NoTrickleDown, TrickleDown }
```

### WheelEvent

Source: [EventBase.cs](../Runtime/Events/EventBase.cs).

```csharp
// Mouse wheel event.
public sealed class WheelEvent : PointerEventBase
// Scroll delta vector.
public Vector2 Delta { get; internal set; }
```

## URUI.Hosting

### Surface

Source: [Surface.cs](../Runtime/Hosting/Surface.cs).

```csharp
// Shared scene UI Surface managing native context, viewport, UI clock, input, and render submission.
public sealed class Surface : MonoBehaviour
// Shared clock across all documents on this Surface (immutable while loaded).
public IUIClock UIClock { get; set; }
// Logical CSS viewport size (zero tracks target screen pixel size).
public Vector2Int Viewport { get; set; }
// Unified input entry point routed once per Surface.
public SurfaceInput Input { get; }
// True while executing synchronous dispatch, load transactions, or paint callbacks.
public bool IsExecutingCallback { get; }
// Defers action until current dispatch exits, or executes immediately when idle.
public void RunAfterEvent(Action action);
// Submits all active documents sharing this Surface once per render pass.
public bool SubmitRendering(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Rendering.RenderTargetIdentifier target, Rect viewport);
// Returns zero-allocation rendering and native plan telemetry snapshot usable in Editor and release Players.
public SurfaceRenderingStatistics GetRenderingStatistics();
```

### SurfaceRenderingStatistics

Source: [SurfaceRenderingStatistics.cs](../Runtime/Hosting/SurfaceRenderingStatistics.cs).

```csharp
public readonly struct SurfaceRenderingStatistics
{
    public bool Ready { get; }
    public int LoadedDocuments { get; }
    public int RecordedGeometryDraws { get; }
    public int RecordedExternalGeometryDraws { get; }
    public uint NativePlanCommandCount { get; }
    public ulong NativePlanGeometryBytes { get; }
    public ulong NativePlanFrameBytes { get; }
    public ulong NativePlanAllocations { get; }
    public ulong NativePlanFrees { get; }
    public ulong NativePlanAllocatedBytes { get; }
    public long GeometryUploadBytes { get; }
    public long ParameterUploadBytes { get; }
    public long ParameterCopyBytes { get; }
}
```

### SurfaceInput

Source: [SurfaceInput.cs](../Runtime/Hosting/SurfaceInput.cs).

```csharp
// Main-thread synchronous input adapter; polls legacy Input Manager unless UseUnityInput is disabled.
public sealed class SurfaceInput
// Polls Unity legacy Input Manager automatically when true.
public bool UseUnityInput { get; set; }
// Hit-tests Unity screen point; visible modal documents capture all input while transparent document/body allow passthrough.
public bool BlocksPointer(Vector2 screenPosition);
// Dispatches pointer input in top-left document coordinates (pointerId non-negative and stable per contact).
public void SendPointer(PointerPhase phase, Vector2 position, int pointerId = 0, bool isTouch = false, int button = 0, int pressedButtons = 0, Vector2 delta = default, InputModifiers modifiers = InputModifiers.None);
// Dispatches key press/release transitions; send characters via SendText.
public void SendKey(KeyCode key, bool pressed, InputModifiers modifiers = InputModifiers.None);
// Inserts committed Unicode text updating native caret and selection cleanly.
public void SendText(string text);
// Updates active IME composition preview without firing control change notifications.
public void SendComposition(string text);
// Commits IME composition string once cleanly.
public void CommitComposition(string text);
// Cancels active IME composition and restores prior text/selection.
public void CancelComposition();
// Resets all pressed/captured pointers, modifiers, and focus cleanly.
public void Reset();
// Cancels active gestures held inside a target element subtree.
public void CancelPointers(Element owner);
```

## URUI.Media

### VideoPlayback

Source: [VideoPlayback.cs](../Runtime/Media/VideoPlayback.cs).

```csharp
// Viewport-agnostic video decoder rendering to a persistent GPU RenderTexture via ref-counted leases.
public sealed class VideoPlayback : MonoBehaviour
public int PresentedFrame { get; private set; }
public ulong PresentedVersion { get; private set; }
public RenderTexture Output { get; }
public bool HasPresentedFrame { get; }
public event Action<VideoPlayback> FramePresented;
public event Action<string> Failed;
// Plays caller-owned VideoClip, retiring prior outputs safely behind active leases.
public void Play(VideoClip clip, bool loop = true);
// Plays platform video URL (resolution ready after Prepare).
public void PlayUrl(string url, bool loop = true);
// Acquires output lease safely even prior to first decoded frame.
public ImageLease AcquireOutput();
public void Pause();
public void Resume();
// Prepares current clip from start while keeping output RenderTexture identity stable.
public void Replay();
public void Stop();
```

## URUI.Rendering.Effects

### EffectComposite

Source: [EffectComposite.cs](../Runtime/Rendering/Effects/EffectComposite.cs).

```csharp
// Final effect composite blending mode (default follows standard CSS blend).
public readonly struct EffectComposite
public bool UsesUnityBlend { get; }
public BlendMode Source { get; }
public BlendMode Destination { get; }
public EffectComposite(BlendMode source, BlendMode destination);
```

### EffectContext

Source: [EffectContext.cs](../Runtime/Rendering/Effects/EffectContext.cs).

```csharp
// Owns resources tied to one effect instance, retiring cleanly via host GPU fences.
public sealed class EffectContext : IDisposable
public EffectDefinition Definition { get; }
public ulong ElementToken { get; }
// Instance identifier key only; elements cannot be queried or mutated inside Prepare/Record.
public URUI.Elements.Element Element { get; }
public T Own<T>(T resource) where T : IDisposable;
// Guards resource allocation strictly to Prepare window.
public void RequirePreparation();
// Retire replaced resources safely behind in-flight GPU fences.
public void Retire(IDisposable resource);
public void Dispose();
```

### EffectDefinition

Source: [EffectDefinition.cs](../Runtime/Rendering/Effects/EffectDefinition.cs).

```csharp
// Immutable shader effect registration added before Document.Load.
public sealed class EffectDefinition
public string Name { get; }
public EffectKind Kind { get; }
public string ProgramKey { get; }
public string ColorContract { get; }
// Custom opaque programs always keep isolated draw boundaries.
public RenderProgramDescriptor RenderProgram { get; }
public RenderParameterLayout RenderParameters { get; }
public Vector4 Expansion { get; }
public EffectImageCondition ImageCondition { get; }
public bool UsesTime { get; }
public IReadOnlyList<StylePropertyDefinition> Parameters { get; }
public IEffectFactory Factory { get; }
public EffectDefinition(string name, EffectKind kind, IEffectFactory factory, string programKey, string colorContract, IEnumerable<StylePropertyDefinition> parameters = null, bool usesTime = false, Vector4 expansion = default, Func<EffectLayout, Vector4> expansionResolver = null, RenderProgramDescriptor renderProgram = default, RenderParameterLayout renderParameters = null);
public EffectDefinition(string name, EffectKind kind, IEffectFactory factory, string programKey, string colorContract, IEnumerable<StylePropertyDefinition> parameters, bool usesTime, Vector4 expansion, Func<EffectLayout, Vector4> expansionResolver, EffectImageCondition imageCondition, Func<EffectLayout, EffectComposite> compositeResolver, Func<EffectLayout, Matrix4x4[]> boundsTransforms = null, RenderProgramDescriptor renderProgram = default, RenderParameterLayout renderParameters = null);
```

### EffectFrame

Source: [EffectFrame.cs](../Runtime/Rendering/Effects/EffectFrame.cs).

```csharp
// Immutable preparation frame snapshot bound to one document generation.
public readonly struct EffectFrame
public ulong ElementToken { get; }
public EffectLayout Layout { get; }
public ComputedStyleSnapshot Style { get; }
public Vector2 Size { get; }
public Vector2 ContentOffset { get; }
public Texture Source { get; }
public EffectGeometry SourceGeometry { get; }
public Vector2Int SourcePixelSize { get; }
// Effect capture bounds in document coordinates.
public Rect SourceCaptureBounds { get; }
// Original source ink UV sub-domain excluding transparent effect halo margins.
public Rect SourceUvBounds { get; }
// Frozen local-plane to document-space homography matrix.
public Matrix4x4 ElementToDocument { get; }
public uint SourceVersion { get; }
public bool SourceIsBottomLeft { get; }
public bool SourceIsPremultiplied { get; }
// True when Source carries pre-captured subtree render target in active color space.
public bool SourceIsCaptured { get; }
public double Time { get; }
```

### EffectGeometry

Source: [EffectGeometry.cs](../Runtime/Rendering/Effects/EffectGeometry.cs).

```csharp
// Immutable geometry snapshot reused across identical native identities during Record.
public sealed class EffectGeometry
public ulong GeometryId { get; }
public IReadOnlyList<Vector3> Positions { get; }
public IReadOnlyList<Vector2> UVs { get; }
public IReadOnlyList<Color32> Colors { get; }
public IReadOnlyList<int> Indices { get; }
public Rect Bounds { get; }
// Native URUI UV bounds; borrowed Unity texture Y-flip semantics are reported separately on EffectFrame.
public Rect UvBounds { get; }
```

### EffectImageCondition

Source: [EffectComposite.cs](../Runtime/Rendering/Effects/EffectComposite.cs).

```csharp
// Allows direct single-image fast path when condition matches computed property; otherwise uses full node composite.
public sealed class EffectImageCondition
public string Property { get; }
public string DirectValue { get; }
public EffectImageCondition(string property, string directValue);
```

### EffectKind

Source: [EffectDefinition.cs](../Runtime/Rendering/Effects/EffectDefinition.cs).

```csharp
public enum EffectKind { Image, Element }
```

### EffectLayout

Source: [EffectLayout.cs](../Runtime/Rendering/Effects/EffectLayout.cs).

```csharp
// Frozen pre-callback styles, content rect, and transforms scoped to active document generation.
public sealed class EffectLayout
public ComputedStyleSnapshot Style { get; }
public Rect ContentRect { get; }
// 2D-plane to document-space homography matrix (divide by perspective w).
public Matrix4x4 ElementToDocument { get; }
public ulong RootToken { get; }
public Rect RootContentRect { get; }
public Matrix4x4 RootToDocument { get; }
public EffectLayout WithStyle(ComputedStyleSnapshot style);
// Valid strictly for frozen coordinate snapshots from the same document.
public EffectLayout WithRoot(EffectLayout source);
```

### EffectPreviewRenderer

Source: [EffectPreviewRenderer.cs](../Runtime/Rendering/Effects/EffectPreviewRenderer.cs).

```csharp
// Offscreen single-quad effect preview renderer reusing production shader paths without creating a second Surface.
public sealed class EffectPreviewRenderer : IDisposable
public EffectPreviewRenderer(EffectDefinition definition);
// source is a straight-alpha Unity Texture; content defines target pixel rect and time is supplied explicitly.
public void Render(Texture source, Rect content, double time, RenderTexture destination, Color background);
public void Dispose();
```

### EffectRegistry

Source: [EffectRegistry.cs](../Runtime/Rendering/Effects/EffectRegistry.cs).

```csharp
// Per-document registry (locked during active page loads).
public sealed class EffectRegistry
// Advances playback after layout and before native draw; never trigger relayout inside.
public event Action Updating;
// Collects controller params across all active documents prior to effect Prepare.
public event Action ParametersPreparing;
public EffectLayout GetLayout(URUI.Elements.Element element);
public IDisposable BindParameters(URUI.Elements.Element element, Func<EffectLayout, EffectLayout> resolve);
// Binds dynamic fixed-layout parameters and optional public styles (restores authored CSS on Dispose).
public IDisposable BindProgramParameters(URUI.Elements.Element element, IEffectParameterBinding resolve);
// Configures subtree capture hints and hit-testing rules without mutating author CSS.
public void SetRuntimeState(URUI.Elements.Element element, bool captureRequired, bool inputDisabled);
// Temporarily overrides clock for an effect element via disposable lease.
public IDisposable BindClock(URUI.Elements.Element element, URUI.Animation.IUIClock clock);
public int Count { get; }
public void RegisterFilter(string name, Func<ILayerFilterProgram> factory, bool cacheable = false);
public void Register(EffectDefinition definition);
public bool TryGetFrame(URUI.Elements.Element element, out EffectFrame frame);
public bool TryGet(string name, out EffectDefinition definition);
```

### EffectRenderContext

Source: [EffectRenderContext.cs](../Runtime/Rendering/Effects/EffectRenderContext.cs).

```csharp
// Overrides source img mesh/vertices/UVs/material properties (MaterialPropertyBlock copied at Record time).
public readonly struct EffectRenderContext
public EffectFrame Frame { get; }
public bool IsMaskImage { get; }
// Routes through standard img geometry/slicing/tint/clipping with processed surface output.
public void DrawOriginal();
// Records offscreen draw commands against a pre-allocated RenderTexture while preserving host UI target/matrices.
public void RecordSurface(RenderTexture target, Mesh mesh, Material material, Matrix4x4 projection, MaterialPropertyBlock properties, int pass = 0);
// Invoked upon command submission for cache commit; must not access DOM.
public void AfterSubmission(Action callback);
// Material follows ElementPaint.cginc; never multiply host-composited element opacity a second time.
public void DrawGeometry(Mesh mesh, Material material, Matrix4x4 localTransform, MaterialPropertyBlock properties, int pass = 0);
public void Draw(Material material, MaterialPropertyBlock properties, int pass = 0);
```

### EffectStatistics

Source: [EffectStatistics.cs](../Runtime/Rendering/Effects/EffectStatistics.cs).

```csharp
public static class EffectStatistics
public static long FilterProcessingPasses { get; internal set; }
public static EffectTargetStatistics GetTargets();
```

### EffectTargetStatistics

Source: [EffectStatistics.cs](../Runtime/Rendering/Effects/EffectStatistics.cs).

```csharp
// Offscreen effect RenderTexture counters (color bytes exclude driver alignment and video buffers).
public readonly struct EffectTargetStatistics
public readonly int LiveTargets, LeasedTargets;
public readonly long LiveColorBytes, LeasedColorBytes, IdleColorBytes, CreatedTargets, Reuses;
```

### FilterBuffer

Source: [FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs).

```csharp
public enum FilterBuffer { Source, Scratch, Destination }
```

### FilterInvocation

Source: [FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs).

```csharp
// Single parsed CSS filter invocation in authored order; lengths are layout pixels and colors use straight alpha.
public sealed class FilterInvocation
public string Name { get; }
public float Value { get; }
public float Sigma { get; }
public Vector2 Offset { get; }
public Color Color { get; }
public RectInt Region { get; }
```

### FilterRenderContext

Source: [FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs).

```csharp
// Filter pass render scope preserving host Linear/Gamma color rules and HDR intermediate precision.
public sealed class FilterRenderContext
public Vector2Int Size { get; }
public Texture Texture(FilterBuffer buffer);
public void Draw(Material material, MaterialPropertyBlock properties, int pass, FilterBuffer source, FilterBuffer destination);
public void Copy(FilterBuffer source, FilterBuffer destination);
```

### IEffectBounds

Source: [IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs).

```csharp
// Prepare updates resources; Record only emits draw commands without touching DOM.
// Optional local paint bounds returned from Prepare for filter/mask target sizing.
public interface IEffectBounds
UnityEngine.Rect InkBounds { get; }
```

### IEffectFactory

Source: [IEffectFactory.cs](../Runtime/Rendering/Effects/IEffectFactory.cs).

```csharp
// Instantiates per-node effect handler; transfer created resources to context.Own.
public interface IEffectFactory
IElementEffect Create(EffectContext context);
```

### IElementEffect

Source: [IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs).

```csharp
public interface IElementEffect : IDisposable
void Prepare(EffectFrame frame);
void Record(EffectRenderContext context);
```

### ILayerFilterProgram

Source: [FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs).

```csharp
public interface ILayerFilterProgram : IDisposable
// Filter program shared by name; reads per-element args during Record without mutating DOM.
void Prepare(FilterInvocation invocation);
// Uses host-owned source/destination/scratch buffers guarded by GPU fences.
void Record(FilterRenderContext context, FilterInvocation invocation);
```

### EffectProgramCoordinates

Source: [EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs).

```csharp
// Frozen coordinate data during effect program preparation.
public readonly struct EffectProgramCoordinates
public Rect ContentRect { get; }
public Rect RootContentRect { get; }
public Matrix4x4 ElementToDocument { get; }
public Matrix4x4 RootToDocument { get; }
// Adopts anchor coordinate space from another laid-out root while keeping content.
public EffectProgramCoordinates WithRoot(EffectLayout root);
```

### EffectProgramSource

Source: [EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs).

```csharp
// Frozen source sampling info; native base params supply uniform tint and UV matrix.
public readonly struct EffectProgramSource
public Texture Texture { get; }
public EffectGeometry Topology { get; }
public bool IsCaptured { get; }
public bool IsBottomLeft { get; }
public bool IsPremultiplied { get; }
public Rect CaptureBounds { get; }
public Rect UvBounds { get; }
public Vector2Int PixelSize { get; }
```

### IEffectParameterBinding

Source: [EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs).

```csharp
// Parameter binding computing packed float4 buffers synchronously without allocating full style snapshots unless requested.
public interface IEffectParameterBinding
// Builds immutable public layout info for subtree allocation.
EffectLayout ResolveLayout(EffectLayout layout);
// Evaluates parameter inputs from frozen coordinates without mutating DOM.
EffectProgramCoordinates ResolveCoordinates(EffectProgramCoordinates coordinates);
// Writes packed shader parameter slots synchronously during Prepare.
void ResolveProgramParameters(string program, Span<Vector4> values);
```

### EffectProgramPreparation

Source: [EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs).

```csharp
// Transient parameter preparation view valid only inside PrepareProgram; Frame builds public snapshot lazily.
public readonly ref struct EffectProgramPreparation
public Vector2 Size { get; }
public Vector2 ContentOffset { get; }
public double Time { get; }
// Packed parameter buffer span valid only during this Prepare call.
public ReadOnlySpan<Vector4> Parameters { get; }
public EffectProgramCoordinates Coordinates { get; }
public EffectProgramSource Source { get; }
public EffectFrame Frame { get; }
```

### IParameterImageEffect

Source: [IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs).

```csharp
// Fixed-layout parameter image shader; host renders directly without calling Record (retire replaced materials via EffectContext).
public interface IParameterImageEffect
UnityEngine.Material Program { get; }
// Time source declared by shader program (None or UIClock).
RenderTimeInput TimeInput { get; }
void PrepareProgram(in EffectProgramPreparation preparation);
// Preview input payload passed verbatim to effect programs without DOM.
void GetPreviewParameters(Span<UnityEngine.Vector4> values);
```

### IParameterImageResources

Source: [IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs).

```csharp
// Commits shared GPU resources after Prepare passes without mutating DOM.
public interface IParameterImageResources
void CommitResources();
```

### IParameterImageRuntime

Source: [IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs).

```csharp
// Extra float4 parameter rows copied after host sampling rows.
public interface IParameterImageRuntime
int RuntimeRowCount { get; }
void CopyRuntimeParameters(Span<UnityEngine.Vector4> values);
```

### IParameterEffectLayout

Source: [IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs).

```csharp
// Declares conservative bounds and composite blend before capture target allocation (max 64 transforms).
public interface IParameterEffectLayout
void PrepareCoverage(in EffectProgramPreparation preparation, Span<UnityEngine.Matrix4x4> transforms, out int count, out UnityEngine.Vector4 expansion, out EffectComposite composite);
```

### IParameterImageGeometry

Source: [IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs).

```csharp
// Immutable local geometry template; increment version when content changes while passing transforms separately.
public interface IParameterImageGeometry : IParameterImageEffect
ExternalGeometryInput Geometry { get; }
UnityEngine.Matrix4x4 GeometryTransform { get; }
```

## URUI.Rendering

### ElementPaintContext

Source: [ElementPaintContext.cs](../Runtime/Rendering/ElementPaintContext.cs).

```csharp
// Custom painter invocation bounds originating at element top-left in local coordinates.
public readonly struct ElementPaintContext
public Element Element { get; }
// True when painting alpha into `--urui-mask-image` (avoid double opacity).
public bool IsMaskImage { get; }
// Clipped out by ancestor clip rects; skip recording.
public bool IsClippedOut { get; }
public Vector2 Size { get; }
// Draws mesh in local node space; MaterialPropertyBlock clones at Record while Mesh/Material remain caller-owned.
public void DrawMesh(Mesh mesh, Material material, Matrix4x4 localTransform, MaterialPropertyBlock properties = null, int submesh = 0, int pass = 0);
```

### RenderTextureEncoding

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderTextureEncoding { Unknown, TextureDeclared, EncodedPremultiplied, LinearPremultiplied, LinearStraight }
```

### RenderColorSpace

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderColorSpace { Unknown, EncodedSrgb, Linear, Active, Target }
```

### RenderAlpha

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderAlpha { Unknown, Premultiplied, Straight, Opaque, PartialPremultiplied }
```

### RenderBlendDomain

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderBlendDomain { Unknown, EncodedSrgb, Linear, Active, Target }
```

### RenderTargetCapability

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderTargetCapability { Unknown, Ldr, Hdr }
```

### RenderSampling

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderSampling { Unknown, BilinearClamp, LocalAddressing, Neighborhood }
```

### RenderGeometry

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderGeometry { Unknown, Local, VertexDisplacement, Expanded, Captured }
```

### RenderTimeInput

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
public enum RenderTimeInput { None, UIClock, External }
```

### RenderProgramDescriptor

Source: [RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs).

```csharp
// Machine-readable render contract; defaults mark custom programs isolated.
public readonly struct RenderProgramDescriptor : IEquatable<RenderProgramDescriptor>
public RenderTextureEncoding Input { get; }
public RenderColorSpace Computation { get; }
public RenderColorSpace Output { get; }
public RenderAlpha Alpha { get; }
public RenderBlendDomain Blend { get; }
public BlendMode SourceBlend { get; }
public BlendMode DestinationBlend { get; }
public BlendMode SourceAlphaBlend { get; }
public BlendMode DestinationAlphaBlend { get; }
public CullMode Cull { get; }
public ColorWriteMask ColorMask { get; }
public RenderTargetCapability Target { get; }
public RenderSampling Sampling { get; }
public RenderGeometry Geometry { get; }
public RenderTimeInput Time { get; }
public bool IsKnown { get; }
public RenderProgramDescriptor(RenderTextureEncoding input, RenderColorSpace computation, RenderColorSpace output, RenderAlpha alpha, RenderBlendDomain blend, BlendMode sourceBlend, BlendMode destinationBlend, RenderTargetCapability target, RenderSampling sampling, RenderGeometry geometry, RenderTimeInput time = RenderTimeInput.None, BlendMode? sourceAlphaBlend = null, BlendMode? destinationAlphaBlend = null, CullMode cull = CullMode.Off, ColorWriteMask colorMask = ColorWriteMask.All);
// Enables shared raster pass candidates; actual batching still verifies targets and resource dependencies.
public bool CanShareRaster(RenderProgramDescriptor other);
public bool Equals(RenderProgramDescriptor other);
public override bool Equals(object obj);
public override int GetHashCode();
```

### RenderKeywordMapping

Source: [RenderParameterLayout.cs](../Runtime/Rendering/RenderParameterLayout.cs).

```csharp
// Maps authored CSS keywords deterministically onto numeric shader slots.
public readonly struct RenderKeywordMapping
public string Keyword { get; }
public float Value { get; }
public RenderKeywordMapping(string keyword, float value);
```

### RenderParameterField

Source: [RenderParameterLayout.cs](../Runtime/Rendering/RenderParameterLayout.cs).

```csharp
// Maps a computed CSS property to fixed float4 components (4 slots for color, 2 slots [value, keywordFlag] for keyworded scalars).
public sealed class RenderParameterField
public StylePropertyDefinition Property { get; }
public int ComponentOffset { get; }
// Optional style cascade origin scalar slot (-1 if unused).
public int OriginOffset { get; }
// Writes zero for unset custom properties while rejecting mismatched CSS types.
public bool Optional { get; }
// Keyword value table encoding [scalar, isKeywordFlag] for properties with finite keyword tokens.
public IReadOnlyList<RenderKeywordMapping> Keywords { get; }
public RenderParameterField(StylePropertyDefinition property, int componentOffset, int originOffset = -1, IEnumerable<RenderKeywordMapping> keywords = null, bool optional = false);
```

### RenderParameterLayout

Source: [RenderParameterLayout.cs](../Runtime/Rendering/RenderParameterLayout.cs).

```csharp
// Immutable float4 shader parameter mapping compiled at registration.
public sealed class RenderParameterLayout
public int RowCount { get; }
public IReadOnlyList<RenderParameterField> Fields { get; }
public RenderParameterLayout(int rowCount, IEnumerable<RenderParameterField> fields);
```

### ExternalGeometryInput

Source: [ExternalGeometryInput.cs](../Runtime/Rendering/ExternalGeometryInput.cs).

```csharp
// Read-only external geometry descriptor for one submesh; host holds MeshData lease until native compilation completes.
public readonly struct ExternalGeometryInput
public ulong GeometryVersion { get; }
public int Submesh { get; }
// Increment GeometryVersion whenever mesh topology, vertices, or attributes change.
public ExternalGeometryInput(Mesh mesh, ulong version, int submesh = 0);
```

### ExternalGeometryProgram

Source: [ExternalGeometryProgram.cs](../Runtime/Rendering/ExternalGeometryProgram.cs).

```csharp
// External geometry material/program contract keeping materials valid behind registered retirement hooks.
public sealed class ExternalGeometryProgram
public Material Material { get; }
public RenderProgramDescriptor Descriptor { get; }
public int Pass { get; }
// Increment ResourceVersion when buffers or unreflected shader resources change.
public ulong ResourceVersion { get; }
// Local vertex position components read by shader (2D ignores Z).
public int PositionComponents { get; }
// Known programs read Position/RGBA8/UV0/parameter tables; unknown shaders retain isolated draw boundaries.
public ExternalGeometryProgram(Material material, RenderProgramDescriptor descriptor = default, int pass = 0, int positionComponents = 3, ulong resourceVersion = 1);
```

### ExternalGeometryContext

Source: [ExternalGeometryContext.cs](../Runtime/Rendering/ExternalGeometryContext.cs).

```csharp
// Synchronous external geometry recording context originating at element top-left.
public readonly struct ExternalGeometryContext
public Vector2 Size { get; }
// Dense parameter buffer span declared at registration (valid only inside callback).
public ReadOnlySpan<Vector4> Parameters { get; }
// Local rect and edge feather distances driving direct or offscreen soft masking.
public void DrawFeatheredRectangle(Rect rectangle, Vector4 feather);
public bool IsClippedOut { get; }
public bool IsMaskImage { get; }
// Freezes read-only mesh input while delegating transforms/clips to render plan.
public void Draw(in ExternalGeometryInput input, ExternalGeometryProgram program, Matrix4x4 localTransform);
```

## URUI.Resources

### CompiledDocumentAsset / CompiledTemplate

Source: [CompiledDocumentAsset.cs](../Runtime/Resources/CompiledDocumentAsset.cs), [CompiledTemplate.cs](../Runtime/Resources/CompiledTemplate.cs).

```csharp
public sealed class CompiledDocumentAsset : ScriptableObject
public const int CompilerVersion = 2;
public string SourceUrl { get; }
public string Fingerprint { get; }
public bool IsFragment { get; }
public IReadOnlyList<TextResource> Dependencies { get; }
public CompiledTemplate GetTemplate(string uri);
public sealed class CompiledTemplate
public string SourceUrl { get; }
```

### GpuRetirement

Source: [GpuRetirement.cs](../Runtime/Resources/Images/GpuRetirement.cs).

```csharp
// Releases transient graphics resources only after active GPU completion fences clear.
public static class GpuRetirement
// Non-blocking fence poll for Editor hosts lacking MonoBehaviour Update.
public static void Collect();
// Releases retired owned UnityEngine.Object safely across Edit/Play transitions.
public static void DestroyOwnedObject(UnityEngine.Object value);
public static void Retire(Action release);
```

### ImageAsset

Source: [ImageAsset.cs](../Runtime/Resources/Images/ImageAsset.cs).

```csharp
// Maps HTML/CSS URI to caller-held Texture2D without requiring Read/Write.
public struct ImageAsset
// Canonical resource URI path used in HTML/CSS, e.g. icons/coin.png.
public string Source { get; }
// Caller-owned Texture2D asset (never destroyed by URUI).
public Texture2D Texture { get; }
// Binds an HTML/CSS image URI to a caller-owned Unity Texture2D.
public ImageAsset(string source, Texture2D texture);
// Associates an atlas Texture2D and cloned SpriteRegion descriptors without assuming texture ownership.
public ImageAsset(string source, Texture2D texture, SpriteRegion[] sprites);
// Immutable sprite region descriptor published with document candidate resources.
public IReadOnlyList<SpriteRegion> Sprites { get; }
```

### ImageLease

Source: [ImageLease.cs](../Runtime/Resources/Images/ImageLease.cs).

```csharp
// Synchronously resolves an ImageLease transferred into URUI ownership.
public sealed class ImageLease : IDisposable
public Texture Texture { get; }
public ImageLease(Texture texture, Action release = null);
public void Dispose();
```

### ImageLeaseSource

Source: [ImageLeaseSource.cs](../Runtime/Resources/Images/ImageLeaseSource.cs).

```csharp
// Shares ownership across producer and leased consumers until final release.
public sealed class ImageLeaseSource : IDisposable
public ImageLeaseSource(Texture texture, Action release);
public ImageLease Acquire();
public void Dispose();
```

### SpriteRegion

Source: [SpriteRegion.cs](../Runtime/Resources/Images/SpriteRegion.cs).

```csharp
// Named sprite region within a texture using top-left source pixel coordinates.
public struct SpriteRegion
// Case-sensitive unique region key within this atlas.
public string Name { get; }
// Top-left source texture pixel bounds.
public Rect Region { get; }
// Intrinsic display scale mapping source pixels to CSS dimensions.
public float DisplayScale { get; }
// Declares a named atlas SpriteRegion validated before document load.
public SpriteRegion(string name, Rect region, float displayScale = 1);
```

### TextResource

Source: [TextResource.cs](../Runtime/Resources/TextResource.cs).

```csharp
// Canonical resource URI mapping raw TextAsset bytes cleanly without encoding conversion.
public struct TextResource
public string Source { get; }
public TextAsset Asset { get; }
public TextResource(string source, TextAsset asset);
```

## URUI.Styles

### ComputedImageKind

Source: [ComputedImageValue.cs](../Runtime/Styles/ComputedImageValue.cs).

```csharp
public enum ComputedImageKind { None, Url, Gradient }
```

### ComputedImageValue

Source: [ComputedImageValue.cs](../Runtime/Styles/ComputedImageValue.cs).

```csharp
// Parsed immutable CSS image token keeping declaration base URL.
public sealed class ComputedImageValue
public ComputedImageKind Kind { get; }
public string Url { get; }
public string CssText { get; }
public string SourceUrl { get; }
```

### ComputedStyleSnapshot

Source: [ComputedStyleSnapshot.cs](../Runtime/Styles/ComputedStyleSnapshot.cs).

```csharp
// Immutable snapshot tied to one stable layout generation; invalidated upon reload or destruction.
public sealed class ComputedStyleSnapshot
// Returns a view overriding styles up to maximumOrigin without mutating live DOM.
public ComputedStyleSnapshot WithOverrides(IReadOnlyDictionary<string, StyleOverride> overrides, StyleValueOrigin maximumOrigin);
public uint Generation { get; }
public ulong Revision { get; }
public string GetKeyword(string name);
// Cascade origin tier (`Initial` vs `Author` vs `Animation`/`Important`/`Transition`).
public StyleValueOrigin GetOrigin(string name);
public ComputedImageValue GetImage(string name);
public float GetNumber(string name);
public float GetPixels(string name);
public Color GetColor(string name);
public bool Contains(StylePropertyDefinition property);
```

### DocumentStyles

Source: [DocumentStyles.cs](../Runtime/Styles/DocumentStyles.cs).

```csharp
// Checks native snapshot revision cleanly before allocating new snapshots or firing listeners.
public sealed class DocumentStyles
public uint Generation { get; private set; }
public ComputedStyleSnapshot GetComputed(Element element);
// Fires on value changes starting next committed frame; suppresses equal writes and honors Dispose.
public IDisposable Subscribe(Element element, Action<ComputedStyleSnapshot> changed);
```

### StyleOverride

Source: [StyleOverride.cs](../Runtime/Styles/StyleOverride.cs).

```csharp
// Direct runtime float4 slot inside effect parameter buffer.
public readonly struct StyleOverride
// Reads typed number without managed allocations or conversions.
public bool TryGetNumber(out float number);
// Reads typed Color safely.
public bool TryGetColor(out Color color);
// Reads typed keyword value safely.
public bool TryGetKeyword(out string keyword);
public static StyleOverride Number(float value);
public static StyleOverride Color(Color value);
public static StyleOverride FromKeyword(string value);
```

### StylePropertyDefinition

Source: [StylePropertyDefinition.cs](../Runtime/Styles/StylePropertyDefinition.cs).

```csharp
// Typed property binding descriptor requested from CSS cascade.
public sealed class StylePropertyDefinition
public string Name { get; }
public StyleValueType Type { get; }
public StylePropertyDefinition(string name, StyleValueType type);
```

### StyleValueOrigin

Source: [ComputedStyleSnapshot.cs](../Runtime/Styles/ComputedStyleSnapshot.cs).

```csharp
public enum StyleValueOrigin { Initial, UserAgent, Author, Animation, Important, Transition }
```

### StyleValueType

Source: [StylePropertyDefinition.cs](../Runtime/Styles/StylePropertyDefinition.cs).

```csharp
public enum StyleValueType { Number, Pixels, Color, Keyword, Image }
```
