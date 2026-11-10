using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>用作者轨道和滑钮呈现普通滚动视口；支持拖动、轨道翻页及键盘。</summary>
    public sealed class ScrollbarVisual : IDisposable
    {
        readonly Element viewport, track, thumb;
        readonly Document document;
        readonly List<IDisposable> subscriptions = new List<IDisposable>();
        readonly Action deferredRefresh;
        int pointer = -1, dragAxis, previousAxis = -1;
        float anchor, trackLength, thumbLength, thumbPosition, limit, page;
        bool disposed, queued;
        bool? shown;
        float writtenLength = float.NaN, writtenPosition = float.NaN;

        /// <summary>三个节点须在同一文档，滑钮是轨道的直接子节点；轨道位于视口外。</summary>
        /// <remarks>方向来自轨道 data-urui-orientation，默认 vertical；作者样式为滑钮声明 border-box。</remarks>
        public ScrollbarVisual(Element viewport, Element track, Element thumb)
        {
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            this.track = track ?? throw new ArgumentNullException(nameof(track));
            this.thumb = thumb ?? throw new ArgumentNullException(nameof(thumb));
            document = viewport.Document;
            if (!ReferenceEquals(document, track.Document) || !ReferenceEquals(document, thumb.Document) || !ReferenceEquals(thumb.Parent, track))
                throw new ArgumentException("滑钮必须是同文档轨道的直接子节点。");
            if (viewport.Contains(track)) throw new ArgumentException("作者轨道必须放在滚动视口外。", nameof(track));
            for (var parent = track.Parent; parent != null; parent = parent.Parent)
                if (parent is AuthoredScrollSkin owner)
                {
                    if (owner.OwnsTrack(track)) throw new InvalidOperationException("此默认滚动条已自动装配。");
                    break;
                }
            deferredRefresh = FlushRefresh;
            document.LayoutUpdated += OnLayout;
            document.Unloaded += OnUnload;
            subscriptions.Add(viewport.RegisterCallback<ScrollEvent>(_ => Refresh()));
            subscriptions.Add(track.RegisterCallback<PointerDownEvent>(Down));
            subscriptions.Add(track.RegisterCallback<PointerMoveEvent>(Move));
            subscriptions.Add(track.RegisterCallback<PointerUpEvent>(Up));
            subscriptions.Add(track.RegisterCallback<PointerCancelEvent>(evt => { if (evt.PointerId == pointer) CancelDrag(); }));
            subscriptions.Add(track.RegisterCallback<PointerCaptureOutEvent>(evt => { if (evt.PointerId == pointer) CancelDrag(); }));
            subscriptions.Add(track.RegisterCallback<KeyDownEvent>(Key));
            track.Focusable = true;
            Refresh();
        }
        int Axis => track.GetAttribute("data-urui-orientation") == "horizontal" ? 0 : 1;
        bool Ready => !disposed && viewport.IsValid && track.IsValid && thumb.IsValid && viewport.HasLayout && track.HasLayout && thumb.HasLayout;
        void OnLayout(Document owner) => Refresh();
        void OnUnload(Document owner) => Dispose();
        /// <summary>同步当前位置、范围、滑钮最小尺寸和可见状态；回调中的写入延后至原生返回。</summary>
        public void Refresh()
        {
            if (!Ready) return;
            if (!document.Surface.IsExecutingCallback) { RefreshNow(); return; }
            if (queued) return;
            queued = true; document.RunAfterEvent(deferredRefresh);
        }
        void FlushRefresh() { queued = false; if (Ready) RefreshNow(); }
        void RefreshNow()
        {
            int axis = Axis;
            if (previousAxis >= 0 && previousAxis != axis)
            {
                CancelDrag();
                thumb.Style.Remove(previousAxis == 0 ? "width" : "height");
                thumb.Style.Remove(previousAxis == 0 ? "left" : "top");
                writtenLength = writtenPosition = float.NaN;
            }
            previousAxis = axis;
            page = axis == 0 ? viewport.ViewportRect.width : viewport.ViewportRect.height;
            trackLength = axis == 0 ? track.ContentRect.width : track.ContentRect.height;
            float content = Mathf.Max(page, viewport.ScrollSize[axis]);
            limit = Mathf.Max(0, content - page);
            var snapshot = document.Styles.GetComputed(thumb);
            snapshot.TryGetPixels(axis == 0 ? "min-width" : "min-height", out var minimum);
            thumbLength = Mathf.Clamp(Mathf.Max(minimum, content > 0 ? trackLength * page / content : trackLength), 0, trackLength);
            float offset = Mathf.Clamp(viewport.ScrollOffset[axis], 0, limit);
            thumbPosition = limit > 0 ? (trackLength - thumbLength) * offset / limit : 0;
            if (writtenLength != thumbLength)
            {
                thumb.Style.SetPixels(axis == 0 ? "width" : "height", thumbLength); writtenLength = thumbLength;
            }
            if (writtenPosition != thumbPosition)
            {
                thumb.Style.SetPixels(axis == 0 ? "left" : "top", thumbPosition); writtenPosition = thumbPosition;
            }
            bool visible = limit > 0 && page > 0 && trackLength > 0;
            if (shown != visible) { track.Visible = visible; shown = visible; }
            track.SetAttribute("aria-valuemin", "0");
            track.SetAttribute("aria-valuemax", limit.ToString("R", CultureInfo.InvariantCulture));
            track.SetAttribute("aria-valuenow", offset.ToString("R", CultureInfo.InvariantCulture));
            track.SetAttribute("aria-orientation", axis == 0 ? "horizontal" : "vertical");
            track.SetAttribute("aria-disabled", viewport.EnabledInHierarchy ? "false" : "true");
        }
        float Coordinate(Vector2 world)
        {
            var local = track.WorldToLocal(world);
            return Axis == 0 ? local.x - track.ContentRect.x : local.y - track.ContentRect.y;
        }
        void Scroll(int axis, float value)
        {
            document.RunAfterEvent(() => {
                if (!Ready || !viewport.EnabledInHierarchy) return;
                var offset = viewport.ScrollOffset; offset[axis] = value;
                viewport.ScrollOffset = offset; Refresh();
            });
        }
        void Down(PointerDownEvent evt)
        {
            if (!Ready || !viewport.EnabledInHierarchy || evt.Button != 0 || pointer >= 0 || limit <= 0) return;
            evt.PreventDefault(); evt.StopPropagation(); track.Focus();
            float coordinate = Coordinate(evt.Position);
            if (coordinate < thumbPosition || coordinate > thumbPosition + thumbLength)
            {
                Scroll(Axis, viewport.ScrollOffset[Axis] + (coordinate < thumbPosition ? -page : page)); return;
            }
            pointer = evt.PointerId; dragAxis = Axis; anchor = coordinate - thumbPosition;
            track.CapturePointer(pointer);
        }
        void Move(PointerMoveEvent evt)
        {
            if (!Ready || evt.PointerId != pointer) return;
            evt.PreventDefault(); evt.StopPropagation();
            if (!viewport.EnabledInHierarchy || dragAxis != Axis) { CancelDrag(); return; }
            DragTo(evt.Position);
        }
        void DragTo(Vector2 position)
        {
            float travel = trackLength - thumbLength;
            Scroll(dragAxis, travel > 0 ? Mathf.Clamp01((Coordinate(position) - anchor) / travel) * limit : 0);
        }
        void Up(PointerUpEvent evt)
        {
            if (evt.PointerId != pointer) return;
            if (Ready && viewport.EnabledInHierarchy && dragAxis == Axis) DragTo(evt.Position);
            evt.PreventDefault(); evt.StopPropagation(); CancelDrag();
        }
        void CancelDrag()
        {
            int previous = pointer; pointer = -1;
            if (previous >= 0 && track.IsValid && track.HasPointerCapture(previous)) track.ReleasePointer(previous);
        }
        void Key(KeyDownEvent evt)
        {
            if (!Ready || !viewport.EnabledInHierarchy) return;
            int axis = Axis;
            float next = viewport.ScrollOffset[axis];
            if (evt.KeyCode == KeyCode.Home) next = 0;
            else if (evt.KeyCode == KeyCode.End) next = limit;
            else if (evt.KeyCode == KeyCode.PageUp) next -= page;
            else if (evt.KeyCode == KeyCode.PageDown) next += page;
            else if (evt.KeyCode == (axis == 0 ? KeyCode.LeftArrow : KeyCode.UpArrow)) next -= 40;
            else if (evt.KeyCode == (axis == 0 ? KeyCode.RightArrow : KeyCode.DownArrow)) next += 40;
            else return;
            evt.PreventDefault(); evt.StopPropagation(); Scroll(axis, next);
        }
        /// <summary>解除输入及布局订阅，释放拖动捕获；保留作者节点。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; document.LayoutUpdated -= OnLayout; document.Unloaded -= OnUnload;
            foreach (var subscription in subscriptions) subscription.Dispose();
            subscriptions.Clear(); document.RunAfterEvent(CancelDrag);
        }
    }
}
