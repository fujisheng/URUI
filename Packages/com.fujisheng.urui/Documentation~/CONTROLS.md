# Controls Guide

Matches Core `0.8.0-preview.1`, namespace `URUI.Controls`. Acquire elements via `document.Create<T>()` or `document.Q<T>()`. For common hierarchy and state APIs see [Document & Element API](API.md); for every signature and base class see [Public API Reference](REFERENCE.md).

## HTML Mapping & Control Types

Link `/urui/base.css` and `/urui/controls.css` in every standalone document; see [Default Controls & Skins](DEFAULT_CONTROLS.md) for styling details.

| C# Type | HTML Entry | Common Members |
| --- | --- | --- |
| `Label` | `<label>` | `Text`, `For` (target input `id`) |
| `Image` | `<img>` | `Source`, `SpriteName`, `Fit` / `Fill` properties |
| `Button` | `<button type="button">` | `Text`, `Clicked` |
| `Toggle` | `<input type="checkbox">` | `Value : bool` |
| `RadioButton` | `<input type="radio" name="group">` | `Value : bool`, `GroupName` |
| `Slider` | `<input type="range">` | `Value : float`, `LowValue` / `HighValue` / `Step` |
| `IntegerSlider` | range + `data-urui-type="URUI.Controls.IntegerSlider"` | `Value : int` (backed by float range bounds) |
| `TextField` | `<input type="text">`, `password`, `<textarea>` | `Value : string`, `Multiline`, selection & commit |
| `FloatField` | `<input type="number">` | `Value : float`, `Step : float` |
| `IntegerField` | number + `data-urui-type="URUI.Controls.IntegerField"` | `Value : int`, `Step : int` |
| `Dropdown` | `<select><option>...</option></select>` | `Choices`, `SelectedIndex`, `Value : string` |
| `ScrollView` | `<urui-scroll-view>` | `ContentContainer`, `ScrollOffset`, `ScrollTo` |
| `Foldout` | `<urui-foldout>` | `Text` header, `Value : bool` expanded state |
| `ProgressBar` | `<urui-progress-bar min="0" max="100" value="25">` | `Text`, `Value`, `LowValue`, `HighValue` |
| `ListView` | `<urui-list-view>` | `ItemsSource`, `MakeItem`/`BindItem`, virtualization, selection |

Control runtime type binds on first wrap—do not change `input type` or `data-urui-type` dynamically after creation. `document.Create<TextField>()` creates a `<textarea>` wrapper (toggle `Multiline`), whereas HTML `<input type="text">` is single-line.

## Values & Notifications

`BaseField<T>` exposes `Value`, `SetValueWithoutNotify(T)`, and `RegisterValueChangedCallback(Action<ChangeEvent<T>>)` returning an `IDisposable` lease (plus matching `UnregisterValueChangedCallback`). Equal values suppress duplicate notifications. `Foldout` and `ProgressBar` provide matching value change hooks; helper wrappers implementing only `INotifyValueChanged<T>` guarantee `Value` / `SetValueWithoutNotify` rather than full callback registration.

```csharp
using System;
using URUI.Controls;
using URUI.Documents;
using UnityEngine;

public static class SettingsExamples
{
    public static IDisposable BindVolume(Document document, float initialVolume)
    {
        var volume = document.Q<Slider>("volume");
        volume.LowValue = 0;
        volume.HighValue = 1;
        volume.Step = .01f;
        volume.SetValueWithoutNotify(initialVolume);
        return volume.RegisterValueChangedCallback(change => Debug.Log(change.NewValue));
    }
}
```

Use `SetValueWithoutNotify` when hydrating from save files or view-models to avoid writeback loops. `Button.Clicked` is a standard C# `Action` event; use `RegisterCallback<ClickEvent>()` when you need event target or propagation control.

`RadioButton.GroupName` groups radios per form/document (arrow keys skip disabled items). `Toggle` activates via pointer or `Enter`/`Space`. `Slider`/`IntegerSlider` support `Direction` (`Horizontal`/`Vertical`) and `Inverted`; `IntegerSlider` uses float range precision and does not guarantee exact 32-bit resolution across full `int32` bounds.

## Text & Numeric Inputs

