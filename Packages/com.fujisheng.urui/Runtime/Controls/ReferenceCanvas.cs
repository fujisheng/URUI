using System;
using UnityEngine;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>
    /// 设备安全区的布局策略；通用默认保留实际边界，项目可明确选择左右对称。
    /// </summary>
    public enum SafeAreaPolicy
    {
        Physical,
        SymmetricHorizontal
    }

    /// <summary>
    /// 在作者根节点上应用参考画布缩放。结构、定位和变换原点由 CSS 声明。
    /// </summary>
    public sealed class ReferenceCanvas : IDisposable
    {
        readonly Element root;
        readonly Element safeContent;
        readonly Vector2 referenceSize;
        readonly float match;
        readonly SafeAreaPolicy safeAreaPolicy;
        Rect viewport;
        Rect safeArea;
        float previousMaximumScale;
        bool applied;
        bool disposed;
        public float Scale
        {
            get;
            private set;
        } = 1;
        public Vector2 LogicalSize
        {
            get;
            private set;
        }

        /// <param name="root">作者声明的绝对定位根，transform-origin 为左上角。</param>
        /// <param name="safeContent">可选安全区容器。</param>
        /// <param name="safeAreaPolicy">默认直接使用设备边界；对称留白由调用方显式选择。</param>
        public ReferenceCanvas(Element root, Vector2 referenceSize, float match, Element safeContent = null, SafeAreaPolicy safeAreaPolicy = SafeAreaPolicy.Physical)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            if (!float.IsFinite(referenceSize.x) || !float.IsFinite(referenceSize.y) || referenceSize.x <= 0 || referenceSize.y <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceSize));
            }
            if (!float.IsFinite(match) || match < 0 || match > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(match));
            }
            if (safeAreaPolicy != SafeAreaPolicy.Physical && safeAreaPolicy != SafeAreaPolicy.SymmetricHorizontal)
            {
                throw new ArgumentOutOfRangeException(nameof(safeAreaPolicy));
            }
            if (safeContent != null && !ReferenceEquals(root.Document, safeContent.Document))
            {
                throw new ArgumentException("安全区必须属于同一文档。", nameof(safeContent));
            }
            this.referenceSize = referenceSize;
            this.match = match;
            this.safeContent = safeContent;
            this.safeAreaPolicy = safeAreaPolicy;
        }

        /// <summary>
        /// 物理像素输入；与显示使用同一节点变换，命中和 WorldToLocal 自动使用相同缩放。
        /// </summary>
        /// <param name="maximumScale">可选正数缩放上限；默认无上限。最小内容宽度等项目策略由调用方计算。</param>
        public void Apply(Rect display, Rect safe, float maximumScale = float.PositiveInfinity)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ReferenceCanvas));
            }
            if (!Finite(display) || !Finite(safe) || display.width <= 0 || display.height <= 0 || safe.width < 0 || safe.height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(display));
            }
            if (float.IsNaN(maximumScale) || maximumScale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumScale));
            }
            if (applied && viewport == display && safeArea == safe && previousMaximumScale == maximumScale)
            {
                return;
            }
            var scale = Mathf.Min(maximumScale, Mathf.Pow(display.width / referenceSize.x, 1 - match) * Mathf.Pow(display.height / referenceSize.y, match));
            var logical = new Vector2(display.width, display.height) / scale;
            root.Style.SetLeft(display.x);
            root.Style.SetTop(display.y);
            root.Style.SetWidth(logical.x);
            root.Style.SetHeight(logical.y);
            root.SetTransform(Vector2.zero, Vector2.one * scale);
            if (safeContent != null)
            {
                var left = Mathf.Clamp(safe.xMin - display.xMin, 0, display.width);
                var right = Mathf.Clamp(display.xMax - safe.xMax, 0, display.width);
                if (safeAreaPolicy == SafeAreaPolicy.SymmetricHorizontal)
                {
                    left = right = Mathf.Max(left, right);
                }
                var top = Mathf.Clamp(display.yMax - safe.yMax, 0, display.height);
                var bottom = Mathf.Clamp(safe.yMin - display.yMin, 0, display.height);
                safeContent.Style.SetLeft(left / scale);
                safeContent.Style.SetTop(top / scale);
                safeContent.Style.SetWidth(Mathf.Max(0, display.width - left - right) / scale);
                safeContent.Style.SetHeight(Mathf.Max(0, display.height - top - bottom) / scale);
            }
            Scale = scale;
            LogicalSize = logical;
            viewport = display;
            safeArea = safe;
            previousMaximumScale = maximumScale;
            applied = true;
        }
        static bool Finite(Rect rect) => float.IsFinite(rect.x) && float.IsFinite(rect.y) && float.IsFinite(rect.width) && float.IsFinite(rect.height);
        public void Dispose()
        {
            disposed = true;
        }
    }
}
