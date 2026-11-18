# URUI Core 接口声明参考

对应包版本 `0.8.0-preview.1`。本页根据当前 Runtime 源码整理公开类型及其直接声明的公开/受保护成员，供查参数、返回类型、默认值和继承关系；不包含 internal/private 类型，也不重复继承成员。Unity 基类接口请查 Unity 文档。

声明块用于查阅，省略实现体；完整使用顺序、资源释放和可运行组件见[文档导航](API_ZH.md)。同名的 partial 类型合并列出，受保护构造函数由 Document 工厂调用。成员注释来自源码，不替代专题说明中的约束。

## URUI.Animation

### AnimationController

源码：[AnimationController.cs](../Runtime/Animation/AnimationController.cs)。

```csharp
// 用作者状态类启动命名组；一个根/状态由最新句柄拥有，生命周期不进入业务 VM。
public sealed class AnimationController
public AnimationPlayback Play(Element root, string activeClass, AnimationGroup animationNames, CancellationToken token = default);
```

### AnimationGroup

源码：[AnimationGroup.cs](../Runtime/Animation/AnimationGroup.cs)。

```csharp
// 命名目标组，或在准备布局后由作者目标解析的有限动画组；不包含时长和曲线。
public sealed class AnimationGroup
public static AnimationGroup Named(IEnumerable<AnimationTarget> targets);
// 等待每个解析目标启动新的有限命名动画，适合延迟入场或序列最后一个实际条目；空列表立即完成。
public static AnimationGroup FromAuthorTargets(Func<IReadOnlyList<Element>> targets);
```

### AnimationPlayback

源码：[AnimationPlayback.cs](../Runtime/Animation/AnimationPlayback.cs)。

```csharp
// 带独立身份的播放句柄。取消、隐藏、销毁和旧句柄都不会发出成功完成。
public sealed class AnimationPlayback : IDisposable
public long Identity { get; }
public Task Finished { get; }
public bool IsPlaying { get; }
public void Cancel();
public void Dispose();
```

### AnimationTarget

源码：[AnimationGroup.cs](../Runtime/Animation/AnimationGroup.cs)。

```csharp
// 一个真实节点上的命名 CSS 动画；全部属性轨道结束后才完成。
public sealed class AnimationTarget
public Element Element { get; }
public string Name { get; }
public AnimationTarget(Element element, string name);
```

### BezierMotion

源码：[BezierMotion.cs](../Runtime/Animation/BezierMotion.cs)。

```csharp
// 根据作者动画信号沿二次贝塞尔路径移动预声明节点；端点取自实时布局。
public sealed class BezierMotion : IDisposable
public BezierMotion(Element path, Element rotation, Element source, Element target);
// 在起终点完成布局后开始一次路径；曲线节奏继续由CSS信号决定。
public void Begin();
// 结束本次路径并回到作者声明的终点。
public void Stop();
public void Dispose();
```

### IUiClock

源码：[IUiClock.cs](../Runtime/Animation/IUiClock.cs)。

```csharp
// 界面动画和效果显式选择的时间源。
public interface IUiClock
double Now { get; }
float Delta { get; }
```

### StaggeredReveal

源码：[StaggeredReveal.cs](../Runtime/Animation/StaggeredReveal.cs)。

```csharp
// 由作者状态 class 激活的逐项进场，布局与关键帧均来自外置资源。
public sealed class StaggeredReveal : IDisposable
// 容器用 --urui-stagger-edge 和以秒为数值的 --urui-stagger-span/--urui-stagger-gap 声明时序。
public StaggeredReveal(Element root, Element container, string activeClass, string prepareClass, string revealedClass);
// 停止序列并恢复原有数值覆盖及 class 状态。
public void Dispose();
```

### UnityUiClock

源码：[UnityUiClock.cs](../Runtime/Animation/UnityUiClock.cs)。

```csharp
// Unity 的缩放或非缩放时钟；不改变媒体解码策略。
public sealed class UnityUiClock : IUiClock
public static IUiClock Scaled { get; }
public static IUiClock Unscaled { get; }
public double Now { get; }
public float Delta { get; }
```

### VisibilityClass

源码：[VisibilityClass.cs](../Runtime/Animation/VisibilityClass.cs)。

```csharp
// 把实际显示状态映射为作者动画类；不创建进度节点、曲线或样式文本。
public sealed class VisibilityClass : IDisposable
public VisibilityClass(Element target, Element activation, string className);
public void Dispose();
```

## URUI.Controls

### BaseField<T>

源码：[BaseField.cs](../Runtime/Controls/BaseField.cs)。

```csharp
// 复用原生表单值，统一校验、静默更新和有类型变化通知。
public abstract class BaseField<T> : Element, INotifyValueChanged<T>
// 文档工厂构造入口。
protected BaseField(ElementContext context);
// 将原生字符串解析为有类型的值。
protected abstract T ParseValue(string text);
// 将有类型的值转换为原生格式。
protected abstract string FormatValue(T value);
// 对值进行范围或内容校验。
protected virtual T NormalizeValue(T input);
// 是否在每次原生编辑时提交，延后输入控件可覆写。
protected virtual bool CommitOnInput { get; }
// 读取原生初始值并启用焦点。
protected override void OnInitialize();
// 已提交值；输入法预览期间维持原来的已提交值。
public T Value { get; set; }
// 更新原生界面和提交值，不产生变化通知。
public void SetValueWithoutNotify(T input);
// 注册可释放的值变化订阅。
public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<T>> callback);
// 解绑指定值变化回调。
public void UnregisterValueChangedCallback(Action<ChangeEvent<T>> callback);
// 更新选项等内部结构，将中间变化合并为最终一次值事件。
protected void ChangeStructure(Action action);
// 提交原生编辑内容；不能解析或非有限的编辑保留已提交值。
protected bool CommitInput();
// 模型值已同步；静默写入仍更新呈现，此钩子不发送值变化事件。
protected virtual void OnValueSynchronized();
// 消费原生值变化并转换成统一的托管值事件。
protected override void OnEvent(EventBase evt);
```

### Button

源码：[Button.cs](../Runtime/Controls/Button.cs)。

```csharp
// 可通过指针、Enter 和空格激活的文本按钮。
public class Button : Element
// 取消已失去焦点、禁用或到期的键盘反馈；使用Surface的界面时钟。
protected override void OnUpdate();
// 禁用立即取消键盘按压，重新启用时不恢复旧输入。
protected override void OnAttributeChanged(string name);
// 文档工厂构造入口。
protected internal Button(ElementContext context);
// 点击与键盘激活通知；禁用时不触发。
public event Action Clicked { add; remove; }
// 按钮默认参与焦点导航。
protected override void OnInitialize();
// 处理原生点击及键盘激活。
protected override void OnEvent(EventBase evt);
// 失效时释放业务回调引用。
protected override void OnInvalidated();
```

### CheckableField

源码：[CheckableField.cs](../Runtime/Controls/CheckableField.cs)。

```csharp
// 管理布尔选择控件的键盘按下、释放与取消状态。
public abstract class CheckableField : BaseField<bool>
// 文档工厂构造入口。
protected CheckableField(ElementContext context);
// 读取原生选中状态。
protected override bool ParseValue(string value);
// 写入原生选中状态。
protected override string FormatValue(bool value);
// 键盘完整激活一次时更新选择。
protected abstract void Activate();
// 按键重复不重复激活，失焦和取消丢弃尚未完成的按下。
protected override void OnEvent(EventBase evt);
```

### Dropdown

源码：[Dropdown.cs](../Runtime/Controls/Dropdown.cs)。

```csharp
// 原生 select 下拉控件，Value 对应 option 的 value 属性。
public class Dropdown : BaseField<string>
// 同步普通、静默和原生选择写入后的作者皮肤。
protected override void OnValueSynchronized();
// 启用状态改变时同步独立的作者触发按钮。
protected override void OnAttributeChanged(string name);
// 失效节点不保留作者呈现订阅。
protected override void OnInvalidated();
// 文档工厂构造入口。
protected internal Dropdown(ElementContext context);
// 读取原生选择值。
protected override string ParseValue(string value);
// 保留选项值字符串。
protected override string FormatValue(string value);
// 选项值的有序快照；设置时文本和值相同，现有 option 节点被销毁。
public IReadOnlyList<string> Choices { get; set; }
// 当前选项索引，没有匹配时为 -1。
public int SelectedIndex { get; set; }
```

### DropdownVisual

源码：[DropdownVisual.cs](../Runtime/Controls/DropdownVisual.cs)。

```csharp
// 把标准 select 的选项和值呈现到作者声明的按钮和列表，皮肤不依赖原生内部节点。
public sealed class DropdownVisual : INotifyValueChanged<string>, IDisposable
// 负责值及选项的标准 select。
public Dropdown Control { get; }
// 列表是否展开。
public bool IsOpen { get; }
// 使用同一文档的作者节点；选项原型位于列表外，动态行由本对象负责销毁。
public DropdownVisual(Dropdown control, Button trigger, Element label, Element popup, Button optionPrototype);
// 更新标准 select 的值，并发送正常值变化通知。
public string Value { get; set; }
// 更新标准值和作者皮肤，不发送用户变化通知。
public void SetValueWithoutNotify(string value);
// 作者直接修改 option 文本或属性后，重新同步列表。
public void Refresh();
// 解除订阅并销毁生成的选项行，保留标准控件和作者原型。
public void Dispose();
```

### FloatField

源码：[FloatField.cs](../Runtime/Controls/FloatField.cs)。

```csharp
// 有类型的 FloatField 表单控件。
public class FloatField : NumericField<float>
// 文档工厂构造入口。
protected internal FloatField(ElementContext context);
// 读取原生控件值。
protected override float ParseValue(string value);
// 使用与区域设置无关的原生格式。
protected override string FormatValue(float value);
// 拒绝非有限数值。
protected override float NormalizeValue(float value);
// 转为步进计算值。
protected override double ToNumber(float value);
// 步进在有限浮点范围内饱和。
protected override float FromNumber(double value);
```

### Foldout

源码：[Foldout.cs](../Runtime/Controls/Foldout.cs)。

```csharp
// 由按钮标题和可折叠内容区组成的容器。
public class Foldout : Element, INotifyValueChanged<bool>
// 文档工厂构造入口。
protected internal Foldout(ElementContext context);
// 业务节点位于可折叠内容区。
public override Element ContentContainer { get; }
// 标题文本，只更新标题节点。
public override string Text { get; set; }
// 是否展开内容，折叠不销毁子节点。
public bool Value { get; set; }
// 静默设置展开状态。
public void SetValueWithoutNotify(bool value);
// 订阅展开状态变化。
public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<bool>> callback);
// 解绑展开状态变化。
public void UnregisterValueChangedCallback(Action<ChangeEvent<bool>> callback);
// 复用作者标题和内容区；空的运行时控件生成同名结构。
protected override void OnInitialize();
// 属性写入与有类型写入共享同一状态；属性写入不产生用户变化事件。
protected override void OnAttributeChanged(string name);
// 标题获得焦点时，左右方向键折叠或展开。
protected override void OnEvent(EventBase evt);
```

### Image

源码：[Image.cs](../Runtime/Controls/Image.cs)。

```csharp
// 映射到 Document 图片资源的图像元素。
public class Image : Element
// 文档工厂构造入口。
protected internal Image(ElementContext context);
// ImageAsset.Source 对应的 HTML 图片路径。
public string Source { get; set; }
// 选择文档图片清单中的命名图集区域；空值恢复 src 来源。
public string SpriteName { get; set; }
// 让图像固有尺寸跟随 Surface 密度；HTML width/height 仍表示 CSS 像素。
public bool DensityScaledNaturalSize { get; set; }
// 普通 CSS object-fit；默认拉伸，Contain 留边，Cover 居中裁剪纹理。
public ImageFit Fit { get; set; }
// 基础填充方式；可在外部 CSS 声明 --urui-image-fill-mode。
public ImageFillMode FillMode { get; set; }
// 九宫格是否绘制中心；普通图像不受此属性影响。
public bool FillCenter { get; set; }
// 线性填充起点为0或1，径向为0到3；超出范围拒绝写入。
public int FillOrigin { get; set; }
// 径向填充的顺时针方向。
public bool FillClockwise { get; set; }
// 填充比例，有限值夹取到0到1；0时不提交图像几何。
public float FillAmount { get; set; }
```

