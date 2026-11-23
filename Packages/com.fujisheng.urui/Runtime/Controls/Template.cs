using System;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>存储尚未实例化的 HTML 模板内容。</summary>
    public sealed class Template : Element
    {
        internal Template(ElementContext context) : base(context)
        {
        }

        /// <summary>将已解析的模板内容克隆到同一文档的空容器。</summary>
        public void CloneContentTo(Element destination)
        {
            RequireValid();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!ReferenceEquals(Document, destination.Document) || !destination.IsValid)
            {
                throw new ArgumentException("模板目标必须是同一文档中的有效节点。", nameof(destination));
            }

            using var update = Document.BeginUpdate();
            if (NativeMethods.urui_element_clone_template_content(SurfaceHandle, NativeHandle, Token, destination.Token) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("模板内容只能克隆到同一文档的空容器。");
            }
            Document.MarkGeometryChanged();
            Document.InitializeSubtree(destination);
        }
    }
}
