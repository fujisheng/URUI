# HTML 与 CSS 支持速查表

[English](HTML_CSS_CHEATSHEET.md) | **中文**

数据来源：[`Packages/com.fujisheng.urui/Documentation~/HtmlCssSupport.json`](../Packages/com.fujisheng.urui/Documentation~/HtmlCssSupport.json)（总计 **525 项**：`118` 标准实现、`219` URUI 扩展、`187` 适配支持、`1` 已废弃 `@spritesheet`）。

---

## 1. 布局与盒模型（以 Flexbox 为核心）

| 能力 | 状态 | 使用说明 |
| --- | --- | --- |
| `display` | **支持**（`block` / `inline` / `inline-block` / `flex` / `inline-flex` / `table*` / `none` / `flow-root`） | **卡片网格请使用 `display: flex; flex-wrap: wrap;`。** 引擎**不支持** CSS Grid（`grid-template-columns` 等）。 |
| `flex`、`flex-direction`、`flex-wrap`、`justify-content`、`align-items`、`align-content` | **完全支持**（`standard`） | 多端排版行为与桌面现代浏览器 Flexbox 一致。 |
| `gap`、`column-gap`、`row-gap` | **完全支持** | 直接控制弹性容器子节点行列间距。 |
| `position`（`static` / `relative` / `absolute` / `fixed` / `sticky`） | **完全支持** | 支持配合 `inset`、`top`、`right`、`bottom`、`left`、`z-index`。 |
| `box-sizing`、`padding`、`margin`、`border`、`border-radius`、`box-shadow` | **支持** | 支持圆角胶囊、描边边框与实体阴影。 |
| `overflow`（`visible` / `hidden` / `scroll` / `auto`） | **支持** | 原生 `render-plan9` 会自动结合像素裁剪边界复用纹理。 |
| `object-fit`（`fill` / `contain` / `cover`） | **完全支持**（`standard`） | 保持图片原本比例适应容器而不改写布局框。 |

---

## 2. 选择器、`@` 规则与 CSS 自定义变量

- **选择器（共 31 类）**：`#id`、`.class`、标签选择器、`*`、后代（`A B`）、子代（`A > B`）、兄弟（`+`、`~`）、属性 `[attr]`、状态伪类 `:hover` / `:active` / `:focus` / `:focus-visible` / `:checked` / `:disabled` / `:drag` / `:selected` / `:placeholder-shown`、结构伪类 `:first-child` / `:last-child` / `:nth-child` / `:not(...)` / `:scope` / `:empty`。
- **`@` 规则（共 7 项）**：`@font-face`、`@keyframes`、`@property`、`@media`、`@supports` / `not (property:value)`、`@decorator`（扩展）以及 `@spritesheet`（*已移除* — 统一改用 C# `ImageAsset` + `SpriteRegion`）。
- **`@property` 声明建议**：凡用于 `transition` 动画或跨层继承的主题颜色/数字变量（如 `@property --sol-surface { syntax: "<color>"; inherits: true; initial-value: #0f172ae8; }`），应先使用 `@property` 明确注册语法。

---

## 3. 单位、字体与视觉滤镜

- **支持单位（14 种）**：`px`、`%`、`<number>`、`em`、`rem`、`vw`、`vh`、`deg`、`rad`、`cm`、`mm`、`in`、`pt`、`pc`。
- **字体栈匹配**：字体原始文件以 `.ttf.bytes` / `.otf.bytes` 放入资源并在 `Document.TextResources` 注册。涉及中英文混排时，可在 `font-family` 中同时指定英文字体与子集化中文字体（例如 `"LatoLatin", "SolsticeSC", "URUI Sans", sans-serif`）。
- **标准滤镜（`filter` / `backdrop-filter`）**：`grayscale()`、`sepia()`、`brightness()`、`contrast()`、`saturate()`、`opacity()`、`blur()`、`drop-shadow()`。
- **URUI.Effect 高级滤镜（`--urui-fx-*`）**：声明 `--urui-effect: ui;` 或 `--urui-image-effect: ui-wave | surface-output;` 启用全部 106 项着色器控制属性，详表见 [Effect CSS 参数参考](../Packages/com.fujisheng.urui.effect/Documentation~/CSS_ZH.md)。

---

## 4. 内置控件标签对应表

| HTML 写法 | 映射 C# 控件类型 | 备注 |
| --- | --- | --- |
| `<label>` | `Label` | 支持 `for="id"` 关联点击。 |
| `<img>` | `Image` | 支持 `Source`、`SpriteName`、`Fit`、九宫格切片与填充进度。 |
| `<button type="button">` | `Button` | 建议显式声明 `type="button"`。 |
| `<input type="text" \| "password">` / `<textarea>` | `TextField` | 支持 `Placeholder`、只读、输入限制与 `Submitted` 事件。 |
| `<input type="number">` | `FloatField` / `IntegerField` | 整数控件请附带 `data-urui-type="URUI.Controls.IntegerField"`。 |
| `<input type="checkbox">` / `<input type="radio" name="...">` | `Toggle` / `RadioButton` | 单选按钮按 `name` 分组并支持方向键切换。 |
| `<input type="range">` | `Slider` / `IntegerSlider` | 支持水平/垂直方向及 `LowValue` / `HighValue` / `Step`。 |
| `<select><option>...</option></select>` | `Dropdown` | 支持 `.urui-select` 自定义作者皮肤淡入淡出。 |
| `<urui-scroll-view>` | `ScrollView` | 支持弹性滚边与惯性滚动扩展行为。 |
| `<urui-list-view>` | `ListView` | 固定行高虚拟滚动容器，适用于成千上万行清单。 |
| `<urui-foldout>` / `<urui-progress-bar>` | `Foldout` / `ProgressBar` | 支持 `data-urui-label` 与数值上下限属性。 |
| `<template id="...">` | `Template` | 惰性模板容器，调用 `CloneContentTo(target)` 实例化。 |
| `<urui-particles src="...">` | `ParticlesElement` *(Effect 包)* | 在 DOM 布局盒内渲染 Unity 粒子系统。 |

> **语法纪律**：URUI 使用严格封闭解析：非空标签必须显式写闭合标签（`<div></div>`、`<span></span>`）；空标签（`<meta>`、`<link>`、`<img>`、`<input>`、`<br>`、`<hr>`）保持标准 HTML5 空标签形态。
