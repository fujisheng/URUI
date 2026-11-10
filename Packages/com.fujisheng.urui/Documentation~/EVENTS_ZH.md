# 输入与事件

输入入口是 **`document.Surface.Input`**，类型 `URUI.Hosting.SurfaceInput`。同一个 Surface 只分发一次原始输入，由实际层级、模态与焦点决定文档目标。事件类型位于 URUI.Events，全部声明见[接口参考](REFERENCE_ZH.md)。

## 注册与解绑

`element.RegisterCallback<TEvent>(Action<TEvent>, TrickleDown = NoTrickleDown)` 返回 IDisposable；`UnregisterCallback<TEvent>` 用相同委托和传播阶段解绑。订阅不能在每帧重复创建。

```csharp
using System;
using URUI.Elements;
using URUI.Events;
using UnityEngine;

public static class PointerExamples
{
    public static IDisposable Observe(Element panel)
    {
        return panel.RegisterCallback<PointerDownEvent>(evt =>
        {
            Debug.Log(evt.Target.Name);
            evt.CurrentTarget.CapturePointer(evt.PointerId);
        }, TrickleDown.TrickleDown);
    }
}
```

调用方在页面解绑时 Dispose。相同委托、事件类型和阶段的重复注册返回同一订阅。Button.Clicked 和输入控件 Submitted 为普通 C# 事件，用同一委托 -= 解绑；值控件 RegisterValueChangedCallback 的用法见[控件](CONTROLS_ZH.md)。

## 事件及其数据

| 事件 | 主要数据/用途 |
| --- | --- |
| `ClickEvent` | 激活目标 |
| `PointerDown/Move/Up/Cancel/Enter/LeaveEvent` | PointerId、Position、LocalPosition、Button、PressedButtons、IsTouch、Modifiers |
| `PointerCaptureOutEvent` | 捕获丢失 |
| `WheelEvent` | 滚轮输入，含滚动量 |
| `KeyDownEvent` / `KeyUpEvent` | Unity KeyCode、InputModifiers |
| `TextInputEvent` | Unicode 已提交文字 |
| `FocusEvent` / `BlurEvent` | 焦点变化，通知期间底层焦点提交可能仍在进行 |
| `ScrollEvent` | 滚动变化 |
| `ChangeEvent<T>` | PreviousValue、NewValue |
| `GeometryChangedEvent` | 布局变化 |
| `AttachToDocumentEvent` / `DetachFromDocumentEvent` | 挂载变化，脱离不等于销毁 |
| `AnimationEndEvent` / `TransitionEndEvent` | 完成的动画/过渡数据，精确字段见接口参考 |

`EventBase.Target` 是实际目标，可能无 id；CurrentTarget 是当前接收者；PropagationPhase 表示当前阶段。业务可派生 EventBase，并用 element.SendEvent 同步派发。布局/挂载只投递目标，普通业务和值事件可沿真实 DOM 祖先传播，包含组合控件内部内容区。

## 传播与默认行为

事件在原生派发期间同步进入托管。首条业务回调前固定目标到根的路径和订阅快照，依次执行向下、目标、向上传播，然后原生执行仍允许的默认行为。

| 方法 | 作用 |
| --- | --- |
| `StopPropagation()` | 当前节点回调完成后停止后续托管路径 |
| `StopImmediatePropagation()` | 同时停止当前节点余下回调 |
| `PreventDefault()` | 请求取消可中断的原生默认行为，不停止托管传播 |

PreventDefault 不能撤销已提交的值变化或焦点通知。当前快照中的解绑立即生效，新注册从下次派发生效；目标销毁、脱离或文档失效会停止分发，同文档重挂载沿本次固定路径完成。异常记录到 Unity Console，不穿越 native ABI。

## SurfaceInput 接口

默认 UseUnityInput=true，轮询 Input Manager (Old/Both)。使用新 Input System、平台键盘或其他来源时，先设为 false；切回内置输入时恢复 true。不要同时重复注入同一输入。

```text
SendPointer(PointerPhase phase, Vector2 position, int pointerId = 0,
    bool isTouch = false, int button = 0, int pressedButtons = 0,
    Vector2 delta = default, InputModifiers modifiers = None)
SendKey(KeyCode key, bool pressed, InputModifiers modifiers = None)
SendText(string text)
SendComposition(string text)
CommitComposition(string text)
CancelComposition()
Reset()
CancelPointers(Element owner)
```

| 参数/方法 | 约束 |
| --- | --- |
| `phase` | Down、Move、Up、Cancel、Wheel |
| `position` | 共享文档左上角坐标，不能直接传 Unity 屏幕左下角坐标 |
| `pointerId` | 非负，接触期间稳定，多个触摸用不同编号 |
| `isTouch` | 是否触摸 |
| `button` / `pressedButtons` | 按钮编号 0..2 / 当前按住按钮的位掩码 |
| `delta` | 移动或滚轮增量，按调用类型提供 |
| `SendKey` | 按下/释放；字符输入另外用 SendText |
| `Reset` | 清理共享输入状态，切换来源或失焦时使用 |
| `CancelPointers(owner)` | 取消指定节点/子树拥有的手势 |

输入需有已加载文档，必须在 Unity 主线程调用；不能在同步输入回调、加载事务或效果回调中重入分发。捕获/取消等特殊清理遵循内部允许的取消边界。

```csharp
using URUI.Events;
using URUI.Hosting;
using UnityEngine;

public static class InputExamples
{
    public static void Tap(Surface surface, Vector2 position, int pointerId)
    {
        var input = surface.Input;
        input.UseUnityInput = false;
        input.SendPointer(PointerPhase.Down, position, pointerId, true, pressedButtons: 1);
        input.SendPointer(PointerPhase.Up, position, pointerId, true);
    }
}
```

CapturePointer/ReleasePointer/HasPointerCapture 作用于具体 pointerId；捕获后越界移动/释放发给捕获节点，取消、失焦或禁用时清理。ScrollView 超过拖动阈值后阻止误点击；ListView 重绑行不会把旧按下转给新数据。

## 焦点、键盘和组合串

Tab/Shift+Tab 按 TabIndex 与 DOM 顺序导航，跳过禁用、隐藏及无布局节点。macOS Command 编辑快捷键由内置适配器处理，剪贴板使用 GUIUtility.systemCopyBuffer。

SendText 插入已提交文字。SendComposition 更新预览；CommitComposition 提交并通知一次；CancelComposition 恢复原内容和选区。外部赋值或焦点切换会结束旧预览。Selection/SelectRange 按 Unicode 字符计数，不是 UTF-8 字节或 UTF-16 代码单元。

系统 IME 与移动软键盘的真实验收范围见[支持矩阵](LIMITATIONS_ZH.md)；注入组合串接口不等于完成操作系统输入法或设备验收。

## 回调内页面操作

回调中 Load/Unload 排队到当前原生调用返回后执行。需要同步创建页面、克隆片段或导航时使用 `document.RunAfterEvent(action)` / `surface.RunAfterEvent(action)`；它在当前分发返回后、本次输入方法返回前运行，空闲时立即执行，不等下一帧。延后前后都可能发生卸载，执行时重新检查节点有效性。

PreventDefault 与传播控制仍在原事件回调中直接调用。绘制效果的 Prepare/Record 中不能修改 DOM 或安排页面变更。重载后重新查询和订阅，业务 OnDisable 释放自己持有的订阅。
