using System;
using System.Collections.Generic;

using URUI.Documents;
using URUI.Elements;
using URUI.Events;
using UnityEngine;

namespace URUI.Controls
{
    /// <summary>
    /// 普通 overflow 容器的鼠标、触摸惯性与弹性边界。
    /// </summary>
    public sealed class ScrollMotion : IDisposable
    {
        readonly Element viewport;
        readonly Element content;
        readonly Document document;
        readonly List<IDisposable> subscriptions = new();
        readonly string previousX;
        readonly string previousY;
        readonly Action publishPosition;
        Vector2 position;
        Vector2 velocity;
        Vector2 previousPosition;
        Vector2 previousNative;
        Vector2 startPosition;
        Vector2 startPointer;
        Vector2 startWorld;
        Vector2 publishedCorrection;
        Vector2 publishedRequest;
        int pointerId = -1;
        bool initialized;
        bool dragging;
        bool disposed;
        bool scrolling;
        bool published;
        bool publishPending;
        Vector2 cachedLimit;
        URUI.Internal.Interop.NativeMethods.ElementRefreshStamp lastIdleStamp;

        /// <summary>
        /// 配置与变换声明在作者 CSS 中；内容必须是视口的直接子节点。
        /// </summary>
        public ScrollMotion(Element viewport, Element content)
        {
            this.viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            if (!ReferenceEquals(content.Parent, viewport))
            {
                throw new ArgumentException("滚动内容必须是视口的直接子节点。", nameof(content));
            }
            if (!viewport.ClassListContains("scroll-motion") || !content.ClassListContains("scroll-motion-content"))
            {
                throw new ArgumentException("作者资源必须声明 scroll-motion 与 scroll-motion-content 样式。");
            }
            document = viewport.Document;
            previousX = content.Style.Get("--urui-scroll-motion-x");
            previousY = content.Style.Get("--urui-scroll-motion-y");
            publishPosition = PublishPosition;
            subscriptions.Add(viewport.RegisterCallback<PointerDownEvent>(Down));
            subscriptions.Add(viewport.RegisterCallback<PointerMoveEvent>(Move));
            subscriptions.Add(viewport.RegisterCallback<PointerUpEvent>(Up));
            subscriptions.Add(viewport.RegisterCallback<PointerCancelEvent>(Cancel));
            subscriptions.Add(viewport.RegisterCallback<PointerCaptureOutEvent>(LostCapture));
            subscriptions.Add(viewport.RegisterCallback<WheelEvent>(Wheel));
            subscriptions.Add(viewport.RegisterCallback<DetachFromDocumentEvent>(_ => Dispose()));
            document.LayoutUpdated += Update;
            document.Unloaded += Unloaded;
        }
        bool Ready => !disposed && viewport.IsValid && content.IsValid && viewport.HasLayout && content.HasLayout;
        /// <summary>
        /// 按内容本地像素定位，取消当前手势及惯性，并保留作者声明的夹紧或弹性边界。
        /// </summary>
        /// <param name="offset">相对内容左上角的滚动量；关闭的轴保持为零。</param>
        /// <exception cref="ObjectDisposedException">控制器已经释放。</exception>
        /// <exception cref="InvalidOperationException">视口或内容尚无有效布局。</exception>
        /// <exception cref="ArgumentOutOfRangeException">滚动量包含非有限数值。</exception>
        public void ScrollTo(Vector2 offset)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ScrollMotion));
            }
            if (!float.IsFinite(offset.x) || !float.IsFinite(offset.y))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (!Ready)
            {
                throw new InvalidOperationException("程序滚动需要已布局的视口和内容。");
            }
            document.RunAfterEvent(() =>
            {
                if (!Ready)
                {
                    return;
                }
                document.Surface.Input.CancelPointers(viewport);
                EndPointer(true);
                AdoptExternal();
                position = Vector2.Scale(offset, Axes);
                if (Boundary == "clamped")
                {
                    position = Clamp(position);
                }
                velocity = Vector2.zero;
                previousPosition = position;
                scrolling = false;
                Publish();
            });
        }
        float Number(string property, float minimum, float maximum = float.MaxValue)
        {
            var value = document.Styles.GetComputed(viewport).GetNumber(property);
            if (value < minimum || value > maximum)
            {
                throw new InvalidOperationException("滚动作者参数无效：" + property);
            }
            return value;
        }
        string Boundary
        {
            get
            {
                var value = document.Styles.GetComputed(viewport).GetKeyword("--urui-scroll-boundary");
                if (value is not ("elastic" or "clamped" or "unrestricted"))
                {
                    throw new InvalidOperationException("--urui-scroll-boundary 必须为 elastic/clamped/unrestricted。");
                }
                return value;
            }
        }
        Vector2 Axes
        {
            get
            {
                static bool Scrolls(string value) => value is "auto" or "scroll";
                var style = document.Styles.GetComputed(viewport);
                return new Vector2(Scrolls(style.GetKeyword("overflow-x")) ? 1 : 0, Scrolls(style.GetKeyword("overflow-y")) ? 1 : 0);
            }
        }
        Vector2 Limit => Vector2.Max(Vector2.zero, content.Layout.max - viewport.ContentRect.max);
        Vector2 Clamp(Vector2 value) => Vector2.Min(Vector2.Max(value, Vector2.zero), Limit);
        void AdoptExternal()
        {
            var actual = viewport.ScrollOffset;
            if (!initialized || (actual - previousNative).sqrMagnitude > .01f)
            {
                position = previousPosition = actual;
                velocity = Vector2.zero;
                previousNative = actual;
                initialized = true;
            }
        }
        void Publish()
        {
            if (publishPending)
            {
                return;
            }
            var desired = Clamp(position);
            // 原生偏移会取整；比较上次请求与实际补偿，保留小数定位且不重复提交静止状态。
            if (published && desired.Equals(publishedRequest) && (previousNative - position).Equals(publishedCorrection) && viewport.ScrollOffset.Equals(previousNative))
            {
                return;
            }
            // Native style writes are deferred until the synchronous input dispatch has unwound.
            publishPending = true;
            document.RunAfterEvent(publishPosition);
        }
        void PublishPosition()
        {
            publishPending = false;
            if (!Ready)
            {
                return;
            }
            published = false;
            var requested = Clamp(position);
            viewport.ScrollOffset = requested;
            // 同步滚动事件可销毁控制器，或已经完成/排队一个更新的请求。
            if (!Ready || published || publishPending)
            {
                return;
            }
            previousNative = viewport.ScrollOffset;
            if (!Ready || published || publishPending)
            {
                return;
            }
            publishedCorrection = previousNative - position;
            content.Style.SetPixels("--urui-scroll-motion-x", publishedCorrection.x);
            content.Style.SetPixels("--urui-scroll-motion-y", publishedCorrection.y);
            publishedRequest = requested;
            // 只缓存本次原生取整结果；回调改写或额外夹紧仍按原路径在下次更新重试。
            published = previousNative.x == Mathf.Floor(requested.x + .5f) && previousNative.y == Mathf.Floor(requested.y + .5f);
        }
        bool ChildCaptured(PointerEventBase evt)
        {
            for (var child = evt.Target; child != null && !ReferenceEquals(child, viewport); child = child.Parent)
            {
                if (child.HasPointerCapture(evt.PointerId))
                {
                    return true;
                }
            }
            return false;
        }
        void Down(PointerDownEvent evt)
        {
            if (!Ready || evt.Button != 0 || evt.IsDefaultPrevented || ChildCaptured(evt) || pointerId >= 0)
            {
                return;
            }
            AdoptExternal();
            velocity = Vector2.zero;
            previousPosition = position;
            pointerId = evt.PointerId;
            dragging = false;
            startPosition = position;
            startWorld = evt.Position;
            startPointer = viewport.WorldToLocal(evt.Position);
        }
        void Move(PointerMoveEvent evt)
        {
            if (!Ready || evt.PointerId != pointerId)
            {
                return;
            }
            if (evt.IsDefaultPrevented || ChildCaptured(evt))
            {
                EndPointer(true);
                return;
            }
            // Suppress native touch scrolling before the drag threshold, while retaining native tap handling.
            evt.PreventDefault();
            if (!dragging)
            {
                if ((evt.Position - startWorld).magnitude < Number("--urui-scroll-drag-threshold", 0))
                {
                    return;
                }
                dragging = true;
                viewport.CapturePointer(pointerId);
            }
            var desired = startPosition - Vector2.Scale(viewport.WorldToLocal(evt.Position) - startPointer, Axes);
            var boundary = Boundary;
            if (boundary != "unrestricted")
            {
                var clamped = Clamp(desired);
                var over = desired - clamped;
                if (boundary == "elastic")
                {
                    static float Rubber(float delta, float size) => size > 0 ? (1 - 1 / (Mathf.Abs(delta) * .55f / size + 1)) * size * Mathf.Sign(delta) : 0;
                    desired = clamped + new Vector2(Rubber(over.x, viewport.ContentRect.width), Rubber(over.y, viewport.ContentRect.height));
                }
                else
                {
                    desired = clamped;
                }
            }
            position = desired;
            Publish();
            evt.StopPropagation();
        }
        void Up(PointerUpEvent evt)
        {
            if (evt.PointerId != pointerId)
            {
                return;
            }
            if (dragging)
            {
                evt.PreventDefault();
                evt.StopPropagation();
            }
            EndPointer(false);
        }
        void Cancel(PointerCancelEvent evt)
        {
            if (evt.PointerId == pointerId)
            {
                evt.PreventDefault();
                EndPointer(true);
            }
        }
        void LostCapture(PointerCaptureOutEvent evt)
        {
            if (evt.PointerId == pointerId)
            {
                EndPointer(true);
            }
        }
        void EndPointer(bool stop)
        {
            var old = pointerId;
            pointerId = -1;
            dragging = false;
            if (stop)
            {
                velocity = Vector2.zero;
            }
            if (old >= 0 && viewport.IsValid)
            {
                viewport.ReleasePointer(old);
            }
        }
        void Wheel(WheelEvent evt)
        {
            if (!Ready || evt.IsDefaultPrevented)
            {
                return;
            }
            AdoptExternal();
            var axes = Axes;
            var delta = evt.Delta;
            if (axes.x == 0 && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            {
                delta.y = delta.x;
            }
            if (axes.y == 0 && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            {
                delta.x = delta.y;
            }
            position += Vector2.Scale(delta, axes) * Number("--urui-scroll-sensitivity", 0);
            if (Boundary == "clamped")
            {
                position = Clamp(position);
            }
            scrolling = true;
            Publish();
            evt.PreventDefault();
            evt.StopPropagation();
        }
        void Update(Document owner)
        {
            if (!Ready)
            {
                return;
            }
            var hadInit = initialized;
            var prevNativeBefore = previousNative;
            AdoptExternal();
            var externalChanged = !hadInit || previousNative != prevNativeBefore;
            if (!externalChanged && !dragging && !scrolling && velocity == Vector2.zero && published && !publishPending
                && document.TryGetRefreshStamp(out var idleStamp) && idleStamp.Matches(lastIdleStamp))
            {
                var cachedClamped = Vector2.Min(Vector2.Max(position, Vector2.zero), cachedLimit);
                if ((cachedClamped - position).sqrMagnitude <= 1e-6f)
                {
                    return;
                }
            }
            var dt = document.Surface.UIClock.Delta;
            if (dt <= 0)
            {
                return;
            }
            var boundary = Boundary;
            var limit = Limit;
            cachedLimit = limit;
            var clamped = Vector2.Min(Vector2.Max(position, Vector2.zero), limit);
            var offset = clamped - position;
            if (!dragging)
            {
                for (var axis = 0; axis < 2; axis++)
                {
                    if (boundary == "elastic" && Mathf.Abs(offset[axis]) > .001f)
                    {
                        var speed = velocity[axis];
                        var elasticity = Number("--urui-scroll-elasticity", .001f) * (scrolling ? 3 : 1);
                        position[axis] = Mathf.SmoothDamp(position[axis], position[axis] + offset[axis], ref speed, elasticity, Mathf.Infinity, dt);
                        velocity[axis] = Mathf.Abs(speed) < 1 ? 0 : speed;
                        var postClamped = Mathf.Clamp(position[axis], 0, limit[axis]);
                        if (Mathf.Abs(postClamped - position[axis]) < .01f)
                        {
                            position[axis] = postClamped;
                        }
                    }
                    else
                    {
                        if (velocity[axis] != 0)
                        {
                            velocity[axis] *= Mathf.Pow(Number("--urui-scroll-deceleration", 0, 1), dt);
                            if (Mathf.Abs(velocity[axis]) < 1)
                            {
                                velocity[axis] = 0;
                            }
                            position[axis] += velocity[axis] * dt;
                        }
                    }
                }
                if (boundary == "clamped")
                {
                    position = Vector2.Min(Vector2.Max(position, Vector2.zero), limit);
                }
            }
            else
            {
                velocity = Vector2.Lerp(velocity, (position - previousPosition) / dt, dt * 10);
            }
            Publish();
            previousPosition = position;
            scrolling = false;
            if (!dragging && !publishPending && velocity == Vector2.zero && published && document.TryGetRefreshStamp(out var afterStamp))
            {
                lastIdleStamp = afterStamp;
            }
        }
        void Unloaded(Document owner) => Dispose();
        /// <summary>
        /// 释放输入订阅并还原动态变换覆盖。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            EndPointer(true);
            document.LayoutUpdated -= Update;
            document.Unloaded -= Unloaded;
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
            subscriptions.Clear();
            document.RunAfterEvent(() =>
            {
                if (!content.IsValid)
                {
                    return;
                }
                if (previousX == null)
                {
                    content.Style.Remove("--urui-scroll-motion-x");
                }
                else
                {
                    content.Style.Set("--urui-scroll-motion-x", previousX);
                }
                if (previousY == null)
                {
                    content.Style.Remove("--urui-scroll-motion-y");
                }
                else
                {
                    content.Style.Set("--urui-scroll-motion-y", previousY);
                }
            });
        }
    }
}
