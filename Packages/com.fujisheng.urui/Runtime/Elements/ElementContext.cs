using URUI.Documents;

namespace URUI.Elements
{
    /// <summary>由文档工厂提供的节点构造上下文，不持有额外原生资源。</summary>
    public sealed class ElementContext
    {
        /// <summary>拥有节点的文档。</summary>
        public Document Document { get; }
        internal uint Generation { get; }
        internal ulong Token { get; }
        internal ElementContext(Document document, uint generation, ulong token)
        {
            Document = document;
            Generation = generation;
            Token = token;
        }
    }
}