### ImageFillMode

源码：[Image.cs](../Runtime/Controls/Image.cs)。

```csharp
// 作者声明的基础填充几何；方向由 FillOrigin 与 FillClockwise 决定。
public enum ImageFillMode { None, Horizontal, Vertical, Radial90, Radial180, Radial360 }
```

### ImageFit

源码：[Image.cs](../Runtime/Controls/Image.cs)。

```csharp
// 图像在作者内容框内的适配方式，不改变节点布局或命中范围。
public enum ImageFit { Fill, Contain, Cover }
```

### INotifyValueChanged<T>

源码：[BaseField.cs](../Runtime/Controls/BaseField.cs)。

```csharp
// 统一的值控件接口；所有调用限所属文档主线程。
public interface INotifyValueChanged<T>
// 已提交的值，相同值不重复通知。
T Value { get; set; }
// 更新值与界面但不发送 ChangeEvent。
void SetValueWithoutNotify(T value);
```

### IntegerField

源码：[IntegerField.cs](../Runtime/Controls/IntegerField.cs)。

```csharp
// 有类型的 IntegerField 表单控件。
public class IntegerField : NumericField<int>
// 文档工厂构造入口。
protected internal IntegerField(ElementContext context);
// 读取原生控件值。
protected override int ParseValue(string value);
// 使用与区域设置无关的原生格式。
protected override string FormatValue(int value);
// 整数可精确转换为 double。
protected override double ToNumber(int value);
// 步进在整数边界饱和，不产生溢出。
protected override int FromNumber(double value);
```

### IntegerSlider

源码：[IntegerSlider.cs](../Runtime/Controls/IntegerSlider.cs)。

```csharp
// 支持范围、指针拖动和键盘操作的 int 滑块。
public class IntegerSlider : RangeField<int>
// 文档工厂构造入口。
protected internal IntegerSlider(ElementContext context);
// 读取原生数值。
protected override int ParseValue(string value);
// 写入区域无关的数值。
protected override string FormatValue(int value);
// 转成滑动计算数值。
protected override float ToNumber(int value);
// 转回控件值。
protected override int FromNumber(float value);
```

### IntegerSliderVisual

源码：[IntegerSliderVisual.cs](../Runtime/Controls/IntegerSliderVisual.cs)。

```csharp
// 整数滑块与作者填充、滑钮节点的呈现组合。
public sealed class IntegerSliderVisual : INotifyValueChanged<int>, IDisposable
// 负责输入、范围约束和键盘操作的整数滑块。
public IntegerSlider Control { get; }
// 使用同一文档内的作者填充和滑钮节点。
public IntegerSliderVisual(IntegerSlider control, Element fill, Element thumb);
// 更新值与外观，保留正常值变化通知。
public int Value { get; set; }
// 同步值和外观，不发出用户变化通知。
public void SetValueWithoutNotify(int value);
// 解除呈现订阅，作者节点仍由文档持有。
public void Dispose();
```

### Label

源码：[Label.cs](../Runtime/Controls/Label.cs)。

```csharp
// 普通文本标签，不解析 HTML。
public class Label : Element
// 文档工厂构造入口。
protected internal Label(ElementContext context);
// 关联输入控件的 Name；点击标签由原生 label 转交给该控件。
public string For { get; set; }
```

### ListView

源码：[ListView.cs](../Runtime/Controls/ListView.cs)。

```csharp
// 固定行高的虚拟列表；只创建可见区域及前后一行，行在文档内复用。
public class ListView : ScrollView
// 文档工厂构造入口。
protected internal ListView(ElementContext context);
// 数据源；设置会重建行。原地修改后调用 RefreshItems。
public IList ItemsSource { get; set; }
// 创建可复用行的工厂；返回同一文档、未挂载且未被其他行使用的元素。
public Func<Element> MakeItem { get; set; }
// 把数据索引绑定到行；应与 UnbindItem 配合清理业务订阅。
public Action<Element, int> BindItem { get; set; }
// 行更换数据或退役前调用，索引代表旧绑定。
public Action<Element, int> UnbindItem { get; set; }
// 每行像素高度，必须为正有限值。
public float FixedItemHeight { get; set; }
// 当前已实例化行数量，可用于检查虚拟化范围。
public int RealizedItemCount { get; }
// 单选索引，-1 取消选择。
public int SelectedIndex { get; set; }
// 选中索引改变时触发；数据缩短导致选择失效时也触发。
public event Action<int> SelectionChanged;
// 列表自身获得焦点后，Enter 确认当前选中项。
public event Action<int> ItemChosen;
// 刷新现有数据绑定，保留可复用行。
public void RefreshItems();
// 销毁现有行并使用当前工厂重新构建。
public void Rebuild();
// 滚动到指定项。
public void ScrollToItem(int index);
// 布局完成后按视口和偏移更新可见行。
protected override void OnUpdate();
// 文档或节点失效后释放数据及业务委托；此时不再访问原生行。
protected override void OnInvalidated();
// 只处理列表自身焦点的导航，不截获行内输入控件的编辑按键。
protected override void OnEvent(EventBase evt);
```

### NumericField<T>

源码：[NumericField.cs](../Runtime/Controls/NumericField.cs)。

```csharp
// 数值编辑共用的键盘步进；中间文本和提交规则由文本输入基类管理。
public abstract class NumericField<T> : TextInputField<T>
// 文档工厂构造入口。
protected NumericField(ElementContext context);
// 转换为精确覆盖 int 与 float 范围的步进值。
protected abstract double ToNumber(T value);
// 夹取到具体数值类型的可表示范围。
protected abstract T FromNumber(double value);
// 保存作者 value 作为无 min 时的步进基点，实时编辑不会移动基点。
protected override void OnInitialize();
// 作者 value 的显式修改更新步进基点。
protected override void OnAttributeChanged(string name);
// 上下方向键步长，必须为正有限数值；无效作者值和 any 的键盘增量默认为 1。
public T Step { get; set; }
// 只处理自身的上下方向键，保留只读文本的选择与复制。
protected override void OnEvent(EventBase evt);
```

### ProgressBar

源码：[ProgressBar.cs](../Runtime/Controls/ProgressBar.cs)。

```csharp
// 可配置范围的进度显示，默认 0 到 100；标题更新保留内部结构。
public class ProgressBar : Element, INotifyValueChanged<float>
// 文档工厂构造入口。
protected internal ProgressBar(ElementContext context);
// 额外业务子节点的容器。
public override Element ContentContainer { get; }
// 进度值，有限值夹取到当前上下限。
public float Value { get; set; }
// 覆盖进度条标题文字。
public override string Text { get; set; }
// 范围下限，必须不大于上限。
public float LowValue { get; set; }
// 范围上限，必须不小于下限。
public float HighValue { get; set; }
// 订阅进度值变化。
public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<float>> callback);
// 解绑进度值变化。
public void UnregisterValueChangedCallback(Action<ChangeEvent<float>> callback);
// 静默更新进度和填充宽度。
public void SetValueWithoutNotify(float value);
// 复用作者填充、标题和内容区；空的运行时控件生成同名结构。
protected override void OnInitialize();
// 属性写入同步范围、值和标题，不发送用户变化通知。
protected override void OnAttributeChanged(string name);
```

### ProgressIndicator

源码：[ProgressIndicator.cs](../Runtime/Controls/ProgressIndicator.cs)。

```csharp
// 把进度行为接到作者节点；填充节点、九宫格和皮肤均由资源声明。
public sealed class ProgressIndicator : INotifyValueChanged<float>
public ProgressIndicator(Element host, Element fill, float minimum = 0, float maximum = 1);
public float Value { get; set; }
public void SetValueWithoutNotify(float value);
```

### RadioButton

源码：[RadioButton.cs](../Runtime/Controls/RadioButton.cs)。

```csharp
// 有类型的 RadioButton 表单控件。
public class RadioButton : CheckableField
// 文档工厂构造入口。
protected internal RadioButton(ElementContext context);
// 单选激活只选中，不取消自身。
protected override void Activate();
// 同一文档中互斥的单选组名称。
public string GroupName { get; set; }
// 方向键在同一表单的组内循环，跳过禁用和隐藏的成员。
protected override void OnEvent(EventBase evt);
```

### RangeField<T>

源码：[RangeField.cs](../Runtime/Controls/RangeField.cs)。

```csharp
// 带范围夹取、独立指针捕获和键盘操作的滑动值控件。
public abstract class RangeField<T> : BaseField<T>
// 原生范围和方向属性变更后立即刷新作者皮肤。
protected override void OnAttributeChanged(string name);
// 同步普通写入、静默写入以及原生输入的皮肤状态。
protected override void OnValueSynchronized();
// 过期节点不保留作者皮肤的引用。
protected override void OnInvalidated();
// 文档工厂构造入口。
protected RangeField(ElementContext context);
// 将有类型数值转换成浮点数。
protected abstract float ToNumber(T value);
// 将浮点数转换成控件数值。
protected abstract T FromNumber(float value);
// 水平或垂直布局，坐标和键盘调整与方向一致。
public SliderDirection Direction { get; set; }
// 反转物理方向与数值方向，切换时不改变已提交值。
public bool Inverted { get; set; }
// 原生滑块使用物理方向数值，反向模式在边界做对称映射。
protected float MapNativeValue(float value);
// 范围下限，必须不大于上限。
public float LowValue { get; set; }
// 范围上限，必须不小于下限。
public float HighValue { get; set; }
// 键盘每次调整的步长，必须为正有限数值。
public float Step { get; set; }
// 普通输入由原生统一校验；反向模式先按语义方向计算步进，再映射物理值。
protected override T NormalizeValue(T input);
// 每个滑块分别持有指针，不复用鼠标拖拽状态。
protected override void OnEvent(EventBase evt);
```

### ReferenceCanvas

源码：[ReferenceCanvas.cs](../Runtime/Controls/ReferenceCanvas.cs)。

```csharp
// 在作者根节点上应用参考画布缩放。结构、定位和变换原点由 CSS 声明。
public sealed class ReferenceCanvas : IDisposable
public float Scale { get; private set; }
public Vector2 LogicalSize { get; private set; }
public ReferenceCanvas(Element root, Vector2 referenceSize, float match, Element safeContent = null, SafeAreaPolicy safeAreaPolicy = SafeAreaPolicy.Physical);
// 物理像素输入；与显示使用同一节点变换，命中和 WorldToLocal 自动使用相同缩放。
public void Apply(Rect display, Rect safe, float maximumScale = float.PositiveInfinity);
public void Dispose();
```

### RowContentSize

源码：[RowContentSize.cs](../Runtime/Controls/RowContentSize.cs)。

```csharp
// 按作者横向子项的实测外界更新内容宽度，保留节点、间距、内边距和边框的 CSS 定义。
public sealed class RowContentSize : IDisposable
// 用于子项具有固定或固有宽度的横向滚动内容；无需在业务代码计算条目数量和单元格宽度。
public RowContentSize(Element content);
// 停止测量并恢复原有尺寸覆盖。
public void Dispose();
```

### SafeAreaPolicy

源码：[ReferenceCanvas.cs](../Runtime/Controls/ReferenceCanvas.cs)。

```csharp
// 设备安全区的布局策略；通用默认保留实际边界，项目可明确选择左右对称。
public enum SafeAreaPolicy { Physical, SymmetricHorizontal }
```

### ScrollbarVisual

源码：[ScrollbarVisual.cs](../Runtime/Controls/ScrollbarVisual.cs)。

```csharp
// 用作者轨道和滑钮呈现普通滚动视口；支持拖动、轨道翻页及键盘。
public sealed class ScrollbarVisual : IDisposable
// 三个节点须在同一文档，滑钮是轨道的直接子节点；轨道位于视口外。
public ScrollbarVisual(Element viewport, Element track, Element thumb);
// 同步当前位置、范围、滑钮最小尺寸和可见状态；回调中的写入延后至原生返回。
public void Refresh();
// 解除输入及布局订阅，释放拖动捕获；保留作者节点。
public void Dispose();
```

### ScrollMotion

源码：[ScrollMotion.cs](../Runtime/Controls/ScrollMotion.cs)。

