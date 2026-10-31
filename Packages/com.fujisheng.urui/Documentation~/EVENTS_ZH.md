# 点击事件

`Element.RegisterClickCallback` 将 C# 业务连接到 RmlUi 的点击通知，返回可释放的订阅。

```csharp
Action<ClickEvent> handler = notification => Debug.Log(notification.Target.Id);
IDisposable subscription = document.Q("action").RegisterClickCallback(handler);

// 组件禁用时释放；重复释放安全。
subscription.Dispose();
```

如果保留了同一个委托，也可调用 `element.UnregisterClickCallback(handler)` 解绑。相等的委托在同一有效元素上重复注册只产生一次回调，并返回同一订阅。使用不同 lambda 注册时，保留各自的返回值以便释放。

## 事件目标

原生桥收到 RmlUi `click` 后，从实际目标向父级寻找最近的非空 ID。`Document` 只将通知交给对应 ID 的有效、已查询 `Element`。

例如，点击没有 ID 的按钮文字，可以通知带 ID 的父元素；如果子元素自己有 ID，则目标为该子元素，父元素不会额外收到通知。`ClickEvent.Target` 表示此目标。

当前公开事件不提供捕获、冒泡、`StopPropagation`、指针捕获或键盘导航协议，也不等于完整输入控件系统。

## 生命周期与回调顺序

业务组件在 `OnDisable` 主动释放订阅。文档重载、禁用、销毁，或者节点删除、替换时，也会使对应元素与订阅失效。在 `Loaded` 中重新查询和绑定即可连接新文档。

点击回调中可以更新文本和样式、解绑回调，或重新加载文档：

- 已解绑的回调在本次分发中不再调用。
- 新注册的回调从下一次点击开始生效。
- 文档重载或元素失效后，原元素的剩余回调停止。
- 用户点击回调抛出的异常会记录到 Unity Console，其他仍有效的回调继续执行。

`Loaded`、`Unloaded` 是独立的文档生命周期通知，不能在其中再次调用 `Load`、`LoadMarkup` 或 `Unload`。它们按普通 C# event 传播异常，业务代码应自行处理回调中的错误。
