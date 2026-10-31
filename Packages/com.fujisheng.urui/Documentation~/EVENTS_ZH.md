# 点击事件与解绑

此页描述候选 URUI API。桥在文档上收到 RmlUi click 后，从目标往父级寻找最近的非空 id，并把这个 ID 放入队列。Document 只将通知交给相同 ID 的有效 Element；父元素不会额外收到所谓冒泡事件。

```csharp
Action<ClickEvent> handler = notification => Debug.Log(notification.Target.Id);
IDisposable subscription = document.Q("action").RegisterClickCallback(handler);

// 以下两种任选其一；重复解绑/释放安全。
subscription.Dispose();
document.Q("action").UnregisterClickCallback(handler);
```

同一个有效 Element 上，相等的委托重复注册只产生一次回调，并返回同一订阅。两个不同 lambda 是不同委托，使用返回的订阅释放最直接。业务 OnDisable 主动 Dispose；重载、禁用或销毁 Document 也会清理已有元素订阅。

回调期间可以更新文字、样式、解绑其他回调或重载文档。已解绑的回调本次不再调用；回调内新注册的回调从下一次事件开始生效；重载后旧元素的剩余回调停止。用户回调异常会记录到 Unity Console 并继续处理其他仍有效的订阅。

Loaded / Unloaded 是生命周期通知，在其中可以查询/绑定或记录状态，但不能重入 Load/Unload。若 Unloaded 禁用组件，外层加载会取消，不会让禁用组件占用 context。

普通托管点击没有 UI Toolkit 的 propagationPhase、StopPropagation、TrickleDown、PointerCapture 和键盘导航承诺。兼容 Host 仍保留旧 PointerInput 通知，但普通候选 API 暂未把它包装成新的元素事件系统。不要把旧通知当作已支持完整输入控件。
