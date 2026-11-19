using UnityEngine;
using URUI.Styles;
namespace URUI.Rendering.Effects
{
    /// <summary>
    /// 一次稳定布局的不可变输入；不得跨文档代际使用。
    /// </summary>
    public readonly struct EffectFrame
    {
        public ulong ElementToken
        {
            get;
        }
        public EffectLayout Layout
        {
            get;
        }
        public ComputedStyleSnapshot Style => Layout.Style;
        public Vector2 Size => Layout.ContentRect.size;
        public Vector2 ContentOffset => Layout.ContentRect.position;
        public Texture Source
        {
            get;
        }
        public EffectGeometry SourceGeometry
        {
            get;
        }
        public Vector2Int SourcePixelSize
        {
            get;
        }
        /// <summary>
        /// 覆盖图在文档空间中的像素矩形；原始图片输入为默认矩形。
        /// </summary>
        public Rect SourceCaptureBounds
        {
            get;
        }
        /// <summary>
        /// 采样输入的原始 ink 在纹理内的 UV 域；不包含为了输出预留的透明 padding。
        /// </summary>
        public Rect SourceUvBounds
        {
            get;
        }
        /// <summary>
        /// 元素本地平面到文档坐标的冻结单应矩阵。
        /// </summary>
        public Matrix4x4 ElementToDocument => Layout.ElementToDocument;
        public uint SourceVersion
        {
            get;
        }
        public bool SourceIsBottomLeft
        {
            get;
        }
        public bool SourceIsPremultiplied
        {
            get;
        }
        /// <summary>
        /// 输入为宿主已经绘制的节点覆盖图；其颜色已处于渲染空间。
        /// </summary>
        public bool SourceIsCaptured
        {
            get;
        }
        public double Time
        {
            get;
        }
        internal EffectFrame(ulong token, EffectLayout layout, Texture source, bool borrowed, double time, uint? sourceVersion = null,
            EffectGeometry geometry = null, Rect captureBounds = default, Rect sourceInkBounds = default)
        {
            ElementToken = token;
            Layout = layout;
            Source = source;
            SourceVersion = sourceVersion ?? (source ? source.updateCount : 0);
            SourceIsBottomLeft = borrowed;
            SourceIsPremultiplied = !borrowed;
            SourceIsCaptured = sourceVersion.HasValue;
            Time = time;
            SourceGeometry = geometry;
            SourcePixelSize = source ? new Vector2Int(source.width, source.height) : Vector2Int.one;
            SourceCaptureBounds = captureBounds;
            SourceUvBounds = SourceIsCaptured ? new Rect((sourceInkBounds.xMin - captureBounds.xMin) / captureBounds.width,
                (captureBounds.yMax - sourceInkBounds.yMax) / captureBounds.height, sourceInkBounds.width / captureBounds.width, sourceInkBounds.height / captureBounds.height) : geometry?.UvBounds ?? new Rect(0, 0, 1, 1);
        }
    }
    internal readonly struct EffectInput
    {
        internal readonly ulong token;
        internal readonly ulong input;
        internal readonly string name;
        internal readonly Texture source;
        internal readonly bool borrowed;
        internal readonly EffectGeometry geometry;
        internal readonly PreparedSourceGeometry preparedSource;
        internal readonly Rect captureBounds;
        internal readonly Rect sourceInkBounds;
        internal EffectInput(ulong token, string name, Texture source, bool borrowed, EffectGeometry geometry, Rect captureBounds = default, Rect sourceInkBounds = default, ulong input = 0, PreparedSourceGeometry preparedSource = default)
        {
            this.token = token;
            this.input = input;
            this.name = name;
            this.source = source;
            this.borrowed = borrowed;
            this.geometry = geometry;
            this.preparedSource = preparedSource;
            this.captureBounds = captureBounds;
            this.sourceInkBounds = sourceInkBounds;
        }
    }
}
