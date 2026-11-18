using System;
using URUI.Elements;
using URUI.Resources;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        /// <summary>获取当前页面的编译模板，拒绝已变更而尚未重新加载的资产代际。</summary>
        public CompiledTemplate GetCompiledTemplate(string uri)
        {
            CheckThread();
            if (content?.CompiledAsset == null || content.CompiledFingerprint != content.CompiledAsset.Fingerprint)
                throw new InvalidOperationException("当前页面编译资产已变更，请重新加载页面。");
            return content.CompiledAsset.GetTemplate(uri);
        }

        /// <summary>捕获编译子树的节点表；动态子树返回 null，继续使用普通查询。</summary>
        public CompiledQuerySnapshot CreateCompiledQuerySnapshot(Element root)
        {
            CheckThread();
            if (root == null || !ReferenceEquals(root.Document, this)) throw new ArgumentException("查询根必须属于当前文档。", nameof(root));
            if (!root.IsValid || content?.CompiledAsset == null || content.CompiledFingerprint != content.CompiledAsset.Fingerprint) return null;
            var status = NativeMethods.urui_element_compiled_info(NativeHandle, root.Token, 0, null, out var info);
            if (status != 1 || (info.Flags & 1) == 0) return null;
            var index = content.CompiledAsset.FindBlueprint(info.Identity);
            if (index == null || !index.TryRange(info.LocalId, out var first, out var end) || end != info.End) return null;
            var tokens = new ulong[end - first];
            if (NativeMethods.urui_element_compiled_tokens(NativeHandle, root.Token, info.Identity, info.Revision, tokens, tokens.Length) != 1) return null;
            return new CompiledQuerySnapshot(this, root, generation, content.CompiledFingerprint, index, first, end, tokens);
        }

        internal bool IsCompiledQueryCurrent(uint version, string fingerprint)
            => IsCurrent(version) && content?.Generation == version && content.CompiledAsset &&
                content.CompiledFingerprint == fingerprint && content.CompiledAsset.Fingerprint == fingerprint;
    }

    /// <summary>同一代文档内复用的编译查询快照。</summary>
    public sealed class CompiledQuerySnapshot
    {
        readonly Document owner;
        readonly Element root;
        readonly uint generation;
        readonly string fingerprint;
        readonly CompiledNodeIndex index;
        readonly int first, end;
        readonly ulong[] tokens;
        internal CompiledQuerySnapshot(Document owner, Element root, uint generation, string fingerprint, CompiledNodeIndex index, int first, int end, ulong[] tokens)
        { this.owner = owner; this.root = root; this.generation = generation; this.fingerprint = fingerprint; this.index = index; this.first = first; this.end = end; this.tokens = tokens; }

        /// <summary>缓存已失效时返回 false，调用者继续使用真实 DOM 查询。</summary>
        public bool TryQuery(string selector, out CompiledQueryResult result)
        {
            result = default;
            if (string.IsNullOrEmpty(selector) || selector.Length < 2 || (selector[0] != '#' && selector[0] != '.') || selector.IndexOf('\0') >= 0) return false;
            if (!owner.IsCompiledQueryCurrent(generation, fingerprint) || !root.IsValid) return false;
            var indices = index.Query(selector, first, end, out var key);
            var kind = selector[0] == '#' ? 1 : 2;
            if (key == null) key = selector.Substring(1);
            var status = NativeMethods.urui_element_compiled_info(root.NativeHandle, root.Token, kind, key, out var info);
            var required = 1 | (kind == 1 ? 2 : 4);
            if (status != 1 || info.Identity != index.Identity || (info.Flags & required) != required || info.End != end) return false;
            var includeRoot = info.RootMatch != 0;
            var count = includeRoot ? 1 : 0;
            for (var i = 0; i < indices.Count; i++) if (tokens[indices.Array[indices.Offset + i] - first] != 0) count++;
            result = new CompiledQueryResult(owner, root, generation, fingerprint, tokens, first, indices, includeRoot, count);
            return true;
        }
    }
    /// <summary>不复制节点数组的查询结果；文档卸载后不可继续枚举。</summary>
    public readonly struct CompiledQueryResult
    {
        readonly Document owner;
        readonly Element root;
        readonly uint generation;
        readonly string fingerprint;
        readonly ulong[] tokens;
        readonly int first;
        readonly ArraySegment<int> indices;
        readonly bool includeRoot;
        public int Count { get; }
        internal CompiledQueryResult(Document owner, Element root, uint generation, string fingerprint, ulong[] tokens, int first, ArraySegment<int> indices, bool includeRoot, int count)
        { this.owner = owner; this.root = root; this.generation = generation; this.fingerprint = fingerprint; this.tokens = tokens; this.first = first; this.indices = indices; this.includeRoot = includeRoot; Count = count; }
        public Enumerator GetEnumerator() => new Enumerator(this);
        public struct Enumerator
        {
            readonly CompiledQueryResult result;
            int position;
            bool rootPending;
            public Element Current { get; private set; }
            internal Enumerator(CompiledQueryResult result) { this.result = result; position = -1; rootPending = result.includeRoot; Current = null; }
            public bool MoveNext()
            {
                if (!result.owner || !result.owner.IsCompiledQueryCurrent(result.generation, result.fingerprint)) throw new InvalidOperationException("编译查询快照已失效。");
                if (rootPending) { rootPending = false; Current = result.root; return true; }
                while (++position < result.indices.Count)
                {
                    var token = result.tokens[result.indices.Array[result.indices.Offset + position] - result.first];
                    if (token == 0) continue;
                    Current = result.owner.WrapElement(token); return true;
                }
                Current = null; return false;
            }
        }
    }
}
