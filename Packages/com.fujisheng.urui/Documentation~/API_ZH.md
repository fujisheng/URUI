# Document 与 Element API

对应 URUI `0.8.0-preview.1`。本页说明页面和节点的使用顺序；所有公开类型、重载、参数类型及继承关系见[接口声明参考](REFERENCE_ZH.md)。先完成[安装](INSTALL_ZH.md)与[快速开始](QUICK_START_ZH.md)。

## 命名空间与入口

| 命名空间 | 常用接口 | 用途 |
| --- | --- | --- |
| `URUI.Hosting` | `Surface`、`SurfaceInput` | 共享视口、时钟、输入与提交 |
| `URUI.Documents` | `Document` | 加载、节点工厂、页面呈现 |
| `URUI.Elements` | `Element`、`ElementStyle`、`Length` | 层级、查询、样式、坐标 |
| `URUI.Controls` | `Button`、`TextField`、`ListView` 等 | [控件](CONTROLS_ZH.md) |
| `URUI.Events` | `EventBase`、`ChangeEvent<T>` 等 | [输入与事件](EVENTS_ZH.md) |
| `URUI.Resources` | `TextResource`、`ImageAsset`、`ImageLease` | [资源与生命周期](LIFECYCLE_ZH.md) |
| `URUI.Styles`、`URUI.Animation` | `DocumentStyles`、`AnimationPlayback` | [样式与动画](STYLES_ANIMATION_ZH.md) |
| `URUI.Rendering`、`URUI.Rendering.Effects` | 绘制窗口、效果注册与帧 | [渲染扩展](RENDERING_ZH.md) |
| `URUI.Media` | `VideoPlayback` | [视频输出](RENDERING_ZH.md#视频输出) |

内部互操作与渲染实现不是业务 API。公开节点由 Document 创建，不能直接 `new Button()`。

## Surface 与 Document 配置

二者都是 MonoBehaviour。场景中放置一个启用的 Surface，每份 Document 显式设置 Surface；可放在同一对象，也可多个页面引用同一 Surface。

| 接口 | 用法与约束 |
| --- | --- |
| `Surface.Viewport : Vector2Int` | 逻辑 CSS 像素尺寸；零跟随屏幕，否则两维均须为正 |
| `Surface.UiClock : IUiClock` | 默认非缩放时钟；加载任何文档后不能切换 |
| `Surface.Input : SurfaceInput` | 同一 Surface 的原始输入入口，跨页面分发 |
| `Document.Surface` | 加载前关联共享宿主 |
| `Document.CompiledSource : CompiledDocumentAsset` | 正式页面的自动加载源；启用时实例化编译计划 |
| `Document.Source : TextAsset` | 显式动态源码配置；正式构建的序列化文档使用 CompiledSource |
| `Document.SourceUrl : string` | HTML 资源基址，例如 `/ui/panel.html` |
| `Document.TextResources`、`Images`、`ImageResolver` | 所属文档卸载后配置资源清单 |
| `Document.IsLoaded` | DOM 已加载可查询，不代表该帧布局/绘制已完成 |

脚本动态创建时，先停用页面对象，配置 Surface/资源后再激活和加载，避免依赖 Unity 启用顺序。完整组件见快速开始。

## 加载、重载和通知

| 接口 | 结果 |
| --- | --- |
| `Load(CompiledDocumentAsset asset)` | 实例化 Editor 编译的 HTML/CSS、模板与节点索引 |
| `Load(TextAsset asset, string sourceUrl)` | 同步加载资产中的完整 HTML |
| `LoadHtml(string html, string sourceUrl)` | 同步加载字符串中的完整 HTML |
| `Unload()` | 卸载当前页面，取消自动恢复请求 |
| `Loaded : Action<Document>` | 成功加载后通知；重新查询节点、建立订阅 |
| `Unloaded : Action<Document>` | 旧文档已卸载；旧节点和订阅已失效 |
| `LayoutUpdated : Action<Document>` | 本轮原生布局刷新后通知，可读布局/样式 |
| `SynchronizeLayout()` | 显式同步；立即测量时使用，避免每个业务 Update 重复调用 |
| `RunAfterEvent(Action)` | 当前事件/原生回调返回后执行；空闲时立即执行 |
| `BeginUpdate()` | 合并初始化中的几何请求；嵌套作用域在最外层结束时同步 |
| `RequestGeometry(Element root)` | 批次内延后同步，普通调用在返回前准备该子树 |
| `FlushPendingGeometry()` | 缓存几何的适配器在读取前提交待处理布局 |

加载先构建候选，失败保留旧 DOM 和资源。成功重载后重新查询，同名旧 Element 不会恢复有效。回调中的生命周期请求会排队，不会在回调内立即切页。业务组件订阅 Loaded 时还应处理已加载情况，见入门组件。

Editor 使用 `URUI.Editor.Compilation.CompiledDocumentCompiler.CompileAndSave(html, sourceUrl, destination, dependencies)` 生成稳定 GUID 的资产。dependencies 提供规范 URL 到 TextAsset 的映射，编译器继续发现 HTML/CSS 的实际依赖闭包；导入器更新已有资产，构建门禁拒绝过期源码、缺失依赖与损坏字节。作者格式始终是 HTML/CSS。

用 `GetCompiledTemplate(uri)` 获取当前页面作用域的模板，再调用 `element.SetInnerHtml(template)`。模板不能跨不同父文档编译计划混用。`CreateCompiledQuerySnapshot(root)` 一次映射子树节点；`TryQuery(".class", out result)` / `TryQuery("#id", out result)` 返回有序结果。结构或对应名称变化时返回 false，调用普通 DOM 查询；卸载后不能继续枚举旧结果。

批次中读取 HasLayout、布局、坐标或计算样式会刷新待处理几何；布局/绘制回调仍读取当前回调快照，避免递归布局。显式 SynchronizeGeometry / SynchronizeLayout 立即执行。业务读取可以形成必要的提前同步边界。

`SetPresentation(int layer, int order, bool visible, bool modal)` 配置顶层文档层级、同层顺序、显隐及模态阻断。使用 FUI 时交给适配层管理。`Focus()` 请求页面焦点，隐藏或被模态阻断时可返回 false。`FocusedElement` 无业务焦点时为 null；`Pick(Vector2)` 返回文档坐标处的实际命中节点。

## 查询与节点工厂

| 接口 | 语义 |
| --- | --- |
| `DocumentElement` / `Body` | html/body，未加载时可能为 null |
| `Q<T>(string name = null, string className = null)` | 当前根及后代中首个类型、id、类名均匹配的节点；无匹配返回 null |
| `Query<T>(...)` | 查询描述，执行时取得当前有序快照 |
| 非泛型 `Q(...)` / `Query(...)` | 首个 Element / 有序只读节点列表 |
| `Create<T>() where T : Element` | 已登记工厂创建的同文档、未挂载节点 |
| `CreateElement(string tag = "div")` | 按标准或注册标签创建 |
| `RegisterElementType<T>(string tag, Func<ElementContext,T> factory)` | 加载自定义标签前登记工厂 |

`name` 对应 HTML 的 id，不带 `#`；`className` 是单个类名，不带 `.`，接口不解析 CSS 选择器。名字可以重复和修改，身份由文档代际与 token 决定；查询不跨文档。

```csharp
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

public static class NodeExamples
{
    public static Label AddCaption(Document document, string title)
    {
        var label = document.Create<Label>();
        label.Name = "caption";
        label.Text = title;
        document.Body.Add(label);
        return label;
    }

    public static void ResetSettings(Element root)
    {
        root.Query<Toggle>(className: "setting")
            .Where(field => field.EnabledInHierarchy)
            .ForEach(field => field.SetValueWithoutNotify(false));
    }
}
```

`ElementQuery<T>` 还提供 `Children()`、`First()`、`Last()`、`AtIndex(int)`、`ToList()`。Children 限定下一层；每次执行重新取快照。没有无分配查询保证。

## 层级、复制与内容

| 接口 | 何时使用 |
| --- | --- |
| `Parent` | 真实 DOM 父级 |
| `ContentContainer` | 组合控件提供给业务的内容区，普通 Element 为自身 |
| `ChildCount`、`this[int]`、`Children()` | 内容区的直接子节点 |
| `Add(Element)`、`Insert(int,Element)` | 将同文档节点挂载到内容区 |
| `Remove(Element)`、`RemoveFromHierarchy()`、`Clear()` | 移出，保留身份、子树及订阅 |
| `Destroy()` | 销毁节点和整个子树 |
| `Hierarchy` | 显式访问真实 DOM，包含控件内部结构 |
| `Clone()` | 结构复制，获得同文档的新身份；另行建立业务订阅 |
| `Contains(Element)`、`GetFirstAncestorOfType<T>()` | 包含关系与祖先查询 |
| `BringToFront()`、`SendToBack()` | 调整兄弟顺序 |

不能跨文档挂载、形成循环、移动或销毁文档根。不要绕过组合控件管理其内部节点。Element 不实现 IDisposable，文档拥有附着/脱离节点；不再需要时可 Destroy。

`Text` 是纯文本写入：普通元素写入会销毁原子节点，读取结构化内容可能抛异常。`SetInnerHtml(string html, string sourceUrl)` 严格解析并事务替换片段，失败保留旧子树；`GetInnerHtml()` 返回 HTML。HTML template 惰性保存，事件中需同步复制节点时使用 RunAfterEvent。

`GetAttribute` 无属性时返回 null；`SetAttribute`/`RemoveAttribute` 操作属性。类名用 `AddToClassList`、`RemoveFromClassList`、`ClassListContains`、`EnableInClassList`。`UserData` 可存业务对象，其外部资源由业务释放。

## 状态、焦点与坐标

| 接口 | 语义 |
| --- | --- |
| `IsValid` / `IsAttached` | 属于当前有效文档 / 已连接文档根；脱离不等于销毁 |
| `SetEnabled(bool)`、`EnabledSelf`、`EnabledInHierarchy` | 交互状态；禁用子树清理捕获和焦点 |
| `Visible` | 隐藏绘制并保留布局 |
| `Style.Display = DisplayStyle.None` | 从布局中移除 |
| `PickingMode` | 当前节点的拾取策略，枚举见接口参考 |
| `Focusable`、`TabIndex`、`Focus()`、`Blur()`、`Focused` | 焦点配置及请求；正 TabIndex 优先，0 按 DOM 顺序 |
| `HasLayout` | 至少完成一次布局，首次布局前不要读矩形 |
| `Layout` | 相对布局父级的 border 矩形 |
| `ContentRect` / `ViewportRect` | 本地内容区 / 扣除滚动条和内边距的实际视口 |
| `WorldBound` | 累积变换后的文档坐标包围框 |
| `LocalToWorld`、`WorldToLocal`、`TryWorldToLocal` | 坐标转换，奇异变换用 Try 判断 |
| `SetTransform(...)` | 二维/三维变换，支持显式 originPercent 重载 |
| `ScrollOffset` / `ScrollSize` | 当前滚动量 / 内容尺寸 |

样式更新后普通 getter 读取最近完成的布局，不自动刷新整页。变化通知用 `GeometryChangedEvent`；测量回调中避免形成布局写入循环。CSS 更新见[样式与动画](STYLES_ANIMATION_ZH.md)。

## 自定义控件

构造函数接收 ElementContext，OnInitialize 建立内部结构；OnUpdate 在布局刷新后执行；OnInvalidated 只清理托管订阅，不再访问原生节点。组合控件可覆写 ContentContainer，通过 Hierarchy 安装内部节点。

```csharp
using URUI.Documents;
using URUI.Elements;

public sealed class CounterElement : Element
{
    int count;
    public CounterElement(ElementContext context) : base(context) { }
    public void Increment()
    {
        count++;
        Text = count.ToString();
    }
    public static void Register(Document document)
    {
        document.RegisterElementType("urui-counter", context => new CounterElement(context));
    }
}
```

加载含 `<urui-counter></urui-counter>` 的 HTML 前调用 Register；加载后用 `Q<CounterElement>()` 或 `Create<CounterElement>()`。工厂不依赖反射构造。