```csharp
// 普通 overflow 容器的鼠标、触摸惯性与弹性边界。
public sealed class ScrollMotion : IDisposable
// 配置与变换声明在作者 CSS 中；内容必须是视口的直接子节点。
public ScrollMotion(Element viewport, Element content);
// 按内容本地像素定位，取消当前手势及惯性，并保留作者声明的夹紧或弹性边界。
public void ScrollTo(Vector2 offset);
// 释放输入订阅并还原动态变换覆盖。
public void Dispose();
```

### ScrollView

源码：[ScrollView.cs](../Runtime/Controls/ScrollView.cs)。

```csharp
// 带内容容器的滚动区域，支持滚轮和触摸拖动。
public class ScrollView : Element
// 文档工厂构造入口。
protected internal ScrollView(ElementContext context);
// 业务子节点放入滚动内容区。
public override Element ContentContainer { get; }
// 复用作者显式内容区；空控件为有类型的运行时创建提供内容区。
protected override void OnInitialize();
// 将同一内容区中的后代滚动到可见范围，已经可见时保留当前位置。
public void ScrollTo(Element child);
// 布局完成后使用视口实际宽度，避免原生滚动容器的百分比子宽度收缩为零。
protected override void OnUpdate();
// 触摸超过拖动阈值后捕获指针，释放时取消误点击。
protected override void OnEvent(EventBase evt);
```

### Slider

源码：[Slider.cs](../Runtime/Controls/Slider.cs)。

```csharp
// 支持范围、指针拖动和键盘操作的 float 滑块。
public class Slider : RangeField<float>
// 文档工厂构造入口。
protected internal Slider(ElementContext context);
// 读取原生数值。
protected override float ParseValue(string value);
// 写入区域无关的数值。
protected override string FormatValue(float value);
// 转成滑动计算数值。
protected override float ToNumber(float value);
// 转回控件值。
protected override float FromNumber(float value);
```

### SliderDirection

源码：[RangeField.cs](../Runtime/Controls/RangeField.cs)。

```csharp
// 滑块的物理布局方向。
public enum SliderDirection { Horizontal, Vertical }
```

### SliderVisual

源码：[SliderVisual.cs](../Runtime/Controls/SliderVisual.cs)。

```csharp
// 将滑块状态投射到作者填充和滑钮节点；支持静默写入及水平/垂直布局。
public sealed class SliderVisual : INotifyValueChanged<float>, IDisposable
// 负责输入、范围约束和键盘操作的标准滑块。
public Slider Control { get; }
// 使用同一文档内的作者填充和滑钮节点。
public SliderVisual(Slider control, Element fill, Element thumb);
// 更新原生值和外观，保留正常值变化通知。
public float Value { get; set; }
// 同步值和外观，不发出用户变化通知。
public void SetValueWithoutNotify(float value);
// 解除呈现订阅，作者节点仍由文档持有。
public void Dispose();
```

### TextField

源码：[TextField.cs](../Runtime/Controls/TextField.cs)。

```csharp
// 支持 Unicode、选区、剪贴板与输入法组合的文本控件。
public class TextField : TextInputField<string>
// 文档工厂构造入口。
protected internal TextField(ElementContext context);
// 输入值不能为 null 或包含 NUL。
protected override string NormalizeValue(string value);
// 读取原生文本。
protected override string ParseValue(string value);
// 保留原始文本内容。
protected override string FormatValue(string value);
// 文本入口与 Value 一致，不替换控件内部结构。
public override string Text { get; set; }
// 多行输入；HTML input 节点只能单行，需使用 textarea 或 C# 创建支持多行切换。
public bool Multiline { get; set; }
// 多行 Enter 保留换行，单行 Enter 提交编辑。
protected override bool SubmitOnEnter { get; }
// 单行模式拦截换行，其他值变化由表单基类处理。
protected override void OnEvent(EventBase evt);
```

### TextFit

源码：[TextFit.cs](../Runtime/Controls/TextFit.cs)。

```csharp
// 按实际文本布局在作者字号范围内缩字，不创建节点；测量完成前由作者 class 控制可见性。
public sealed class TextFit : IDisposable
// 保留作者尺寸与对齐的外层节点。
public Element Host { get; }
// 保留普通布局流和自然换行高度的直接子文本节点。
public Element Content { get; }
// 最近测量是否在作者最小字号约束内完整容纳文本。
public bool Fits { get; private set; }
// 开始观察实际布局；控制器拥有内容字号覆盖，释放时恢复作者原值。
public TextFit(Element host, Element content);
// 绑定或池化更新时立即进入待测量状态，防止先显示旧字号；布局尺寸和文本变化也会自动重测。
public void Refresh();
// 停止观察并恢复字号覆盖与作者 class，节点仍属于原文档。
public void Dispose();
```

### TextInputField<T>

源码：[TextInputField.cs](../Runtime/Controls/TextInputField.cs)。

```csharp
// 文本与数值编辑共用的选区、只读、延后提交和提交通知。
public abstract class TextInputField<T> : BaseField<T>, ITextInputControl
// 文档工厂构造入口。
protected TextInputField(ElementContext context);
// 只读仍允许焦点、选取和复制；程序赋值不受限制。
public bool IsReadOnly { get; set; }
// 启用后仅在 Enter、失焦或 CommitValue 时提交编辑。
public bool IsDelayed { get; set; }
// 非延后模式随编辑提交值。
protected override bool CommitOnInput { get; }
// 单行控件按 Enter 提交，多行文本控件可覆写。
protected virtual bool SubmitOnEnter { get; }
// 有效编辑通过 Enter 提交时触发；失焦不重复触发。
public event Action<T> Submitted { add; remove; }
// 当前编辑文本，赋值必须能解析成有效值。
public override string Text { get; set; }
// 空值提示文字。
public string Placeholder { get; set; }
// 最大 Unicode 字符数，0 表示不限制。
public int MaxLength { get; set; }
// 完整闪烁周期的每秒次数；0 表示持续显示。作者也可在 CSS 设置 --urui-caret-blink-rate。
public float CaretBlinkRate { get; set; }
// 光标宽度，单位为参考像素；作者也可设置 --urui-caret-width。
public int CaretWidth { get; set; }
// 按 Unicode 字符索引选取区间，自动请求焦点。
public void SelectRange(int start, int end);
// 当前 Unicode 字符选区；读取前必须获得焦点。
public Vector2Int Selection { get; }
// 提交并规范化编辑文本；无效输入恢复上次有效值并返回 false。
public bool CommitValue();
// 替换尚在编辑的原生文字，延后模式不因此提交。
protected void ReplaceEditingText(string text);
// 只读阻止编辑命令，Enter 与失焦按同一提交规则处理。
protected override void OnEvent(EventBase evt);
// 释放提交回调。
protected override void OnInvalidated();
```

### TextLayout

源码：[TextLayout.cs](../Runtime/Controls/TextLayout.cs)。

```csharp
// 把九向文本对齐组合到普通作者节点；外层保留尺寸，内容节点保留自然换行高度。
public sealed class TextLayout
public TextLayout(Element host, Element content, TextAnchor alignment = TextAnchor.UpperLeft);
public Element Host { get; }
public Element Content { get; }
public TextAnchor Alignment { get; set; }
```

### Toggle

源码：[Toggle.cs](../Runtime/Controls/Toggle.cs)。

```csharp
// 有类型的 Toggle 表单控件。
public class Toggle : CheckableField
// 文档工厂构造入口。
protected internal Toggle(ElementContext context);
// 一次完整键盘激活切换当前值。
protected override void Activate();
```

### ToggleVisual

源码：[ToggleVisual.cs](../Runtime/Controls/ToggleVisual.cs)。

```csharp
// 将原生开关状态组合到普通作者节点，动画与图片由外部样式控制。
public sealed class ToggleVisual : INotifyValueChanged<bool>, IDisposable
// 负责值、焦点及键盘操作的原生开关。
public Toggle Control { get; }
// 选中时在外观容器启用 checked 类，不依赖业务命名。
public ToggleVisual(Toggle control, Element host);
// 更新值和作者状态，并保留原生通知。
public bool Value { get; set; }
// 静默更新状态和外观。
public void SetValueWithoutNotify(bool value);
// 解绑输入；不销毁作者节点。
public void Dispose();
```

## URUI.Documents

### Document

源码：[Document.Elements.cs](../Runtime/Documents/Document.Elements.cs)、[Document.Styles.cs](../Runtime/Documents/Document.Styles.cs)、[Document.Content.cs](../Runtime/Documents/Document.Content.cs)、[Document.Templates.cs](../Runtime/Documents/Document.Templates.cs)、[Document.Lifecycle.cs](../Runtime/Documents/Document.Lifecycle.cs)、[Document.Configuration.cs](../Runtime/Documents/Document.Configuration.cs)、[Document.Animations.cs](../Runtime/Documents/Document.Animations.cs)、[Document.RenderCommandBuffer.cs](../Runtime/Documents/Document.RenderCommandBuffer.cs)、[Document.ThreadSafety.cs](../Runtime/Documents/Document.ThreadSafety.cs)、[Document.Loading.cs](../Runtime/Documents/Document.Loading.cs)、[Document.Input.cs](../Runtime/Documents/Document.Input.cs)、[Document.Effects.cs](../Runtime/Documents/Document.Effects.cs)、[Document.Painting.cs](../Runtime/Documents/Document.Painting.cs)、[Document.cs](../Runtime/Documents/Document.cs)、[Document.Layout.cs](../Runtime/Documents/Document.Layout.cs)、[Document.Events.cs](../Runtime/Documents/Document.Events.cs)。

