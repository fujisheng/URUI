using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    public partial class Element
    {
        readonly float[] metrics = new float[16];
        readonly float[] projectedPoint = new float[2];
        ElementResolvedStyle resolvedStyle;

        float[] ReadMetrics()
        {
            RequireValid();
            if (!HasLayout)
            {
                throw new InvalidOperationException("元素尚未参与已完成的布局，请等待 GeometryChangedEvent 或下一帧。");
            }

            RequireSuccess(NativeMethods.urui_element_box(token, metrics, metrics.Length), "读取几何");
            return metrics;
        }
        static Rect ReadRect(float[] values, int offset) => new Rect(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);
        /// <summary>最近完成的原生布局中的边框矩形，位置相对于布局父级。</summary>
        public Rect Layout => ReadRect(ReadMetrics(), 0);
        /// <summary>本地坐标中的内容矩形。</summary>
        public Rect ContentRect => ReadRect(ReadMetrics(), 4);
        /// <summary>累积变换后的文档坐标轴对齐包围框。</summary>
        public Rect WorldBound => ReadRect(ReadMetrics(), 8);
        /// <summary>最近完成的计算样式和实际尺寸读取入口。</summary>
        public ElementResolvedStyle ResolvedStyle => resolvedStyle ?? (resolvedStyle = new ElementResolvedStyle(this));
        /// <summary>当前滚动偏移，设置时由 RmlUi 根据内容范围夹取。</summary>
        public Vector2 ScrollOffset
        {
            get
            {
                var values = ReadMetrics();
                return new Vector2(values[12], values[13]);
            }
            set
            {
                RequireValid();
                ElementStyle.RequireFinite(value.x); ElementStyle.RequireFinite(value.y);
                RequireSuccess(NativeMethods.urui_element_scroll(token, value.x, value.y), "设置滚动偏移");
                document.FinishNativeCall();
            }
        }
        /// <summary>可滚动内容的整体尺寸。</summary>
        public Vector2 ScrollSize
        {
            get
            {
                var values = ReadMetrics();
                return new Vector2(values[14], values[15]);
            }
        }
        /// <summary>将元素本地边框坐标转换到文档坐标。</summary>
        public Vector2 LocalToWorld(Vector2 point) => ConvertPoint(point, true);
        /// <summary>将文档坐标投影到元素本地边框坐标；不可逆变换抛异常。</summary>
        public Vector2 WorldToLocal(Vector2 point) => ConvertPoint(point, false);
        Vector2 ConvertPoint(Vector2 point, bool toWorld)
        {
            RequireValid();
            ElementStyle.RequireFinite(point.x); ElementStyle.RequireFinite(point.y);
            RequireSuccess(NativeMethods.urui_element_point(token, toWorld ? 1 : 0, point.x, point.y, projectedPoint), "转换坐标");
            return new Vector2(projectedPoint[0], projectedPoint[1]);
        }
        /// <summary>检查本地坐标点是否位于元素边框内。</summary>
        public bool ContainsPoint(Vector2 point) => new Rect(Vector2.zero, Layout.size).Contains(point);
    }

    /// <summary>原生计算样式读取器，尺寸来自已完成的布局；仅限主线程。</summary>
    public sealed class ElementResolvedStyle
    {
        readonly Element element;
        internal ElementResolvedStyle(Element element) { this.element = element; }
        /// <summary>最终边框宽度，像素。</summary>
        public float Width => element.Layout.width;
        /// <summary>最终边框高度，像素。</summary>
        public float Height => element.Layout.height;
        /// <summary>读取级联与变量解析后的 RCSS 值；尺寸像素请使用 Width/Height。</summary>
        public string Get(string property)
        {
            URUI.Documents.Document.RequireName(property, nameof(property));
            if (!element.HasLayout)
            {
                throw new InvalidOperationException("元素尚未完成布局。");
            }

            return NativeMethods.ReadStyle(element.CheckedToken(), property, false);
        }
    }
}
