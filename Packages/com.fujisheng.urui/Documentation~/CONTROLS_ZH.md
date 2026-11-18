# 控件使用

本页对应 Core `0.8.0-preview.1`，命名空间为 `URUI.Controls`。节点统一用 `document.Create<T>()` 或 `document.Q<T>()` 获取；通用层级和状态见 [Element API](API_ZH.md)。全部签名及继承成员所属基类见[接口声明参考](REFERENCE_ZH.md)。

## HTML 与类型

每份完整文档链接 `/urui/base.css` 和 `/urui/controls.css`；默认皮肤规则见[默认控件](DEFAULT_CONTROLS_ZH.md)。

| C# 类型 | HTML 入口 | 常用接口 |
| --- | --- | --- |
| `Label` | `<label>` | `Text`、`For`，For 对应输入节点 id |
| `Image` | `<img>` | `Source`、`SpriteName`、Fit/Fill 系列 |
| `Button` | `<button type="button">` | `Text`、`Clicked` |
| `Toggle` | `<input type="checkbox">` | `Value : bool` |
| `RadioButton` | `<input type="radio" name="group">` | `Value : bool`、`GroupName` |
| `Slider` | `<input type="range">` | `Value : float`、LowValue/HighValue/Step |
| `IntegerSlider` | range 加 `data-urui-type="URUI.Controls.IntegerSlider"` | `Value : int`，范围仍由 float 表达 |
| `TextField` | `<input type="text">`、password、`<textarea>` | `Value : string`、Multiline、选区与提交 |
| `FloatField` | `<input type="number">` | `Value : float`、`Step : float` |
| `IntegerField` | number 加 `data-urui-type="URUI.Controls.IntegerField"` | `Value : int`、`Step : int` |
| `Dropdown` | `<select><option>...</option></select>` | `Choices`、`SelectedIndex`、`Value : string` |
| `ScrollView` | `<urui-scroll-view>` | ContentContainer、ScrollOffset、ScrollTo |
| `Foldout` | `<urui-foldout>` | `Text` 标题、`Value : bool` 展开状态 |
| `ProgressBar` | `<urui-progress-bar min="0" max="100" value="25">` | Text、Value、LowValue、HighValue |
| `ListView` | `<urui-list-view>` | 数据源、行工厂、虚拟化与单选 |

类型在首次包装时确定，不应事后改 input type 或 data-urui-type 来转换已有节点。C# 创建 TextField 使用 textarea，可切换 Multiline；原生 HTML input 只能单行。原生 input 等空元素与普通需显式闭合的元素应按 HTML 支持规则书写。

## 值更新和订阅

`BaseField<T>` 提供 `Value`、`SetValueWithoutNotify(T)`、`RegisterValueChangedCallback(Action<ChangeEvent<T>>)` 和对应 Unregister 方法。注册返回 IDisposable，Dispose 解绑；相同值不重复通知。Foldout 和 ProgressBar 也提供相应值订阅。只实现 `INotifyValueChanged<T>` 的辅助组合对象仅保证 Value/SetValueWithoutNotify，不保证拥有这些注册方法。

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

`ChangeEvent<T>.PreviousValue/NewValue` 是变化前后值。恢复存档、刷新 ViewModel 时常用静默赋值，防止业务回写循环。Button.Clicked 为 Action 事件，用同一委托 `+=`/`-=` 解绑；需要事件目标或传播控制时用 `RegisterCallback<ClickEvent>()`。

RadioButton 的 GroupName 在所属表单/文档内分组，方向键跳过禁用项；Toggle 的 Enter/Space 与指针都可激活。Slider/IntegerSlider 的 Direction 支持 Horizontal/Vertical，Inverted 翻转方向；LowValue/HighValue/Step 为继承的 float 属性。IntegerSlider 不保证完整 int32 精度。

## 文本与数字输入

TextField、IntegerField、FloatField 继承 `TextInputField<T>`：

| 接口 | 用法 |
| --- | --- |
| `Placeholder : string` | 空值提示文字 |
| `MaxLength : int` | 最大文本长度，按控件规则处理 Unicode 字符 |
| `IsReadOnly : bool` | 允许选择、复制和程序赋值，阻止用户编辑 |
| `IsDelayed : bool` | 延后模式仅在提交/失焦时更新 Value |
| `Selection : Vector2Int` | 只读选区端点，按 Unicode 字符计数 |
| `SelectRange(int start, int end)` | 设置选区，不能直接把 C# UTF-16 索引当作字符索引 |
| `CommitValue() : bool` | 请求提交当前编辑内容，返回提交处理结果 |
| `Submitted : Action<T>` | 单行 Enter 的有效提交；多行 TextField 的 Enter 用于换行 |
| `CaretBlinkRate : float`、`CaretWidth : int` | 光标闪烁和宽度 |

