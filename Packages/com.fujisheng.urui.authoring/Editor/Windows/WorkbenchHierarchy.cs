using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using URUI.Authoring.Presentation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    internal sealed class WorkbenchHierarchy : VisualElement
    {
        sealed class Item
        {
            internal string Identity;
            internal string Label;
            internal string Hint;
            internal int Id;
            internal SourceDocument.Node Node
            {
                get
                {
                    var author = AuthoringEnvironment.Author;
                    string key = author.ResolveIdentity(Identity);
                    return key == null ? null : author.Model.Find(key);
                }
            }
        }

        readonly TreeView tree = new TreeView
        {
            name = "urui-node-tree",
            selectionType = SelectionType.Multiple,
            fixedItemHeight = 25,
            viewDataKey = "urui-authoring-tree"
        };
        readonly Dictionary<string, int> rowIds = new Dictionary<string, int>();
        readonly Dictionary<string, Item> visible = new Dictionary<string, Item>();
        readonly Label noLayers = new Label(L.Tr("未找到匹配的图层"));
        string query = "";
        string path;
        long structure = -1;
        long revision = -1;
        bool rebuilding;
        bool showMetadata;
        int nextId;
        Label dropRow;

        internal WorkbenchHierarchy()
        {
            style.flexGrow = 1;
            style.minHeight = 105;
            var toolbar = new VisualElement();
            toolbar.AddToClassList("urui-layers-options");
            var full = new Toggle(L.Tr("完整结构"));
            full.RegisterValueChangedCallback(evt =>
            {
                showMetadata = evt.newValue;
                Rebuild();
            });
            toolbar.Add(full);
            var search = new UnityEditor.UIElements.ToolbarSearchField
            {
                name = "urui-node-search",
                tooltip = L.Tr("搜索节点")
            };
            search.AddToClassList("urui-search");
            WorkbenchUI.SetPlaceholder(search, L.Tr("搜索图层…"));
            search.RegisterValueChangedCallback(evt =>
            {
                query = evt.newValue.Trim();
                Rebuild();
            });
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter) || query.Length == 0)
                {
                    return;
                }
                var match = visible.Values.FirstOrDefault()?.Node;
                if (match == null)
                {
                    return;
                }
                AuthoringEnvironment.Select(ElementSelectionProxy.ForAuthor(match.Key));
                tree.Focus();
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            Add(search);
            Add(toolbar);
            tree.AddToClassList("urui-layer-tree");
            tree.style.flexGrow = 1;
            tree.makeItem = MakeRow;
            tree.bindItem = (row, i) =>
            {
                var item = tree.GetItemDataForIndex<Item>(i);
                var label = (Label)row;
                label.userData = item;
                label.text = item.Label;
                label.tooltip = item.Hint;
            };
            tree.selectionChanged += SelectItems;
            tree.RegisterCallback<ValidateCommandEvent>(evt =>
            {
                if (evt.commandName == "Copy" || evt.commandName == "Paste" || evt.commandName == "Delete" || evt.commandName == "SoftDelete")
                {
                    evt.StopPropagation();
                }
            });
            tree.RegisterCallback<ExecuteCommandEvent>(evt =>
            {
                if (RunCommand(evt.commandName))
                {
                    evt.StopPropagation();
                }
            });
            tree.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.commandKey || evt.ctrlKey) && evt.keyCode == KeyCode.D && RunCommand("Duplicate"))
                {
                    evt.StopImmediatePropagation();
                    return;
                }
                if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
                {
                    if (RunCommand("Delete"))
                    {
                        evt.StopPropagation();
                    }
                }
            });
            Add(tree);
            noLayers.AddToClassList("urui-empty");
            noLayers.style.display = DisplayStyle.None;
            Add(noLayers);
        }

        internal void Refresh()
        {
            var author = AuthoringEnvironment.Author;
            if (path != author.File?.Path)
            {
                path = author.File?.Path;
                rowIds.Clear();
                nextId = 0;
                Rebuild();
            }
            else if (structure != author.StructureRevision)
            {
                Rebuild();
            }
            else if (revision != author.Revision)
            {
                revision = author.Revision;
                if (author.PropertyEditsRevision == revision && author.LastPropertyEdits?.Any(edit =>
                    edit.Property == "text" || edit.Property == "attr.class" || edit.Property == "attr.id" || edit.Property == "attr.type") == true)
                {
                    Rebuild();
                }
            }
        }

        void Rebuild()
        {
            var author = AuthoringEnvironment.Author;
            structure = author.StructureRevision;
            revision = author.Revision;
            visible.Clear();
            var items = new List<TreeViewItemData<Item>>();
            if (author.Model != null)
            {
                foreach (var node in author.Model.Roots)
                {
                    AddNode(items, node, author.Model);
                }
            }
            if (rowIds.Count > visible.Count + 128)
            {
                List<string> stale = null;
                foreach (var key in rowIds.Keys)
                {
                    if (!visible.ContainsKey(key) && author.ResolveIdentity(key) == null)
                    {
                        (stale ??= new List<string>()).Add(key);
                    }
                }
                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        rowIds.Remove(stale[i]);
                    }
                }
            }
            rebuilding = true;
            try
            {
                bool empty = query.Length != 0 && items.Count == 0;
                noLayers.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
                tree.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
                tree.SetRootItems(items);
                tree.Rebuild();
                foreach (var root in items)
                {
                    tree.ExpandItem(root.id);
                }
                Reveal(false);
            }
            finally
            {
                rebuilding = false;
            }
        }

        void AddNode(List<TreeViewItemData<Item>> rows, SourceDocument.Node node, SourceDocument model)
        {
            bool full = showMetadata || query.Length > 0;
            if (!full && node.Tag == "html")
            {
                foreach (var child in node.Children)
                {
                    AddNode(rows, child, model);
                }
                return;
            }
            if (!NodePresentation.Visible(node.Tag, node.Attribute("id"), full) && !node.Children.Any(HasNamedNode))
            {
                return;
            }

            string id = node.Attribute("id");
            string classes = node.Attribute("class");
            string text = !node.IsVoid && node.Children.Count == 0 && !NodePresentation.IsMetadata(node.Tag)
                ? System.Net.WebUtility.HtmlDecode(model.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd)).Trim() : "";
            string type = L.Tr(NodePresentation.TypeLabel(node.Tag, node.Attribute("type")));
            var children = new List<TreeViewItemData<Item>>();
            if (query.Length == 0)
            {
                foreach (var child in node.Children)
                {
                    AddNode(children, child, model);
                }
                AddRow(rows, children, node, id, classes, type, text);
                return;
            }
            if (Matches(id, classes, node.Tag, type, text))
            {
                AddRow(rows, children, node, id, classes, type, text);
            }
            foreach (var child in node.Children)
            {
                AddNode(rows, child, model);
            }
        }

        bool Matches(string id, string classes, string tag, string type, string text)
        {
            if (query.StartsWith("#", StringComparison.Ordinal))
            {
                return id?.IndexOf(query.Substring(1), StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (query.StartsWith(".", StringComparison.Ordinal))
            {
                return classes?.IndexOf(query.Substring(1), StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return new[] { id, classes, tag, type, text }.Any(value => value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        void AddRow(List<TreeViewItemData<Item>> rows, List<TreeViewItemData<Item>> children, SourceDocument.Node node,
            string id, string classes, string type, string text)
        {
            string identity = AuthoringEnvironment.Author.NodeIdentity(node.Key);
            if (!rowIds.TryGetValue(identity, out int treeId))
            {
                rowIds.Add(identity, treeId = ++nextId);
            }
            string title = WorkbenchUI.NodeTitle(node.Tag, id, classes, text, node.Attribute("type"));
            var ancestors = new Stack<string>();
            for (var parent = node.Parent; parent != null; parent = parent.Parent)
            {
                ancestors.Push(parent.Attribute("id") ?? parent.Attribute("class") ?? parent.Tag);
            }
            string hint = type + "  ·  <" + node.Tag + ">\n" + title;
            if (!string.IsNullOrEmpty(id))
            {
                hint += "\n#" + id;
            }
            if (!string.IsNullOrEmpty(classes))
            {
                hint += "\n." + classes.Replace(" ", " .");
            }
            if (ancestors.Count != 0)
            {
                hint += "\n" + string.Join(" › ", ancestors);
            }
            string label = WorkbenchUI.Glyph(node.Tag == "input" ? node.Attribute("type") ?? node.Tag : node.Tag) + "   " + title;
            var item = new Item
            {
                Identity = identity,
                Id = treeId,
                Label = label,
                Hint = hint
            };
            visible[identity] = item;
            rows.Add(new TreeViewItemData<Item>(treeId, item, children));
        }

        static bool HasNamedNode(SourceDocument.Node node) =>
            !string.IsNullOrEmpty(node.Attribute("id")) || node.Children.Any(HasNamedNode);

        void SelectItems(IEnumerable<object> items)
        {
            if (rebuilding)
            {
                return;
            }
            AuthoringEnvironment.SetNodeSelection(items.OfType<Item>().Select(item => item.Node).Where(node => node != null)
                .Select(node => ElementSelectionProxy.ForAuthor(node.Key)));
        }

        internal void Reveal(bool scroll = true)
        {
            var proxies = AuthoringEnvironment.SelectedNodes.Where(proxy => proxy && !proxy.IsLive && proxy.IsTargetValid).ToArray();
            if (scroll && proxies.Length != 0 && !visible.ContainsKey(AuthoringEnvironment.Author.NodeIdentity(proxies[0].SourceKey)))
            {
                if (query.Length != 0)
                {
                    this.Q<UnityEditor.UIElements.ToolbarSearchField>().SetValueWithoutNotify("");
                    query = "";
                }
                else
                {
                    showMetadata = true;
                    this.Q<Toggle>().SetValueWithoutNotify(true);
                }
                Rebuild();
            }
            var ids = new List<int>();
            foreach (var proxy in proxies)
            {
                var node = proxy.SourceNode;
                for (var parent = node.Parent; parent != null; parent = parent.Parent)
                {
                    if (visible.TryGetValue(AuthoringEnvironment.Author.NodeIdentity(parent.Key), out var branch))
                    {
                        tree.ExpandItem(branch.Id);
                    }
                }
                if (visible.TryGetValue(AuthoringEnvironment.Author.NodeIdentity(node.Key), out var item))
                {
                    ids.Add(item.Id);
                }
            }
            tree.SetSelectionByIdWithoutNotify(ids);
            if (scroll && ids.Count > 0)
            {
                tree.ScrollToItemById(ids[0]);
            }
        }

        Label MakeRow()
        {
            var label = new Label();
            label.AddToClassList("urui-layer-name");
            label.style.flexGrow = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            WorkbenchUI.EnableDrag(label, () => label.userData is Item item && item.Node is { } node && node.Tag != "body" && node.Tag != "html"
                ? new WorkbenchDrag(identity: item.Identity) : null, click: () =>
                {
                    if (Selection.count != 0 && label.userData is Item selected && tree.selectedIds.Contains(selected.Id))
                    {
                        SelectItems(tree.selectedItems);
                    }
                });
            label.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (!(label.userData is Item item) || item.Node == null)
                {
                    return;
                }
                var proxy = ElementSelectionProxy.ForAuthor(item.Node.Key);
                evt.menu.AppendAction(L.Tr("创建子节点…"), _ =>
                {
                    AuthoringEnvironment.Select(proxy);
                    HierarchyWindow.ShowCreateMenu();
                }, _ => item.Node.IsVoid ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction(L.Tr("删除"), _ =>
                {
                    AuthoringEnvironment.Select(proxy);
                    RunCommand("Delete");
                }, _ => item.Node.Tag == "body" || item.Node.Tag == "html" ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            }));
            label.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var placement = RowPlacement(label, evt.localMousePosition.y, out int zone);
                var drag = WorkbenchDrag.Current;
                if (drag == null)
                {
                    return;
                }
                ClearDrop();
                if (drag.CanDrop(placement))
                {
                    MarkDrop(label, zone);
                }
                DragAndDrop.visualMode = drag.CanDrop(placement) ? (drag.Identity == null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Move) : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });
            label.RegisterCallback<DragLeaveEvent>(_ => ClearDrop());
            label.RegisterCallback<DragExitedEvent>(_ => ClearDrop());
            label.RegisterCallback<DragPerformEvent>(evt =>
            {
                var drag = WorkbenchDrag.Current;
                ClearDrop();
                if (drag == null)
                {
                    return;
                }
                var placement = RowPlacement(label, evt.localMousePosition.y, out _);
                AuthoringEnvironment.Try(() => drag.Drop(placement));
                evt.StopPropagation();
            });
            return label;
        }

        static NodePlacement RowPlacement(Label label, float y, out int zone)
        {
            var node = (label.userData as Item)?.Node;
            float height = label.layout.height;
            zone = y < height * .25f ? -1 : y > height * .75f ? 1 : 0;
            if (node == null)
            {
                return default;
            }
            if (zone == 0 && node.IsVoid)
            {
                zone = y < height / 2 ? -1 : 1;
            }
            return zone == 0 ? NodePlacement.ChildOf(node) : NodePlacement.Beside(node, zone > 0);
        }

        void MarkDrop(Label label, int zone)
        {
            dropRow = label;
            label.style.backgroundColor = new Color(.20f, .48f, .65f, .6f);
            label.style.borderTopWidth = zone < 0 ? 2 : 0;
            label.style.borderBottomWidth = zone > 0 ? 2 : 0;
            label.style.borderTopColor = label.style.borderBottomColor = new Color(.45f, .82f, .99f);
        }

        void ClearDrop()
        {
            if (dropRow == null)
            {
                return;
            }
            dropRow.style.backgroundColor = StyleKeyword.Null;
            dropRow.style.borderTopWidth = 0;
            dropRow.style.borderBottomWidth = 0;
            dropRow = null;
        }

        bool RunCommand(string command)
        {
            if (!NodePlacement.Editing || AuthoringEnvironment.ActiveNode?.SourceNode is not SourceDocument.Node node)
            {
                return false;
            }
            var author = AuthoringEnvironment.Author;
            var selected = AuthoringEnvironment.SelectedNodes
                .Where(proxy => proxy.SourceNode is { Tag: not ("body" or "html" or "head") })
                .Select(proxy => proxy.SourceKey).ToArray();
            switch (command)
            {
                case "Copy":
                    if (selected.Length == 0)
                    {
                        return false;
                    }
                    EditorGUIUtility.systemCopyBuffer = author.CopyNodes(selected);
                    return true;
                case "Paste":
                    string fragment = EditorGUIUtility.systemCopyBuffer;
                    var destination = NodePlacement.AcceptsChildren(node) ? NodePlacement.ChildOf(node) : NodePlacement.Beside(node, true);
                    AuthoringEnvironment.Try(() => destination.Paste(fragment));
                    return true;
                case "Duplicate":
                    if (selected.Length == 0)
                    {
                        return false;
                    }
                    AuthoringEnvironment.Try(() => NodePlacement.Beside(node, true).Paste(author.CopyNodes(selected)));
                    return true;
                case "Delete":
                case "SoftDelete":
                    AuthoringEnvironment.Try(() => SessionUndoBridge.Record(author, () => author.RemoveNodes(selected, author.Revision)));
                    return true;
                default:
                    return false;
            }
        }
    }
}
