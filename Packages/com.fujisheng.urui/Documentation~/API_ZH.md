# C# API

URUI 0.2 提供类型化控件、查询、样式与输入事件。所有节点操作限 Unity 主线程，一个活动 Document；RML/RCSS 的布局规则仍由 RmlUi 决定。平台证据见[支持范围](LIMITATIONS_ZH.md)。

## 公开模块

| 命名空间 | 职责 |
| --- | --- |
| `URUI.Documents` | `Document`、`DocumentInput`：加载、节点工厂、输入与焦点 |
| `URUI.Elements` | `Element`、`ElementContext`、`ElementHierarchy`、`ElementQuery<T>`、样式/长度/几何 |
| `URUI.Controls` | 文本、图片、按钮、值控件、滚动/折叠/进度容器与虚拟列表 |
| `URUI.Events` | 事件、传播阶段、指针阶段与输入修饰键 |
| `URUI.Resources` | `ImageAsset` 图片映射 |

内部渲染和互操作命名空间不是业务接口。无数据绑定、FUI、uGUI 或 Unity UIElements 运行依赖。剪贴板使用引擎内置 IMGUI 模块的 `GUIUtility.systemCopyBuffer`。

## Document 与身份

`Load(TextAsset)` / `LoadMarkup(string)` 同步加载，`Loaded` 后可查询；布局由随后的更新完成。`Source`、`Images`、`FontPath` 在卸载后配置，原始资产仍由调用者持有。`Unload` 释放文档、全部脱离的节点和自有渲染资源；Loaded/Unloaded 中禁止重入加载。

```csharp
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

var root = document.Root;
var save = root.Q<Button>("save");
var label = document.Create<Label>();
label.Name = "caption";
label.Text = "Ready";
root.Add(label);
```

`Create<T>()` 通过文档工厂创建节点；`CreateElement(tag)` 保留按标签创建方式。RML、泛型查询、父子遍历始终复用同一 token 的同一个类型实例。节点不按名字重新定位，名字可变或重复。输入控件的 type 与内部控件标记在首次包装时确定，之后不能通过修改属性改变类型。

`IsValid` 检查节点和文档世代；`IsAttached` 检查是否连接到文档根。移出仍有效，Destroy、结构替换、卸载后失效。节点不能跨文档移动，也不能形成循环或移动/销毁文档根。Element 不实现 IDisposable：文档拥有节点，订阅返回的 IDisposable 仅负责解绑。

## 层级与组合控件

| 接口 | 语义 |
| --- | --- |
| `Parent` | 真实 DOM 父级；组合控件的业务子节点父级可能是内部内容区 |
| `ContentContainer` | 业务子节点容器，普通 Element 为自身 |
| `ChildCount`、索引、`Children()` | 内容区的直接子节点；Children 返回有序快照 |
| `Add`、`Insert`、`Remove`、`Clear` | 操作内容区；Clear 只移出节点，不销毁 |
| `Hierarchy` | 显式访问真实 DOM 子节点，包含控件内部节点 |
| `RemoveFromHierarchy` | 脱离父级并保留身份及订阅 |
| `Destroy` | 销毁节点及其子树 |
| `Contains`、`GetFirstAncestorOfType<T>` | 包含关系与祖先查询 |
| `BringToFront`、`SendToBack` | 调整兄弟顺序 |

`Text` 是纯文本；普通元素写 Text 会销毁原子节点，读结构化内容会抛异常。TextField、Foldout、ProgressBar 的 Text 分别更新值或标题，保留内部结构。`SetInnerMarkup` 需要已挂载节点，不提供事务性解析或自动绑定；不要使用它绕过组合控件的内部所有权。

## 查询

非泛型 `Q(name, className)` / `Query(name, className)` 保留自身包含、DOM 深度优先和快照语义。名字与单个类名精确匹配，不解析 CSS 选择器。

```csharp
var first = root.Q<Button>("save");
root.Query<Toggle>(className: "setting")
    .Where(field => field.EnabledInHierarchy)
    .ForEach(field => field.SetValueWithoutNotify(true));
```