```csharp
// 在 Unity 场景中加载 HTML 文档。所有操作限 Unity 主线程；多个文档共享显式关联的 Surface。 禁用时释放渲染资源，启用时重新加载最近请求的内容；重载后请重新查询元素。
public sealed partial class Document : MonoBehaviour
// 注册自定义标签和类型工厂；现有标签与已经确定的节点类型不被替换。
public void RegisterElementType<T>(string tag, Func<ElementContext, T> factory) where T : Element;
// 当前文档根；未加载时为 null，所有访问限主线程。
public Element DocumentElement { get; }
// 当前文档的 body；未加载时为 null。
public Element Body { get; }
// 创建当前文档拥有的有类型节点；脱离层级时仍有效，卸载时失效。
public T Create<T>() where T : Element;
// 按 HTML 标签创建脱离层级的节点；文档负责其整个生命周期。
public Element CreateElement(string tag = "div");
// 在根及后代查询首个有类型节点。
public T Q<T>(string name = null, string className = null) where T : Element;
// 构造有类型查询；文档需已加载。
public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element;
// 在根及后代查询首项；未加载时为 null。
public Element Q(string name = null, string className = null);
// 当前根及后代的深度优先快照；保留原有非泛型查询契约。
public IReadOnlyList<Element> Query(string name = null, string className = null);
// 当前文档的类型化计算样式及稳定值订阅。
public DocumentStyles Styles { get; }
// 顶层文档的宿主顺序与模态状态；页面内的 CSS z-index 不改变此顺序。
public void SetPresentation(int layer, int order, bool visible, bool modal);
// 释放已加载的图片缓存；仍使用该路径的节点会再次解析图片，不卸载其他页面。
public void ReleaseImage(string source);
// 载入已有 CSS 资产；默认追加，默认皮肤可明确放在作者规则之前。
public void AddStyleSheet(TextAsset asset, string sourceUrl, bool beforeAuthor = false);
// 文档使用的共享 Surface；加载前必须明确配置。
public URUI.Hosting.Surface Surface { get; set; }
// 文档真实 URL，用于诊断和相对资源解析；配置值在重新启用时复用。
public string SourceUrl { get; set; }
// 同步预加载图片到文档纹理缓存；成功返回源像素尺寸，不修改节点或布局。
public bool TryLoadImage(string source, out Vector2Int dimensions);
// 下次加载使用的外置资源；数组使用副本；注册同步借用 TextAsset 原始数据，可包含字体字节。
public TextResource[] TextResources { get; set; }
// 静态 Images 未匹配时，在主线程同步解析规范化的 HTML 路径。
public Func<string, ImageLease> ImageResolver { get; set; }
// 在启用组件时加载的 HTML TextAsset。资产由调用者持有。
public TextAsset Source { get; set; }
// 正式页面的 Editor 编译资产。
public CompiledDocumentAsset CompiledSource { get; set; }
// 下次加载使用的借用图片映射；Texture2D 无需开启 Read/Write。
public ImageAsset[] Images { get; set; }
public AnimationController Animations { get; }
// 当前帧 URUI 已录制的命令缓冲。为空表示文档未加载或宿主尚未就绪。 仅在不持有需退役的 GPU 资源时可借用；可释放图片、图层及 Linear 标准颜色合成须使用 SubmitRendering。
public CommandBuffer RenderCommandBuffer { get; }
// 向当前 SRP context 排列 UI 绘制和资源 fence。
public bool SubmitRendering(ScriptableRenderContext context);
// 向调用方持有的 SRP 颜色目标排列 UI、图层合成与资源 fence，由管线完成 Submit。
public bool SubmitRendering(ScriptableRenderContext context, RenderTargetIdentifier target, UnityEngine.Rect viewport);
// 加载调用者持有的完整 HTML 资产；失败保留原页面。
public void Load(TextAsset asset, string sourceUrl);
// 实例化编译计划，失败保留原页面。
public void Load(CompiledDocumentAsset asset);
// 严格解析并加载完整 HTML；成功后才使原页面的元素与订阅失效。
public void LoadHtml(string html, string sourceUrl);
// 关闭当前文档；同 Surface 的其他页面和共享上下文继续工作。
public void Unload();
// 请求此文档的键盘焦点；优先保留当前输入或选择作者 autofocus，隐藏或被模态阻断时返回 false。
public bool Focus();
// 当前焦点元素，没有焦点时为 null。
public Element FocusedElement { get; }
// 命中文档坐标处的实际节点，不要求节点设置 Name。
public Element Pick(Vector2 position);
public EffectRegistry Effects { get; }
// 撤销可能引用旧资源的录制，再经宿主 GPU fence 退役。替换期间保留原 painter 注册。
public void RetirePainterResources(IDisposable resource);
// 绑定普通节点的作者 shader 装饰器；注销或文档卸载后，GPU 完成才调用 release。
public IDisposable AttachPainter(Element element, string shader, Action<ElementPaintContext> paint, Action release = null);
// 几何准备窗口仅在回调内有效；Core 持有实际 MeshData，直到原生编译消费完成。
public IDisposable AttachGeometryProvider(Element element, string shader, Action<ExternalGeometryContext> prepare, Action<int> onRecorded = null, Action release = null, RenderParameterLayout parameters = null);
// 文档已同步完成加载，可以查询元素。
public bool IsLoaded { get; }
// 每次成功加载后触发。可查询元素和注册回调，加载与卸载请求在通知结束后执行。
public event Action<Document> Loaded;
// 已加载的文档卸载后触发；所有旧元素和点击订阅已经失效。
public event Action<Document> Unloaded;
// 所有现存元素完成本轮原生布局刷新后触发；祖先移动或滚动也会通知。
public event Action<Document> LayoutUpdated { add; remove; }
// 同步样式、布局与空间变换，不为普通元素生成完整绘制记录；初始化后可立即读取几何。
public void SynchronizeGeometry();
// 同步文档几何，并保证指定已挂载子树内的绘制器就绪。
public void SynchronizeGeometry(Element root);
// 同步完成当前样式、布局和空间变换，以便初始化逻辑读取几何；不采集输入或触发 LayoutUpdated。元素几何变化仍正常通知。
public void SynchronizeLayout();
public IDisposable BeginUpdate();
public void RequestGeometry(Element root);
public bool FlushPendingGeometry();
public CompiledTemplate GetCompiledTemplate(string uri);
public CompiledQuerySnapshot CreateCompiledQuerySnapshot(Element root);
// 在当前同步事件和原生回调返回后执行；空闲时立即执行，不等待下一帧。
public void RunAfterEvent(Action action);
```

### CompiledQuerySnapshot

源码：[Document.CompiledQueries.cs](../Runtime/Documents/Document.CompiledQueries.cs)。

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

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 对应 CSS 的 Align 枚举；Unset 移除行内覆盖。
public enum Align { Unset, Auto, FlexStart, FlexEnd, Center, Stretch, Baseline }
```

### DisplayStyle

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 对应 CSS 的 DisplayStyle 枚举；Unset 移除行内覆盖。
public enum DisplayStyle { Unset, Block, Inline, InlineBlock, Flex, InlineFlex, None }
```

### Element

源码：[Element.Events.cs](../Runtime/Elements/Element.Events.cs)、[Element.cs](../Runtime/Elements/Element.cs)、[Element.Hierarchy.cs](../Runtime/Elements/Element.Hierarchy.cs)、[Element.State.cs](../Runtime/Elements/Element.State.cs)、[Element.Geometry.cs](../Runtime/Elements/Element.Geometry.cs)。

```csharp
public partial class Element
// 注册指定类型与阶段的同步回调；同一委托和阶段仅注册一次，Dispose 只解绑。
public IDisposable RegisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where TEvent : EventBase;
// 解绑指定委托和阶段；节点失效后的重复清理安全。
public void UnregisterCallback<TEvent>(Action<TEvent> callback, TrickleDown trickleDown = TrickleDown.NoTrickleDown) where TEvent : EventBase;
// 同步发送托管自定义事件，目标固定为本元素。
public void SendEvent(EventBase value);
// 捕获已有指针，使越界后的移动与释放仍发送到本元素。
public void CapturePointer(int pointerId);
// 仅释放本元素持有的指针捕获。
public void ReleasePointer(int pointerId);
// 检查是否拥有指定指针。
public bool HasPointerCapture(int pointerId);
// 控件事件钩子；在本元素普通回调之后执行。
protected virtual void OnEvent(EventBase value);
// 对应 HTML id；允许空名和同名，局部 Q 返回首项，Query 返回全部。
public string Name { get; set; }
// 原生标签名，例如 div、span；文本节点的标签为 #text。
public string TagName { get; }
// 普通文本。读取结构化子节点会抛异常；写入会销毁原来的子节点。
public virtual string Text { get; set; }
// 行内 CSS 写入入口；布局和样式计算仍由 URUI 完成。
public ElementStyle Style { get; }
// 节点未销毁，且所属文档仍是当前加载世代；脱离层级的节点仍有效。
public bool IsValid { get; }
// 节点当前连接到所属文档的根；有效但脱离层级的节点返回 false。
public bool IsAttached { get; }
// 读取真实属性；不存在时返回 null，存在但为空时返回空字符串。属性名区分大小写。
public string GetAttribute(string name);
// 检查真实属性是否存在，包括值为空的属性。
public bool HasAttribute(string name);
// 写入真实属性；小写 id 与 Name 操作相同节点，class 与类名方法保持一致。
public void SetAttribute(string name, string value);
// 移除属性；重复移除安全，移除 id 后 Name 为空。
public void RemoveAttribute(string name);
// 属性成功写入或移除后，同步控件的作者呈现。
protected virtual void OnAttributeChanged(string name);
// 严格解析 HTML 片段并原子替换内容；解析、建树或控件初始化失败时保留原子树。
public void SetInnerHtml(string html, string sourceUrl);
public void SetInnerHtml(CompiledTemplate template);
// 序列化当前元素的 HTML 内容，保留标准空元素与显式结束标签。
public string GetInnerHtml();
// 按节点身份设置二维位移与缩放，分量必须为有限数值。
public void SetTransform(Vector2 translation, Vector2 scale);
// 更新运行时TRS，保留作者变换原点的单位、数值与样式表来源。
public void SetTransform(Vector3 translation, Quaternion rotation, Vector3 scale);
// 更新运行时 TRS 与百分比变换原点；使用原生变换原语，不生成样式文本。
public void SetTransform(Vector3 translation, Quaternion rotation, Vector3 scale, Vector2 originPercent);
// 查询是否具有单个 CSS 类名，区分大小写。
public bool ClassListContains(string className);
// 添加单个 CSS 类名，重复添加安全。
public void AddToClassList(string className);
// 移除单个 CSS 类名，重复移除安全。
public void RemoveFromClassList(string className);
// 启用或移除单个类名；不接受用空白分隔的多个类名。
public void EnableInClassList(string className, bool enabled);
// 实例化作者子树；新根未挂载，由当前文档拥有，卸载文档后同时失效。
public Element Clone();
// 真实 DOM 父级；文档根和脱离层级的根节点返回 null。
public Element Parent { get; }
// 直接 DOM 子节点数量，不包括 URUI 的内部非 DOM 装饰节点。
public int ChildCount { get; }
// 按 DOM 顺序读取直接子节点，索引必须在 [0, ChildCount) 内。
public Element this[int index] { get; }
// 直接子节点的有序快照；后续结构变化不会修改此列表。
public IReadOnlyList<Element> Children();
// 在自身及 DOM 后代中按名字和单个类名查找首项；两个条件同时匹配，null 表示不过滤。
public Element Q(string name = null, string className = null);
// 自身及 DOM 后代的深度优先有序快照；精确匹配名字/类名，不解析 CSS 或 UQuery 表达式。
public IReadOnlyList<Element> Query(string name = null, string className = null);
// 将同一文档的节点移动到末尾；可挂载新建或脱离层级的完整子树。
public void Add(Element child);
// 移动到最终子节点索引；ChildCount 表示追加，普通校验失败不会移动节点。
public void Insert(int index, Element child);
// 移出指定的直接子节点，节点和其回调仍可复用。
public void Remove(Element child);
// 脱离父级但保持有效及文档所有权，之后可重新 Add；文档根不能脱离。
public void RemoveFromHierarchy();
// 将所有直接子节点移出层级；它们仍由文档拥有，需要删除时显式 Destroy。
public void Clear();
// 销毁此节点及其子树，使相关包装与订阅失效；不能销毁文档根。
public void Destroy();
// 供派生控件使用的构造入口；上下文只能由 Document 工厂提供。
protected internal Element(ElementContext context);
// 拥有本节点的文档。
public Document Document { get; }
// 业务子节点的容器；组合控件可覆写。
public virtual Element ContentContainer { get; }
// 访问真实 DOM 子节点，绕过内容容器重定向。
public ElementHierarchy Hierarchy { get; }
// 调用者关联数据；不向原生层序列化。
public object UserData { get; set; }
// 本节点自己的启用状态。
public bool EnabledSelf { get; }
// 本节点及所有祖先均启用。
public bool EnabledInHierarchy { get; }
// 设置本节点启用状态；禁用后不接受交互默认行为。
public void SetEnabled(bool value);
// 是否绘制；隐藏时保留布局。
public bool IsVisibleInHierarchy { get; }
public bool Visible { get; set; }
// 是否可获得键盘焦点。
public bool Focusable { get; set; }
// HTML tabindex；负数保留程序焦点并退出顺序导航，正数优先于零。
public int TabIndex { get; set; }
// 是否为当前文档焦点节点。
public bool Focused { get; }
// 请求焦点，禁用或未挂载时返回 false。
public bool Focus();
// 释放本节点焦点。
public void Blur();
// 节点自身的指针命中策略。
public PickingMode PickingMode { get; set; }
// 包装完成后的初始化钩子，此时可创建子节点和注册回调。
protected virtual void OnInitialize();
// 每次完成原生布局后的控件更新钩子。
protected virtual void OnUpdate();
// 节点失效后的资源清理钩子；此时不得再访问原生节点。
protected virtual void OnInvalidated();
// 节点已经参与至少一次完成的文档布局。
public bool HasLayout { get; }
// 查询当前节点及后代中的首个有类型元素。
public T Q<T>(string name = null, string className = null) where T : Element;
// 构造当前节点及后代的有类型查询。
public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element;
// 检查是否为自身或后代节点。
public bool Contains(Element element);
// 返回最近的指定类型祖先，不包含自身。
public T GetFirstAncestorOfType<T>() where T : Element;
// 移动到兄弟节点末尾。
public void BringToFront();
// 移动到兄弟节点开头。
public void SendToBack();
// 最近完成的原生布局中的边框矩形，位置相对于布局父级。
public Rect Layout { get; }
// 本地坐标中的内容矩形。
public Rect ContentRect { get; }
// 累积变换后的文档坐标轴对齐包围框。
public Rect WorldBound { get; }
// 扣除边框、内边距及可见滚动条的本地视口，适用于普通 overflow 节点。
public Rect ViewportRect { get; }
// 读取不含变换原点平移的本地矩阵，以及显式旋转原语的乘积；含 matrix 原语时返回 false。
public bool TryGetLocalTransform(out Matrix4x4 localTransform, out Quaternion logicalRotation);
// 最近完成的计算样式和实际尺寸读取入口。
public ElementResolvedStyle ResolvedStyle { get; }
// 当前滚动偏移，设置时由 URUI 根据内容范围夹取。
public Vector2 ScrollOffset { get; set; }
// 可滚动内容的整体尺寸。
public Vector2 ScrollSize { get; }
// 将元素本地边框坐标转换到文档坐标。
public Vector2 LocalToWorld(Vector2 point);
// 将文档坐标投影到元素本地边框坐标；不可逆变换抛异常。
public Vector2 WorldToLocal(Vector2 point);
// 尝试将文档坐标投影到本地；零缩放等不可逆变换返回 false。
public bool TryWorldToLocal(Vector2 point, out Vector2 localPoint);
// 检查本地坐标点是否位于元素边框内。
public bool ContainsPoint(Vector2 point);
```

