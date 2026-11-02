using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>映射到 Document 图片资源的图像元素。</summary>
    public class Image : Element
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal Image(ElementContext context) : base(context) { }
        /// <summary>ImageAsset.Source 对应的 RML 图片路径。</summary>
        public string Source { get => GetAttribute("src") ?? ""; set => SetAttribute("src", value); }
    }
}