`Query<T>()` 支持 `Where`、`Children`、`First`、`Last`、`AtIndex`、`ToList`、`ForEach`，每次执行重新取当前快照。类型筛选包含派生类。没有无分配或完整 UQuery 兼容承诺。

## 状态、样式与坐标

`SetEnabled`、`EnabledSelf`、`EnabledInHierarchy` 控制交互状态及继承。禁用子树会取消其指针并释放子树内焦点；`Visible` 隐藏绘制但保留布局，`Style.Display = DisplayStyle.None` 移出布局。`Focusable`、`TabIndex`、`Focus/Blur` 与 `Document.FocusedElement` 管理焦点。正 TabIndex 先按值排序，默认 0 随后按 DOM 顺序；Shift+Tab 反向。文档根的内部焦点不作为业务焦点返回。

```csharp
panel.Style.Display = DisplayStyle.Flex;
panel.Style.FlexDirection = FlexDirection.Column;
panel.Style.Width = Length.Percent(50);
panel.Style.PaddingLeft = 12;
panel.Style.Opacity = .8f;
panel.Style.BackgroundColor = UnityEngine.Color.blue;
panel.Style.Width = Length.Unset;
```

Style 的长度保留 px/%/em/rem/auto；Unset 或 nullable 属性的 null 移除行内覆盖。还可使用既有 Set/SetPixels/SetWidth 等方法。`Style.Get` 只读真正行内值；`ResolvedStyle.Get` 读取级联与变量解析结果，返回值可能仍包含相对单位。强类型枚举只覆盖当前明确映射的 RCSS，复杂表达式使用原生字符串接口。

`HasLayout` 表示节点至少参与一次已完成的布局。`Layout` 是相对布局父级的边框矩形，`ContentRect` 是本地内容区，`WorldBound` 是累积变换后的文档坐标包围框；`ResolvedStyle.Width/Height` 是实际边框像素尺寸。新节点未布局时读取会抛异常；样式更改后读的是最近完成的布局，使用 `GeometryChangedEvent` 观察变化。`LocalToWorld` / `WorldToLocal` 支持实际变换，奇异变换明确报告失败。

## 控件与值

| 控件 | 主要接口 |
| --- | --- |
| `Label` / `Image` | Text、Label.For 关联输入控件；Image.Source 对应 ImageAsset.Source |
| `Button` | Text、Clicked；指针、Enter 与 Space 激活 |
| `Toggle` / `RadioButton` | bool Value；Enter/Space 完整按下释放；RadioButton 方向键在所属表单/文档组内导航并跳过禁用项 |
| `Slider` / `IntegerSlider` | Value、LowValue、HighValue、Step、Direction、Inverted；独立指针捕获，方向/反向与键盘一致 |
| `TextField` | Value/Text、Multiline；共享文本输入能力见下文 |
| `IntegerField` / `FloatField` | 区域无关数值、Step 上下键步进；不完整/非有限输入保留上次提交值，提交或失焦恢复规范文本 |
| `Dropdown` | Value、Choices、SelectedIndex；使用原生选项存储 |
| `ScrollView` | ContentContainer、ScrollOffset、ScrollSize、ViewportRect、ScrollTo；焦点键盘与触摸滚动 |
| `Foldout` | Text、bool Value、SetValueWithoutNotify |
| `ProgressBar` | Text、Value、LowValue、HighValue；默认 0..100，RML 使用 min/max/value |
| `ListView` | 固定行高虚拟化、行复用及单选，见下文 |

BaseField<T> 统一 Value、SetValueWithoutNotify、RegisterValueChangedCallback、UnregisterValueChangedCallback；变化事件包含 PreviousValue/NewValue，相同值不重复通知。输入法预览不修改已提交值，提交后通知一次。

