using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Samples.Tideglass
{
    /// <summary>Tilts the framed crew portrait and moves its reflected light over the print.</summary>
    internal sealed class TideglassPortraitCard : IDisposable
    {
        const int StepsFromCenter = 48;
        readonly Element mount, face, sheen;
        readonly IDisposable[] subscriptions;
        Vector2Int? lastPosition;
        bool reduced;

        internal TideglassPortraitCard(Element mount, bool reducedMotion)
        {
            this.mount = mount;
            face = mount.Q(className: "portrait-face");
            sheen = mount.Q(className: "portrait-sheen");
            reduced = reducedMotion;
            subscriptions = new[]
            {
                mount.RegisterCallback<PointerEnterEvent>(Follow),
                mount.RegisterCallback<PointerMoveEvent>(Follow),
                mount.RegisterCallback<PointerLeaveEvent>(Leave),
                mount.RegisterCallback<PointerCancelEvent>(Leave)
            };
        }

        void Follow(PointerEventBase pointer)
        {
            if (reduced || pointer.IsTouch || !mount.TryWorldToLocal(pointer.Position, out var local))
                return;

            // Measure against the fixed mount so a lifting corner cannot move its own input coordinates.
            var size = mount.Layout.size;
            if (size.x <= 0 || size.y <= 0)
                return;
            var position = new Vector2Int(
                Mathf.RoundToInt(Mathf.Clamp(2 * local.x / size.x - 1, -1, 1) * StepsFromCenter),
                Mathf.RoundToInt(Mathf.Clamp(2 * local.y / size.y - 1, -1, 1) * StepsFromCenter));
            if (position == lastPosition)
                return;
            if (!lastPosition.HasValue)
                mount.AddToClassList("inspecting");
            lastPosition = position;

            float x = (float)position.x / StepsFromCenter, y = (float)position.y / StepsFromCenter;
            face.Style.Set("transform", FormattableString.Invariant(
                $"translateZ(8px) rotateX({y * 6:0.##}deg) rotateY({-x * 9:0.##}deg)"));
            sheen.Style.Set("transform", FormattableString.Invariant(
                $"translateX({x * 33:0.##}%) translateY({y * 37:0.##}%)"));
        }

        void Leave(PointerEventBase pointer)
        {
            if (!pointer.IsTouch)
                Reset();
        }

        internal void Reset()
        {
            if (!lastPosition.HasValue)
                return;
            lastPosition = null;
            mount.RemoveFromClassList("inspecting");
            face.Style.Remove("transform");
            sheen.Style.Remove("transform");
        }

        internal void SetReducedMotion(bool value)
        {
            reduced = value;
            if (value)
                Reset();
        }

        public void Dispose()
        {
            foreach (var subscription in subscriptions)
                subscription.Dispose();
        }
    }
}
