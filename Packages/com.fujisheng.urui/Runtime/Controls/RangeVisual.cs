using System;
using URUI.Elements;

namespace URUI.Controls
{
    // 只同步作者节点的动态轴，不创建原生内部节点或覆盖另一轴的作者尺寸。
    internal sealed class RangeVisual<T> : IDisposable
    {
        readonly RangeField<T> control;
        readonly Element fill;
        readonly Element thumb;
        SliderDirection? previousDirection;
        readonly Action deferredRefresh;
        bool queued;
        bool disposed;
        internal RangeVisual(RangeField<T> control, Element fill, Element thumb)
        {
            this.control = control ?? throw new ArgumentNullException(nameof(control));
            this.fill = fill ?? throw new ArgumentNullException(nameof(fill));
            this.thumb = thumb ?? throw new ArgumentNullException(nameof(thumb));
            if (!ReferenceEquals(control.Document, fill.Document) || !ReferenceEquals(control.Document, thumb.Document))
            {
                throw new ArgumentException("皮肤节点必须与滑块属于同一文档。");
            }
            deferredRefresh = FlushRefresh;
            control.PresentationChanged += RequestRefresh;
            try
            {
                RequestRefresh();
            }
            catch
            {
                control.PresentationChanged -= RequestRefresh;
                throw;
            }
        }
        void RequestRefresh()
        {
            if (disposed || !control.IsValid)
            {
                return;
            }
            if (!control.Document.Surface.IsExecutingCallback)
            {
                Refresh();
                return;
            }
            if (queued)
            {
                return;
            }
            queued = true;
            control.Document.RunAfterEvent(deferredRefresh);
        }
        void FlushRefresh()
        {
            queued = false;
            if (!disposed)
            {
                Refresh();
            }
        }
        void Refresh()
        {
            if (!control.IsValid || !fill.IsValid || !thumb.IsValid)
            {
                return;
            }
            var direction = control.Direction;
            if (previousDirection.HasValue && previousDirection.Value != direction)
            {
                fill.Style.Remove(previousDirection.Value == SliderDirection.Horizontal ? "width" : "height");
                thumb.Style.Remove(previousDirection.Value == SliderDirection.Horizontal ? "left" : "top");
            }
            previousDirection = direction;
            var percent = control.PresentationPercent;
            fill.Style.SetPercent(direction == SliderDirection.Horizontal ? "width" : "height", percent);
            thumb.Style.SetPercent(direction == SliderDirection.Horizontal ? "left" : "top", percent);
            var host = control.Parent;
            if (host != null && host.TagName == "urui-slider")
            {
                host.SetAttribute("data-urui-orientation", direction == SliderDirection.Vertical ? "vertical" : "horizontal");
            }
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            control.PresentationChanged -= RequestRefresh;
        }
    }
}
