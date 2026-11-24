using System;
using System.Collections.Generic;
using System.IO;
using URUI.Authoring.Source;
using URUI.Editor.Authoring;

namespace URUI.Authoring.Sessions
{
    [Serializable]
    public sealed class AuthoringEdit
    {
        public string Operation = "set";
        public string Node;
        public string Property;
        public string Value;
        public string Parent;
        public int Index = -1;
    }
    /// <summary>
    /// 作者命令只发布通过原生语义校验的工作副本；保存为独立操作。
    /// </summary>
    public sealed class AuthoringSession
    {
        public string Id
        {
            get;
        } = Guid.NewGuid().ToString("N");
        public SourceWorkspace Workspace
        {
            get;
        }
        internal string AnimationPath;
        internal string AnimationPage;
        public SourceFile File
        {
            get;
            private set;
        }
        public SourceDocument Model
        {
            get;
            private set;
        }
        public long Revision => Workspace.Revision;
        public long StructureRevision
        {
            get;
            private set;
        }
        public event Action Changed;
        NodeIdentities identities;
        sealed class IdentitySnapshot
        {
            internal string Path;
            internal string Text;
            internal NodeIdentities Identities;
        }
        readonly Dictionary<int, IdentitySnapshot> identityHistory = new Dictionary<int, IdentitySnapshot>();
        public string NodeIdentity(string key) => identities?.Identity(key);
        public string ResolveIdentity(string identity) => identities?.Key(identity);
        public IReadOnlyList<AuthoringEdit> LastPropertyEdits
        {
            get;
            private set;
        }
        public long PropertyEditsRevision
        {
            get;
            private set;
        } = -1;
        public long PropertyEditsPreviousRevision
        {
            get;
            private set;
        } = -1;
        void Publish()
        {
            if (File != null && identities != null)
            {
                int pos = Workspace.HistoryPosition;
                identityHistory[pos] = new IdentitySnapshot
                {
                    Path = File.Path,
                    Text = File.Text,
                    Identities = identities.Copy()
                };
                if (identityHistory.Count > SourceWorkspace.MaxHistoryEntries + 4)
                {
                    int minKeep = pos - SourceWorkspace.MaxHistoryEntries;
                    int maxKeep = pos + SourceWorkspace.MaxHistoryEntries;
                    List<int> stale = null;
                    foreach (int key in identityHistory.Keys)
                    {
                        if (key < minKeep || key > maxKeep)
                        {
                            stale ??= new List<int>();
                            stale.Add(key);
                        }
                    }
                    if (stale != null)
                    {
                        for (int i = 0; i < stale.Count; i++)
                        {
                            identityHistory.Remove(stale[i]);
                        }
                    }
                }
            }
            Changed?.Invoke();
        }
        public AuthoringSession(string projectRoot)
        {
            // 依赖 HTML 可以是模板片段；当前页面仍按完整文档校验，Open 也会独立校验新页面。
            Workspace = new SourceWorkspace(projectRoot, ValidateWorkspaceSource);
        }
        string ValidateWorkspaceSource(string path, string text)
        {
            string error = Validate(path, text);
            if (error != null && Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase) && (File == null || path != File.Path)
                && System.IO.File.Exists(path) && Validate(path, System.IO.File.ReadAllText(path)) != null)
            {
                return ValidateMarkup(path, text, true);
            }
            return error;
        }
        public void Open(string path) => Open(path, false);

