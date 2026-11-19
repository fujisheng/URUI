using System;
using System.Collections.Generic;
using URUI.Authoring.Source;
using URUI.Documents;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Sessions
{
    /// <summary>
    /// 只匹配加载时记录的真实节点包装，运行时新建/克隆同名节点没有作者映射。
    /// </summary>
    public sealed class SourceMap
    {
        readonly Dictionary<string, string> keys = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, InspectedNode> nodes = new Dictionary<string, InspectedNode>(StringComparer.Ordinal);
        public string SourceText
        {
            get;
        }
        public SourceMap(Document document, SourceDocument source)
        {
            SourceText = source.Text;
            if (RuntimeInspectionBridge.LoadedMarkup(document) != source.Text)
            {
                return;
            }
            var tree = RuntimeInspectionBridge.SourceTree(document);
            if (tree == null || document.Body == null)
            {
                return;
            }
            string bodyId = RuntimeInspectionBridge.Inspect(document.Body).Id;
            var body = Find(tree, bodyId);
            SourceDocument.Node authorBody = null;
            foreach (var node in source.Nodes)
            {
                if (node.Tag == "body")
                {
                    if (authorBody != null)
                    {
                        return;
                    }
                    authorBody = node;
                }
            }
            if (body != null && authorBody != null)
            {
                Match(body, authorBody);
            }
        }
        public string Key(InspectedNode node) => node != null && node.IsValid && keys.TryGetValue(node.Id, out var key) ? key : null;
        public InspectedNode Node(string key) => nodes.TryGetValue(key, out var node) && node.IsValid ? node : null;
        void Match(RuntimeInspectionBridge.SourceNodeSnapshot runtime, SourceDocument.Node source)
        {
            if (runtime.Tag != source.Tag || runtime.OriginalName != (source.Attribute("id") ?? "") || source.Tag == "template")
            {
                return;
            }
            keys[runtime.Node.Id] = source.Key;
            nodes[source.Key] = runtime.Node;
            if (runtime.Children.Count != source.Children.Count)
            {
                return;
            }
            for (int i = 0; i < runtime.Children.Count; i++)
            {
                Match(runtime.Children[i], source.Children[i]);
            }
        }
        static RuntimeInspectionBridge.SourceNodeSnapshot Find(RuntimeInspectionBridge.SourceNodeSnapshot tree, string id)
        {
            if (tree.Node.Id == id)
            {
                return tree;
            }
            foreach (var child in tree.Children)
            {
                var result = Find(child, id);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
