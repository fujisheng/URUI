using URUI.Styles;
namespace URUI.Documents
{
    public sealed partial class Document
    {
        DocumentStyles styles;
        /// <summary>
        /// 当前文档的类型化计算样式及稳定值订阅。
        /// </summary>
        public DocumentStyles Styles
        {
            get
            {
                CheckThread();
                return styles ??= new DocumentStyles(this);
            }
        }
    }
}