### ElementContext

源码：[ElementContext.cs](../Runtime/Elements/ElementContext.cs)。

```csharp
// 由文档工厂提供的节点构造上下文，不持有额外原生资源。
public sealed class ElementContext
// 拥有节点的文档。
public Document Document { get; }
```

### ElementHierarchy

源码：[ElementHierarchy.cs](../Runtime/Elements/ElementHierarchy.cs)。

```csharp
// 真实 DOM 层级访问器；组合控件内部节点也包含在内。
public readonly struct ElementHierarchy
// 直接 DOM 子节点数量。
public int ChildCount { get; }
// 直接 DOM 子节点的有序快照。
public IReadOnlyList<Element> Children();
// 挂载到真实 DOM 子节点末尾。
public void Add(Element child);
// 插入真实 DOM 子节点索引。
public void Insert(int index, Element child);
// 脱离全部真实 DOM 子节点；节点仍由文档拥有。
public void Clear();
```

### ElementQuery<T>

源码：[ElementQuery.cs](../Runtime/Elements/ElementQuery.cs)。

```csharp
// 可复用的有类型查询；每次执行遍历当前节点，结果是有序快照。
public sealed class ElementQuery<T> where T : Element
// 追加谓词，执行时按加入顺序筛选。
public ElementQuery<T> Where(Func<T, bool> predicate);
// 将范围限制为直接内容子节点。
public ElementQuery<T> Children();
// 创建当前匹配节点的列表，不修改节点身份。
public List<T> ToList();
// 返回首项，没有匹配时为 null。
public T First();
// 返回末项，没有匹配时为 null。
public T Last();
// 返回指定匹配索引，不存在时为 null。
public T AtIndex(int index);
// 对有序快照逐项执行操作。
public void ForEach(Action<T> action);
```

### ElementResolvedStyle

源码：[Element.Geometry.cs](../Runtime/Elements/Element.Geometry.cs)。

```csharp
// 原生计算样式读取器，尺寸来自已完成的布局；仅限主线程。
public sealed class ElementResolvedStyle
// 读取最近计算样式中的有限数值长度或 auto，不创建托管 CSS 字符串。
public bool TryGetLength(string property, out Length length);
// 最终边框宽度，像素。
public float Width { get; }
// 最终边框高度，像素。
public float Height { get; }
// 读取级联与变量解析后的 CSS 值；尺寸像素请使用 Width/Height。
public string Get(string property);
```

### ElementStyle

