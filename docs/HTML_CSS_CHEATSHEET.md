# HTML & CSS Support Cheatsheet

**English** | [中文](HTML_CSS_CHEATSHEET_ZH.md)

Derived from [`Packages/com.fujisheng.urui/Documentation~/HtmlCssSupport.json`](../Packages/com.fujisheng.urui/Documentation~/HtmlCssSupport.json) (**525 tracked entries**: `118` standard, `219` URUI extensions, `187` fixed/scoped implementations, `1` explicitly removed `@spritesheet`).

---

## 1. Layout & Box Model (Flexbox First)

| Feature | Status | Notes |
| --- | --- | --- |
| `display` | **Supported** (`block`, `inline`, `inline-block`, `flex`, `inline-flex`, `table*`, `none`, `flow-root`) | **Use Flexbox for multi-column cards.** CSS Grid (`display: grid`, `grid-template-columns`) is **not** supported. |
| `flex`, `flex-direction`, `flex-wrap`, `justify-content`, `align-items`, `align-content` | **Supported** (`standard`) | Full responsive flex wrapping grids work identically across URUI and browsers. |
| `gap`, `column-gap`, `row-gap` | **Supported** (`standard` / `shorthand`) | Clean spacing inside flex rows/columns. |
| `position` (`static`, `relative`, `absolute`, `fixed`, `sticky`) | **Supported** (`standard`) | Paired with `inset`, `top`, `right`, `bottom`, `left`, `z-index`. |
| `box-sizing`, `padding`, `margin`, `border`, `border-radius`, `box-shadow` | **Supported** | Rounded corners (`px` or `999px` pill) & solid shadows render crisply. |
| `overflow` (`visible`, `hidden`, `scroll`, `auto`) | **Supported** | Also drives clip masks cleanly in native `render-plan9`. |
| `object-fit` (`fill`, `contain`, `cover`) | **Supported** (`standard`) | Adjusts `<img>` draw geometry without mutating layout bounds. |

---

## 2. Selectors, At-Rules & CSS Custom Properties

- **Selectors (`31` tracked)**: `#id`, `.class`, `type`, `*`, descendant (`A B`), child (`A > B`), sibling (`+`, `~`), `[attr]`, `:hover`, `:active`, `:focus`, `:focus-visible`, `:checked`, `:disabled`, `:drag`, `:selected`, `:placeholder-shown`, `:first-child`, `:last-child`, `:nth-child`, `:not(...)`, `:scope`, `:empty`.
- **At-Rules (`7` tracked)**: `@font-face`, `@keyframes`, `@property`, `@media`, `@supports` / `not (property:value)`, `@decorator` (extension), and `@spritesheet` (*removed* — use `ImageAsset` + `SpriteRegion` instead).
- **`@property` Rule (Important!)**: Declare custom color or numeric variables (e.g. `@property --sol-surface { syntax: "<color>"; inherits: true; initial-value: #0f172ae8; }`) before using them in CSS transitions or inheritance chains across child elements. Standalone `.css` assets only know properties declared inside built-in CSS + that stylesheet's `@property` rules.

---

## 3. Units, Typography & Visual Effects

- **Units (`14`)**: `px`, `%`, `<number>`, `em`, `rem`, `vw`, `vh`, `deg`, `rad`, `cm`, `mm`, `in`, `pt`, `pc`.
- **Fonts (`@font-face`)**: Register binary `.ttf.bytes` / `.otf.bytes` assets via `Document.TextResources` matching CSS `src: url(...)`. Always include fallback font families in `font-family` (`"Tideglass Sans", "URUI Sans", sans-serif`) for mixed Latin + CJK copy.
- **Standard Filters (`filter`, `backdrop-filter`)**: `grayscale()`, `sepia()`, `brightness()`, `contrast()`, `saturate()`, `opacity()`, `blur()`, `drop-shadow()`.
- **URUI.Effect Extensions (`--urui-fx-*`)**: Enable via `--urui-effect: ui;` or `--urui-image-effect: ui-wave | surface-output;` (`106` custom properties documented in [Effect CSS Reference](../Packages/com.fujisheng.urui.effect/Documentation~/CSS.md)).

---

## 4. HTML Controls & Custom Tags

| HTML Tag | C# Control Type | Notes |
| --- | --- | --- |
| `<label>` | `Label` | Supports `for="target-id"`. |
| `<img>` | `Image` | Supports `Image.Source`, `SpriteName`, `Fit`, nine-slice CSS properties. |
| `<button type="button">` | `Button` | Explicit `type="button"`. |
| `<input type="text" \| "password">` / `<textarea>` | `TextField` | Single-line input or multiline textarea; supports `Placeholder`, `IsDelayed`, `Submitted`. |
| `<input type="number">` | `FloatField` / `IntegerField` | Use `data-urui-type="URUI.Controls.IntegerField"` for integer fields. |
| `<input type="checkbox">` / `<input type="radio" name="...">` | `Toggle` / `RadioButton` | Group radios inside `<form>` or document by `name`. |
| `<input type="range">` | `Slider` / `IntegerSlider` | Horizontal/vertical slider with configurable `min`, `max`, `step`. |
| `<select><option>...</option></select>` | `Dropdown` | Or `.urui-select` authored fade-in popup skin. |
| `<urui-scroll-view>` | `ScrollView` | Smooth touch/wheel scrolling with inertia extension class. |
| `<urui-list-view>` | `ListView` | Fixed-row virtualized scrolling for thousands of items. |
| `<urui-foldout>` / `<urui-progress-bar>` | `Foldout` / `ProgressBar` | Explicit `data-urui-label`, `min`/`max`/`value`. |
| `<template id="...">` | `Template` | Lazy HTML template cloned via `CloneContentTo(container)`. |
| `<urui-particles src="...">` | `ParticlesElement` *(URUI.Effect)* | Pure `ParticleSystem` prefab rendered inline in DOM layout. |

> **Authoring Rule**: Non-void HTML elements must be closed explicitly (`<div></div>`, `<span></span>`, `<button></button>`). Void elements (`<meta>`, `<link>`, `<img>`, `<input>`, `<br>`, `<hr>`) do not take closing tags.