`TextField`, `IntegerField`, and `FloatField` inherit from `TextInputField<T>`:

| Member | Behavior |
| --- | --- |
| `Placeholder : string` | Empty-state placeholder string |
| `MaxLength : int` | Maximum length counted in Unicode scalar values according to control rules |
| `IsReadOnly : bool` | Permits focus, selection, copy, and scripted writes while blocking user edits |
| `IsDelayed : bool` | Commits `Value` only on `Enter` submit or blur |
| `Selection : Vector2Int` | Read-only selection indices in Unicode character units |
| `SelectRange(int start, int end)` | Selects range in Unicode character units (never pass raw UTF-16 `.Length` directly when surrogate pairs exist) |
| `CommitValue() : bool` | Explicitly commits pending draft text |
| `Submitted : Action<T>` | Fired on single-line `Enter` commit (multiline `TextField` inserts a newline) |
| `CaretBlinkRate : float`, `CaretWidth : int` | Caret blink frequency and width |

Numeric fields keep live editing drafts in `Text` and committed values in `Value`; incomplete or non-finite inputs retain the last valid `Value` and reformat cleanly on commit or blur. `Step` drives up/down arrow stepping, saturating `IntegerField` cleanly within `int` range. IME composition previews never overwrite committed `Value` until committed.

```csharp
using URUI.Controls;
using URUI.Documents;

public static class FormExamples
{
    public static void Configure(Document document)
    {
        var quantity = document.Q<IntegerField>("quantity");
        quantity.IsDelayed = true;
        quantity.Step = 5;
        quantity.SetValueWithoutNotify(10);
        var title = document.Q<TextField>("title");
        title.Placeholder = "Enter title";
        title.MaxLength = 20;
        title.Multiline = false;
        title.SelectRange(0, 0);
    }
}
```

## Images, 9-Slice & Fills

`Source` selects a registered image path and `SpriteName` selects a named `SpriteRegion` inside an `ImageAsset`. `DensityScaledNaturalSize` toggles Surface density scaling for intrinsic size (see [Lifecycle & Resources](LIFECYCLE.md)).

| API / CSS | Options & Meaning |
| --- | --- |
| `Fit` / `object-fit` | `Fill`, `Contain`, `Cover` (adjusts content geometry/UV without changing hit box) |
| `FillMode` / `image-fill-mode` | `None`, `Horizontal`, `Vertical`, `Radial90`, `Radial180`, `Radial360` |
| `FillAmount` / `image-fill-amount` | Normalized `0..1` |
| `FillOrigin` / `image-fill-origin` | Horizontal `0` left/`1` right; vertical `0` bottom/`1` top; Radial90 corners `0..3`; 180/360 edges `0..3` |
| `FillClockwise` / `image-fill-clockwise` | Radial sweep direction (`0`/`1` in CSS) |
| `FillCenter` / `image-fill-center` | Render 9-slice center patch (`0`/`1` in CSS) |
| `image-slice-top/right/bottom/left` | 9-slice source border insets (proportional shrink when tight) |

## Dropdown, ScrollView & Foldout

Setting `Dropdown.Choices` copies choices and retains the previous selection if still present, otherwise selects index `0`. `SelectedIndex` returns `-1` when unmatched and requires an in-bounds index on assignment. Use `DropdownVisual` for rich authored popups; `<select>` retains a minimal native popup list.

`ScrollView.Add`/`Insert` targets `ContentContainer`. `ScrollTo(Element child)` scrolls a descendant into view; `ScrollOffset` uses local content units and `ViewportRect` reports the inner visible region. Pointer drags beyond threshold cancel accidental clicks. `Foldout.Text` sets its header label, `Value` expands/collapses, and child elements live in `ContentContainer`.

`ProgressBar` defaults to `0..100` with `Text`, `Value`, `LowValue`, `HighValue`. To drive custom authored fill elements instead of `<urui-progress-bar>`, construct `new ProgressIndicator(host, fill, minimum, maximum)`.

## Fixed-Height Virtualized ListView

Configure visible height, `FixedItemHeight`, `MakeItem`, `BindItem`, and optional `UnbindItem` **before** setting `ItemsSource`. Only visible rows plus overscan buffer are materialized; dynamic row heights, multi-select, and drag reorder are not supported.

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

