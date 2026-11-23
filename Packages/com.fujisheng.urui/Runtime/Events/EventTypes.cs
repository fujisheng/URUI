using System;
using System.Collections.Generic;

namespace URUI.Events
{
    internal static class EventTypes
    {
        static readonly Dictionary<Type, int> nativeIds = new()
        {
            [typeof(ClickEvent)] = 1,
            [typeof(PointerDownEvent)] = 2,
            [typeof(PointerMoveEvent)] = 3,
            [typeof(PointerUpEvent)] = 4,
            [typeof(PointerCancelEvent)] = 5,
            [typeof(PointerEnterEvent)] = 6,
            [typeof(PointerLeaveEvent)] = 7,
            [typeof(WheelEvent)] = 8,
            [typeof(KeyDownEvent)] = 9,
            [typeof(KeyUpEvent)] = 10,
            [typeof(FocusEvent)] = 11,
            [typeof(BlurEvent)] = 12,
            [typeof(NativeValueChangedEvent)] = 13,
            [typeof(TextInputEvent)] = 14,
            [typeof(ScrollEvent)] = 15,
            [typeof(PointerCaptureOutEvent)] = 16,
            [typeof(AnimationEndEvent)] = 17,
            [typeof(TransitionEndEvent)] = 18,
            [typeof(AnimationCreatedEvent)] = 19,
        };

        internal static int NativeId(Type type) => nativeIds.TryGetValue(type, out var id) ? id : 0;

        internal static EventBase Create(int type)
        {
            switch (type)
            {
                case 1:
                    return new ClickEvent();
                case 2:
                    return new PointerDownEvent();
                case 3:
                    return new PointerMoveEvent();
                case 4:
                    return new PointerUpEvent();
                case 5:
                    return new PointerCancelEvent();
                case 6:
                    return new PointerEnterEvent();
                case 7:
                    return new PointerLeaveEvent();
                case 8:
                    return new WheelEvent();
                case 9:
                    return new KeyDownEvent();
                case 10:
                    return new KeyUpEvent();
                case 11:
                    return new FocusEvent();
                case 12:
                    return new BlurEvent();
                case 13:
                    return new NativeValueChangedEvent();
                case 14:
                    return new TextInputEvent();
                case 15:
                    return new ScrollEvent();
                case 16:
                    return new PointerCaptureOutEvent();
                case 17:
                    return new AnimationEndEvent();
                case 18:
                    return new TransitionEndEvent();
                case 19:
                    return new AnimationCreatedEvent();
                default:
                    throw new NotSupportedException("未知 URUI 原生事件。");
            }
        }
    }
}
