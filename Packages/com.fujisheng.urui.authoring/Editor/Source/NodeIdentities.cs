using System;
using System.Collections.Generic;
using System.Text;

namespace URUI.Authoring.Source
{
    /// <summary>编辑器内存身份，不写作者源码。已知结构命令通过标记副本提供明确对应关系；外部修改重新分配。</summary>
    public sealed class NodeIdentities
    {
        readonly Dictionary<string, string> identities = new Dictionary<string, string>(StringComparer.Ordinal);
        public NodeIdentities(SourceDocument model)
        {
            foreach (var node in model.Nodes)
                identities[node.Key] = Guid.NewGuid().ToString("N");
        }
        NodeIdentities(Dictionary<string, string> values)
        {
            foreach (var pair in values)
                identities.Add(pair.Key, pair.Value);
        }
        public string Identity(string key) => key != null && identities.TryGetValue(key, out var value) ? value : null;
        public string Key(string identity)
        {
            foreach (var pair in identities)
                if (pair.Value == identity)
                    return pair.Key;
            return null;
        }
        public NodeIdentities Copy() => new NodeIdentities(identities);
        public NodeIdentities Transform(SourceDocument model, SourceDocument result, Func<SourceDocument, string> operation)
        {
            string marker = "data-urui-editor-" + Guid.NewGuid().ToString("N");
            var marked = new StringBuilder(model.Text.Length + model.Nodes.Count * 90);
            int previous = 0;
            foreach (var node in model.Nodes)
            {
                int at = node.OpenEnd - 1;
                if (model.Text[at - 1] == '/')
                    at--;
                marked.Append(model.Text, previous, at - previous);
                marked.Append(' ').Append(marker).Append("=\"").Append(Identity(node.Key)).Append('"');
                previous = at;
            }
            marked.Append(model.Text, previous, model.Text.Length - previous);
            var transformed = SourceDocument.Parse(operation(SourceDocument.Parse(marked.ToString())));
            if (transformed.Nodes.Count != result.Nodes.Count)
                throw new InvalidOperationException("节点对应关系校验失败。");
            var next = new NodeIdentities(result);
            for (int i = 0; i < result.Nodes.Count; i++)
            {
                if (result.Nodes[i].Tag != transformed.Nodes[i].Tag)
                    throw new InvalidOperationException("节点对应关系结构不匹配。");
                string identity = transformed.Nodes[i].Attribute(marker);
                if (identity != null)
                    next.identities[result.Nodes[i].Key] = identity;
            }
            return next;
        }
    }
}
