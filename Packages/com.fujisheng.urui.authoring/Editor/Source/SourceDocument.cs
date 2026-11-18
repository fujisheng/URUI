using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace URUI.Authoring.Source
{
    /// <summary>保留原始文本的 HTML 词法范围；语义合法性由 URUI 的校验器负责。</summary>
    public sealed class SourceDocument
    {
        public sealed class AttributeSpan
        {
            public string Name { get; internal set; }
            public string Value { get; internal set; }
            public int Start { get; internal set; }
            public int End { get; internal set; }
            public int ValueStart { get; internal set; }
            public int ValueEnd { get; internal set; }
            public char Quote { get; internal set; }
        }

        public sealed class Node
        {
            readonly List<Node> children = new List<Node>();
            readonly List<AttributeSpan> attributes = new List<AttributeSpan>();
            public string Key { get; internal set; }
            public string Tag { get; internal set; }
            public Node Parent { get; internal set; }
            public int Start { get; internal set; }
            public int OpenEnd { get; internal set; }
            public int CloseStart { get; internal set; }
            public int End { get; internal set; }
            public bool IsVoid { get; internal set; }
            public IReadOnlyList<Node> Children => children;
            public IReadOnlyList<AttributeSpan> Attributes => attributes;
            internal List<Node> MutableChildren => children;
            internal List<AttributeSpan> MutableAttributes => attributes;
            public string Attribute(string name)
            { foreach (var a in attributes) if (a.Name == name) return a.Value; return null; }
        }

        static readonly HashSet<string> voidTags = new HashSet<string>(StringComparer.Ordinal)
        { "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr" };
        readonly List<Node> nodes = new List<Node>();
        readonly List<Node> roots = new List<Node>();
        public string Text { get; private set; }
        public IReadOnlyList<Node> Nodes => nodes;
        public IReadOnlyList<Node> Roots => roots;

        public static SourceDocument Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var result = new SourceDocument { Text = text };
            result.Scan();
            return result;
        }

        public Node Find(string key)
        {
            foreach (var node in nodes) if (node.Key == key) return node;
            throw new InvalidOperationException("stale_node: source node does not exist in this revision");
        }

        public string SetAttribute(string key, string name, string value)
        {
            RequireName(name);
            var node = Find(key);
            foreach (var attribute in node.Attributes)
            {
                if (attribute.Name != name) continue;
                if (value == null) return Replace(attribute.Start, attribute.End, "");
                char quote = attribute.Quote == '\0' ? '"' : attribute.Quote;
                var encoded = Escape(value, quote);
                return attribute.Quote == '\0'
                    ? Replace(attribute.Start, attribute.End, name + "=" + quote + encoded + quote)
                    : Replace(attribute.ValueStart, attribute.ValueEnd, encoded);
            }
            if (value == null) return Text;
            int at = node.OpenEnd - 1;
            if (Text[at - 1] == '/') at--;
            return Replace(at, at, " " + name + "=\"" + Escape(value, '"') + "\"");
        }

        public string SetStyle(string key, string property, string value)
        {
            var node = Find(key);
            string old = node.Attribute("style") ?? "";
            string style = CssSource.SetDeclaration(old, property, value);
            if (style == old) return Text;
            foreach (var attribute in node.Attributes)
            {
                if (attribute.Name != "style" || attribute.Quote == '\0' || style.Length == 0) continue;
                int prefix = 0, suffix = 0;
                while (prefix < old.Length && prefix < style.Length && old[prefix] == style[prefix]) prefix++;
                if (prefix > 0 && prefix < old.Length && char.IsLowSurrogate(old[prefix])) prefix--;
                while (suffix < old.Length - prefix && suffix < style.Length - prefix && old[old.Length - suffix - 1] == style[style.Length - suffix - 1]) suffix++;
                if (suffix > 0 && suffix < old.Length && char.IsLowSurrogate(old[old.Length - suffix])) suffix--;
                var offsets = DecodedOffsets(Text.Substring(attribute.ValueStart, attribute.ValueEnd - attribute.ValueStart));
                return Replace(attribute.ValueStart + offsets[prefix], attribute.ValueStart + offsets[old.Length - suffix], Escape(style.Substring(prefix, style.Length - prefix - suffix), attribute.Quote));
            }
            return SetAttribute(key, "style", style.Length == 0 ? null : style);
        }
        static List<int> DecodedOffsets(string raw)
        {
            var result = new List<int> { 0 };
            for (int i = 0; i < raw.Length;)
            {
                int end = raw[i] == '&' ? raw.IndexOf(';', i + 1) : -1;
                if (end > i && end - i < 32)
                {
                    string entity = raw.Substring(i, end - i + 1), decoded = WebUtility.HtmlDecode(entity);
                    if (decoded != entity) { for (int n = 0; n < decoded.Length; n++) result.Add(n == decoded.Length - 1 ? end + 1 : i); i = end + 1; continue; }
                }
                result.Add(++i);
            }
            return result;
        }

        public string SetText(string key, string value)
        {
            var node = Find(key);
            if (node.IsVoid || node.Children.Count != 0 || node.Tag == "style" || node.Tag == "script")
                throw new InvalidOperationException("Text editing requires a leaf text container.");
            return Replace(node.OpenEnd, node.CloseStart, Escape(value ?? "", '\0'));
        }

        public string Remove(string key)
        { var node = Find(key); return Replace(node.Start, node.End, ""); }

        public string Insert(string parentKey, string markup, int index)
        {
            var parent = Find(parentKey);
            if (parent.IsVoid) throw new InvalidOperationException("Cannot insert inside a void element.");
            Parse(markup);
            int at = Insertion(parent, index);
            return Replace(at, at, markup);
        }

        public string Move(string key, string parentKey, int index)
        {
            var node = Find(key);
            var parent = Find(parentKey);
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor == node) throw new InvalidOperationException("Cannot move a node into its descendant.");
            if (parent.IsVoid) throw new InvalidOperationException("Cannot insert inside a void element.");
            int at = Insertion(parent, index);
            if (at >= node.Start && at <= node.End) return Text;
            string fragment = Text.Substring(node.Start, node.End - node.Start);
            string removed = Text.Remove(node.Start, node.End - node.Start);
            if (at > node.End) at -= node.End - node.Start;
            return removed.Insert(at, fragment);
        }

        static int Insertion(Node parent, int index)
        {
            if (index < -1 || index > parent.Children.Count) throw new InvalidOperationException("Invalid child index.");
            return index < 0 || index == parent.Children.Count ? parent.CloseStart : parent.Children[index].Start;
        }
        string Replace(int start, int end, string replacement) => Text.Substring(0, start) + replacement + Text.Substring(end);
        internal static string Escape(string value, char quote)
        {
            string result = value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return quote == '\'' ? result.Replace("'", "&#39;") : quote == '"' ? result.Replace("\"", "&quot;") : result;
        }
        internal static void RequireName(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("Empty property or attribute name.");
            foreach (char c in name) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':'))
                throw new InvalidOperationException("Invalid property or attribute name.");
        }

        void Scan()
        {
            var stack = new Stack<Node>();
            int p = 0;
            while (p < Text.Length)
            {
                if (Text[p] != '<') { p++; continue; }
                if (At(p, "<!--")) { int end = Text.IndexOf("-->", p + 4, StringComparison.Ordinal); if (end < 0) Fail(p); p = end + 3; continue; }
                if (At(p, "<!") || At(p, "<?")) { p = TagEnd(p) + 1; continue; }
                int start = p++;
                bool closing = p < Text.Length && Text[p] == '/';
                if (closing) p++;
                int nameStart = p;
                while (p < Text.Length && (char.IsLetterOrDigit(Text[p]) || Text[p] == '-' || Text[p] == ':' || Text[p] == '_')) p++;
                if (nameStart == p) Fail(start);
                string tag = Text.Substring(nameStart, p - nameStart).ToLowerInvariant();
                int tagEnd = TagEnd(p);
                if (closing)
                {
                    if (stack.Count == 0 || stack.Peek().Tag != tag) Fail(start);
                    var closed = stack.Pop(); closed.CloseStart = start; closed.End = tagEnd + 1;
                    p = tagEnd + 1; continue;
                }
                var node = new Node { Key = "n" + nodes.Count.ToString(CultureInfo.InvariantCulture), Tag = tag, Start = start, OpenEnd = tagEnd + 1 };
                node.Parent = stack.Count > 0 ? stack.Peek() : null;
                if (node.Parent == null) roots.Add(node); else node.Parent.MutableChildren.Add(node);
                nodes.Add(node);
                ReadAttributes(node, p, tagEnd);
                node.IsVoid = voidTags.Contains(tag) || Text[tagEnd - 1] == '/';
                p = tagEnd + 1;
                if (node.IsVoid) { node.CloseStart = node.OpenEnd; node.End = node.OpenEnd; continue; }
                if (tag == "style" || tag == "script" || tag == "textarea" || tag == "title")
                {
                    int close = Text.IndexOf("</" + tag, p, StringComparison.OrdinalIgnoreCase);
                    if (close < 0) Fail(p);
                    node.CloseStart = close; node.End = TagEnd(close) + 1; p = node.End;
                }
                else stack.Push(node);
            }
            if (stack.Count != 0) Fail(stack.Peek().Start);
        }

        void ReadAttributes(Node node, int p, int end)
        {
            while (p < end)
            {
                while (p < end && char.IsWhiteSpace(Text[p])) p++;
                if (p == end || Text[p] == '/') break;
                int start = p;
                while (p < end && !char.IsWhiteSpace(Text[p]) && Text[p] != '=' && Text[p] != '/') p++;
                if (p == start) Fail(p);
                string name = Text.Substring(start, p - start);
                int nameEnd = p;
                while (p < end && char.IsWhiteSpace(Text[p])) p++;
                var attribute = new AttributeSpan { Name = name, Start = start, Value = "", ValueStart = p, ValueEnd = p };
                bool hasValue = p < end && Text[p] == '=';
                if (hasValue)
                {
                    p++; while (p < end && char.IsWhiteSpace(Text[p])) p++;
                    if (p >= end) Fail(p);
                    char quote = Text[p] == '\'' || Text[p] == '"' ? Text[p++] : '\0';
                    attribute.Quote = quote; attribute.ValueStart = p;
                    if (quote != '\0') { while (p < end && Text[p] != quote) p++; if (p == end) Fail(p); }
                    else { while (p < end && !char.IsWhiteSpace(Text[p]) && Text[p] != '>') p++; }
                    attribute.ValueEnd = p;
                    attribute.Value = WebUtility.HtmlDecode(Text.Substring(attribute.ValueStart, p - attribute.ValueStart));
                    if (quote != '\0') p++;
                }
                attribute.End = hasValue ? p : nameEnd;
                foreach (var previous in node.Attributes) if (previous.Name == name) Fail(start);
                node.MutableAttributes.Add(attribute);
            }
        }
        int TagEnd(int p)
        {
            char quote = '\0';
            for (; p < Text.Length; p++)
            {
                char c = Text[p];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == '>') return p;
            }
            Fail(p); return -1;
        }
        bool At(int at, string value) => at + value.Length <= Text.Length && string.CompareOrdinal(Text, at, value, 0, value.Length) == 0;
        static void Fail(int at) => throw new InvalidOperationException("Invalid or incomplete source markup at UTF-16 offset " + at);
    }
}
