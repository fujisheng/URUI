# Default Controls & Authored Skins

Every complete HTML document links `/urui/base.css` and `/urui/controls.css`. The default theme provides a clean UGUI-inspired state palette using standard supported HTML/CSS with zero Unity UGUI runtime dependency.

| State | Graphic Tint | Transition |
| --- | --- | --- |
| Normal | `#ffffffff` | 0.1s |
| Highlighted / Selected | `#f5f5f5ff` | 0.1s |
| Pressed | `#c8c8c8ff` | 0.1s |
| Disabled | `#c8c8c880` | 0.1s |

Default sizes are 160x30 px for `<button>`, 20x20 px for Toggle/Radio, and 160x20 px for Slider. Your authored typography, dimensions, backgrounds, and state rules cascade normally with no forced minimum sizes.

```html
<button type="button">Confirm</button>
<button type="button" data-urui-skin="image" style="width:48px;height:48px;">
  <img src="icon.png" data-urui-graphic>
  <span class="badge">3</span>
</button>
```

Each document allows up to **511** combined built-in + custom registered CSS properties (ID 0 reserved); Core controls, full `URUI.Effect`, and custom studio tokens coexist comfortably within this budget, and exceeding it fails candidate load cleanly while preserving the active document.

`data-urui-skin="image"` removes default button padding, border, and fill; `data-urui-graphic` (or `.urui-control-graphic`) marks only the visual target that receives state tint multiplication (`--urui-graphic-tint`) across its image, CSS background, border, and border-image without dimming nested text badges, particles, or masks and without extra `RenderTexture` allocations.

Target the narrowest visual leaf rather than tagging whole containers with text children. Set `--urui-graphic-tint: white` or override state rules whenever a graphic should stay untinted. Pressing `Space` with keyboard focus displays `Pressed` until key release; `Enter` triggers a brief press flash. Disabled, blurred, hidden, or cloned controls clear transient keyboard press states.

The authored dropdown skin (`urui-select` / `.urui-select-options`) fades in/out over 0.15s by default (`--urui-select-fade: 0` disables shared fade; closing disables hit testing immediately during fade-out, and reopening cancels pending hide callbacks). Plain `<select>` retains a minimal native popup list; see `Runtime Controls` for interactive skin examples. Input placeholders render italic at 50% alpha, text selections preserve text color over `#a8ceffc0`, and the caret blinks at 1 px, 0.85 Hz (`readonly` uses the same visuals).

Standard browsers read the exact same HTML/CSS files using the declarative `@supports (property:value)` / `not (property:value)` subset (compound boolean feature queries are not used). Modern browsers approximate graphic tints via CSS brightness/opacity filters, while native URUI multiplies vertex colors directly. Interactive `urui-*` browser previews use `/urui/preview.js`. HDR, Linear colorspace blending, Unity shaders, and particles are Unity-only targets rather than pixel-identical browser output.