public sealed class ListExample : IDisposable
{
    readonly ListView list;
    readonly Dictionary<Element, IDisposable> bindings = new Dictionary<Element, IDisposable>();

    public ListExample(Document document, IList items, Action<int> choose)
    {
        list = document.Create<ListView>();
        list.Style.Height = 280;
        list.FixedItemHeight = 28;
        list.MakeItem = () => document.Create<Button>();
        list.BindItem = (row, index) =>
        {
            row.Text = items[index].ToString();
            bindings[row] = row.RegisterCallback<URUI.Events.ClickEvent>(_ => choose(index));
        };
        list.UnbindItem = (row, index) =>
        {
            if (bindings.TryGetValue(row, out var lease)) lease.Dispose();
            bindings.Remove(row);
        };
        document.Body.Add(list);
        list.ItemsSource = items;
    }

    public void Refresh() => list.RefreshItems();
    public void Dispose()
    {
        if (list.IsValid) list.Destroy();
        foreach (var lease in bindings.Values) lease.Dispose();
        bindings.Clear();
    }
}
```

`MakeItem` must return a fresh, unmounted node belonging to the same document. Every `BindItem` is paired with `UnbindItem` when rows recycle or the document unloads; `UnbindItem` may fire after row invalidation, so dispose only user subscriptions there without mutating invalidated DOM nodes.

| Member | When to Call |
| --- | --- |
| `ItemsSource : IList` | Rebuilds rows on source swap; call `RefreshItems()` after in-place data mutation |
| `RefreshItems()` | Rebinds currently realized rows |
| `Rebuild()` | Destroys realized rows and recreates after changing `MakeItem` (do not call inside `BindItem`/`UnbindItem`) |
| `RealizedItemCount` | Number of active pooled row elements |
| `SelectedIndex` | Single-selection index (`-1` clears selection) |
| `SelectionChanged : Action<int>` | Selection change notification (including clamped selection after list shrink) |
| `ItemChosen : Action<int>` | Fired when `Enter` activates a focused list selection |
| `ScrollToItem(int)` | Scrolls index into view |

Arrow keys, `Home`/`End`, and `PageUp`/`PageDown` navigate rows while preserving edit keys when an input inside a row holds focus.

## Authored Composite Skins & Layout Helpers

The helpers below bind onto existing authored elements without owning DOM lifetime; dispose any implementing `IDisposable` when unbinding:

| Type & Constructor | Purpose & Constraints |
| --- | --- |
| `SliderVisual(Slider control, Element fill, Element thumb)` | Drives authored fill & thumb visuals from native `Slider` |
| `IntegerSliderVisual(IntegerSlider, Element, Element)` | Integer slider variant |
| `ToggleVisual(Toggle control, Element root)` | Synchronizes `checked` CSS class on `root` |
| `DropdownVisual(Dropdown control, Button trigger, Element label, Element popup, Button optionPrototype)` | Rich authored dropdown popup (`Value`, `IsOpen`, `Refresh`) |
| `ScrollbarVisual(Element viewport, Element track, Element thumb)` | Authored custom scrollbar track/thumb (`Refresh`) |
| `TextLayout(Element host, Element content, TextAnchor alignment)` | 9-anchor alignment helper for direct child content (`Alignment` mutable; non-`IDisposable`) |
| `TextFit(Element host, Element content)` | Binary-searches font size within `--urui-text-fit-min/max` (`Refresh`, `Fits`); `content` must be a direct child |
| `RowContentSize(Element content)` | Measures dynamic row content bounds |
| `ReferenceCanvas(Element root, Vector2 referenceSize, float match, Element safeContent = null, SafeAreaPolicy safeAreaPolicy = Physical)` | Computes responsive reference scale (`Scale`, `LogicalSize`) and Safe Area insets via `Apply(display, safe, maximumScale)` |

`TextFit` applies the `text-fit-pending` class during pre-measurement frames so authored CSS controls temporary hiding. `ReferenceCanvas` preserves asymmetric device safe areas by default (`Physical`), or pass `SymmetricHorizontal` for mirrored padding. See [Styles & Animation](STYLES_ANIMATION.md) for `ScrollMotion` and `StaggeredReveal`.
