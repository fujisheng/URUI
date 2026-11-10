using UnityEngine;
using URUI.Styles;

namespace URUI.Rendering.Effects
{
    /// <summary>回调前冻结的样式、内容矩形和文档平面变换；根身份只属于当前文档代际。</summary>
    public sealed class EffectLayout
    {
        public ComputedStyleSnapshot Style { get; }
        public Rect ContentRect { get; }
        /// <summary>本地 z=0 平面到文档的单应矩阵；乘法后需要除以 w。</summary>
        public Matrix4x4 ElementToDocument { get; }
        public ulong RootToken { get; }
        public Rect RootContentRect { get; }
        public Matrix4x4 RootToDocument { get; }
        internal EffectLayout(ComputedStyleSnapshot style, Rect content, Matrix4x4 element, ulong root, Rect rootContent, Matrix4x4 rootMatrix)
        { Style=style; ContentRect=content; ElementToDocument=element; RootToken=root; RootContentRect=rootContent; RootToDocument=rootMatrix; }
        public EffectLayout WithStyle(ComputedStyleSnapshot style) => new(style ?? throw new System.ArgumentNullException(nameof(style)),ContentRect,ElementToDocument,RootToken,RootContentRect,RootToDocument);
        /// <summary>仅适用于同一文档中已经冻结的坐标快照。</summary>
        public EffectLayout WithRoot(EffectLayout source) => new(Style,ContentRect,ElementToDocument,source.RootToken,source.RootContentRect,source.RootToDocument);
    }
}
