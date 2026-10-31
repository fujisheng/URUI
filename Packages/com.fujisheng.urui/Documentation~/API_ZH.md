# 托管 API

这些接口属于未发布候选。namespace `URUI`，程序集 `URUI.Runtime`；有 asmdef 的业务程序集只需引用 `URUI.Runtime`。底层兼容程序集 `RmlUnity.Runtime` 自动作为实现依赖。

## 文档

| 成员 | 行为 |
| --- | --- |
| `Source` | Inspector 的 TextAsset；启用时自动加载。运行时赋值后显式 Load，或下次启用加载 |
| `Images` | 图片映射数组；读写使用副本，Texture 本身仍由调用者持有 |
| `FontPath` | 加载前的字体文件绝对路径；空值使用平台默认 |
| `Load(TextAsset)` / `LoadMarkup(string)` | 同步加载；重载前卸载旧内容；成功触发 Loaded |
| `IsLoaded` | 原生文档可查询，不保证已完成本帧布局/绘制 |
| `Q(string id)` | 按唯一 RML id 查询，不带 #；缺少/未加载返回 null；同一有效节点复用包装对象 |
| `Unload()` | 同步释放 context、渲染资源和订阅；可重复调用 |
| `Loaded` / `Unloaded` | 加载和释放通知；生命周期回调中不允许重入加载/卸载 |

修改 Source/Images/FontPath 前先 Unload。所有运行时操作在 Unity 主线程完成；异步读取文件后切回 Unity 主线程再调用。不要在并行任务或终结器中 Dispose 引用。

## 元素与样式

| 成员 | 行为 |
| --- | --- |
| `Id`, `IsValid` | 查询 ID 与原对象当前有效性 |
| `SetText(value)` | 普通文字；不把 `<`、`&` 当作结构；也可能替换旧子节点 |
| `SetAttribute(name, value)` | 属性写入；禁止 id 改名 |
| `SetInnerMarkup(rml)` | 显式替换内部结构；旧子节点失效 |
| `Append(tag, id)` | 追加独一 ID 的 RML 元素并返回引用 |
| `Remove()` | 删除节点并释放本引用；旧子节点失效 |
| `SetTransform(translation, scale)` | 写入二维位移和缩放 |
| `Dispose()` | 释放包装对象的引用和订阅，不删除 RML 节点；以后可重新 Q |
| `RegisterClickCallback` / `UnregisterClickCallback` | 注册/解绑点击；注册返回 IDisposable |
| `Style.Set(property, value)` | 原始 RCSS 行内样式，例如 `Set("display", "flex")` |
| `Style.SetPixels(property, value)` | px 数值；拒绝 NaN/Infinity |
| `Style.SetWidth/SetHeight/SetLeft/SetTop` | 常见 px 样式便捷方法 |
| `Style.SetOpacity(value)` | 0～1，使用区域无关数值格式 |
| `Style.SetDisplay(visible)` | true 写 block，false 写 none；恢复 flex/inline 请显式 Set |
| `Style.SetBackgroundColor(Color)` | RGBA 颜色 |

无原生 computed-style 读取接口，因此不提供会误导的样式 getter。无效元素的写操作抛 InvalidOperationException，缺少查询返回 null；无效配置抛 ArgumentException 类异常；多 context、禁用时加载、生命周期重入和跨线程操作抛 InvalidOperationException。底层不支持的 RCSS 名称/值也会报告失败。

## 与 UIToolkit 的对应与差异

借鉴 [UIDocument](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.UIDocument.html) 的组件承载文档、[UQuery](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-UQuery.html) 的显式查询、[注册/解绑](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Events-Handling.html) 的成对回调管理。

URUI.Q 仅按 RML id；没有泛型 Button/Label 控件匹配、CSS class 查询、UXML、USS、UI Builder、PanelSettings 或完整事件池/冒泡/捕获协议。RCSS 使用 RmlUi 自身规则，不保证与浏览器 CSS 或 UITK USS 相同。没有复制 Unity 参考源码的实现。
