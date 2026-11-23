using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    /// <summary>
    /// 单次原生调用读取的完整空间几何、滚动、本地变换与状态快照。
    /// </summary>
    public readonly struct ElementSpatialSnapshot
    {
        public readonly Rect Layout;
        public readonly Rect ContentRect;
        public readonly Rect WorldBound;
        public readonly Rect ViewportRect;
        public readonly Vector2 ScrollOffset;
        public readonly Vector2 ScrollSize;
        public readonly Matrix4x4 LocalTransform;
        public readonly Quaternion LogicalRotation;
        public readonly bool HasSeparateRotation;
        public readonly ElementState StateFlags;

        internal ElementSpatialSnapshot(
            Rect layout, Rect contentRect, Rect worldBound, Rect viewportRect,
            Vector2 scrollOffset, Vector2 scrollSize,
            Matrix4x4 localTransform, Quaternion logicalRotation, bool hasSeparateRotation,
            ElementState stateFlags)
        {
            Layout = layout;
            ContentRect = contentRect;
            WorldBound = worldBound;
            ViewportRect = viewportRect;
            ScrollOffset = scrollOffset;
            ScrollSize = scrollSize;
            LocalTransform = localTransform;
            LogicalRotation = logicalRotation;
            HasSeparateRotation = hasSeparateRotation;
            StateFlags = stateFlags;
        }

        public bool IsValid => (StateFlags & ElementState.Valid) != 0;
        public bool IsAttached => (StateFlags & ElementState.Attached) != 0;
        public bool IsVisibleInHierarchy => (StateFlags & ElementState.VisibleInHierarchy) != 0;
        public bool EnabledInHierarchy => (StateFlags & ElementState.DisabledInHierarchy) == 0;
    }

    public partial class Element
    {
        // Shared main-thread scratch buffers avoid 120+ bytes of per-Element array allocations.
        static readonly float[] sharedMetricsScratch = new float[16];
        static readonly float[] sharedPointScratch = new float[2];
        static readonly float[] sharedTransformScratch = new float[32];
        static readonly float[] sharedViewportScratch = new float[4];

        Dictionary<(string property, bool local), (string recent, string prior)> styleStrings;
        Dictionary<(int field, string name), string> strings;
        ElementResolvedStyle resolvedStyle;

        string ReadString(int field, string name = null)
        {
            strings ??= new Dictionary<(int, string), string>();
            var key = (field, name);
            strings.TryGetValue(key, out var previous);
            var value = NativeMethods.ReadElement(SurfaceHandle, NativeHandle, token, field, name, previous);
            if (value != null && value.Length <= 64 * 1024)
            {
                strings[key] = value;
            }
            else
            {
                strings.Remove(key);
            }
            return value;
        }

        internal string ReadStyle(string property, bool local)
        {
            var checkedToken = CheckedToken();
            styleStrings ??= new Dictionary<(string, bool), (string, string)>();
            var key = (property, local);
            styleStrings.TryGetValue(key, out var previous);
            var value = NativeMethods.ReadStyle(SurfaceHandle, NativeHandle, checkedToken, property, local, previous.recent, previous.prior);
            // 每个已读属性最多保留两个有限文本；空值不丢弃显隐切换前的字符串。
            if (value != null && value.Length <= 64 * 1024 && !ReferenceEquals(value, previous.recent))
            {
                styleStrings[key] = (value, previous.recent);
            }
            return value;
        }

        internal ReadOnlySpan<byte> ReadStyleNumberText(string property)
            => NativeMethods.ReadStyleNumberText(SurfaceHandle, NativeHandle, CheckedToken(), property);

        float[] ReadMetrics()
        {
            RequireValid();
            if (!HasLayout)
            {
                throw new InvalidOperationException("元素尚未参与已完成的布局，请等待 GeometryChangedEvent 或下一帧。");
            }

            var surface = SurfaceHandle;
            RequireSuccess(surface, NativeMethods.urui_element_box(surface, NativeHandle, token, sharedMetricsScratch, sharedMetricsScratch.Length), "读取几何");
            return sharedMetricsScratch;
        }

        static Rect ReadRect(float[] values, int offset) => new Rect(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);

        /// <summary>
        /// 单次原生调用同时读取几何框、视口、滚动、本地变换和状态位。
        /// </summary>
        public unsafe ElementSpatialSnapshot GetSpatialSnapshot()
        {
            RequireValid();
            if (!HasLayout)
            {
                throw new InvalidOperationException("元素尚未完成布局。");
            }
            var surface = SurfaceHandle;
            RequireSuccess(surface, NativeMethods.urui_element_spatial_snapshot(surface, NativeHandle, token, out var native), "读取空间快照");
            var local = new Matrix4x4();
            var rot = new Matrix4x4();
            for (var i = 0; i < 16; i++)
            {
                local[i] = native.LocalMatrix[i];
                rot[i] = native.RotationMatrix[i];
            }
            var separate = native.SeparateRotation != 0;
            return new ElementSpatialSnapshot(
                new Rect(native.Layout[0], native.Layout[1], native.Layout[2], native.Layout[3]),
                new Rect(native.Content[0], native.Content[1], native.Content[2], native.Content[3]),
                new Rect(native.WorldBounds[0], native.WorldBounds[1], native.WorldBounds[2], native.WorldBounds[3]),
                new Rect(native.Viewport[0], native.Viewport[1], native.Viewport[2], native.Viewport[3]),
                new Vector2(native.Scroll[0], native.Scroll[1]),
                new Vector2(native.Scroll[2], native.Scroll[3]),
                local,
                separate ? rot.rotation : Quaternion.identity,
                separate,
                native.StateFlags > 0 ? (ElementState)native.StateFlags : ElementState.None);
        }

        /// <summary>
        /// 最近完成的原生布局中的边框矩形，位置相对于布局父级。
        /// </summary>
        public Rect Layout => ReadRect(ReadMetrics(), 0);

        /// <summary>
        /// 本地坐标中的内容矩形。
        /// </summary>
        public Rect ContentRect => ReadRect(ReadMetrics(), 4);

        /// <summary>
        /// 累积变换后的文档坐标轴对齐包围框。
        /// </summary>
        public Rect WorldBound => ReadRect(ReadMetrics(), 8);

        /// <summary>
        /// 扣除边框、内边距及可见滚动条的本地视口，适用于普通 overflow 节点。
        /// </summary>
        public Rect ViewportRect
        {
            get
            {
                RequireValid();
                if (!HasLayout)
                {
                    throw new InvalidOperationException("读取视口需要已完成的布局。");
                }
                var surface = SurfaceHandle;
                RequireSuccess(surface, NativeMethods.urui_element_viewport(surface, NativeHandle, token, sharedViewportScratch, sharedViewportScratch.Length), "读取视口");
                return ReadRect(sharedViewportScratch, 0);
            }
        }

        /// <summary>
        /// 读取不含变换原点平移的本地矩阵，以及显式旋转原语的乘积；含 matrix 原语时返回 false。
        /// </summary>
        /// <remarks>
        /// 矩阵来自最近计算样式。旋转与缩放的语义不能从任意矩阵唯一恢复；false 时 localTransform 仍有效。
        /// </remarks>
        public bool TryGetLocalTransform(out Matrix4x4 localTransform, out Quaternion logicalRotation)
        {
            RequireValid();
            if (!HasLayout)
            {
                throw new InvalidOperationException("元素尚未完成布局。");
            }
            if (NativeMethods.urui_transform_api_version() != 2)
            {
                throw new NotSupportedException("不支持的原生变换协议。");
            }
            var surface = SurfaceHandle;
            var result = NativeMethods.urui_element_local_transform(surface, NativeHandle, token, sharedTransformScratch, sharedTransformScratch.Length);
            if (result != 2)
            {
                RequireSuccess(surface, result, "读取本地变换");
            }
            localTransform = new Matrix4x4();
            var rotation = new Matrix4x4();
            for (var index = 0; index < 16; index++)
            {
                localTransform[index] = sharedTransformScratch[index];
                rotation[index] = sharedTransformScratch[16 + index];
            }
            logicalRotation = result == NativeStatus.Ok ? rotation.rotation : Quaternion.identity;
            return result == NativeStatus.Ok;
        }

        /// <summary>
        /// 最近完成的计算样式和实际尺寸读取入口。
        /// </summary>
        public ElementResolvedStyle ResolvedStyle => resolvedStyle ??= new ElementResolvedStyle(this);

        /// <summary>
        /// 当前滚动偏移，设置时由 URUI 根据内容范围夹取。
        /// </summary>
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
                ElementStyle.RequireFinite(value.x);
                ElementStyle.RequireFinite(value.y);
                var surface = SurfaceHandle;
                RequireSuccess(surface, NativeMethods.urui_element_scroll(surface, NativeHandle, token, value.x, value.y), "设置滚动偏移");
                document.FinishNativeCall();
            }
        }

        /// <summary>
        /// 可滚动内容的整体尺寸。
        /// </summary>
        public Vector2 ScrollSize
        {
            get
            {
                var values = ReadMetrics();
                return new Vector2(values[14], values[15]);
            }
        }

        /// <summary>
        /// 将元素本地边框坐标转换到文档坐标。
        /// </summary>
        public Vector2 LocalToWorld(Vector2 point) => ConvertPoint(point, true);

        /// <summary>
        /// 将文档坐标投影到元素本地边框坐标；不可逆变换抛异常。
        /// </summary>
        public Vector2 WorldToLocal(Vector2 point) => ConvertPoint(point, false);

        /// <summary>
        /// 尝试将文档坐标投影到本地；零缩放等不可逆变换返回 false。
        /// </summary>
        public bool TryWorldToLocal(Vector2 point, out Vector2 localPoint)
        {
            RequireValid();
            document.ReadGeometryBarrier();
            ElementStyle.RequireFinite(point.x);
            ElementStyle.RequireFinite(point.y);
            var surface = SurfaceHandle;
            var result = NativeMethods.urui_element_point(surface, NativeHandle, token, 0, point.x, point.y, sharedPointScratch);
            localPoint = default;
            if (result == NativeStatus.NotReady)
            {
                return false;
            }
            RequireSuccess(surface, result, "转换坐标");
            localPoint = new Vector2(sharedPointScratch[0], sharedPointScratch[1]);
            return true;
        }

        Vector2 ConvertPoint(Vector2 point, bool toWorld)
        {
            RequireValid();
            document.ReadGeometryBarrier();
            ElementStyle.RequireFinite(point.x);
            ElementStyle.RequireFinite(point.y);
            var surface = SurfaceHandle;
            RequireSuccess(surface, NativeMethods.urui_element_point(surface, NativeHandle, token, toWorld ? 1 : 0, point.x, point.y, sharedPointScratch), "转换坐标");
            return new Vector2(sharedPointScratch[0], sharedPointScratch[1]);
        }

        /// <summary>
        /// 检查本地坐标点是否位于元素边框内。
        /// </summary>
        public bool ContainsPoint(Vector2 point) => new Rect(Vector2.zero, Layout.size).Contains(point);
    }

    /// <summary>
    /// 原生计算样式读取器，尺寸来自已完成的布局；仅限主线程。
    /// </summary>
    public sealed class ElementResolvedStyle
    {
        readonly Element element;
        internal ElementResolvedStyle(Element element)
        {
            this.element = element;
        }

        /// <summary>
        /// 最终边框宽度，像素。
        /// </summary>
        public float Width => element.Layout.width;

        /// <summary>
        /// 最终边框高度，像素。
        /// </summary>
        public float Height => element.Layout.height;

        /// <summary>
        /// 读取最近计算样式中的有限数值长度或 auto，不创建托管 CSS 字符串。
        /// </summary>
        public bool TryGetLength(string property, out Length length)
        {
            URUI.Documents.Document.RequireName(property, nameof(property));
            if (!element.HasLayout)
            {
                throw new InvalidOperationException("元素尚未完成布局。");
            }
            length = default;
            var text = element.ReadStyleNumberText(property);
            while (!text.IsEmpty && text[0] <= 32)
            {
                text = text.Slice(1);
            }
            while (!text.IsEmpty && text[text.Length - 1] <= 32)
            {
                text = text.Slice(0, text.Length - 1);
            }
            if (AsciiEquals(text, "auto"))
            {
                length = Length.Auto;
                return true;
            }
            if (!System.Buffers.Text.Utf8Parser.TryParse(text, out float value, out var consumed) || !float.IsFinite(value))
            {
                return false;
            }
            var suffix = text.Slice(consumed);
            if (suffix.IsEmpty || AsciiEquals(suffix, "px"))
            {
                length = Length.Pixels(value);
            }
            else if (AsciiEquals(suffix, "%"))
            {
                length = Length.Percent(value);
            }
            else if (AsciiEquals(suffix, "em"))
            {
                length = Length.Em(value);
            }
            else if (AsciiEquals(suffix, "rem"))
            {
                length = Length.Rem(value);
            }
            else
            {
                return false;
            }
            return true;
        }

        static bool AsciiEquals(ReadOnlySpan<byte> value, string expected)
        {
            if (value.Length != expected.Length)
            {
                return false;
            }
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != expected[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 读取级联与变量解析后的 CSS 值；尺寸像素请使用 Width/Height。
        /// </summary>
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
