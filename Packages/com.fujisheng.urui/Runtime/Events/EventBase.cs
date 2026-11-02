using System;
using UnityEngine;
using URUI.Elements;

namespace URUI.Events
{
    /// <summary>事件回调注册的传播方向。</summary>
    public enum TrickleDown { NoTrickleDown, TrickleDown }
    /// <summary>当前事件在目标路径中的阶段。</summary>
    public enum PropagationPhase { None, TrickleDown = 1, AtTarget = 2, BubbleUp = 4 }
    /// <summary>输入时按下的修饰键。</summary>
    [Flags]
    public enum InputModifiers { None = 0, Shift = 1, Control = 2, Alt = 4, Command = 8 }

    /// <summary>同步事件；同一分发共享停止状态，所有操作限主线程。</summary>
    public abstract class EventBase
    {
        /// <summary>最初命中的元素。</summary>
        public Element Target { get; internal set; }
        /// <summary>当前执行回调的元素。</summary>
        public Element CurrentTarget { get; internal set; }
        /// <summary>当前传播阶段。</summary>
        public PropagationPhase PropagationPhase { get; internal set; }
        /// <summary>是否停止后续元素的传播。</summary>
        public bool IsPropagationStopped { get; private set; }
        /// <summary>是否同时停止当前元素余下的回调。</summary>
        public bool IsImmediatePropagationStopped { get; private set; }
        internal bool Dispatching;
        /// <summary>是否请求取消可取消的控件默认行为；不改变托管传播。</summary>
        public bool IsDefaultPrevented { get; private set; }
        /// <summary>取消默认行为；焦点等不可取消的原生通知不受影响。</summary>
        public void PreventDefault() => IsDefaultPrevented = true;
        /// <summary>是否向祖先传播；布局和挂载通知只投递目标。</summary>
        public virtual bool Bubbles => true;

        /// <summary>完成当前元素回调后停止传播；原生事件遵循 RmlUi 默认行为规则。</summary>
        public void StopPropagation() => IsPropagationStopped = true;
        /// <summary>立即停止本次事件余下的回调。</summary>
        public void StopImmediatePropagation()
        {
            IsImmediatePropagationStopped = true;
            IsPropagationStopped = true;
        }
    }

    /// <summary>指针数据，文档坐标以左上角为原点。</summary>
    public abstract class PointerEventBase : EventBase
    {
        /// <summary>同一接触期间稳定的指针编号。</summary>
        public int PointerId { get; internal set; }
        /// <summary>文档坐标位置。</summary>
        public Vector2 Position { get; internal set; }
        /// <summary>相对于当前接收元素的坐标。</summary>
        public Vector2 LocalPosition => CurrentTarget.WorldToLocal(Position);
        /// <summary>发生变化的按钮，触摸为 0。</summary>
        public int Button { get; internal set; }
        /// <summary>按下按钮的位掩码。</summary>
        public int PressedButtons { get; internal set; }
        /// <summary>当前修饰键。</summary>
        public InputModifiers Modifiers { get; internal set; }
        /// <summary>是否来自触摸。</summary>
        public bool IsTouch { get; internal set; }
    }

    /// <summary>滚轮输入。</summary>
    public sealed class WheelEvent : PointerEventBase
    {
        /// <summary>横纵滚动增量。</summary>
        public Vector2 Delta { get; internal set; }
    }

    /// <summary>键盘事件数据。</summary>
    public abstract class KeyboardEventBase : EventBase
    {
        /// <summary>Unity 键码。</summary>
        public KeyCode KeyCode { get; internal set; }
        /// <summary>当前修饰键。</summary>
        public InputModifiers Modifiers { get; internal set; }
    }

    /// <summary>文本输入通知。</summary>
    public sealed class TextInputEvent : EventBase
    {
        /// <summary>UTF-16 托管文本。</summary>
        public string Text { get; internal set; }
    }
    /// <summary>最近完成的布局发生变化。</summary>
    public sealed class GeometryChangedEvent : EventBase
    {
        /// <summary>上一次完成的布局。</summary>
        public Rect OldRect { get; internal set; }
        /// <summary>本次完成的布局。</summary>
        public Rect NewRect { get; internal set; }
    }
    /// <summary>控件值变化；相同值与静默赋值不通知。</summary>
    public sealed class ChangeEvent<T> : EventBase
    {
        /// <summary>变化前的值。</summary>
        public T PreviousValue { get; internal set; }
        /// <summary>变化后的值。</summary>
        public T NewValue { get; internal set; }
    }
    internal sealed class NativeValueChangedEvent : EventBase { }

    /// <summary>指针按下。</summary>
    public sealed class PointerDownEvent : PointerEventBase { }

    /// <summary>指针移动，包含悬停。</summary>
    public sealed class PointerMoveEvent : PointerEventBase { }

    /// <summary>指针释放。</summary>
    public sealed class PointerUpEvent : PointerEventBase { }

    /// <summary>指针被系统取消。</summary>
    public sealed class PointerCancelEvent : PointerEventBase { }

    /// <summary>指针进入。</summary>
    public sealed class PointerEnterEvent : PointerEventBase { }

    /// <summary>指针离开。</summary>
    public sealed class PointerLeaveEvent : PointerEventBase { }

    /// <summary>失去指针捕获。</summary>
    public sealed class PointerCaptureOutEvent : PointerEventBase { }

    /// <summary>按键按下，包含重复输入。</summary>
    public sealed class KeyDownEvent : KeyboardEventBase { }

    /// <summary>按键释放。</summary>
    public sealed class KeyUpEvent : KeyboardEventBase { }

    /// <summary>元素获得焦点。</summary>
    public sealed class FocusEvent : EventBase { }

    /// <summary>元素失去焦点。</summary>
    public sealed class BlurEvent : EventBase { }

    /// <summary>滚动位置变化。</summary>
    public sealed class ScrollEvent : EventBase { }

    /// <summary>连接到文档。</summary>
    public sealed class AttachToDocumentEvent : EventBase { }

    /// <summary>脱离文档，节点仍然有效。</summary>
    public sealed class DetachFromDocumentEvent : EventBase { }
}
