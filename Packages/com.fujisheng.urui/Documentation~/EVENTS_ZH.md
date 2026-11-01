# 点击事件

```csharp
var button = document.Root.Q("action");
Action<ClickEvent> handler = evt => Debug.Log(evt.CurrentTarget.Name);
IDisposable subscription = button.RegisterCallback(handler);
button.UnregisterCallback(handler);
subscription.Dispose();
```

当前只支持 `ClickEvent`；可以显式写 `RegisterCallback<ClickEvent>`。相同委托重复注册返回同一订阅，不重复调用；不同 lambda 是不同订阅。订阅 Dispose 或 Unregister 只解绑，不删除节点。

## 目标与接收者

原生队列保存实际点击节点的弱身份，不按 ID 重新查找。因此匿名节点可以收点击，同名节点和同名替换不会误收旧通知。

- `Target`：真实点击目标，可能是未命名的子节点。
- `CurrentTarget`：当前正在执行回调的注册节点。

分发时先保存从目标到根的父级路径，以及路径上所有已注册的订阅，再按目标到根的顺序通知。这是普通点击通知，不提供 UITK 的捕获阶段、StopPropagation、指针捕获或事件池协议。

## 回调期间的变化

- 解绑后，本次快照中的对应回调也不会继续执行。
- 新增订阅从下一次点击生效，包括在子回调中新注册的祖先订阅。
- 目标销毁、脱离文档或文档卸载/重载后，停止剩余分发。
- 目标在同一文档内重新挂载时，本次仍使用已经保存的父级路径。
- 队列中的目标在交付前失效或已脱离层级时，丢弃通知。
- 用户回调异常记录到 Unity Console，其他仍有效的回调继续执行。

移出层级会保留订阅，重新挂载后继续使用。Destroy 和文档卸载会同步清空相关回调，避免闭包等待下次查询才被释放。OnDisable 中仍建议释放业务持有的订阅令牌。

Loaded/Unloaded 是独立的 C# 生命周期事件，不能在其中重入加载/卸载；它们按普通 C# event 传播异常。
