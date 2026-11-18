using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using URUI.Authoring.Presentation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;

namespace URUI.Authoring.Windows
{
    /// <summary>The source insertion point is bound to the document and revision seen when it was chosen.</summary>
    internal readonly struct NodePlacement
    {
        readonly string parent;
        readonly int index;
        readonly long revision;
        internal bool IsValid => parent != null;
        internal static bool Editing => !EditorApplication.isPlayingOrWillChangePlaymode &&
            AuthoringEnvironment.Mode == WorkspaceMode.Layout && AuthoringEnvironment.Author.Model != null;

        internal static bool AcceptsChildren(SourceDocument.Node node) =>
            node?.Tag is ("body" or "div" or "main" or "section" or "article" or "aside" or "header" or "footer" or "nav" or "form");

        NodePlacement(SourceDocument.Node node, int index)
        {
            parent = AuthoringEnvironment.Author.NodeIdentity(node.Key);
            this.index = index;
            revision = AuthoringEnvironment.Author.Revision;
        }

        internal static NodePlacement ChildOf(SourceDocument.Node node)
        {
            if (!Editing)
                return default;
            if (node == null || node.IsVoid)
                node = AuthoringEnvironment.Author.Model.Nodes.FirstOrDefault(item => item.Tag == "body");
            return node == null ? default : new NodePlacement(node, -1);
        }

        internal static NodePlacement Beside(SourceDocument.Node node, bool after)
        {
            if (!Editing)
                return default;
            if (node == null || node.Tag == "body" || node.Parent == null)
                return ChildOf(null);
            var children = node.Parent.Children;
            for (int i = 0; i < children.Count; i++)
                if (children[i] == node)
                    return new NodePlacement(node.Parent, after ? i + 1 : i);
            return default;
        }

        internal void Insert(string kind) => InsertMarkup(PrepareTemplate(kind));

        internal void Paste(string markup) => InsertMarkup(CopyWithUniqueIds(markup, AuthoringEnvironment.Author.Model));

        internal void InsertMarkup(string markup)
        {
            if (!IsValid || !Editing)
                throw new InvalidOperationException("No editable parent.");
            var editor = AuthoringEnvironment.Author;
            var ids = new HashSet<string>(editor.Model.Nodes.Select(node => node.Attribute("id")), StringComparer.Ordinal);
            foreach (var node in SourceDocument.Parse(markup).Nodes)
            {
                string id = node.Attribute("id");
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    throw new InvalidOperationException("The pasted markup contains an id already in use: " + id);
            }
            string[] created = null;
            var placement = this;
            SessionUndoBridge.Record(editor, () => created = editor.InsertNodes(placement.parent, placement.index, markup, placement.revision));
            AuthoringEnvironment.SetNodeSelection(created.Select(id => ElementSelectionProxy.ForAuthor(editor.ResolveIdentity(id))));
        }

        internal bool CanMove(string nodeIdentity)
        {
            var author = AuthoringEnvironment.Author;
            string key = author.ResolveIdentity(nodeIdentity);
            string parentKey = author.ResolveIdentity(parent);
            if (key == null || parentKey == null || author.Model.Find(key).Tag is "html" or "head" or "body")
                return false;
            for (var node = author.Model.Find(parentKey); node != null; node = node.Parent)
                if (node.Key == key)
                    return false;
            return true;
        }

        internal void Move(string nodeIdentity, long dragRevision)
        {
            var editor = AuthoringEnvironment.Author;
            if (!IsValid || !Editing || dragRevision != revision || revision != editor.Revision)
                throw new InvalidOperationException("source_conflict: stale drag target");
            string key = editor.ResolveIdentity(nodeIdentity);
            string parentKey = editor.ResolveIdentity(parent);
            if (key == null || parentKey == null)
                throw new InvalidOperationException("stale_node");
            var command = new AuthoringEdit { Operation = "move", Node = key, Parent = parentKey, Index = index };
            SessionUndoBridge.Record(editor, () => editor.Execute(new[] { command }, dragRevision));
            AuthoringEnvironment.Select(ElementSelectionProxy.ForAuthor(editor.ResolveIdentity(nodeIdentity)));
        }

        static string PrepareTemplate(string kind)
        {
            var template = SourceDocument.Parse(NodePresentation.Template(kind));
            string stem = char.ToUpperInvariant(kind[0]) + kind.Substring(1);
            var used = new HashSet<string>(AuthoringEnvironment.Author.Model.Nodes.Select(node => node.Attribute("id")));
            string id = stem;
            for (int suffix = 1; used.Contains(id); suffix++)
                id = stem + suffix;
            return template.SetAttribute(template.Roots[0].Key, "id", id);
        }

        internal static string CopyWithUniqueIds(string markup, SourceDocument document)
        {
            var source = SourceDocument.Parse(markup);
            var existing = new HashSet<string>(document.Nodes.Select(node => node.Attribute("id")), StringComparer.Ordinal);
            var used = new HashSet<string>(existing, StringComparer.Ordinal);
            var fragmentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in source.Nodes)
            {
                string id = node.Attribute("id");
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!fragmentIds.Add(id))
                    throw new InvalidOperationException("The pasted markup contains a duplicate id: " + id);
                used.Add(id);
            }
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string id in fragmentIds)
            {
                if (!existing.Contains(id))
                    continue;
                string stem = id + "-copy";
                string candidate = stem;
                for (int suffix = 2; !used.Add(candidate); suffix++)
                    candidate = stem + "-" + suffix;
                renamed.Add(id, candidate);
            }
            if (renamed.Count == 0)
                return markup;

            var result = source;
            for (int i = 0; i < source.Nodes.Count; i++)
                foreach (var attribute in source.Nodes[i].Attributes)
                {
                    if (string.IsNullOrEmpty(attribute.Value))
                        continue;
                    string value = RenameReference(attribute.Name, attribute.Value, renamed);
                    if (value != attribute.Value)
                        result = SourceDocument.Parse(result.SetAttribute(result.Nodes[i].Key, attribute.Name, value));
                }
            return result.Text;
        }

        static string RenameReference(string name, string value, Dictionary<string, string> renamed)
        {
            if (name is "id" or "for" or "list" or "form")
                return renamed.TryGetValue(value, out string target) ? target : value;
            if (name == "href" && value.StartsWith("#", StringComparison.Ordinal))
                return renamed.TryGetValue(value.Substring(1), out string anchor) ? "#" + anchor : value;
            if (name is "aria-labelledby" or "aria-describedby" or "aria-controls" or "aria-owns" or "headers")
            {
                string[] ids = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                bool changed = false;
                for (int i = 0; i < ids.Length; i++)
                    if (renamed.TryGetValue(ids[i], out string replacement))
                    {
                        ids[i] = replacement;
                        changed = true;
                    }
                if (changed)
                    return string.Join(" ", ids);
            }
            return value;
        }
    }

    internal sealed class WorkbenchDrag
    {
        const string Key = "URUI.Authoring.NodePlacement";
        internal readonly string Kind, Identity, Fragment;
        readonly long revision;
        internal WorkbenchDrag(string kind = null, string identity = null, string fragment = null)
        {
            Kind = kind;
            Identity = identity;
            Fragment = fragment;
            revision = AuthoringEnvironment.Author.Revision;
        }

        internal static WorkbenchDrag Current => DragAndDrop.GetGenericData(Key) as WorkbenchDrag;
        internal static void Start(string label, string kind = null, string identity = null, string fragment = null)
            => new WorkbenchDrag(kind, identity, fragment).Begin(label);

        internal void Begin(string label)
        {
            if (!NodePlacement.Editing)
                return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(Key, this);
            DragAndDrop.StartDrag(label);
        }

        internal bool CanDrop(NodePlacement placement) =>
            placement.IsValid && revision == AuthoringEnvironment.Author.Revision && NodePlacement.Editing &&
            (Identity == null || placement.CanMove(Identity));

        internal void Drop(NodePlacement placement, bool nativeDrag = true)
        {
            if (!CanDrop(placement))
                return;
            if (nativeDrag)
            {
                DragAndDrop.AcceptDrag();
                DragAndDrop.SetGenericData(Key, null);
            }
            if (Kind != null)
                placement.Insert(Kind);
            else if (Fragment != null)
                placement.InsertMarkup(Fragment);
            else
                placement.Move(Identity, revision);
        }
    }
}
