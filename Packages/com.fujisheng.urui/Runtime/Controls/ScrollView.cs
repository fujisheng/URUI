using UnityEngine;
using System;
using URUI.Internal.Interop;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>带内容容器的滚动区域，支持滚轮和触摸拖动。</summary>
    public class ScrollView : Element
    {
        Element content;
        float contentWidth = float.NaN;
        readonly float[] viewport = new float[4];
        int dragging = -1;
        Vector2 start, offset;
        bool moved;
        /// <summary>文档工厂构造入口。</summary>
        protected internal ScrollView(ElementContext context) : base(context) { }
        /// <summary>业务子节点放入滚动内容区。</summary>
        public override Element ContentContainer => content ?? this;
        /// <summary>扣除边框、内边距和可见滚动条后的内容视口。</summary>
        public Rect ViewportRect
        {
            get
            {
                RequireValid();
                if (!HasLayout)
                {
                    throw new InvalidOperationException("请等待滚动区域完成布局。");
                }

                if (NativeMethods.urui_element_viewport(Token, viewport, viewport.Length) != 1)
                {
                    throw new InvalidOperationException("无法读取滚动视口。");
                }

                return new Rect(viewport[0], viewport[1], viewport[2], viewport[3]);
            }
        }
        /// <summary>创建滚动内容区并迁移已有 RML 子节点。</summary>
        protected override void OnInitialize()
        {
            var existing = Hierarchy.Children();
            Style.Display = DisplayStyle.Block;
            Style.Overflow = Overflow.Auto;
            Focusable = true;
            content = Document.Create<Element>();
            content.AddToClassList("urui-scroll-content");
            content.Style.Display = DisplayStyle.Block;
            content.Style.Position = Position.Relative;
            content.Style.Width = Length.Percent(100);
            Hierarchy.Add(content);
            foreach (var child in existing)
            {
                content.Add(child);
            }
        }
        /// <summary>将同一内容区中的后代滚动到可见范围，已经可见时保留当前位置。</summary>
        public void ScrollTo(Element child)
        {
            RequireValid();
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            if (!child.IsValid || !ContentContainer.Contains(child))
            {
                throw new ArgumentException("目标必须是当前内容区的有效后代。", nameof(child));
            }

            if (!HasLayout || !child.HasLayout)
            {
                throw new InvalidOperationException("请等待目标和滚动区域完成布局。");
            }

            var topLeft = WorldToLocal(child.LocalToWorld(Vector2.zero));
            var bottomRight = WorldToLocal(child.LocalToWorld(child.Layout.size));
            var viewport = ViewportRect;
            var delta = new Vector2(topLeft.x < viewport.xMin ? topLeft.x - viewport.xMin : bottomRight.x > viewport.xMax ? bottomRight.x - viewport.xMax : 0,
                topLeft.y < viewport.yMin ? topLeft.y - viewport.yMin : bottomRight.y > viewport.yMax ? bottomRight.y - viewport.yMax : 0);
            ScrollOffset += delta;
        }
        /// <summary>布局完成后使用视口实际宽度，避免原生滚动容器的百分比子宽度收缩为零。</summary>
        protected override void OnUpdate()
        {
            if (!HasLayout)
            {
                return;
            }

            float width = ViewportRect.width;
            if (contentWidth != width)
            {
                contentWidth = width;
                content.Style.Width = width;
            }
        }
        /// <summary>触摸超过拖动阈值后捕获指针，释放时取消误点击。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (evt is PointerCancelEvent || evt is PointerCaptureOutEvent || evt is BlurEvent)
            {
                dragging = -1;
                return;
            }
            if (!EnabledInHierarchy || !HasLayout)
            {
                return;
            }

            if (evt is KeyDownEvent key && ReferenceEquals(evt.Target, this))
            {
                var next = ScrollOffset;
                switch (key.KeyCode)
                {
                    case KeyCode.DownArrow: next.y += 40; break;
                    case KeyCode.UpArrow: next.y -= 40; break;
                    case KeyCode.RightArrow: next.x += 40; break;
                    case KeyCode.LeftArrow: next.x -= 40; break;
                    case KeyCode.PageDown: next.y += ViewportRect.height; break;
                    case KeyCode.PageUp: next.y -= ViewportRect.height; break;
                    case KeyCode.Home: next = Vector2.zero; break;
                    case KeyCode.End: next.y = ScrollSize.y; break;
                    default: return;
                }
                ScrollOffset = next;
                key.PreventDefault();
                return;
            }

            if (evt is PointerDownEvent down && down.IsTouch && dragging < 0)
            {
                dragging = down.PointerId; start = down.Position; offset = ScrollOffset; moved = false;
            }
            else if (evt is PointerMoveEvent move && move.PointerId == dragging)
            {
                if (!moved && Vector2.Distance(move.Position, start) > 4)
                {
                    moved = true; CapturePointer(dragging);
                }
                if (moved) { ScrollOffset = offset + start - move.Position; move.PreventDefault(); }
            }
            else if (evt is PointerUpEvent up && up.PointerId == dragging)
            {
                if (moved) { up.PreventDefault(); ReleasePointer(dragging); }
                dragging = -1;
            }
            else if (evt is PointerCancelEvent || evt is PointerCaptureOutEvent)
            {
                dragging = -1;
            }
        }
    }
}
