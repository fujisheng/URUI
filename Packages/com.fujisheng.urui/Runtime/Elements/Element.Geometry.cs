using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    public partial class Element
    {
        readonly float[] metrics = new float[16];
        Dictionary<(string property, bool local), string> styleStrings;
        Dictionary<(int field, string name), string> strings;
        string ReadString(int field, string name = null)
        {
            strings ??= new Dictionary<(int, string), string>();
            var key = (field, name);
            strings.TryGetValue(key, out var previous);
            var value = NativeMethods.ReadElement(NativeHandle, token, field, name, previous);
            if (value != null && value.Length <= 64 * 1024) { strings[key] = value; }
            else { strings.Remove(key); }
            return value;
        }
        internal string ReadStyle(string property, bool local)
        {
            var token = CheckedToken();
            styleStrings ??= new Dictionary<(string, bool), string>();
            var key = (property, local);
            styleStrings.TryGetValue(key, out var previous);
            var value = NativeMethods.ReadStyle(NativeHandle, token, property, local, previous);
            if (value != null) { styleStrings[key] = value; }
            else { styleStrings.Remove(key); }
            return value;
        }
        readonly float[] projectedPoint = new float[2];
        float[] transformSnapshot;
        float[] viewportSnapshot;
        ElementResolvedStyle resolvedStyle;

        float[] ReadMetrics()
        {
            RequireValid();
            if (!HasLayout)
            {
                throw new InvalidOperationException("元素尚未参与已完成的布局，请等待 GeometryChangedEvent 或下一帧。");
            }

            RequireSuccess(NativeMethods.urui_element_box(NativeHandle, token, metrics, metrics.Length), "读取几何");
            return metrics;
        }
        static Rect ReadRect(float[] values, int offset) => new Rect(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);
        /// <summary>最近完成的原生布局中的边框矩形，位置相对于布局父级。</summary>
        public Rect Layout => ReadRect(ReadMetrics(), 0);
        /// <summary>本地坐标中的内容矩形。</summary>
        public Rect ContentRect => ReadRect(ReadMetrics(), 4);
        /// <summary>累积变换后的文档坐标轴对齐包围框。</summary>
        public Rect WorldBound => ReadRect(ReadMetrics(), 8);
        /// <summary>扣除边框、内边距及可见滚动条的本地视口，适用于普通 overflow 节点。</summary>
        public Rect ViewportRect
        {
            get
            {
                RequireValid();
                if(!HasLayout){throw new InvalidOperationException("读取视口需要已完成的布局。");}
                var values=viewportSnapshot??(viewportSnapshot=new float[4]);
                RequireSuccess(NativeMethods.urui_element_viewport(NativeHandle, token,values,values.Length),"读取视口");
                return ReadRect(values,0);
            }
        }
        /// <summary>读取不含变换原点平移的本地矩阵，以及显式旋转原语的乘积；含 matrix 原语时返回 false。</summary>
        /// <remarks>矩阵来自最近计算样式。旋转与缩放的语义不能从任意矩阵唯一恢复；false 时 localTransform 仍有效。</remarks>
        public bool TryGetLocalTransform(out Matrix4x4 localTransform, out Quaternion logicalRotation)
        {
            RequireValid();
            if (!HasLayout) { throw new InvalidOperationException("元素尚未完成布局。"); }
            if (NativeMethods.urui_transform_api_version() != 1) { throw new NotSupportedException("不支持的原生变换协议。"); }
            var values = transformSnapshot ?? (transformSnapshot = new float[32]);
            var result = NativeMethods.urui_element_local_transform(NativeHandle, token, values, values.Length);
            if (result != 2) { RequireSuccess(result, "读取本地变换"); }
            localTransform = new Matrix4x4();
            var rotation = new Matrix4x4();
            for (var index = 0; index < 16; index++)
            {
                localTransform[index] = values[index];
                rotation[index] = values[16 + index];
            }
            logicalRotation = result == 1 ? rotation.rotation : Quaternion.identity;
            return result == 1;
        }
        /// <summary>最近完成的计算样式和实际尺寸读取入口。</summary>
        public ElementResolvedStyle ResolvedStyle => resolvedStyle ?? (resolvedStyle = new ElementResolvedStyle(this));
        /// <summary>当前滚动偏移，设置时由 URUI 根据内容范围夹取。</summary>
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
                RequireSuccess(NativeMethods.urui_element_scroll(NativeHandle, token, value.x, value.y), "设置滚动偏移");
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
        /// <summary>尝试将文档坐标投影到本地；零缩放等不可逆变换返回 false。</summary>
        public bool TryWorldToLocal(Vector2 point, out Vector2 localPoint)
        {
            RequireValid();
            ElementStyle.RequireFinite(point.x); ElementStyle.RequireFinite(point.y);
            var result = NativeMethods.urui_element_point(NativeHandle, token, 0, point.x, point.y, projectedPoint);
            localPoint = default;
            if (result == -2) { return false; }
            RequireSuccess(result, "转换坐标");
            localPoint = new Vector2(projectedPoint[0], projectedPoint[1]);
            return true;
        }
        Vector2 ConvertPoint(Vector2 point, bool toWorld)
        {
            RequireValid();
            ElementStyle.RequireFinite(point.x); ElementStyle.RequireFinite(point.y);
            RequireSuccess(NativeMethods.urui_element_point(NativeHandle, token, toWorld ? 1 : 0, point.x, point.y, projectedPoint), "转换坐标");
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
        /// <summary>读取级联与变量解析后的 CSS 值；尺寸像素请使用 Width/Height。</summary>
        public string Get(string property)
        {
            URUI.Documents.Document.RequireName(property, nameof(property));
            if (!element.HasLayout)
            {
                throw new InvalidOperationException("元素尚未完成布局。");
            }

            return element.ReadStyle(property, false);
        }
    }
}
