using System;

namespace URUI.Events
{
    internal static class EventTypes
    {
        static readonly Type[] nativeTypes = { null, typeof(ClickEvent), typeof(PointerDownEvent), typeof(PointerMoveEvent), typeof(PointerUpEvent), typeof(PointerCancelEvent), typeof(PointerEnterEvent), typeof(PointerLeaveEvent), typeof(WheelEvent), typeof(KeyDownEvent), typeof(KeyUpEvent), typeof(FocusEvent), typeof(BlurEvent), typeof(NativeValueChangedEvent), typeof(TextInputEvent), typeof(ScrollEvent), typeof(PointerCaptureOutEvent) };

        internal static int NativeId(Type type) => Math.Max(0, Array.IndexOf(nativeTypes, type));

        internal static EventBase Create(int type)
        {
            switch (type)
            {
                case 1: return new ClickEvent();
                case 2: return new PointerDownEvent();
                case 3: return new PointerMoveEvent();
                case 4: return new PointerUpEvent();
                case 5: return new PointerCancelEvent();
                case 6: return new PointerEnterEvent();
                case 7: return new PointerLeaveEvent();
                case 8: return new WheelEvent();
                case 9: return new KeyDownEvent();
                case 10: return new KeyUpEvent();
                case 11: return new FocusEvent();
                case 12: return new BlurEvent();
                case 13: return new NativeValueChangedEvent();
                case 14: return new TextInputEvent();
                case 15: return new ScrollEvent();
                case 16: return new PointerCaptureOutEvent();
                default: throw new NotSupportedException("未知 URUI 原生事件。");
            }
        }
    }
}
