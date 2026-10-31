# C# API

业务接口位于 namespace **URUI**、程序集 **URUI.Runtime**。公开类型为 `Document`、`Element`、`ElementStyle`、`ImageAsset` 和 `ClickEvent`。有 asmdef 的业务程序集引用 `URUI.Runtime`。

## Document

`Document` 是承载 RML 文档的 `MonoBehaviour`。所有加载、查询、更新和释放操作均在 Unity 主线程完成。

| 成员 | 行为 |
| --- | --- |
| `Source` | `TextAsset` 文档源；启用时加载。运行时赋值后可显式 `Load`，或在下次启用时加载 |
| `Images` | `ImageAsset[]` 图片映射；数组读写使用副本，纹理仍由应用持有 |
| `FontPath` | 字体文件绝对路径；为空时使用平台默认路径 |
| `Load(TextAsset)` | 同步加载应用持有的文本资产 |
| `LoadMarkup(string)` | 同步加载 RML 字符串 |
| `IsLoaded` | 文档已加载，可查询元素；不表示当帧布局和绘制已经完成 |
| `Q(string id)` | 按唯一 RML ID 查询，不带 `#`；未加载或缺少节点时返回 `null` |
| `Unload()` | 同步释放文档、渲染资源与订阅；可重复调用 |
| `Loaded` / `Unloaded` | 加载完成和卸载通知；不能在生命周期回调中再次加载或卸载 |

修改 `Source`、`Images`、`FontPath` 前先 `Unload()`。`Load` 和 `LoadMarkup` 会替换当前文档；旧元素引用随之失效。

## Element

通过 `Document.Q(id)` 获取元素；同一有效节点复用包装对象。`Id` 表示查询时的 ID，`IsValid` 表示原节点和所属文档仍有效。

| 成员 | 行为 |
| --- | --- |
| `SetText(value)` | 写入普通文本，不将 `<`、`&` 解释为 RML；可能替换原子节点的子元素 |
| `SetAttribute(name, value)` | 设置属性；不允许修改 `id` |
| `SetInnerMarkup(rml)` | 显式替换内部 RML；旧子节点与其订阅失效 |
| `Append(tag, id)` | 追加具有唯一 ID 的 RML 元素，返回其 `Element` |
| `Remove()` | 删除节点并释放本引用和订阅；子节点随结构删除而失效 |
| `SetTransform(translation, scale)` | 使用两个 `Vector2` 设置二维位移和缩放 |
| `RegisterClickCallback(callback)` | 注册 `Action<ClickEvent>`，返回可释放的 `IDisposable` |
| `UnregisterClickCallback(callback)` | 解绑指定委托；重复解绑安全 |
| `Dispose()` | 释放引用与订阅，不删除 RML 节点；可重新查询 |

元素 ID 必须唯一。通过 `SetInnerMarkup` 编写结构时也要维持这一约束。元素被移除、替换、卸载或重载后，旧包装不会指向相同 ID 的新节点。

## ElementStyle

通过 `element.Style` 写入 RmlUi RCSS 行内样式。

| 成员 | 行为 |
| --- | --- |
| `Set(property, value)` | 写入 RCSS 字符串，例如 `Set("display", "flex")` |
| `SetPixels(property, value)` | 写入 px 数值 |
| `SetWidth/SetHeight/SetLeft/SetTop(value)` | 常用 px 属性 |
| `SetOpacity(value)` | 写入 0～1 透明度 |
| `SetDisplay(visible)` | `true` 写入 `block`，`false` 写入 `none`；其他布局用 `Set` 指定 |
| `SetBackgroundColor(Color)` | 写入 RGBA 背景色 |

数值接口拒绝 NaN 和 Infinity。样式更新按 RCSS 的布局规则解释；当前没有已计算样式的读取接口。

## 图片与事件

`new ImageAsset("icons/coin.png", texture)` 将 RML/RCSS 中的路径映射到可读 `Texture2D`。`Source`、`Texture` 为只读属性；URUI 不销毁应用的纹理。

`ClickEvent.Target` 是收到通知的 `Element`。事件目标选择和释放方式见[点击事件](EVENTS_ZH.md)，资产配置见[生命周期与资源](LIFECYCLE_ZH.md)。

## 错误与边界

无效元素的写入、跨线程操作、禁用时加载、第二个活动文档、生命周期回调重入会抛出 `InvalidOperationException`。无效参数或资源配置会抛出 `ArgumentException` 及其派生异常。被 RmlUi 拒绝的属性或样式写入会报告失败，加载问题也会输出到 Unity Console。

`Q` 只接受 ID，不是 CSS 选择器或通用控件查询。URUI 不提供 Unity UI Toolkit 的 UXML、USS、泛型控件、UI Builder、PanelSettings 或完整事件协议。RML/RCSS 语义以包内 RmlUi 版本为准。
