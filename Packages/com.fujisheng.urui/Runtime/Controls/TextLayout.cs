using System;
using UnityEngine;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>
    /// 把九向文本对齐组合到普通作者节点；外层保留尺寸，内容节点保留自然换行高度。
    /// </summary>
    public sealed class TextLayout
    {
        readonly Element host;
        TextAnchor alignment;
        public TextLayout(Element host, Element content, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            if (!ReferenceEquals(host.Document, content.Document) || !ReferenceEquals(content.Parent, host))
            {
                throw new ArgumentException("文本内容必须是同一文档中外层的直接子节点。", nameof(content));
            }
            host.AddToClassList("text-layout");
            content.AddToClassList("text-content");
            Alignment = alignment;
        }
        public Element Host => host;
        public Element Content
        {
            get;
        }
        public TextAnchor Alignment
        {
            get => alignment;
            set
            {
                if ((int)value < 0 || (int)value > 8)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                var row = (int)value / 3;
                var column = (int)value % 3;
                host.EnableInClassList("align-top", row == 0);
                host.EnableInClassList("align-middle", row == 1);
                host.EnableInClassList("align-bottom", row == 2);
                host.EnableInClassList("align-left", column == 0);
                host.EnableInClassList("align-center", column == 1);
                host.EnableInClassList("align-right", column == 2);
                alignment = value;
            }
        }
    }
}
