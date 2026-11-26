using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Samples.Tideglass
{
    /// <summary>Owns the drag on an orbital display and snaps its data selection on release.</summary>
    internal sealed class TideglassOrbit : IDisposable
    {
        readonly Element pad;
        readonly Element subject;
        readonly Action<int> step;
        readonly IDisposable[] subscriptions;
        Vector2 origin;
        int pointer = -1;
        float startAngle;
        float angle;
        bool reduced;

        internal TideglassOrbit(Element pad, Element subject, Action<int> step, bool reduced)
        {
            this.pad = pad;
            this.subject = subject;
            this.step = step;
            this.reduced = reduced;
            subscriptions = new[]
            {
                pad.RegisterCallback<PointerDownEvent>(Begin),
                pad.RegisterCallback<PointerMoveEvent>(Drag),
                pad.RegisterCallback<PointerUpEvent>(Release),
                pad.RegisterCallback<PointerCancelEvent>(Cancel),
                pad.RegisterCallback<WheelEvent>(Wheel)
            };
        }

        void Begin(PointerDownEvent gesture)
        {
            if (pointer >= 0 || gesture.Button != 0)
                return;
            origin = gesture.Position;
            startAngle = angle;
            pointer = gesture.PointerId;
            pad.CapturePointer(pointer);
        }

        void Drag(PointerMoveEvent gesture)
        {
            if (gesture.PointerId == pointer && !reduced)
            {
                float dx = gesture.Position.x - origin.x;
                SetAngle(Mathf.Clamp(startAngle + dx * .17f, -30, 30));
            }
        }

        void Release(PointerUpEvent gesture)
        {
            if (gesture.PointerId != pointer)
                return;
            float dx = gesture.Position.x - origin.x;
            ReleaseCapture();
            if (step == null)
                return;

            SetAngle(0);
            if (Mathf.Abs(dx) >= 45)
                pad.Document.Surface.RunAfterEvent(() => step(dx > 0 ? -1 : 1));
        }

        void Cancel(PointerCancelEvent gesture)
        {
            if (gesture.PointerId != pointer)
                return;
            ReleaseCapture();
            SetAngle(startAngle);
        }

        void Wheel(WheelEvent gesture)
        {
            if (step == null || pointer >= 0 || Mathf.Abs(gesture.Delta.y) < .01f)
                return;
            int direction = gesture.Delta.y > 0 ? 1 : -1;
            pad.Document.Surface.RunAfterEvent(() => step(direction));
            gesture.PreventDefault();
        }

        void SetAngle(float value)
        {
            if (angle == value)
                return;
            angle = value;
            if (Mathf.Approximately(angle, 0))
                subject.Style.Remove("transform");
            else
            {
                subject.Style.Set("transform", FormattableString.Invariant(
                    $"translateX({angle * .9f:0.0}px) rotateY({angle:0.0}deg)"));
            }
        }

        internal void SetReducedMotion(bool value)
        {
            reduced = value;
            if (value)
            {
                startAngle = 0;
                SetAngle(0);
            }
        }

        void ReleaseCapture()
        {
            int id = pointer;
            pointer = -1;
            pad.ReleasePointer(id);
        }

        public void Dispose()
        {
            if (pointer >= 0)
                ReleaseCapture();
            foreach (var subscription in subscriptions)
                subscription.Dispose();
        }
    }
}