        internal void Open(string path, bool discardChanges)
        {
            var file = Workspace.Open(path);
            string text = discardChanges ? file.SavedText : file.Text;
            if (text == null)
            {
                throw new InvalidOperationException("Open an existing HTML page.");
            }
            string error = Validate(file.Path, text);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }
            var model = SourceDocument.Parse(text);
            if (discardChanges)
            {
                Workspace.DiscardChanges();
                SessionUndoBridge.Clear(Id);
            }
            var previousFile = File;
            File = file;
            Model = model;
            if (previousFile != null && previousFile != file)
            {
                Workspace.PruneCleanUnused(file.Path);
            }
            Workspace.InvalidateContext();
            identities = new NodeIdentities(model);
            identityHistory.Clear();
            StructureRevision++;
            Publish();
        }
        public void Set(string key, string property, string value, long expectedRevision)
            => Execute(new[] { new AuthoringEdit {
Node = key,
Property = property,
Value = value
} }, expectedRevision);

        /// <summary>
        /// Insert one element at a known position and return the new element's session identity.
        /// </summary>
        public string InsertNode(string parentIdentity, int index, string markup, long expectedRevision)
            => InsertNodes(parentIdentity, index, markup, expectedRevision, requireSingle: true)[0];

        internal string[] InsertNodes(string parentIdentity, int index, string markup, long expectedRevision, bool requireSingle = false)
        {
            if (expectedRevision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale authoring revision");
            }
            string parentKey = ResolveIdentity(parentIdentity);
            if (parentKey == null)
            {
                throw new InvalidOperationException("stale_node: insertion parent no longer exists");
            }
            var fragment = SourceDocument.Parse(markup);
            int count = fragment.Roots.Count;
            if (count == 0 || (requireSingle && count != 1))
            {
                throw new InvalidOperationException(requireSingle ? "InsertNode requires one root element." : "Paste requires at least one HTML element.");
            }
            int position = index == -1 ? Model.Find(parentKey).Children.Count : index;
            Execute(new[] { new AuthoringEdit {
Operation = "insert",
Parent = parentKey,
Index = index,
Value = markup
} }, expectedRevision);
            var children = Model.Find(ResolveIdentity(parentIdentity)).Children;
            var created = new string[count];
            for (int i = 0; i < created.Length; i++)
            {
                created[i] = NodeIdentity(children[position + i].Key);
            }
            return created;
        }
        /// <summary>
        /// 按源码顺序复制选中根节点；祖先与子节点同时选中时只复制祖先。
        /// </summary>
        public string CopyNodes(IEnumerable<string> keys)
        {
            var result = new System.Text.StringBuilder();
            foreach (var node in SelectedRoots(keys))
            {
                result.Append(Model.Text, node.Start, node.End - node.Start);
            }
            return result.ToString();
        }
        /// <summary>
        /// 在同一工作副本事务中移除选中根节点；旧版本、无效结构或校验失败不发布修改。
        /// </summary>
        public void RemoveNodes(IEnumerable<string> keys, long expectedRevision)
        {
            if (expectedRevision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale authoring revision");
            }
            var nodes = SelectedRoots(keys);
            string candidate = Model.Text;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                candidate = candidate.Remove(nodes[i].Start, nodes[i].End - nodes[i].Start);
            }
            if (nodes.Count != 0)
            {
                EditWorkset(new[] { new SourceChange(File.Path, candidate, File.Revision) }, expectedRevision, shadow =>
                            {
                                for (int i = nodes.Count - 1; i >= 0; i--)
                                {
                                    shadow = SourceDocument.Parse(shadow.Remove(nodes[i].Key));
                                }
                                return shadow.Text;
                            });
            }
        }
        List<SourceDocument.Node> SelectedRoots(IEnumerable<string> keys)
        {
            if (Model == null)
            {
                throw new InvalidOperationException("Open an HTML source first.");
            }
            var selected = new HashSet<string>(keys);
            var result = new List<SourceDocument.Node>();
            foreach (string key in selected)
            {
                var node = Model.Find(key);
                bool covered = false;
                for (var parent = node.Parent; parent != null; parent = parent.Parent)
                {
                    if (selected.Contains(parent.Key))
                    {
                        covered = true;
                        break;
                    }
                }
                if (!covered)
                {
                    result.Add(node);
                }
            }
            result.Sort((left, right) => left.Start.CompareTo(right.Start));
            return result;
        }
        public string SetWithStyleSheet(string key, string property, string value, string source, long expectedRevision)
        {
            if (expectedRevision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale authoring revision");
            }
            string identity = NodeIdentity(key);
            bool linked = false;
            foreach (var node in Model.Nodes)
            {
                if (node.Tag == "link" && node.Attribute("href") == source && node.Attribute("rel") == "stylesheet")
                {
                    linked = true;
                }
            }
            if (linked)
            {
                Set(key, property, value, expectedRevision);
                return ResolveIdentity(identity);
            }
            string Build(SourceDocument model)
            {
                model = SourceDocument.Parse(SetSource(model, key, property, value));
                SourceDocument.Node head = null;
                foreach (var node in model.Nodes)
                {
                    if (node.Tag == "head")
                    {
                        head = node;
                    }
                }
                if (head == null)
                {
                    throw new InvalidOperationException("An HTML head is required for the effect stylesheet.");
                }
                return model.Insert(head.Key, "<link rel=\"stylesheet\" href=\"" + SourceDocument.Escape(source, '"') + "\">", -1);
            }
            EditWorkset(new[] { new SourceChange(File.Path, Build(Model), File.Revision) }, expectedRevision, Build);
            return ResolveIdentity(identity);
        }

        public void Execute(IReadOnlyList<AuthoringEdit> edits, long expectedRevision)
        {
            if (File == null)
            {
                throw new InvalidOperationException("Open an HTML source first.");
            }
            if (expectedRevision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale authoring revision");
            }
            var candidate = Model;
            bool structural = false;
            bool allSet = edits.Count > 1;
            if (allSet)
            {
                for (int i = 0; i < edits.Count; i++)
                {
                    if (edits[i].Operation != "set")
                    {
                        allSet = false;
                        break;
                    }
                }
            }
            bool appliedBatch = false;
            if (allSet)
            {
                var batch = new SourceDocument.PropertyEdit[edits.Count];
                for (int i = 0; i < edits.Count; i++)
                {
                    var edit = edits[i];
                    if (edit.Property == "image")
                    {
                        string relative = edit.Value == null ? null : Preview.AuthoringAssetPaths.Reference(Workspace.ProjectRoot, File.Path, edit.Value);
                        batch[i] = new SourceDocument.PropertyEdit(edit.Node, "attr.src", relative);
                    }
                    else
                    {
                        batch[i] = new SourceDocument.PropertyEdit(edit.Node, edit.Property, edit.Value);
                    }
                }
                if (Model.TryApplyPropertyBatch(batch, out string spliced))
                {
                    candidate = spliced == Model.Text ? Model : SourceDocument.Parse(spliced);
                    appliedBatch = true;
                }
            }
            if (!appliedBatch)
            {
                foreach (var edit in edits)
                {
                    string next;
                    switch (edit.Operation)
                    {
                        case "set":
                            if (edit.Property == "image")
                            {
                                string relative = edit.Value == null ? null : Preview.AuthoringAssetPaths.Reference(Workspace.ProjectRoot, File.Path, edit.Value);
                                next = candidate.SetAttribute(edit.Node, "src", relative);
                            }
                            else
                            {
                                next = SetSource(candidate, edit.Node, edit.Property, edit.Value);
                            }
                            break;
                        case "insert":
                            next = candidate.Insert(edit.Parent, edit.Value, edit.Index);
                            break;
                        case "remove":
                            next = candidate.Remove(edit.Node);
                            break;
                        case "move":
                            next = candidate.Move(edit.Node, edit.Parent, edit.Index);
                            break;
                        default:
                            throw new InvalidOperationException("Unsupported authoring operation: " + edit.Operation);
                    }
                    if (edit.Operation != "set" && edits.Count != 1)
                    {
                        throw new InvalidOperationException("Structural commands require an individual transaction because node keys are revision-local.");
                    }
                    structural |= edit.Operation != "set";
                    candidate = SourceDocument.Parse(next);
                }
            }
            var nextIdentities = identities;
            if (structural)
            {
                var edit = edits[0];
                nextIdentities = identities.Transform(Model, candidate, m => edit.Operation == "insert" ? m.Insert(edit.Parent, edit.Value, edit.Index) : edit.Operation == "move" ? m.Move(edit.Node, edit.Parent, edit.Index) : m.Remove(edit.Node));
            }
            PropertyEditsPreviousRevision = Revision;
            Workspace.Edit(File.Path, candidate.Text, File.Revision);
            LastPropertyEdits = structural ? null : new List<AuthoringEdit>(edits);
            PropertyEditsRevision = Revision;
            identities = nextIdentities;
            if (structural || Shape(Model) != Shape(candidate))
            {
                StructureRevision++;
            }
            Model = candidate;
            Publish();
        }
        public void SetSharedRule(string path, int ruleStart, string property, string value, long expectedRevision)
        {
            if (expectedRevision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale authoring revision");
            }
            var file = Workspace.Open(path);
            Workspace.Edit(file.Path, CssSource.SetRuleDeclaration(file.Text, ruleStart, property, value), file.Revision);
            Publish();
        }
        public void EditFile(string path, string text, long expectedRevision)
        {
            if (expectedRevision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale authoring revision");
            }
            var file = Workspace.Open(path);
            string previous = file.Text;
            Workspace.Edit(path, text, file.Revision);
            if (file == File)
            {
                var candidate = SourceDocument.Parse(file.Text);
                if (previous != file.Text)
                {
                    StructureRevision++;
                    identities = new NodeIdentities(candidate);
                }
                Model = candidate;
            }
            Publish();
        }
        /// <summary>
        /// 在同一事务内发布页面、样式及序列；所有候选通过校验后才更新工作集。
        /// </summary>
        public void EditWorkset(IEnumerable<SourceChange> changes, long expectedRevision, Func<SourceDocument, string> knownHtmlOperation = null)
        {
            if (expectedRevision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale authoring revision");
            }
            var candidates = new List<SourceChange>(changes);
            SourceDocument next = null;
            NodeIdentities nextIdentities = identities;
            if (File != null)
            {
                foreach (var change in candidates)
                {
                    if (Workspace.CheckPath(change.Path) != File.Path || change.Text == File.Text)
                    {
                        continue;
                    }
                    next = SourceDocument.Parse(change.Text);
                    if (knownHtmlOperation != null)
                    {
                        if (knownHtmlOperation(Model) != change.Text)
                        {
                            throw new InvalidOperationException("Known source command does not match the candidate.");
                        }
                        nextIdentities = identities.Transform(Model, next, knownHtmlOperation);
                    }
                    else
                    {
                        nextIdentities = new NodeIdentities(next);
                    }
                }
            }
            Workspace.EditBatch(candidates);
            if (next != null)
            {
                StructureRevision++;
                Model = next;
                identities = nextIdentities;
            }
            Publish();
        }
        public static string SetSource(SourceDocument model, string key, string property, string value)
        {
            if (property.StartsWith("style.", StringComparison.Ordinal))
            {
                return model.SetStyle(key, property.Substring(6), value);
            }
            if (property.StartsWith("attr.", StringComparison.Ordinal))
            {
                return model.SetAttribute(key, property.Substring(5), value);
            }
            if (property == "text")
            {
                return model.SetText(key, value);
            }
            if (property == "value")
            {
                var node = model.Find(key);
                if (node.Attribute("type") == "checkbox" || node.Attribute("type") == "radio")
                {
                    return model.SetAttribute(key, "checked", value == "true" ? "" : null);
                }
                return node.Tag == "textarea" ? model.SetText(key, value) : model.SetAttribute(key, "value", value);
            }
            throw new InvalidOperationException("unsupported_property: " + property);
        }
        public void Undo()
        {
            long before = Revision;
            Workspace.Undo();
            Reparse(before != Revision);
        }
        public void Redo()
        {
            long before = Revision;
            Workspace.Redo();
            Reparse(before != Revision);
        }
        public void Reload(bool discardLocal)
        {
            Workspace.Reload(File.Path, discardLocal);
            StructureRevision++;
            Reparse();
        }
        void Reparse(bool invalidate = false)
        {
            if (File != null)
            {
                var next = SourceDocument.Parse(File.Text);
                if (invalidate || Shape(Model) != Shape(next))
                {
                    StructureRevision++;
                }
                Model = next;
                if (identityHistory.TryGetValue(Workspace.HistoryPosition, out var saved) && saved.Path == File.Path && saved.Text == File.Text)
                {
                    identities = saved.Identities.Copy();
                }
                else
                {
                    identities = new NodeIdentities(next);
                }
            }
            Publish();
        }
        internal void RefreshModel() => Reparse(true);
        static string Shape(SourceDocument model)
        {
            var result = new System.Text.StringBuilder();
            foreach (var node in model.Nodes)
            {
                result.Append(node.Tag).Append('/').Append(node.Parent?.Key).Append(';');
            }
            return result.ToString();
        }
        public void Save()
        {
            Animation.AnimationWorkspace.ValidateForSave(this);
            var paths = new List<string>();
            foreach (var file in Workspace.Files)
            {
                paths.Add(file.Path);
            }
            Workspace.Save(paths.ToArray());
            Publish();
        }
        public void Apply(IReadOnlyList<LiveEdit> selectedChanges, SourceMap map, long expectedRevision)
        {
            if (map.SourceText != File.Text || File.HasExternalChange)
            {
                throw new InvalidOperationException("source_conflict: source changed since runtime load");
            }
            var edits = new List<AuthoringEdit>();
            foreach (var change in selectedChanges)
            {
                string key = map.Key(change.Node);
                if (key == null)
                {
                    throw new InvalidOperationException("no_source_mapping: generated, internal or expired node");
                }
                edits.Add(new AuthoringEdit
                {
                    Node = key,
                    Property = change.Property,
                    Value = change.Value
                });
            }
            Execute(edits, expectedRevision);
        }
        public static string Validate(string path, string text)
            => ValidateMarkup(path, text, false);

        static string ValidateMarkup(string path, string text, bool fragment)
        {
            if (path.EndsWith(".urui-sequence.json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    URUI.Animation.SequenceDefinition.Parse(text);
                }
                catch (InvalidOperationException error)
                {
                    return error.Message;
                }
                return null;
            }
            if (Path.GetExtension(path).Equals(".css", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var issue in HtmlCssValidator.ValidateCss(text, path).Diagnostics)
                {
                    if (issue.IsError)
                    {
                        return issue.Code + ": " + issue.Message + " at " + issue.Line + ":" + issue.Column;
                    }
                }
            }
            else
            {
                foreach (var issue in HtmlCssValidator.Validate(text, path, fragment).Diagnostics)
                {
                    if (issue.IsError)
                    {
                        return issue.Code + ": " + issue.Message + " at " + issue.Line + ":" + issue.Column;
                    }
                }
                SourceDocument.Parse(text);
            }
            return null;
        }
    }
}
