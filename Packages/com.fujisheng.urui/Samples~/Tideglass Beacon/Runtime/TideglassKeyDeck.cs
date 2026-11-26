using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Controls;
using URUI.Elements;
using URUI.Events;

namespace URUI.Samples.Tideglass
{
    // The hit plane belongs to the console. Only its thin, floating keycap responds to the contact.
    internal sealed class TideglassKeyDeck : IDisposable
    {
        readonly List<IDisposable> subscriptions = new();
        Element active, cap, reflection;
        int contact = -1;
        Vector3Int lastPose;
        bool reduced;

        internal TideglassKeyDeck(Element root, bool reducedMotion)
        {
            reduced = reducedMotion;
            foreach (var element in root.Query(className: "plane-key"))
            {
                if (element is not Button key) continue;
                subscriptions.Add(key.RegisterCallback<PointerMoveEvent>(e => Track(key, e)));
                subscriptions.Add(key.RegisterCallback<PointerEnterEvent>(e => Track(key, e)));
                subscriptions.Add(key.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.Button != 0 || contact >= 0) return;
                    Track(key, e, true);
                }));
                subscriptions.Add(key.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.PointerId != contact) return;
                    contact = -1;
                    if (e.IsTouch) Clear();
                    else Track(key, e);
                }));
                subscriptions.Add(key.RegisterCallback<PointerLeaveEvent>(e => Leave(key, e)));
                subscriptions.Add(key.RegisterCallback<PointerCancelEvent>(e => Leave(key, e)));
            }
        }

        void Track(Button key, PointerEventBase e, bool down = false)
        {
            if (reduced || !key.EnabledSelf || (contact >= 0 && e.PointerId != contact) || (e.IsTouch && !down && contact < 0))
                return;
            if (active != key)
            {
                Clear();
                active = key;
                cap = key.Q(className: "key-face");
                reflection = key.Q(className: "key-reflection");
            }
            if (down) contact = e.PointerId;
            if (!key.TryWorldToLocal(e.Position, out var point)) return;
            var size = key.Layout.size;
            float u = Mathf.Clamp(2f * point.x / Mathf.Max(1, size.x) - 1, -1, 1);
            float v = Mathf.Clamp(2f * point.y / Mathf.Max(1, size.y) - 1, -1, 1);
            // Positive local y points down. Raising the edge under the pointer keeps the opposite edge grounded.
            var pose = new Vector3Int(Mathf.RoundToInt(v * 11), Mathf.RoundToInt(-u * 19), contact >= 0 ? -5 : 4);
            if (pose == lastPose) return;
            lastPose = pose;
            cap.Style.Set("transform", FormattableString.Invariant(
                $"translateZ({pose.z}px) rotateX({pose.x * .25f:0.##}deg) rotateY({pose.y * .25f:0.##}deg)"));
            reflection?.Style.Set("transform", FormattableString.Invariant($"translateX({-pose.y * 1.35f:0.#}%)"));
        }

        void Leave(Button key, PointerEventBase e)
        {
            if (active == key && (contact < 0 || e.PointerId == contact)) Clear();
        }

        void Clear()
        {
            if (cap?.IsValid == true) cap.Style.Remove("transform");
            if (reflection?.IsValid == true) reflection.Style.Remove("transform");
            active = cap = reflection = null;
            lastPose = default;
            contact = -1;
        }

        internal void SetReducedMotion(bool value)
        {
            reduced = value;
            if (value) Clear();
        }

        public void Dispose()
        {
            Clear();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