数字字段的 Text 是编辑草稿，Value 是已提交数值；不完整/非有限数字保留上次有效 Value，提交或失焦后恢复规范文字。Step 控制上下键步进，IntegerField 在 int 边界饱和。TextField.Text 与 Value 保持对应，组合串预览不立即成为已提交值。

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
        title.Placeholder = "请输入名称";
        title.MaxLength = 20;
        title.Multiline = false;
        title.SelectRange(0, 0);
    }
}
```

## 图片、九宫格和填充

Source 对应注册图片路径，SpriteName 对应 ImageAsset 中命名的 SpriteRegion。DensityScaledNaturalSize 控制自然尺寸密度缩放，图片所有权见[资源](LIFECYCLE_ZH.md)。

| 接口/CSS | 取值与作用 |
| --- | --- |
| `Fit` / `object-fit` | Fill、Contain、Cover；改变内容几何/UV，不改变布局命中框 |
| `FillMode` / `image-fill-mode` | None、Horizontal、Vertical、Radial90、Radial180、Radial360 |
| `FillAmount` / `image-fill-amount` | 0..1 |
| `FillOrigin` / `image-fill-origin` | 水平 0左/1右，垂直 0下/1上；radial90 为左下/左上/右上/右下；180 为下/左/上/右；360 为下/右/上/左 |
| `FillClockwise` / `image-fill-clockwise` | 径向方向，CSS 用 0/1 |
| `FillCenter` / `image-fill-center` | 是否保留九宫格中心，CSS 用 0/1 |
| `image-slice-top/right/bottom/left` | CSS 声明源图四边长度；空间不足时边界按比例收缩 |

## Dropdown、ScrollView 和 Foldout

Dropdown.Choices 是 IReadOnlyList<string>；设置时复制选项，优先保留仍存在的旧值，否则选择首项。SelectedIndex 读取无匹配时为 -1，设置时必须处于现有选项范围内。Value 为选项字符串。复杂作者皮肤用 DropdownVisual，原生 select 仍有基础弹出列表。

ScrollView.Add/Insert 将业务子节点放进 ContentContainer。`ScrollTo(Element child)` 显示后代；ScrollOffset 用局部滚动量，ViewportRect 用于计算可见范围。触摸拖动超过阈值后取消误点击。Foldout.Text 写标题，Value 控制展开，其 ContentContainer 放业务内容。

ProgressBar 默认范围 0..100；`Text` 为标题，`Value/LowValue/HighValue` 为 float。只想驱动作者填充节点可用 `new ProgressIndicator(host, fill, minimum, maximum)`，它提供 Value 和静默赋值。

## 固定行高 ListView

先设置可见高度、FixedItemHeight、工厂和绑定，再设置 ItemsSource。列表只创建可见行及缓冲行；不支持动态行高、多选或拖动重排。

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

MakeItem 必须返回唯一、同文档、未挂载的有效节点。每次 BindItem 与对应 UnbindItem 配对，行换索引、退役和文档卸载时清理；解绑可能在旧节点失效后发生，只释放业务订阅，不写旧 DOM。

| 接口 | 使用时机 |
| --- | --- |
| `ItemsSource : IList` | 更换数据源时重建；原地修改后手动 RefreshItems |
| `RefreshItems()` | 刷新现有行绑定 |
| `Rebuild()` | 更换工厂后销毁旧行并重建，不在绑定/解绑回调中调用 |
| `RealizedItemCount` | 查看实际行实例数 |
| `SelectedIndex` | 单选索引，-1 清除选择 |
| `SelectionChanged : Action<int>` | 选择变化，也包含数据缩短使选择失效 |
| `ItemChosen : Action<int>` | 列表获得焦点后 Enter 确认 |
| `ScrollToItem(int)` | 滚动显示指定索引 |

列表支持方向键、Home/End、PageUp/PageDown；行内输入控件获得焦点时保留编辑按键。

## 作者控件组合与布局辅助

以下对象组合已有节点，不拥有 DOM。实现 IDisposable 的对象应在页面解绑时释放。

| 类型/构造参数 | 功能与约束 |
| --- | --- |
| `SliderVisual(Slider control, Element fill, Element thumb)` | 原生值驱动作者填充/滑钮；Value/静默赋值通过组合对象写入 |
| `IntegerSliderVisual(IntegerSlider, Element, Element)` | 整数滑块对应组合 |
| `ToggleVisual(Toggle control, Element root)` | 将状态映射到作者容器 checked class |
| `DropdownVisual(Dropdown control, Button trigger, Element label, Element popup, Button optionPrototype)` | 作者弹层，提供 Value、IsOpen、Refresh |
| `ScrollbarVisual(Element viewport, Element track, Element thumb)` | 作者滚动条，Refresh 更新几何 |
| `TextLayout(Element host, Element content, TextAnchor alignment)` | 直接子内容九向对齐，Alignment 可更新；不实现 IDisposable |
| `TextFit(Element host, Element content)` | 按实际布局在 `--urui-text-fit-min/max` 范围缩字；Refresh 重测，Fits 读取结果；内容必须为直接子节点 |
| `RowContentSize(Element content)` | 观察行内容并更新所需尺寸 |
| `ReferenceCanvas(Element root, Vector2 referenceSize, float match, Element safeContent = null, SafeAreaPolicy safeAreaPolicy = Physical)` | Apply(display,safe,maximumScale) 更新统一缩放/安全区；Scale、LogicalSize 读取结果 |

TextFit 通过 text-fit-pending class 标记待测量状态，作者 CSS 决定此时如何隐藏，不能只创建控制器而省略样式。ReferenceCanvas 默认保留设备四边安全区，要求左右对称时显式选择 SymmetricHorizontal。通用惯性滚动与逐项入场见[样式与动画](STYLES_ANIMATION_ZH.md)。