TextField 与数值字段继承 TextInputField<T>，共享 Placeholder、MaxLength、Selection、SelectRange、IsReadOnly、IsDelayed、CommitValue 和 Submitted。只读允许选择/复制与程序赋值，阻止键盘编辑、粘贴、直接文本及组合串输入。延后模式在 Enter、失焦或 CommitValue 时提交；切回即时模式会提交当前草稿。无效数字恢复上一有效值，不抛出业务输入异常。Submitted 只在单行 Enter 有效提交时触发；多行 TextField 保留 Enter 换行。

```csharp
var quantity = document.Create<IntegerField>();
quantity.IsDelayed = true;
quantity.Step = 5;
quantity.Submitted += value => UnityEngine.Debug.Log(value);
var text = document.Create<TextField>();
text.IsReadOnly = true;
```

数值字段的 Text 返回当前编辑文字，Value 是已提交值；TextField 的 Text 保持与 Value 一致。IntegerField 的步进在 int 边界饱和。IntegerSlider 仍由原生 float 范围控件绘制，不能承诺覆盖完整 int32 精度范围。

TextField 兼容原生 input text/password 与 textarea；C# 创建以 textarea 为载体，可切换 Multiline。RML input 本身只能单行；需要多行或动态切换时使用 textarea。选区索引采用 Unicode 字符而非 C# UTF-16 代码单元。字体仍需调用者提供相应字形。

## ListView

```csharp
var list = document.Create<ListView>();
list.Style.Height = 280;
list.FixedItemHeight = 28;
list.MakeItem = () => document.Create<Label>();
list.BindItem = (row, index) => row.Text = items[index].ToString();
list.UnbindItem = (row, index) => { /* 释放上次绑定的业务订阅 */ };
list.ItemsSource = items;
root.Add(list);
```

只实例化可见行及前后缓冲；ItemsSource 原地修改后调用 RefreshItems，更换工厂后调用 Rebuild。MakeItem 必须返回唯一、同文档、未挂载的有效节点。每次绑定保留对应的 UnbindItem 委托，在更换索引、行退役和文档卸载时清理；卸载后的解绑应只做业务清理和订阅释放，不再写旧节点。绑定回调中重载文档会在控件更新安全退出后执行。

选择使用 SelectedIndex/SelectionChanged，支持 ScrollToItem。列表获得焦点后，方向键、Home/End、PageUp/PageDown 选择并显示对应项，Enter 通过 ItemChosen 确认。行内输入控件拥有焦点时保留其编辑按键。固定行高和单选是本版本的约束，不提供动态行高或 TreeView。

## 控件基础样式

包内 Controls.rcss.txt 在业务样式之前加载，提供基本尺寸、焦点/禁用/选中外观、滑块、下拉选项、滚动条和进度条。业务 RML 中的同等或更高优先级规则继续覆盖基础样式；通用 div 的布局仍由业务决定。Runtime Controls 样例只定义页面布局，使用这份包内控件样式。

ScrollView.ViewportRect 是扣除内边距与滚动条后的实际内容视口；ContentRect 保持普通元素盒模型含义。计算可见行和滚动到子项时应使用 ViewportRect，不能把容器全部宽度当作滚动内容宽度。

## 自定义控件

派生 Element，构造函数接收 ElementContext，在 OnInitialize 构造内部节点、注册回调；需要组合内容时覆写 ContentContainer，通过 Hierarchy 放入内部结构。OnUpdate 在原生布局后执行；OnInvalidated 只做托管清理，不再访问原生节点。

在加载自定义 RML 前注册：`document.RegisterElementType("counter", context => new Counter(context));`，之后可用 `document.Create<Counter>()`。构造工厂不通过反射创建，适用于 IL2CPP。

## 输入与事件

`Document.Input` 默认轮询旧 Input Manager；设置 UseUnityInput=false 后可由外部系统使用 SendPointer/SendKey/SendText 和组合串接口提供输入。发送指针时使用文档左上角坐标、稳定的非负编号；完整接口与传播规则见[事件说明](EVENTS_ZH.md)。
