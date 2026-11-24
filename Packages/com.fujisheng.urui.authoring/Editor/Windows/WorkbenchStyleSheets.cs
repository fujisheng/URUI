using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using URUI.Authoring.Preview;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    internal sealed class WorkbenchStyleSheets : VisualElement
    {
        sealed class Sheet
        {
            internal string Name;
            internal string Text;
            internal string Path;
            internal string LinkIdentity;
            internal string InlineIdentity;
            internal bool Writable => Path != null;
        }

        readonly ScrollView contents = new ScrollView();
        readonly Action selectedRuleChanged;
        readonly Dictionary<string, Sheet> sheets = new Dictionary<string, Sheet>(StringComparer.Ordinal);
        readonly HashSet<string> expanded = new HashSet<string>(StringComparer.Ordinal);
        string selectedSheet;
        string selectedSelector;
        int selectedOccurrence;
        long loadedRevision = -1;
        bool loadedPreview;
        internal bool HasSelection => selectedSheet != null;
        internal string SelectedPath => selectedSheet != null && sheets.TryGetValue(selectedSheet, out var sheet) ? sheet.Path : null;

        internal WorkbenchStyleSheets(Action selectedRuleChanged)
        {
            this.selectedRuleChanged = selectedRuleChanged;
            style.minHeight = 100;
            style.flexGrow = 1;
            var header = new Toolbar();
            var heading = WorkbenchUI.Heading(L.Tr("样式表"));
            heading.style.flexGrow = 1;
            header.Add(heading);
            var add = new ToolbarButton(AddSheet)
            {
                text = "+",
                tooltip = L.Tr("引用样式表…")
            };
            header.Add(add);
            Add(header);
            Add(contents);
            contents.style.flexGrow = 1;
        }

        internal void ClearSelection()
        {
            selectedSheet = selectedSelector = null;
            selectedOccurrence = 0;
        }

        internal void Refresh()
        {
            var author = AuthoringEnvironment.Author;
            var preview = AuthoringEnvironment.Preview;
            bool ready = preview?.IsReady == true;
            if (loadedRevision == author.Revision && loadedPreview == ready)
            {
                return;
            }
            loadedRevision = author.Revision;
            loadedPreview = ready;
            contents.Clear();
            sheets.Clear();
            if (author.Model == null)
            {
                return;
            }

            var writable = author.Workspace.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
            var entries = new Dictionary<string, Sheet>(StringComparer.Ordinal);
            if (ready)
            {
                foreach (var resource in preview.Resources.Where(resource => resource.Source.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
                {
                    string assetPath = AuthoringAssetPaths.AssetPath(resource.Source);
                    string full = assetPath == null ? null : Path.Combine(author.Workspace.ProjectRoot, assetPath);
                    bool editable = full != null && writable.ContainsKey(full) && writable[full].Text != null;
                    var sheet = new Sheet
                    {
                        Name = resource.Source,
                        Text = editable ? writable[full].Text : resource.Asset.text,
                        Path = editable ? full : null
                    };
                    entries[resource.Source] = sheet;
                }
            }

            // Direct references follow the author HTML order. Imports and fragment dependencies follow them.
            var directUris = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in author.Model.Nodes)
            {
                if (IsSheetLink(node))
                {
                    string uri = ResolveLink(author, node.Attribute("href"));
                    if (uri == null)
                    {
                        continue;
                    }
                    entries.TryGetValue(uri, out var resolved);
                    string identity = author.NodeIdentity(node.Key);
                    AddSheetRow(identity, new Sheet
                    {
                        Name = node.Attribute("href"),
                        Path = resolved?.Path,
                        Text = resolved?.Text,
                        LinkIdentity = identity
                    });
                    directUris.Add(uri);
                }
                if (node.Tag == "style" && !node.IsVoid)
                {
                    string identity = author.NodeIdentity(node.Key);
                    AddSheetRow(identity, new Sheet
                    {
                        Name = "<style>",
                        Text = author.File.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd),
                        Path = author.File.Path,
                        InlineIdentity = identity
                    });
                }
            }
            foreach (var pair in entries)
            {
                if (!directUris.Contains(pair.Key))
                {
                    AddSheetRow(pair.Key, pair.Value);
                }
            }
            if (selectedSheet != null && !sheets.ContainsKey(selectedSheet))
            {
                ClearSelection();
            }
            expanded.IntersectWith(sheets.Keys);
        }

        void AddSheetRow(string key, Sheet sheet)
        {
            sheets[key] = sheet;
            var fold = new Foldout
            {
                text = sheet.Name,
                value = expanded.Contains(key),
                tooltip = sheet.Path ?? sheet.Name
            };
            fold.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    expanded.Add(key);
                    if (fold.contentContainer.childCount == 0)
                    {
                        AddRuleRows(fold, key, sheet);
                    }
                }
                else
                {
                    expanded.Remove(key);
                }
            });
            contents.Add(fold);
            if (fold.value)
            {
                AddRuleRows(fold, key, sheet);
            }
        }

        void AddRuleRows(Foldout fold, string key, Sheet sheet)
        {
            fold.Add(new Button(() => Select(key, null, 0)) { text = L.Tr("查看样式表") });
            foreach (var group in Rules(sheet).GroupBy(rule => rule.Prelude))
            {
                int occurrence = 0;
                foreach (var rule in group)
                {
                    int choice = occurrence++;
                    var button = new Button(() => Select(key, rule.Prelude, choice))
                    {
                        text = rule.Prelude,
                        tooltip = rule.Prelude
                    };
                    button.style.unityTextAlign = TextAnchor.MiddleLeft;
                    button.style.textOverflow = TextOverflow.Ellipsis;
                    fold.Add(button);
                }
            }
        }

        void Select(string key, string selector, int occurrence)
        {
            AuthoringEnvironment.Select(null);
            selectedSheet = key;
            selectedSelector = selector;
            selectedOccurrence = occurrence;
            selectedRuleChanged();
        }

        internal string PreviewText => selectedSheet != null && sheets.TryGetValue(selectedSheet, out var sheet) ? sheet.Text : null;

        internal void DrawInspector(VisualElement target)
        {
            if (selectedSheet == null || !sheets.TryGetValue(selectedSheet, out var sheet))
            {
                return;
            }
            target.Add(WorkbenchUI.Heading(sheet.Name));
            var description = new Label(sheet.Writable ? L.Tr("共享规则") : L.Tr("只读样式或未解析的引用"));
            description.style.whiteSpace = WhiteSpace.Normal;
            target.Add(description);
            if (selectedSelector == null)
            {
                DrawSheetInspector(target, sheet);
                return;
            }

            var rule = Rules(sheet).Where(candidate => candidate.Prelude == selectedSelector).Skip(selectedOccurrence).FirstOrDefault();
            if (rule == null)
            {
                target.Add(new Label(L.Tr("规则已变化，请重新选择。")));
                return;
            }
            var selectorField = new TextField(L.Tr("选择器"))
            {
                value = rule.Prelude,
                isDelayed = true
            };
            selectorField.SetEnabled(sheet.Writable);
            selectorField.RegisterValueChangedCallback(evt => AuthoringEnvironment.Try(() =>
            {
                string selector = RequireSelector(evt.newValue);
                var next = sheet.Text.Substring(0, rule.Start) + selector + " " + sheet.Text.Substring(rule.BodyStart - 1);
                string old = selectedSelector;
                int oldOccurrence = selectedOccurrence;
                selectedSelector = selector;
                selectedOccurrence = Rules(sheet).Count(candidate => candidate.Start < rule.Start && candidate.Prelude == selector);
                try
                {
                    EditSheet(sheet, next);
                }
                catch
                {
                    selectedSelector = old;
                    selectedOccurrence = oldOccurrence;
                    selectorField.SetValueWithoutNotify(rule.Prelude);
                    throw;
                }
            }));
            target.Add(selectorField);
            string body = sheet.Text.Substring(rule.BodyStart, rule.BodyEnd - rule.BodyStart);
            foreach (var declaration in CssSource.Declarations(body).GroupBy(item => item.Name).Select(items => items.Last()))
            {
                var field = new TextField(declaration.Name)
                {
                    value = body.Substring(declaration.ValueStart, declaration.ValueEnd - declaration.ValueStart),
                    isDelayed = true
                };
                field.SetEnabled(sheet.Writable);
                field.RegisterValueChangedCallback(evt => AuthoringEnvironment.Try(() => ChangeDeclaration(sheet, rule, declaration.Name, evt.newValue)));
                target.Add(field);
            }
            var fields = new VisualElement();
            fields.SetEnabled(sheet.Writable);
            var name = new TextField(L.Tr("属性"));
            var value = new TextField(L.Tr("值"));
            fields.Add(name);
            fields.Add(value);
            fields.Add(new Button(() => AuthoringEnvironment.Try(() => ChangeDeclaration(sheet, rule, name.value.Trim(), value.value))) { text = L.Tr("设置声明") });
            fields.Add(new Button(() => AuthoringEnvironment.Try(() =>
            {
                EditSheet(sheet, sheet.Text.Remove(rule.Start, rule.BodyEnd - rule.Start + 1));
                selectedSelector = null;
                selectedRuleChanged();
            }))
            {
                text = L.Tr("删除规则")
            });
            target.Add(fields);
        }

        void DrawSheetInspector(VisualElement target, Sheet sheet)
        {
            if (sheet.LinkIdentity != null)
            {
                var buttons = new Toolbar();
                buttons.Add(new ToolbarButton(() => ReorderLink(sheet, -1))
                {
                    text = "↑",
                    tooltip = L.Tr("提前样式表")
                });
                buttons.Add(new ToolbarButton(() => ReorderLink(sheet, 1))
                {
                    text = "↓",
                    tooltip = L.Tr("延后样式表")
                });
                buttons.Add(new ToolbarButton(() => AuthoringEnvironment.Try(() =>
                {
                    var author = AuthoringEnvironment.Author;
                    string link = author.ResolveIdentity(sheet.LinkIdentity);
                    if (link == null)
                    {
                        return;
                    }
                    SessionUndoBridge.Record(author, () => author.RemoveNodes(new[] { link }, author.Revision));
                }))
                {
                    text = L.Tr("移除引用")
                });
                target.Add(buttons);
            }

            if (!sheet.Writable)
            {
                return;
            }
            var selector = new TextField(L.Tr("选择器"));
            target.Add(selector);
            target.Add(new Button(() => AuthoringEnvironment.Try(() =>
            {
                string name = RequireSelector(selector.value);
                selectedSelector = name;
                selectedOccurrence = Rules(sheet).Count(rule => rule.Prelude == name);
                try
                {
                    EditSheet(sheet, sheet.Text + "\n" + name + " {\n}\n");
                }
                catch
                {
                    selectedSelector = null;
                    throw;
                }
            }))
            {
                text = L.Tr("添加规则")
            });
        }

        internal void AddExtraction(VisualElement inspector, ElementSelectionProxy proxy)
        {
            if (!proxy || proxy.IsLive || !NodePlacement.Editing || AuthoringEnvironment.SelectedNodes.Count != 1)
            {
                return;
            }
            string inline = proxy.Read("attr.style");
            var targets = sheets.Values.Where(sheet => sheet.Writable && sheet.LinkIdentity != null).GroupBy(sheet => sheet.Path).Select(group => group.First()).ToArray();
            if (string.IsNullOrWhiteSpace(inline) || targets.Length == 0)
            {
                return;
            }
            var fold = new Foldout
            {
                text = L.Tr("提取局部样式为 class"),
                value = false
            };
            var name = new TextField("class");
            var options = targets.Select(sheet => FileUtil.GetProjectRelativePath(sheet.Path)).ToList();
            var file = new DropdownField(L.Tr("目标样式表"), options, 0);
            fold.Add(file);
            fold.Add(name);
            fold.Add(new Label(inline) { style = { whiteSpace = WhiteSpace.Normal } });
            fold.Add(new Button(() => AuthoringEnvironment.Try(() =>
            {
                var sheet = targets[file.index];
                string className = name.value.Trim();
                if (!Regex.IsMatch(className, "^[A-Za-z_][A-Za-z0-9_-]*$"))
                {
                    throw new InvalidOperationException("Use an unescaped CSS class name.");
                }
                var author = AuthoringEnvironment.Author;
                var node = proxy.SourceNode;
                if (node == null || proxy.Read("attr.style") != inline)
                {
                    throw new InvalidOperationException("source_conflict: inline style changed");
                }
                if (Regex.IsMatch(inline, @"url\s*\(|!\s*important", RegexOptions.IgnoreCase))
                {
                    throw new InvalidOperationException(L.Tr("含相对资源或优先级标记的声明请手动迁移。"));
                }
                if (sheets.Values.Any(item => item.Text != null && Regex.IsMatch(item.Text, @"\." + Regex.Escape(className) + @"(?![\w-])")) ||
                    author.Model.Nodes.Any(item => Classes(item.Attribute("class")).Contains(className)))
                {
                    throw new InvalidOperationException(L.Tr("此 class 已存在。"));
                }
                var css = author.Workspace.Open(sheet.Path);
                string currentClass = node.Attribute("class");
                string AddClass(SourceDocument model)
                {
                    var candidate = SourceDocument.Parse(model.SetAttribute(node.Key, "class", string.IsNullOrWhiteSpace(currentClass) ? className : currentClass + " " + className));
                    return candidate.SetAttribute(node.Key, "style", null);
                }
                var changes = new[]
                {
                    new SourceChange(author.File.Path, AddClass(author.Model), author.File.Revision),
                    new SourceChange(css.Path, css.Text + "\n." + className + " { " + inline + " }\n", css.Revision)
                };
                long expected = author.Revision;
                if (!EditorUtility.DisplayDialog(L.Tr("提取局部样式为 class"), file.value + "\n." + className + " { " + inline + " }\n" + L.Tr("移到共享规则后，级联结果可能改变。"), L.Tr("应用"), L.Tr("取消")))
                {
                    return;
                }
                SessionUndoBridge.Record(author, () => author.EditWorkset(changes, expected, AddClass));
            }))
            {
                text = L.Tr("提取")
            });
            inspector.Add(fold);
        }

        static string[] Classes(string value) => (value ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        void AddSheet()
        {
            AuthoringEnvironment.Try(() =>
            {
                if (!NodePlacement.Editing)
                {
                    return;
                }
                string selected = EditorUtility.OpenFilePanel(L.Tr("引用样式表…"), Application.dataPath, "css");
                if (string.IsNullOrEmpty(selected))
                {
                    return;
                }
                var author = AuthoringEnvironment.Author;
                author.Workspace.CheckPath(selected);
                string source = AuthoringAssetPaths.Reference(author.Workspace.ProjectRoot, author.File.Path, selected);
                string uri = ResolveLink(author, source);
                if (author.Model.Nodes.Any(node => IsSheetLink(node) && ResolveLink(author, node.Attribute("href")) == uri))
                {
                    return;
                }
                var head = author.Model.Nodes.FirstOrDefault(node => node.Tag == "head");
                if (head == null)
                {
                    throw new InvalidOperationException("An HTML head is required for stylesheets.");
                }
                SessionUndoBridge.Record(author, () => author.InsertNode(author.NodeIdentity(head.Key), -1, "<link rel=\"stylesheet\" href=\"" + SourceDocument.Escape(source, '"') + "\">", author.Revision));
            });
        }

        static void ReorderLink(Sheet sheet, int direction)
        {
            AuthoringEnvironment.Try(() =>
            {
                var author = AuthoringEnvironment.Author;
                string key = author.ResolveIdentity(sheet.LinkIdentity);
                if (key == null)
                {
                    return;
                }
                var link = author.Model.Find(key);
                var children = link.Parent.Children.ToList();
                int oldIndex = children.IndexOf(link);
                for (int i = oldIndex + direction; i >= 0 && i < children.Count; i += direction)
                {
                    if (!IsSheetLink(children[i]) && children[i].Tag != "style")
                    {
                        continue;
                    }
                    var edit = new AuthoringEdit
                    {
                        Operation = "move",
                        Node = key,
                        Parent = link.Parent.Key,
                        Index = i + (direction > 0 ? 1 : 0)
                    };
                    SessionUndoBridge.Record(author, () => author.Execute(new[] { edit }, author.Revision));
                    return;
                }
            });
        }

        void ChangeDeclaration(Sheet sheet, CssSource.Rule rule, string name, string value) =>
            EditSheet(sheet, CssSource.SetRuleDeclaration(sheet.Text, rule.Start, name, value.Length == 0 ? null : value));

        void EditSheet(Sheet sheet, string text)
        {
            var author = AuthoringEnvironment.Author;
            if (!NodePlacement.Editing || !sheet.Writable || loadedRevision != author.Revision)
            {
                throw new InvalidOperationException("source_conflict: stylesheet selection changed");
            }
            long expected = loadedRevision;
            var file = author.Workspace.Open(sheet.Path);
            if (sheet.InlineIdentity == null)
            {
                if (file.Text != sheet.Text)
                {
                    throw new InvalidOperationException("source_conflict: stylesheet changed");
                }
                SessionUndoBridge.Record(author, () => author.EditFile(sheet.Path, text, expected));
                return;
            }

            string key = author.ResolveIdentity(sheet.InlineIdentity);
            if (key == null)
            {
                throw new InvalidOperationException("source_conflict: inline stylesheet was removed");
            }
            var original = author.Model.Find(key);
            if (file.Text.Substring(original.OpenEnd, original.CloseStart - original.OpenEnd) != sheet.Text)
            {
                throw new InvalidOperationException("source_conflict: inline stylesheet changed");
            }
            string ReplaceInline(SourceDocument model)
            {
                var node = model.Find(key);
                return model.Text.Substring(0, node.OpenEnd) + text + model.Text.Substring(node.CloseStart);
            }
            var change = new SourceChange(file.Path, ReplaceInline(author.Model), file.Revision);
            SessionUndoBridge.Record(author, () => author.EditWorkset(new[] { change }, expected, ReplaceInline));
        }

        static IEnumerable<CssSource.Rule> Rules(Sheet sheet)
        {
            if (sheet.Text == null)
            {
                return Array.Empty<CssSource.Rule>();
            }
            var rules = CssSource.Rules(sheet.Text);
            return rules.Where(rule => !rule.Prelude.StartsWith("@", StringComparison.Ordinal) &&
                !rules.Any(parent => parent.Start < rule.Start && parent.BodyEnd > rule.BodyEnd &&
                    parent.Prelude.IndexOf("keyframes", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        static string RequireSelector(string selector)
        {
            string value = (selector ?? "").Trim();
            if (value.Length == 0 || value[0] == '@' || value.IndexOfAny(new[] { '{', '}', ';' }) != -1)
            {
                throw new InvalidOperationException("Enter a CSS selector.");
            }
            return value;
        }

        static bool IsSheetLink(SourceDocument.Node node) => node.Tag == "link" &&
            string.Equals(node.Attribute("rel"), "stylesheet", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(node.Attribute("href"));

        static string ResolveLink(AuthoringSession author, string reference)
        {
            try
            {
                return URUI.Editor.Inspection.RuntimeInspectionBridge.ResolveResourceUrl(AuthoringAssetPaths.DocumentUri(author.Workspace.ProjectRoot, author.File.Path), reference);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
