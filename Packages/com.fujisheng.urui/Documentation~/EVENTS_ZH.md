# 输入与事件

事件在原生派发期间同步进入托管，随后 RmlUi 执行允许的默认行为。托管层在首条业务回调前固定实际目标到根的路径与全部订阅快照，再完成向下、目标、向上传播；不存在事后点击队列的第二条托管通知链路。

```csharp
using URUI.Events;

var subscription = panel.RegisterCallback<PointerDownEvent>(evt =>
{
    UnityEngine.Debug.Log(evt.Target.Name);
    evt.CurrentTarget.CapturePointer(evt.PointerId);
}, TrickleDown.TrickleDown);
subscription.Dispose();
```

## 事件种类

- ClickEvent；PointerDown/Move/Up/Cancel/Enter/LeaveEvent；PointerCaptureOutEvent；WheelEvent。
- KeyDownEvent、KeyUpEvent、TextInputEvent、FocusEvent、BlurEvent、ScrollEvent。
- ChangeEvent<T>；GeometryChangedEvent；AttachToDocumentEvent、DetachFromDocumentEvent。
- 派生 EventBase 的业务事件，通过 element.SendEvent 同步发送。

Target 是真实节点，可能没有名字；CurrentTarget 是当前接收者，PropagationPhase 表示当前阶段。指针事件提供 PointerId、Position、LocalPosition、Button、PressedButtons、IsTouch、Modifiers。键盘事件提供 Unity KeyCode 和 InputModifiers；文本使用托管 Unicode 字符串。

布局与挂载通知只投递目标。普通业务和值事件可沿 DOM 祖先传播，组合内容区也是实际路径的一部分。Focus/Blur 是 RmlUi 焦点变化通知，通知期间底层焦点提交仍可能进行中。

## 控制传播与默认行为

- StopPropagation 完成当前元素的回调后停止后续托管路径。
- StopImmediatePropagation 同时停止当前元素余下的回调。
- PreventDefault 独立请求取消原生默认行为；不停止托管传播。仅对 RmlUi 可中断的事件生效，不能用它撤销已经发生的值变化或焦点通知。
- 相同委托、事件类型、传播阶段只注册一次，返回同一订阅。Dispose/UnregisterCallback 只解绑。
- 解绑在当前快照中也生效；新增订阅从下一次事件生效。
- 目标销毁、脱离或文档失效后停止分发；同文档重挂载沿本次已固定的路径完成。
- 回调异常记录到 Unity Console；异常不穿越原生 ABI。

ClickEvent 从旧版只读 struct 升级为 EventBase 派生类，旧的 where TEvent:struct 扩展需要迁移。原有注册、解绑和订阅释放写法保留。

## 指针与取消

鼠标与触摸通过同一输入入口，触摸保留各自编号和捕获。CapturePointer 使越界后的移动/释放发送到捕获节点；释放、取消、失焦或禁用子树后会清理状态。滑块与触摸滚动区域分别管理自己的手势，不把并发手指当成同一鼠标按钮。

输入来自外部系统时先设置 `document.Input.UseUnityInput = false`，然后调用：

```csharp
document.Input.SendPointer(PointerPhase.Down, position, pointerId: 1, isTouch: true);
document.Input.SendPointer(PointerPhase.Move, nextPosition, pointerId: 1, isTouch: true);
document.Input.SendPointer(PointerPhase.Up, nextPosition, pointerId: 1, isTouch: true);
```

ScrollView 触摸移动超过 4 像素后进入拖动；释放时阻止误点击。ListView 的行在重绑后不会把旧按下对应的点击选择到新数据。

## 键盘、文本与输入法

SendKey 使用 Unity KeyCode；Shift、Control、Alt、Command 使用 InputModifiers。Tab/Shift+Tab 按 TabIndex 与 DOM 顺序导航，并排除禁用、隐藏和无布局的元素。内置轮询支持按键重复、剪贴板与应用失焦清理；macOS Command 快捷键映射到原生编辑操作。

SendText 插入已提交文本。SendComposition 更新预览，CommitComposition 提交并通知一次，CancelComposition 恢复原内容和选区。切换焦点或外部赋值会结束旧预览。Selection/SelectRange 采用 Unicode 字符索引，不是 UTF-8 字节或 UTF-16 代码单元索引。

内置后端仍使用 Input Manager (Old)/Both；新 Input System 可通过公开输入入口接入，不自动增加该包依赖。系统 IME 与移动软键盘的人工/设备验证范围见支持矩阵，自动注入组合串不等于真实系统输入法验收。

## 生命周期

事件回调中请求 Load/Unload 会排队到当前原生调用返回后执行，避免销毁正在执行的 context；因此回调内部不会立刻得到新文档。Loaded/Unloaded 禁止重入加载。文档卸载使旧节点与订阅失效，业务仍应在 OnDisable 释放自己持有的订阅。