源码：[ElementStyle.cs](../Runtime/Elements/ElementStyle.cs)、[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 读写真实 URUI 行内样式。计算结果由 ResolvedStyle 单独读取。
public sealed partial class ElementStyle
// 写入 CSS 属性，例如 Set("position", "absolute")。
public void Set(string property, string value);
// 写入带 px 单位的属性，拒绝 NaN 和 Infinity。
public void SetPixels(string property, float value);
// 更新无单位的运行时值；不构造 CSS 字符串。
public void SetNumber(string property, float value);
// 更新百分比运行时值；布局与皮肤仍由资源声明。
public void SetPercent(string property, float value);
// 更新带 dp 单位的动态值，保留文档密度缩放语义。
public void SetDensityPixels(string property, float value);
// 写入已注册属性的关键字序号；原生解析器校验该序号，不构造样式文本。
public void SetKeyword(string property, int value);
// 更新动态颜色值；属性必须接受颜色，不生成 CSS 文本。
public void SetColor(string property, Color value);
// 设置宽度（px）。
public void SetWidth(float value);
// 设置高度（px）。
public void SetHeight(float value);
// 设置距左边界的距离（px），定位规则仍由 CSS 决定。
public void SetLeft(float value);
// 设置距上边界的距离（px）。
public void SetTop(float value);
// 设置 0 到 1 的透明度。
public void SetOpacity(float value);
// 切换 display: block/none。恢复原本的 flex/inline 等布局请直接调用 Set。
public void SetDisplay(bool visible);
// 以 RGBA 字节写入背景颜色。
public void SetBackgroundColor(Color value);
// 移除行内 CSS 覆盖，让类名或样式表重新决定该属性。
public void Remove(string property);
// 读取真实行内覆盖；未设置时为 null，不包含样式表规则。
public string Get(string property);
// width 的行内长度覆盖。
public Length Width { get; set; }
// height 的行内长度覆盖。
public Length Height { get; set; }
// min-width 的行内长度覆盖。
public Length MinWidth { get; set; }
// min-height 的行内长度覆盖。
public Length MinHeight { get; set; }
// max-width 的行内长度覆盖。
public Length MaxWidth { get; set; }
// max-height 的行内长度覆盖。
public Length MaxHeight { get; set; }
// left 的行内长度覆盖。
public Length Left { get; set; }
// top 的行内长度覆盖。
public Length Top { get; set; }
// right 的行内长度覆盖。
public Length Right { get; set; }
// bottom 的行内长度覆盖。
public Length Bottom { get; set; }
// margin-left 的行内长度覆盖。
public Length MarginLeft { get; set; }
// margin-right 的行内长度覆盖。
public Length MarginRight { get; set; }
// margin-top 的行内长度覆盖。
public Length MarginTop { get; set; }
// margin-bottom 的行内长度覆盖。
public Length MarginBottom { get; set; }
// padding-left 的行内长度覆盖。
public Length PaddingLeft { get; set; }
// padding-right 的行内长度覆盖。
public Length PaddingRight { get; set; }
// padding-top 的行内长度覆盖。
public Length PaddingTop { get; set; }
// padding-bottom 的行内长度覆盖。
public Length PaddingBottom { get; set; }
// font-size 的行内长度覆盖。
public Length FontSize { get; set; }
// flex-basis 的行内长度覆盖。
public Length FlexBasis { get; set; }
// border-left-width 的行内长度覆盖。
public Length BorderLeftWidth { get; set; }
// border-right-width 的行内长度覆盖。
public Length BorderRightWidth { get; set; }
// border-top-width 的行内长度覆盖。
public Length BorderTopWidth { get; set; }
// border-bottom-width 的行内长度覆盖。
public Length BorderBottomWidth { get; set; }
// display 的行内枚举覆盖。
public DisplayStyle Display { get; set; }
// position 的行内枚举覆盖。
public Position Position { get; set; }
// flex-direction 的行内枚举覆盖。
public FlexDirection FlexDirection { get; set; }
// flex-wrap 的行内枚举覆盖。
public FlexWrap FlexWrap { get; set; }
// align-items 的行内枚举覆盖。
public Align AlignItems { get; set; }
// align-self 的行内枚举覆盖。
public Align AlignSelf { get; set; }
// justify-content 的行内枚举覆盖。
public Justify JustifyContent { get; set; }
// overflow 的行内枚举覆盖。
public Overflow Overflow { get; set; }
// 水平方向的行内溢出处理。
public Overflow OverflowX { get; set; }
// 垂直方向的行内溢出处理。
public Overflow OverflowY { get; set; }
// opacity 的行内数值；null 移除覆盖。
public float? Opacity { get; set; }
// flex-grow 的行内数值；null 移除覆盖。
public float? FlexGrow { get; set; }
// flex-shrink 的行内数值；null 移除覆盖。
public float? FlexShrink { get; set; }
// 背景颜色行内覆盖；null 移除覆盖。
public Color? BackgroundColor { get; set; }
```

### FlexDirection

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 对应 CSS 的 FlexDirection 枚举；Unset 移除行内覆盖。
public enum FlexDirection { Unset, Row, RowReverse, Column, ColumnReverse }
```

### FlexWrap

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 对应 CSS 的 FlexWrap 枚举；Unset 移除行内覆盖。
public enum FlexWrap { Unset, Nowrap, Wrap, WrapReverse }
```

### Justify

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 对应 CSS 的 Justify 枚举；Unset 移除行内覆盖。
public enum Justify { Unset, FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }
```

### Length

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 保留单位的样式长度，所有数值必须有限。
public readonly struct Length
// 长度数值；Auto/Unset 不使用此值。
public float Value { get; }
// 长度单位。
public LengthUnit Unit { get; }
// 移除行内覆盖。
public static Length Unset { get; }
// 由布局决定的长度。
public static Length Auto { get; }
// 像素长度。
public static Length Pixels(float value);
// 百分比长度，100 表示完整父级尺寸。
public static Length Percent(float value);
// 相对当前字号的长度。
public static Length Em(float value);
// 相对根字号的长度。
public static Length Rem(float value);
// 数值隐式转换为像素。
public static implicit operator Length(float value);
// 返回保留原单位的 CSS 表达式。
public override string ToString();
```

### LengthUnit

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// CSS 长度单位；Unset 表示没有行内覆盖。
public enum LengthUnit { Unset, Pixel, Percent, Em, Rem, Auto }
```

### Overflow

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 对应 CSS 的 Overflow 枚举；Unset 移除行内覆盖。
public enum Overflow { Unset, Visible, Hidden, Auto, Scroll }
```

### PickingMode

源码：[Element.State.cs](../Runtime/Elements/Element.State.cs)。

```csharp
// 命中策略；Ignore 元素本身不作为命中目标，子元素仍可命中。
public enum PickingMode { Position, Ignore }
```

### Position

源码：[ElementStyle.Properties.cs](../Runtime/Elements/ElementStyle.Properties.cs)。

```csharp
// 对应 CSS 的 Position 枚举；Unset 移除行内覆盖。
public enum Position { Unset, Static, Relative, Absolute }
```

## URUI.Events

### AnimationEndEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 作者关键帧动画完成；URUI 对每个完成的属性分别通知。
public sealed class AnimationEndEvent : EventBase
public string PropertyName { get; internal set; }
public string AnimationName { get; internal set; }
public ulong Identity { get; internal set; }
```

### AttachToDocumentEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 连接到文档。
public sealed class AttachToDocumentEvent : EventBase
```

### BlurEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 元素失去焦点。
public sealed class BlurEvent : EventBase
```

### ChangeEvent<T>

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 控件值变化；相同值与静默赋值不通知。
public sealed class ChangeEvent<T> : EventBase
// 变化前的值。
public T PreviousValue { get; internal set; }
// 变化后的值。
public T NewValue { get; internal set; }
```

### ClickEvent

源码：[ClickEvent.cs](../Runtime/Events/ClickEvent.cs)。

```csharp
// URUI 实际点击目标；包含指针信息并支持停止传播。
public sealed class ClickEvent : PointerEventBase
```

### DetachFromDocumentEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 脱离文档，节点仍然有效。
public sealed class DetachFromDocumentEvent : EventBase
```

### EventBase

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 同步事件；同一分发共享停止状态，所有操作限主线程。
public abstract class EventBase
// 最初命中的元素。
public Element Target { get; internal set; }
// 当前执行回调的元素。
public Element CurrentTarget { get; internal set; }
// 当前传播阶段。
public PropagationPhase PropagationPhase { get; internal set; }
// 是否停止后续元素的传播。
public bool IsPropagationStopped { get; private set; }
// 是否同时停止当前元素余下的回调。
public bool IsImmediatePropagationStopped { get; private set; }
// 是否请求取消可取消的控件默认行为；不改变托管传播。
public bool IsDefaultPrevented { get; private set; }
// 取消默认行为；焦点等不可取消的原生通知不受影响。
public void PreventDefault();
// 是否向祖先传播；布局和挂载通知只投递目标。
public virtual bool Bubbles { get; }
// 完成当前元素回调后停止传播；原生事件遵循 URUI 默认行为规则。
public void StopPropagation();
// 立即停止本次事件余下的回调。
public void StopImmediatePropagation();
```

### FocusEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 元素获得焦点。
public sealed class FocusEvent : EventBase
```

### GeometryChangedEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 最近完成的布局发生变化。
public sealed class GeometryChangedEvent : EventBase
// 上一次完成的布局。
public Rect OldRect { get; internal set; }
// 本次完成的布局。
public Rect NewRect { get; internal set; }
```

### InputModifiers

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 输入时按下的修饰键。
public enum InputModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Command = 8 }
```

### KeyboardEventBase

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 键盘事件数据。
public abstract class KeyboardEventBase : EventBase
// Unity 键码。
public KeyCode KeyCode { get; internal set; }
// 当前修饰键。
public InputModifiers Modifiers { get; internal set; }
```

### KeyDownEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 按键按下，包含重复输入。
public sealed class KeyDownEvent : KeyboardEventBase
```

### KeyUpEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 按键释放。
public sealed class KeyUpEvent : KeyboardEventBase
```

### PointerCancelEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 指针被系统取消。
public sealed class PointerCancelEvent : PointerEventBase
```

### PointerCaptureOutEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 失去指针捕获。
public sealed class PointerCaptureOutEvent : PointerEventBase
```

### PointerDownEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 指针按下。
public sealed class PointerDownEvent : PointerEventBase
```

### PointerEnterEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 指针进入。
public sealed class PointerEnterEvent : PointerEventBase
```

### PointerEventBase

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 指针数据，文档坐标以左上角为原点。
public abstract class PointerEventBase : EventBase
// 同一接触期间稳定的指针编号。
public int PointerId { get; internal set; }
// 文档坐标位置。
public Vector2 Position { get; internal set; }
// 相对于当前接收元素的坐标。
public Vector2 LocalPosition { get; }
// 发生变化的按钮，触摸为 0。
public int Button { get; internal set; }
// 按下按钮的位掩码。
public int PressedButtons { get; internal set; }
// 当前修饰键。
public InputModifiers Modifiers { get; internal set; }
// 是否来自触摸。
public bool IsTouch { get; internal set; }
```

### PointerLeaveEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 指针离开。
public sealed class PointerLeaveEvent : PointerEventBase
```

### PointerMoveEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 指针移动，包含悬停。
public sealed class PointerMoveEvent : PointerEventBase
```

### PointerPhase

源码：[Document.Input.cs](../Runtime/Documents/Document.Input.cs)。

```csharp
// 归一化指针输入阶段。
public enum PointerPhase { Down, Move, Up, Cancel, Wheel }
```

### PointerUpEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 指针释放。
public sealed class PointerUpEvent : PointerEventBase
```

### PropagationPhase

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 当前事件在目标路径中的阶段。
public enum PropagationPhase { None, TrickleDown = 1, AtTarget = 2, BubbleUp = 4 }
```

### ScrollEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 滚动位置变化。
public sealed class ScrollEvent : EventBase
```

### TextInputEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 文本输入通知。
public sealed class TextInputEvent : EventBase
// UTF-16 托管文本。
public string Text { get; internal set; }
```

### TransitionEndEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 作者样式过渡完成。
public sealed class TransitionEndEvent : EventBase
public string PropertyName { get; internal set; }
```

### TrickleDown

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 事件回调注册的传播方向。
public enum TrickleDown { NoTrickleDown, TrickleDown }
```

### WheelEvent

源码：[EventBase.cs](../Runtime/Events/EventBase.cs)。

```csharp
// 滚轮输入。
public sealed class WheelEvent : PointerEventBase
// 横纵滚动增量。
public Vector2 Delta { get; internal set; }
```

## URUI.Hosting

### Surface

源码：[Surface.cs](../Runtime/Hosting/Surface.cs)。

```csharp
// 拥有共享原生上下文、视口、时钟、输入与绘制资源；顶层页面显式关联此组件。
public sealed class Surface : MonoBehaviour
// 同一 Surface 的所有页面共享此时钟；加载期间不能更换时基。
public IUiClock UiClock { get; set; }
// 逻辑 CSS 像素尺寸；零表示跟随屏幕尺寸。
public Vector2Int Viewport { get; set; }
// 一次分发到整个 Surface 的输入入口。
public SurfaceInput Input { get; }
// 当前位于同步分发、加载事务或效果回调中，尚不能同步创建顶层页面。
public bool IsExecutingCallback { get; }
// 在 Surface 的同步分发和原生调用返回后执行；空闲时立即执行。
public void RunAfterEvent(Action action);
// 提交同一 Surface 的完整 UI，一次调用覆盖所有活动文档。
public bool SubmitRendering(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Rendering.RenderTargetIdentifier target, Rect viewport);
```

### SurfaceInput

源码：[SurfaceInput.cs](../Runtime/Hosting/SurfaceInput.cs)。

```csharp
// 主线程同步输入适配器。默认轮询旧 Input Manager，可由外部输入系统替代。
public sealed class SurfaceInput
// 是否使用 Unity 旧输入后端。关闭后仍可显式发送输入。
public bool UseUnityInput { get; set; }
// 发送文档坐标指针输入；触摸编号必须非负，且在接触期间保持稳定。
public void SendPointer(PointerPhase phase, Vector2 position, int pointerId = 0, bool isTouch = false, int button = 0, int pressedButtons = 0, Vector2 delta = default, InputModifiers modifiers = InputModifiers.None);
// 发送键盘事件；文本字符使用 SendText，修饰键随本次事件提供。
public void SendKey(KeyCode key, bool pressed, InputModifiers modifiers = InputModifiers.None);
// 插入已经提交的文本，原生层按 Unicode 字符更新选区和光标。
public void SendText(string text);
// 更新输入法组合串，预览不产生控件值变化通知。
public void SendComposition(string text);
// 提交输入法组合串并产生一次值变化通知。
public void CommitComposition(string text);
// 取消组合串，恢复原先的已提交内容和选区。
public void CancelComposition();
// 取消全部按下/捕获状态并释放焦点；应用失焦时自动调用。
public void Reset();
// 取消指定子树持有的指针交互；隐藏或移除交互区域前可调用。
public void CancelPointers(Element owner);
```

## URUI.Media

### VideoPlayback

源码：[VideoPlayback.cs](../Runtime/Media/VideoPlayback.cs)。

```csharp
// 与视图格式无关的视频解码器。逐帧呈现到持久 GPU 纹理，租约可延迟资源释放。
public sealed class VideoPlayback : MonoBehaviour
public int PresentedFrame { get; private set; }
public ulong PresentedVersion { get; private set; }
public RenderTexture Output { get; }
public bool HasPresentedFrame { get; }
public event Action<VideoPlayback> FramePresented;
public event Action<string> Failed;
// 播放源资产；调用方持有 VideoClip。更换源会使旧输出退役，已有租约仍有效。
public void Play(VideoClip clip, bool loop = true);
// 播放 URL；在 Prepare 完成后获得输出尺寸。
public void PlayUrl(string url, bool loop = true);
// 输出可在首帧前取得，此时内容透明；租约交由渲染器归还。
public ImageLease AcquireOutput();
public void Pause();
public void Resume();
// 从开头准备当前源，保留已被渲染器持有的输出纹理身份。
public void Replay();
public void Stop();
```

## URUI.Rendering.Effects

### EffectComposite

源码：[EffectComposite.cs](../Runtime/Rendering/Effects/EffectComposite.cs)。

```csharp
// 效果的最终目标混合；default 沿用标准 CSS 合成，显式状态使用 Unity 固定 Blend 因子。
public readonly struct EffectComposite
public bool UsesUnityBlend { get; }
public BlendMode Source { get; }
public BlendMode Destination { get; }
public EffectComposite(BlendMode source, BlendMode destination);
```

### EffectContext

源码：[EffectContext.cs](../Runtime/Rendering/Effects/EffectContext.cs)。

```csharp
// 实例资源所有者；隐藏、移除、卸载和构造失败均走宿主 GPU 退役。
public sealed class EffectContext : IDisposable
public EffectDefinition Definition { get; }
public ulong ElementToken { get; }
// 仅用于已显式注册的实例输入身份；Prepare/Record 中不能查询或修改此节点。
public URUI.Elements.Element Element { get; }
public T Own<T>(T resource) where T : IDisposable;
// 资源辅助器在解析或创建新资源前检查调用阶段，防止 Record 内发生资源副作用。
public void RequirePreparation();
// Prepare 替换资源时使用；在途 GPU 帧完成前不销毁旧资源。
public void Retire(IDisposable resource);
public void Dispose();
```

### EffectDefinition

源码：[EffectDefinition.cs](../Runtime/Rendering/Effects/EffectDefinition.cs)。

```csharp
// 文档加载前注册的不可变效果声明；参数默认值由作者样式拥有。
public sealed class EffectDefinition
public string Name { get; }
public EffectKind Kind { get; }
public string ProgramKey { get; }
public string ColorContract { get; }
// 未知用户程序保留外部操作；不能用 ColorContract 文本推测合批能力。
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

源码：[EffectFrame.cs](../Runtime/Rendering/Effects/EffectFrame.cs)。

```csharp
// 一次稳定布局的不可变输入；不得跨文档代际使用。
public readonly struct EffectFrame
public ulong ElementToken { get; }
public EffectLayout Layout { get; }
public ComputedStyleSnapshot Style { get; }
public Vector2 Size { get; }
public Vector2 ContentOffset { get; }
public Texture Source { get; }
public EffectGeometry SourceGeometry { get; }
public Vector2Int SourcePixelSize { get; }
// 覆盖图在文档空间中的像素矩形；原始图片输入为默认矩形。
public Rect SourceCaptureBounds { get; }
// 采样输入的原始 ink 在纹理内的 UV 域；不包含为了输出预留的透明 padding。
public Rect SourceUvBounds { get; }
// 元素本地平面到文档坐标的冻结单应矩阵。
public Matrix4x4 ElementToDocument { get; }
public uint SourceVersion { get; }
public bool SourceIsBottomLeft { get; }
public bool SourceIsPremultiplied { get; }
// 输入为宿主已经绘制的节点覆盖图；其颜色已处于渲染空间。
public bool SourceIsCaptured { get; }
public double Time { get; }
```

### EffectGeometry

源码：[EffectGeometry.cs](../Runtime/Rendering/Effects/EffectGeometry.cs)。

```csharp
// 原始绘制几何的不可变副本；同一原生几何身份复用，不依赖 Record 阶段的 DOM。
public sealed class EffectGeometry
public ulong GeometryId { get; }
public IReadOnlyList<Vector3> Positions { get; }
public IReadOnlyList<Vector2> UVs { get; }
public IReadOnlyList<Color32> Colors { get; }
public IReadOnlyList<int> Indices { get; }
public Rect Bounds { get; }
// URUI 原始 UV 坐标范围；借用 Unity 纹理的 Y 翻转另由 EffectFrame 声明。
public Rect UvBounds { get; }
```

### EffectImageCondition

源码：[EffectComposite.cs](../Runtime/Rendering/Effects/EffectComposite.cs)。

```csharp
// 一个计算属性满足给定值时才允许原图直接路径；其余值使用节点最终合成。
public sealed class EffectImageCondition
public string Property { get; }
public string DirectValue { get; }
public EffectImageCondition(string property, string directValue);
```

### EffectKind

源码：[EffectDefinition.cs](../Runtime/Rendering/Effects/EffectDefinition.cs)。

```csharp
public enum EffectKind { Image, Element }
```

### EffectLayout

源码：[EffectLayout.cs](../Runtime/Rendering/Effects/EffectLayout.cs)。

```csharp
// 回调前冻结的样式、内容矩形和文档平面变换；根身份只属于当前文档代际。
public sealed class EffectLayout
public ComputedStyleSnapshot Style { get; }
public Rect ContentRect { get; }
// 本地 z=0 平面到文档的单应矩阵；乘法后需要除以 w。
public Matrix4x4 ElementToDocument { get; }
public ulong RootToken { get; }
public Rect RootContentRect { get; }
public Matrix4x4 RootToDocument { get; }
public EffectLayout WithStyle(ComputedStyleSnapshot style);
// 仅适用于同一文档中已经冻结的坐标快照。
public EffectLayout WithRoot(EffectLayout source);
```

### EffectPreviewRenderer

源码：[EffectPreviewRenderer.cs](../Runtime/Rendering/Effects/EffectPreviewRenderer.cs)。

```csharp
// 无 DOM 的矩形图像预览；复用生产效果实例、网格和 Record 路径，不创建第二个 Surface。
public sealed class EffectPreviewRenderer : IDisposable
public EffectPreviewRenderer(EffectDefinition definition);
// source 是普通 Unity 直 Alpha 纹理。content 为目标中的像素矩形，时间由调用方明确给定。
public void Render(Texture source, Rect content, double time, RenderTexture destination, Color background);
public void Dispose();
```

### EffectRegistry

源码：[EffectRegistry.cs](../Runtime/Rendering/Effects/EffectRegistry.cs)。

```csharp
// 显式文档级注册；加载期间不可更改类型或默默覆盖定义。
public sealed class EffectRegistry
// 布局更新后、原生绘制前更新播放时间；禁止在此回调重新同步布局。
public event Action Updating;
// 原生绘制已冻结变换、任何效果 Prepare 之前，收集控制器参数。所有文档先收集再 Prepare。
public event Action ParametersPreparing;
public EffectLayout GetLayout(URUI.Elements.Element element);
public IDisposable BindParameters(URUI.Elements.Element element, Func<EffectLayout, EffectLayout> resolve);
// 绑定固定参数与惰性公开布局；绑定租约释放后恢复作者样式。
public IDisposable BindProgramParameters(URUI.Elements.Element element, IEffectParameterBinding resolve);
// 声明运行时参数需要捕获及显式命中策略；不修改作者 CSS，默认两个标志均为 false。
public void SetRuntimeState(URUI.Elements.Element element, bool captureRequired, bool inputDisabled);
// 覆盖一个效果实例的时间源；调用方持有租约，解除后恢复Document时钟，不更换CSS的文档时基。
public IDisposable BindClock(URUI.Elements.Element element, URUI.Animation.IUiClock clock);
public int Count { get; }
public void RegisterFilter(string name, Func<ILayerFilterProgram> factory, bool cacheable = false);
public void Register(EffectDefinition definition);
public bool TryGetFrame(URUI.Elements.Element element, out EffectFrame frame);
public bool TryGet(string name, out EffectDefinition definition);
```

### EffectRenderContext

源码：[EffectRenderContext.cs](../Runtime/Rendering/Effects/EffectRenderContext.cs)。

```csharp
// 修饰原 img 网格、顶点颜色、切片及 UV；属性块由 Unity 在录制时复制。
public readonly struct EffectRenderContext
public EffectFrame Frame { get; }
public bool IsMaskImage { get; }
// 使用原 img 的标准绘制路径；处理输出仍沿用原九宫格、颜色与裁剪。
public void DrawOriginal();
// 录制预先分配的离屏表面。操作属于同一 Document 提交，返回时宿主恢复 UI 目标及矩阵。
public void RecordSurface(RenderTexture target, Mesh mesh, Material material, Matrix4x4 projection, MaterialPropertyBlock properties, int pass = 0);
// 只有本次记录实际进入渲染提交后执行；用于提交缓存版本，不能访问 DOM。
public void AfterSubmission(Action callback);
// 材质遵守 ElementPaint.cginc；元素 opacity 由宿主对子树统一合成，效果不得再次相乘。
public void DrawGeometry(Mesh mesh, Material material, Matrix4x4 localTransform, MaterialPropertyBlock properties, int pass = 0);
public void Draw(Material material, MaterialPropertyBlock properties, int pass = 0);
```

### EffectStatistics

源码：[EffectStatistics.cs](../Runtime/Rendering/Effects/EffectStatistics.cs)。

```csharp
public static class EffectStatistics
public static long FilterProcessingPasses { get; internal set; }
public static EffectTargetStatistics GetTargets();
```

### EffectTargetStatistics

源码：[EffectStatistics.cs](../Runtime/Rendering/Effects/EffectStatistics.cs)。

```csharp
// 效果目标的实际所有权计数；颜色字节不含驱动对齐、深度缓冲或视频解码输入。
public readonly struct EffectTargetStatistics
public readonly int LiveTargets, LeasedTargets;
public readonly long LiveColorBytes, LeasedColorBytes, IdleColorBytes, CreatedTargets, Reuses;
```

### FilterBuffer

源码：[FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs)。

```csharp
public enum FilterBuffer { Source, Scratch, Destination }
```

### FilterInvocation

源码：[FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs)。

```csharp
// URUI 已解析并按作者顺序发出的一个滤镜调用；长度为布局像素，颜色为直通 alpha。
public sealed class FilterInvocation
public string Name { get; }
public float Value { get; }
public float Sigma { get; }
public Vector2 Offset { get; }
public Color Color { get; }
public RectInt Region { get; }
```

### FilterRenderContext

源码：[FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs)。

```csharp
// 有序子树滤镜的录制范围；中间颜色采用宿主线性/伽马约定和预乘 alpha，支持 HDR。
public sealed class FilterRenderContext
public Vector2Int Size { get; }
public Texture Texture(FilterBuffer buffer);
public void Draw(Material material, MaterialPropertyBlock properties, int pass, FilterBuffer source, FilterBuffer destination);
public void Copy(FilterBuffer source, FilterBuffer destination);
```

### IEffectBounds

源码：[IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs)。

```csharp
// Prepare 可更新资源；Record 只录制现有资源，不访问或修改 DOM。
// 可选的 Prepare 结果：节点本地坐标中的实际绘制范围，供子树滤镜/遮罩分配区域。
public interface IEffectBounds
UnityEngine.Rect InkBounds { get; }
```

### IEffectFactory

源码：[IEffectFactory.cs](../Runtime/Rendering/Effects/IEffectFactory.cs)。

```csharp
// 每个作者节点创建一个实例；失败前创建的资源交给 context.Own。
public interface IEffectFactory
IElementEffect Create(EffectContext context);
```

### IElementEffect

源码：[IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs)。

```csharp
public interface IElementEffect : IDisposable
void Prepare(EffectFrame frame);
void Record(EffectRenderContext context);
```

### ILayerFilterProgram

源码：[FilterChain.cs](../Runtime/Rendering/Effects/FilterChain.cs)。

```csharp
public interface ILayerFilterProgram : IDisposable
// 程序按名称在文档共享。只准备与调用顺序无关的资源；参数从Record的invocation读取，不能保存某节点的可变参数或修改DOM。
void Prepare(FilterInvocation invocation);
// 只使用准备好的资源；source/destination/scratch 属于宿主并有 GPU 租约保护。
void Record(FilterRenderContext context, FilterInvocation invocation);
```

### EffectProgramCoordinates

源码：[EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs)。

```csharp
// 程序准备时的冻结坐标事实；不包含样式对象。
public readonly struct EffectProgramCoordinates
public Rect ContentRect { get; }
public Rect RootContentRect { get; }
public Matrix4x4 ElementToDocument { get; }
public Matrix4x4 RootToDocument { get; }
// 使用另一个布局的根坐标，保留自身内容与变换。
public EffectProgramCoordinates WithRoot(EffectLayout root);
```

### EffectProgramSource

源码：[EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs)。

```csharp
// 冻结采样事实；Topology 为原始拓扑，统一乘色和 UV 变换仍由原生基础参数提供。
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

源码：[EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs)。

```csharp
// 固定参数程序的私有绑定。连续值与坐标按同步回调解析；公开布局仅在请求快照时生成。
public interface IEffectParameterBinding
// 生成可保留的公开布局；返回值及其样式必须不可变。
EffectLayout ResolveLayout(EffectLayout layout);
// 解析冻结坐标事实，不得访问或修改 DOM。
EffectProgramCoordinates ResolveCoordinates(EffectProgramCoordinates coordinates);
// 覆盖当前程序的固定布局值；Span 仅在本次同步回调中有效，不得保留或修改 DOM。
void ResolveProgramParameters(string program, Span<Vector4> values);
```

### EffectProgramPreparation

源码：[EffectProgramPreparation.cs](../Runtime/Rendering/Effects/EffectProgramPreparation.cs)。

```csharp
// 仅在 PrepareProgram 回调中有效的连续参数视图；Frame 按需生成独立公开快照。
public readonly ref struct EffectProgramPreparation
public Vector2 Size { get; }
public Vector2 ContentOffset { get; }
public double Time { get; }
// 声明布局中的数值；只在当前同步回调中使用，不得保存该 Span。
public ReadOnlySpan<Vector4> Parameters { get; }
public EffectProgramCoordinates Coordinates { get; }
public EffectProgramSource Source { get; }
public EffectFrame Frame { get; }
```

### IParameterImageEffect

源码：[IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs)。

```csharp
// 固定参数图片程序。宿主直接提交原始几何和冻结参数，不调用 Record。 材质在 Prepare 后不可变；替换材质须通过 EffectContext 退役。
public interface IParameterImageEffect
UnityEngine.Material Program { get; }
// 当前不可变程序实际消费的时间源；取值为 None 或 UiClock。
RenderTimeInput TimeInput { get; }
void PrepareProgram(in EffectProgramPreparation preparation);
// 无 DOM 预览的数值输入；字段含义由程序拥有，Core 不解释属性名称。
void GetPreviewParameters(Span<UnityEngine.Vector4> values);
```

### IParameterImageResources

源码：[IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs)。

```csharp
// 在所有效果准备完成后上传共享资源；不能访问 DOM。
public interface IParameterImageResources
void CommitResources();
```

### IParameterImageRuntime

源码：[IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs)。

```csharp
// 程序拥有的额外运行时行，接在宿主的两行采样输入后。只复制已冻结的值。
public interface IParameterImageRuntime
int RuntimeRowCount { get; }
void CopyRuntimeParameters(Span<UnityEngine.Vector4> values);
```

### IParameterEffectLayout

源码：[IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs)。

```csharp
// 在租用捕获图之前声明保守覆盖范围和最终混合；最多返回 64 个覆盖变换。
public interface IParameterEffectLayout
void PrepareCoverage(in EffectProgramPreparation preparation, Span<UnityEngine.Matrix4x4> transforms, out int count, out UnityEngine.Vector4 expansion, out EffectComposite composite);
```

### IParameterImageGeometry

源码：[IElementEffect.cs](../Runtime/Rendering/Effects/IElementEffect.cs)。

```csharp
// 不可变的局部几何模板；内容变化递增版本，变换单独录入参数。
public interface IParameterImageGeometry : IParameterImageEffect
ExternalGeometryInput Geometry { get; }
UnityEngine.Matrix4x4 GeometryTransform { get; }
```

## URUI.Rendering

### ElementPaintContext

源码：[ElementPaintContext.cs](../Runtime/Rendering/ElementPaintContext.cs)。

```csharp
// 作者 shader 装饰器所在位置的有序绘制范围，坐标原点为该节点左上角。
public readonly struct ElementPaintContext
public Element Element { get; }
// 当前绘制用于 mask-image 的 alpha；遮罩不重复应用节点自身透明度。
public bool IsMaskImage { get; }
// 祖先裁剪交集为空；不会按节点盒猜测扩展粒子的可见范围。
public bool IsClippedOut { get; }
public Vector2 Size { get; }
// 在本节点局部坐标绘制；属性块在录制时由 Unity 复制，材质和网格仍须保持有效。
public void DrawMesh(Mesh mesh, Material material, Matrix4x4 localTransform, MaterialPropertyBlock properties = null, int submesh = 0, int pass = 0);
```

### RenderTextureEncoding

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderTextureEncoding { Unknown, TextureDeclared, EncodedPremultiplied, LinearPremultiplied, LinearStraight }
```

### RenderColorSpace

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderColorSpace { Unknown, EncodedSrgb, Linear, Active, Target }
```

### RenderAlpha

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderAlpha { Unknown, Premultiplied, Straight, Opaque, PartialPremultiplied }
```

### RenderBlendDomain

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderBlendDomain { Unknown, EncodedSrgb, Linear, Active, Target }
```

### RenderTargetCapability

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderTargetCapability { Unknown, Ldr, Hdr }
```

### RenderSampling

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderSampling { Unknown, BilinearClamp, LocalAddressing, Neighborhood }
```

### RenderGeometry

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderGeometry { Unknown, Local, VertexDisplacement, Expanded, Captured }
```

### RenderTimeInput

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
public enum RenderTimeInput { None, UiClock, External }
```

### RenderProgramDescriptor

源码：[RenderProgramDescriptor.cs](../Runtime/Rendering/RenderProgramDescriptor.cs)。

```csharp
// 程序的机器可读渲染约定；默认值表示未知用户程序，必须保持外部边界。
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
// 共享栅格区间的必要条件；实际计划仍须核对目标、资源和有序依赖。
public bool CanShareRaster(RenderProgramDescriptor other);
public bool Equals(RenderProgramDescriptor other);
public override bool Equals(object obj);
public override int GetHashCode();
```

### RenderKeywordMapping

源码：[RenderParameterLayout.cs](../Runtime/Rendering/RenderParameterLayout.cs)。

```csharp
// 把作者关键字映射为程序使用的有限数值；不依赖 CSS 枚举内部编号。
public readonly struct RenderKeywordMapping
public string Keyword { get; }
public float Value { get; }
public RenderKeywordMapping(string keyword, float value);
```

### RenderParameterField

源码：[RenderParameterLayout.cs](../Runtime/Rendering/RenderParameterLayout.cs)。

```csharp
// 一个 CSS 计算属性到固定分量区间的映射；颜色占四个分量，带关键字映射的数值占两个分量（值、关键字标记）。
public sealed class RenderParameterField
public StylePropertyDefinition Property { get; }
public int ComponentOffset { get; }
// 可选的级联来源分量；-1 表示程序不读取来源。
public int OriginOffset { get; }
// 缺失的属性输出零；存在但类型无效的作者值仍报错。
public bool Optional { get; }
// 关键字的显式映射；数值可声明有限关键字，第二分量区分数值（0）和关键字（1）。图像只输出变化标识，不编码纹理。
public IReadOnlyList<RenderKeywordMapping> Keywords { get; }
public RenderParameterField(StylePropertyDefinition property, int componentOffset, int originOffset = -1, IEnumerable<RenderKeywordMapping> keywords = null, bool optional = false);
```

### RenderParameterLayout

源码：[RenderParameterLayout.cs](../Runtime/Rendering/RenderParameterLayout.cs)。

```csharp
// 程序的不可变 float4 布局；注册时编译属性访问器，未注册自定义值保留 CSS 解析语义。
public sealed class RenderParameterLayout
public int RowCount { get; }
public IReadOnlyList<RenderParameterField> Fields { get; }
public RenderParameterLayout(int rowCount, IEnumerable<RenderParameterField> fields);
```

### ExternalGeometryInput

源码：[ExternalGeometryInput.cs](../Runtime/Rendering/ExternalGeometryInput.cs)。

```csharp
// 一个子网格的只读输入声明；宿主持有 MeshData 消费租约直到当前原生输入窗口结束。
public readonly struct ExternalGeometryInput
public ulong GeometryVersion { get; }
public int Submesh { get; }
// 几何内容、索引或布局改变时必须递增 version；变换通过绘制参数独立提供。
public ExternalGeometryInput(Mesh mesh, ulong version, int submesh = 0);
```

### ExternalGeometryProgram

源码：[ExternalGeometryProgram.cs](../Runtime/Rendering/ExternalGeometryProgram.cs)。

```csharp
// 外部几何程序及渲染约定。材质由调用者持有至注册的 GPU 退役回调。
public sealed class ExternalGeometryProgram
public Material Material { get; }
public RenderProgramDescriptor Descriptor { get; }
public int Pass { get; }
// 未在 ShaderLab 属性中声明的矩阵、缓冲等资源变化时递增；普通属性同时由宿主冻结。
public ulong ResourceVersion { get; }
// 已知程序读取的局部位置分量数；二维程序忽略源网格的 Z。
public int PositionComponents { get; }
// 已知程序使用位置、RGBA8 顶点色、UV0 和统一参数表；未知程序保留独立绘制边界。
public ExternalGeometryProgram(Material material, RenderProgramDescriptor descriptor = default, int pass = 0, int positionComponents = 3, ulong resourceVersion = 1);
```

### ExternalGeometryContext

源码：[ExternalGeometryContext.cs](../Runtime/Rendering/ExternalGeometryContext.cs)。

```csharp
// 外部几何的同步准备窗口；局部坐标从作者节点左上角起，回调结束后窗口失效。
public readonly struct ExternalGeometryContext
public Vector2 Size { get; }
// 注册时声明的连续 CSS 参数，只在当前同步回调有效。
public ReadOnlySpan<Vector4> Parameters { get; }
// 声明节点局部坐标中的矩形及左上右下羽化距离；原生根据组语义决定直接遮罩或离屏绘制。
public void DrawFeatheredRectangle(Rect rectangle, Vector4 feather);
public bool IsClippedOut { get; }
public bool IsMaskImage { get; }
// 冻结当前只读网格输入；变换和裁剪交由原生计划与 Shader，回调不能修改文档。
public void Draw(in ExternalGeometryInput input, ExternalGeometryProgram program, Matrix4x4 localTransform);
```

## URUI.Resources

### CompiledDocumentAsset / CompiledTemplate

源码：[CompiledDocumentAsset.cs](../Runtime/Resources/CompiledDocumentAsset.cs)、[CompiledTemplate.cs](../Runtime/Resources/CompiledTemplate.cs)。

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

源码：[GpuRetirement.cs](../Runtime/Resources/Images/GpuRetirement.cs)。

```csharp
// 在真实 GPU fence 完成后释放即时 Graphics 队列此前使用的资源。
public static class GpuRetirement
// 编辑器等没有 MonoBehaviour Update 的宿主轮询已提交的 GPU fence；不会强制等待 GPU。
public static void Collect();
// 释放已确认退役的自有对象；编辑器退出 Play 后仍可完成延后的释放。
public static void DestroyOwnedObject(UnityEngine.Object value);
public static void Retire(Action release);
```

### ImageAsset

源码：[ImageAsset.cs](../Runtime/Resources/Images/ImageAsset.cs)。

```csharp
// 将 HTML 中的图片路径映射到调用者持有的 Texture2D（无需 Read/Write）。
public struct ImageAsset
// HTML/CSS 使用的相对路径，例如 icons/coin.png。
public string Source { get; }
// 调用者持有的原图；URUI 不销毁它。
public Texture2D Texture { get; }
// 创建图片路径与 Unity 纹理的映射；有效性在文档加载时统一检查。
public ImageAsset(string source, Texture2D texture);
// 创建图片和命名图集区域；复制区域数组，不取得纹理资产所有权。
public ImageAsset(string source, Texture2D texture, SpriteRegion[] sprites);
// 不可通过此视图修改的区域声明；区域数据随文档候选资源一起发布。
public IReadOnlyList<SpriteRegion> Sprites { get; }
```

### ImageLease

源码：[ImageLease.cs](../Runtime/Resources/Images/ImageLease.cs)。

```csharp
// 同步解析取得的图片租约。返回给 URUI 后，将 Dispose 权交给 URUI。
public sealed class ImageLease : IDisposable
public Texture Texture { get; }
public ImageLease(Texture texture, Action release = null);
public void Dispose();
```

### ImageLeaseSource

源码：[ImageLeaseSource.cs](../Runtime/Resources/Images/ImageLeaseSource.cs)。

```csharp
// 生产者和渲染租约共同持有动态图片，最后一个持有者退出后归还资源。
public sealed class ImageLeaseSource : IDisposable
public ImageLeaseSource(Texture texture, Action release);
public ImageLease Acquire();
public void Dispose();
```

### SpriteRegion

源码：[SpriteRegion.cs](../Runtime/Resources/Images/SpriteRegion.cs)。

```csharp
// 一张图片中的命名区域；坐标从原图左上角开始，单位为源像素。
public struct SpriteRegion
// 所属文档内唯一且区分大小写的区域名称。
public string Name { get; }
// 左上角原点的源像素矩形。
public Rect Region { get; }
// 区域源像素到 URUI 自然尺寸的显示比例。
public float DisplayScale { get; }
// 创建区域声明；名称、边界与比例在文档加载前统一验证。
public SpriteRegion(string name, Rect region, float displayScale = 1);
```

### TextResource

源码：[TextResource.cs](../Runtime/Resources/TextResource.cs)。

```csharp
// 外置 CSS 和字体等文件的规范 URL；同步借用 TextAsset 的原生只读内容，保留二进制文件字节。
public struct TextResource
public string Source { get; }
public TextAsset Asset { get; }
public TextResource(string source, TextAsset asset);
```

## URUI.Styles

### ComputedImageKind

源码：[ComputedImageValue.cs](../Runtime/Styles/ComputedImageValue.cs)。

```csharp
public enum ComputedImageKind { None, Url, Gradient }
```

### ComputedImageValue

源码：[ComputedImageValue.cs](../Runtime/Styles/ComputedImageValue.cs)。

```csharp
// 由原生 CSS image 解析器验证的不可变图像值，保留声明来源用于相对资源解析。
public sealed class ComputedImageValue
public ComputedImageKind Kind { get; }
public string Url { get; }
public string CssText { get; }
public string SourceUrl { get; }
```

### ComputedStyleSnapshot

源码：[ComputedStyleSnapshot.cs](../Runtime/Styles/ComputedStyleSnapshot.cs)。

```csharp
// 一个稳定布局版本的独立值副本；重载或销毁后的代际不能继续使用。
public sealed class ComputedStyleSnapshot
// 创建独立的效果参数视图；不写入 DOM，也不改变 CSS 级联。仅覆盖指定来源以下的值。
public ComputedStyleSnapshot WithOverrides(IReadOnlyDictionary<string, StyleOverride> overrides, StyleValueOrigin maximumOrigin);
public uint Generation { get; }
public ulong Revision { get; }
public string GetKeyword(string name);
// 计算值的级联来源；注册初始值不算作者覆盖，动画、important 与 transition 保留各自优先级。
public StyleValueOrigin GetOrigin(string name);
public ComputedImageValue GetImage(string name);
public float GetNumber(string name);
public float GetPixels(string name);
public Color GetColor(string name);
public bool Contains(StylePropertyDefinition property);
```

### DocumentStyles

源码：[DocumentStyles.cs](../Runtime/Styles/DocumentStyles.cs)。

```csharp
// 复用原生稳定版本；只有实际值变化时才分配新快照、通知订阅。
public sealed class DocumentStyles
public uint Generation { get; private set; }
public ComputedStyleSnapshot GetComputed(Element element);
// 从下一次稳定帧通知起通知；等值写入不重复通知，取消后不再回调。
public IDisposable Subscribe(Element element, Action<ComputedStyleSnapshot> changed);
```

### StyleOverride

源码：[StyleOverride.cs](../Runtime/Styles/StyleOverride.cs)。

```csharp
// 效果视图中的类型化值；不参与文档 CSS 的级联或继承。
public readonly struct StyleOverride
// 读取数值覆盖；不匹配时返回 false，不分配或进行类型转换。
public bool TryGetNumber(out float number);
// 读取颜色覆盖；不匹配时返回 false。
public bool TryGetColor(out Color color);
// 读取关键字覆盖；不匹配时返回 false。
public bool TryGetKeyword(out string keyword);
public static StyleOverride Number(float value);
public static StyleOverride Color(Color value);
public static StyleOverride FromKeyword(string value);
```

### StylePropertyDefinition

源码：[StylePropertyDefinition.cs](../Runtime/Styles/StylePropertyDefinition.cs)。

```csharp
// 效果所需的计算样式类型；CSS 仍拥有默认值和继承规则。
public sealed class StylePropertyDefinition
public string Name { get; }
public StyleValueType Type { get; }
public StylePropertyDefinition(string name, StyleValueType type);
```

### StyleValueOrigin

源码：[ComputedStyleSnapshot.cs](../Runtime/Styles/ComputedStyleSnapshot.cs)。

```csharp
public enum StyleValueOrigin { Initial, UserAgent, Author, Animation, Important, Transition }
```

### StyleValueType

源码：[StylePropertyDefinition.cs](../Runtime/Styles/StylePropertyDefinition.cs)。

```csharp
public enum StyleValueType { Number, Pixels, Color, Keyword, Image }
```
