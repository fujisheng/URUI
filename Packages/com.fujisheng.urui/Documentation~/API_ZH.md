# C# API

当前文档描述 URUI 的 Element API。Unity 运行验证范围为 6000.3.22f1 / macOS ARM64 Editor；平台产物与检查范围见[支持矩阵](LIMITATIONS_ZH.md)。没有数据绑定或 FUI 依赖。

## 命名空间

| 命名空间 | 公开类型 |
| --- | --- |
| `URUI.Documents` | `Document` |
| `URUI.Elements` | `Element`、`ElementStyle` |
| `URUI.Events` | `ClickEvent` |
| `URUI.Resources` | `ImageAsset` |

以上类型均位于 `URUI.Runtime` 程序集。按使用的类型添加 using；内部命名空间不是业务扩展接口。

## Document

| 接口 | 行为 |
| --- | --- |
| `Source`、`Images`、`FontPath` | 下次加载使用的资产配置；修改前先卸载，资产仍由调用者持有 |
| `Load(TextAsset)`、`LoadMarkup(string)` | 同步解析，完成后触发 Loaded；布局与命中在后续更新中完成 |
| `IsLoaded`、`Loaded`、`Unloaded` | 文档生命周期；生命周期通知中不能重入加载/卸载 |
| `Root` | 当前 RML 根节点；未加载时为 null，根的 Parent 为 null |
| `CreateElement(string tag = "div")` | 创建由文档拥有、尚未挂载的原生节点 |
| `Q(name: null, className: null)` | 从根开始查首项，未加载时返回 null |
| `Query(name: null, className: null)` | 从根开始查有序快照，未加载时返回空列表 |
| `Unload()` | 释放活动文档、脱离的子树和渲染资源，取消自动恢复 |

一个活动文档、Unity 主线程访问。组件启用会加载 Source；业务组件应订阅 Loaded，并处理已经加载的情况，不能假设不同组件的 OnEnable 顺序。

## Element 身份与层级

`Element` 表示原节点，不按名字重新定位。相同节点在查询、Parent、索引、脱离与重新挂载中复用同一个托管包装。

| 接口 | 行为 |
| --- | --- |
| `Name` | 读写 RML id，可为空或重复；名字变化不改变身份 |
| `TagName` | 只读标签名，文本节点为 `#text` |
| `IsValid` | 原节点和所属文档世代仍有效；脱离层级仍为 true |
| `IsAttached` | 当前连接到文档根；无效节点访问会抛异常 |
| `Parent`、`ChildCount`、`this[index]` | 真实 DOM 父子关系，不含 RmlUi 内部非 DOM 装饰节点 |
| `Children()` | 直接子节点的有序快照 |
| `Add(child)` | 移到末尾，可挂载脱离的子树 |
| `Insert(index, child)` | 移到最终索引，ChildCount 表示追加；同父级移动按移除后的顺序定位 |
| `Remove(child)`、`RemoveFromHierarchy()` | 移出但不销毁，保留子树和订阅 |
| `Clear()` | 移出所有直接子节点，文档继续拥有它们 |
| `Destroy()` | 销毁节点及子树，对应包装和订阅失效 |

引用预算耗尽时，查询或父级获取会明确报告失败，不会将真实父级误报为 null。不能移动/销毁文档根，不能跨文档挂载或形成循环。索引/循环等普通校验失败不会先移动节点。`Element` 不实现 IDisposable；文档统一管理节点，长期不再需要的子树应 Destroy。订阅的 IDisposable 仅用于解绑。

```csharp
var panel = document.Root.Q("panel");
var label = document.CreateElement("span");
label.Name = "caption";
label.Text = "Ready";
label.AddToClassList("caption");
panel.Add(label);
label.RemoveFromHierarchy();
panel.Insert(0, label);
label.Destroy();
```

## 查询

`Q`/`Query` 搜索自身及 DOM 后代，以兄弟顺序做深度优先遍历。name 和单个 className 均为可选的精确过滤条件，提供两者时同时匹配。查询不会跳到文档外，也不解析 `#document` 等特殊名字。

```csharp
var panel = document.Root.Q("panel");
var first = panel.Q(className: "item");
foreach (var item in panel.Query(name: "item", className: "selected"))
{
    item.Style.SetOpacity(0.5f);
}
```

Query 返回 `IReadOnlyList<Element>` 快照，不是 UQueryBuilder、CSS 选择器或实时集合。名字可重复，Q 取首项，Query 取全部；RML 的样式与外部 ID 使用仍应遵守应用自己的命名约定。没有泛型控件查询、Button/Label 控件层或无分配查询保证。

## 文本、属性、类名和样式

| 接口 | 行为 |
| --- | --- |
| `Text` | 读写普通文本，保留空白、Unicode、实体样式字符和花括号；不解析 RML。读取结构化子元素会抛异常，写入会销毁旧子节点 |
| `SetInnerMarkup(markup)` | 必须先挂载节点，再使用 RmlUi 解析器替换子结构，遵循其容错规则，没有事务性校验或自动绑定 |
| `GetAttribute/HasAttribute/SetAttribute/RemoveAttribute` | 真实、区分大小写的属性；缺失返回 null，存在但空返回空字符串。小写 id 对应 Name |
| `ClassListContains/AddToClassList/RemoveFromClassList/EnableInClassList` | 单个、区分大小写的类名，类名方法与 class 属性保持一致 |
| `Style.Set(property, value)` | 写入原生 RCSS 行内属性 |
| `Style.SetPixels`、`SetWidth/Height/Left/Top` | 有限数值的 px 写入 |
| `Style.SetOpacity/SetBackgroundColor/SetDisplay` | 常用样式写入；display 的 true 明确写 block，恢复样式表规则应 Remove |
| `Style.Remove(property)` | 移除行内覆盖，让类名/样式表重新决定值 |
| `SetTransform(translation, scale)` | 有限二维位移和缩放，保持 RmlUi 的变换顺序与 origin 语义 |

不伪造已计算样式 getter，也不提供完整 UITK StyleLength/布局类型体系。

## 事件与未来适配边界

`RegisterCallback<ClickEvent>` 返回订阅；`UnregisterCallback<ClickEvent>` 解绑同一委托。当前只有 ClickEvent，其他结构类型明确抛 NotSupportedException。实际目标、接收者及回调修改规则见[点击事件](EVENTS_ZH.md)。

未来 FUI 适配器可以包装 Element 的 Name/Parent、创建/移动/销毁、属性/样式与订阅令牌。当前不实现 FUI 接口、不添加依赖、不提供数据绑定、渲染器工厂或空的扩展框架。
