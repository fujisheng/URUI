using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Presentation;
using URUI.Documents;
using URUI.Editor.Inspection;
using URUI.Hosting;

namespace URUI.Authoring.Windows
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "URUI.Authoring.Windows", sourceClassName: "UruiHierarchyWindow")]
    public sealed class HierarchyWindow : EditorWindow
    {
        [SerializeField]
        TreeViewState treeState;
        NodeTree tree;
        double lastReload;
        long structureRevision = -1;
        [InitializeOnLoadMethod]
        static void RegisterLocalizedMenus()
        {
            EditorApplication.delayCall += () =>
            {
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Hierarchy", 100, Open);
            };
        }
        [MenuItem("Window/URUI/Hierarchy")]
        public static void Open() => RuntimeDebugWindows.OpenHierarchy();
        [OnOpenAsset(1)]
        static bool OnOpenAsset(int instanceId, int line)
        {
            string path = AssetDatabase.GetAssetPath(instanceId);
            if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            AuthoringEnvironment.Try(() =>
            {
                if (AuthoringWindow.OpenPageWithConfirmation(path))
                {
                    AuthoringWindow.Open();
                }
            });
            return true;
        }
        void OnEnable()
        {
            treeState ??= new TreeViewState();
            tree = new NodeTree(treeState);
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += Refresh;
            AuthoringEnvironment.NodeSelectionChanged += FollowSelection;
            L.LanguageChanged += RefreshLanguage;
            FollowSelection();
        }
        void OnDisable()
        {
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= Refresh;
            AuthoringEnvironment.NodeSelectionChanged -= FollowSelection;
            L.LanguageChanged -= RefreshLanguage;
        }
        void RefreshLanguage()
        {
            tree?.Reload();
            Repaint();
        }
        void FollowSelection()
        {
            if (AuthoringEnvironment.ActiveNode is ElementSelectionProxy proxy)
            {
                tree?.Reveal(proxy);
            }
            Repaint();
        }
        void Refresh()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastReload > .25 || structureRevision != AuthoringEnvironment.Author.StructureRevision)
            {
                lastReload = now;
                structureRevision = AuthoringEnvironment.Author.StructureRevision;
                tree?.Reload();
            }
            Repaint();
        }
        void OnGUI()
        {
            L.Title(this, "URUI Hierarchy");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(EditorApplication.isPlaying ? L.Tr("LIVE · temporary") : L.Tr("AUTHORING"), EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L.Tr("Canvas"), EditorStyles.toolbarButton))
                {
                    CanvasWindow.Open();
                }
                if (GUILayout.Button(L.Tr("Open…"), EditorStyles.toolbarButton))
                {
                    string path = EditorUtility.OpenFilePanel(L.Tr("Open URUI HTML"), Application.dataPath, "html");
                    if (!string.IsNullOrEmpty(path))
                    {
                        AuthoringEnvironment.Try(() => AuthoringWindow.OpenPageWithConfirmation(path));
                    }
                }
            }
            if (EditorApplication.isPlaying)
            {
                var docs = AuthoringEnvironment.Documents();
                if (docs.Length == 0)
                {
                    EditorGUILayout.HelpBox(L.Tr("Select a loaded URUI Document or Surface in Unity Hierarchy."), MessageType.Info);
                }
                else
                {
                    int selected = Array.IndexOf(docs, AuthoringEnvironment.LiveDocument);
                    var names = new string[docs.Length];
                    for (int i = 0; i < names.Length; i++)
                    {
                        names[i] = docs[i].name + " · " + docs[i].SourceUrl;
                    }
                    int next = EditorGUILayout.Popup(L.Tr("Document"), Math.Max(0, selected), names);
                    if (selected < 0 || selected != next)
                    {
                        AuthoringEnvironment.SetDocument(docs[next]);
                    }
                }
            }
            else if (AuthoringEnvironment.Author.File != null)
            {
                EditorGUILayout.LabelField(System.IO.Path.GetFileName(AuthoringEnvironment.Author.File.Path) + (AuthoringEnvironment.Author.File.Dirty ? " *" : ""));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L.Tr("+ Node")))
                    {
                        ShowCreateMenu();
                    }
                    if (GUILayout.Button(L.Tr("Save")))
                    {
                        AuthoringEnvironment.Try(AuthoringEnvironment.Save);
                    }
                    if (GUILayout.Button(L.Tr("Reload")))
                    {
                        AuthoringEnvironment.Try(() => AuthoringEnvironment.Author.Reload(false));
                    }
                }
            }
            tree.DrawToolbar();
            string nextSearch = EditorGUILayout.TextField(L.Tr("搜索节点"), tree.searchString);
            if (nextSearch != tree.searchString)
            {
                tree.searchString = nextSearch;
            }
            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandHeight(true));
            tree.OnGUI(area);
            EditorGUILayout.LabelField(L.Message(AuthoringEnvironment.Status), EditorStyles.wordWrappedMiniLabel);
            HandleKeys();
        }
        internal static void ShowCreateMenu()
        {
            var menu = new GenericMenu();
            string[] kinds = { "panel", "image", "text", "button", "input", "toggle", "slider", "progress", "particles" };
            string[] labels = { L.Tr("容器 Panel"), L.Tr("图片 Image"), L.Tr("文字 Text"), L.Tr("按钮 Button"), L.Tr("输入框 Input"), L.Tr("勾选框 Toggle"), L.Tr("滑动条 Slider"), L.Tr("进度条 Progress"), L.Tr("粒子 Particles") };
            for (int i = 0; i < kinds.Length; i++)
            {
                string kind = kinds[i];
                if (kind == "particles")
                {
                    bool available = false;
                    foreach (var extension in AuthoringEnvironment.Extensions)
                    {
                        if (extension.Id == "effect")
                        {
                            available = true;
                        }
                    }
                    if (!available)
                    {
                        continue;
                    }
                }
                menu.AddItem(new GUIContent(labels[i]), false, () => AuthoringEnvironment.Try(() => CreateNode(kind)));
            }
            menu.AddSeparator("");
            foreach (string entry in new[] { "div", "span", "img", "textarea", "select" })
            {
                string tag = entry;
                menu.AddItem(new GUIContent(L.Tr("HTML 标签/") + tag), false, () => AuthoringEnvironment.Try(() => CreateNode(tag)));
            }
            menu.ShowAsContext();
        }
        static void CreateNode(string tag)
        {
            var selected = AuthoringEnvironment.ActiveNode;
            NodePlacement.ChildOf(selected?.SourceNode).Insert(tag);
        }
        static void RenameNode(string key, string value)
        {
            var author = AuthoringEnvironment.Author;
            SessionUndoBridge.Record(author, () => author.Set(key, "attr.id", value, author.Revision));
        }
        static void MoveNode(string key, string parent, int index)
        {
            var author = AuthoringEnvironment.Author;
            SessionUndoBridge.Record(author, () => author.Execute(new[] { new AuthoringEdit {
Operation = "move",
Node = key,
Parent = parent,
Index = index
} }, author.Revision));
        }
        internal static void HandleKeys()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
            {
                return;
            }
            if ((evt.control || evt.command) && evt.keyCode == KeyCode.S && !EditorApplication.isPlaying)
            {
                AuthoringEnvironment.Try(AuthoringEnvironment.Save);
                evt.Use();
                return;
            }
            var proxy = AuthoringEnvironment.ActiveNode;
            if (!proxy || !proxy.IsTargetValid || proxy.IsLive)
            {
                return;
            }
            var author = AuthoringEnvironment.Author;
            var node = proxy.SourceNode;
            var keys = new List<string>();
            foreach (var obj in AuthoringEnvironment.SelectedNodes)
            {
                if (obj is ElementSelectionProxy selected && !selected.IsLive && selected.IsTargetValid)
                {
                    keys.Add(selected.SourceKey);
                }
            }
            if ((evt.control || evt.command) && evt.keyCode == KeyCode.C)
            {
                EditorGUIUtility.systemCopyBuffer = author.CopyNodes(keys);
                evt.Use();
            }
            else if ((evt.control || evt.command) && evt.keyCode == KeyCode.V)
            {
                AuthoringEnvironment.Try(() => SessionUndoBridge.Record(author, () => author.Execute(new[] { new AuthoringEdit {
Operation = "insert",
Parent = node.Key,
Value = EditorGUIUtility.systemCopyBuffer
} }, author.Revision)));
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                AuthoringEnvironment.Try(() => SessionUndoBridge.Record(author, () => author.RemoveNodes(keys, author.Revision)));
                evt.Use();
            }
        }
        internal sealed class NodeTree : TreeView
        {
            readonly Dictionary<string, int> ids = new Dictionary<string, int>();
            readonly Dictionary<int, string> sourceKeys = new Dictionary<int, string>();
            readonly Dictionary<int, InspectedNode> liveNodes = new Dictionary<int, InspectedNode>();
            readonly bool runtimeWindow;
            sealed class NodeItem : TreeViewItem
            {
                internal string Tooltip;
                internal string SearchText;
                internal string Binding;
                internal string Classes;
                internal NodeItem(int id, int depth, string tag, string name, string inputType = null, string classes = null, string text = null, bool runtime = false)
                    : base(id, depth, runtime ? WorkbenchUI.NodeTitle(tag, name, classes, text, inputType) : L.NodeName(tag, name))
                {
                    Binding = name;
                    Classes = classes;
                    SearchText = string.Join(" ", displayName, tag, name, classes, text);
                    Tooltip = "<" + tag + "> · " + L.Tr(NodePresentation.TypeLabel(tag, inputType)) + (string.IsNullOrEmpty(name) ? L.Tr("\n未设置绑定 id") : "\nid: " + name);
                    if (!string.IsNullOrEmpty(classes))
                    {
                        Tooltip += "\nclass: " + classes;
                    }
                    string iconName = tag == "img" ? "Texture2D Icon" : tag == "urui-particles" ? "ParticleSystem Icon" : tag == "span" || tag == "p" || tag == "label" ? "TextAsset Icon" : "RectTransform Icon";
                    icon = EditorGUIUtility.FindTexture(iconName) ?? EditorGUIUtility.ObjectContent(null, tag == "img" ? typeof(Texture2D) : tag == "span" || tag == "p" || tag == "label" ? typeof(TextAsset) : typeof(RectTransform)).image as Texture2D;
                }
            }
            internal bool ShowFullStructure
            {
                get;
                set;
            }
            internal void DrawToolbar()
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label(L.Tr("Hierarchy"), EditorStyles.miniBoldLabel);
                    GUILayout.FlexibleSpace();
                    bool full = GUILayout.Toggle(ShowFullStructure, new GUIContent(L.Tr("完整结构"), L.Tr("显示 HTML 外层和非界面文档节点。搜索时也会包含这些节点。")), EditorStyles.toolbarButton);
                    if (full != ShowFullStructure)
                    {
                        ShowFullStructure = full;
                        Reload();
                    }
                }
            }
            protected override void RowGUI(RowGUIArgs args)
            {
                base.RowGUI(args);
                if (args.item is NodeItem item)
                {
                    GUI.Label(args.rowRect, new GUIContent("", item.Tooltip));
                }
            }
            int nextId = 1;
            int visited;
            long sourceStructure = -1;
            string trackedSession;
            Document trackedDocument;
            bool autoExpand;
            bool syncing;
            readonly List<string> staleIdKeys = new List<string>();
            public NodeTree(TreeViewState state, bool runtimeWindow = false) : base(state)
            {
                this.runtimeWindow = runtimeWindow;
                showBorder = true;
                Reload();
            }

            internal void ResetRuntimeItems()
            {
                ids.Clear();
                nextId = 1;
                state.expandedIDs.Clear();
                SetSelection(Array.Empty<int>());
                Reload();
            }

            internal bool SelectFirstMatch()
            {
                var rows = GetRows();
                if (string.IsNullOrEmpty(searchString) || rows.Count == 0)
                {
                    return false;
                }
                var selection = new[] { rows[0].id };
                SetSelection(selection, TreeViewSelectionOptions.RevealAndFrame);
                SelectionChanged(selection);
                return true;
            }

            protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
            {
                if (!runtimeWindow || item is not NodeItem node || string.IsNullOrEmpty(search))
                {
                    return base.DoesItemMatchSearch(item, search);
                }
                if (search[0] == '#')
                {
                    return string.Equals(node.Binding, search.Substring(1), StringComparison.OrdinalIgnoreCase);
                }
                if (search[0] == '.')
                {
                    return Array.Exists(node.Classes?.Split((char[])null, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                                            name => name.Equals(search.Substring(1), StringComparison.OrdinalIgnoreCase));
                }
                return node.SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            int Id(string key)
            {
                if (!ids.TryGetValue(key, out int id))
                {
                    ids.Add(key, id = nextId++);
                }
                return id;
            }
            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem(0, -1, "Root") { children = new List<TreeViewItem>() };
                sourceKeys.Clear();
                liveNodes.Clear();
                visited = 0;
                if (EditorApplication.isPlaying)
                {
                    var doc = AuthoringEnvironment.LiveDocument;
                    var sessionId = AuthoringEnvironment.Live?.Id;
                    if (trackedSession != sessionId || trackedDocument != doc)
                    {
                        trackedSession = sessionId;
                        trackedDocument = doc;
                        ids.Clear();
                        nextId = 1;
                        state.expandedIDs.Clear();
                    }
                    if (doc && doc.IsLoaded && AuthoringEnvironment.Live != null)
                    {
                        AddLive(root, RuntimeInspectionBridge.Root(doc), 0);
                    }
                }
                else if (!runtimeWindow && AuthoringEnvironment.Author.Model != null)
                {
                    var authorKey = AuthoringEnvironment.Author.Id + ":" + AuthoringEnvironment.Author.File?.Path;
                    if (trackedSession != authorKey || trackedDocument != null)
                    {
                        trackedSession = authorKey;
                        trackedDocument = null;
                        ids.Clear();
                        nextId = 1;
                        state.expandedIDs.Clear();
                    }
                    autoExpand = sourceStructure != AuthoringEnvironment.Author.StructureRevision;
                    sourceStructure = AuthoringEnvironment.Author.StructureRevision;
                    foreach (var node in AuthoringEnvironment.Author.Model.Roots)
                    {
                        AddSource(root, node, 0);
                    }
                    autoExpand = false;
                }
                if (ids.Count > 256)
                {
                    staleIdKeys.Clear();
                    foreach (var pair in ids)
                    {
                        if (!liveNodes.ContainsKey(pair.Value) && !sourceKeys.ContainsKey(pair.Value))
                        {
                            staleIdKeys.Add(pair.Key);
                        }
                    }
                    for (int i = 0; i < staleIdKeys.Count; i++)
                    {
                        int deadId = ids[staleIdKeys[i]];
                        ids.Remove(staleIdKeys[i]);
                        state.expandedIDs.Remove(deadId);
                    }
                    staleIdKeys.Clear();
                }
                return root;
            }
            bool Full => ShowFullStructure || !string.IsNullOrEmpty(searchString);
            static bool HasNamedDescendant(SourceDocument.Node node)
            {
                foreach (var child in node.Children)
                {
                    if (!string.IsNullOrEmpty(child.Attribute("id")) || HasNamedDescendant(child))
                    {
                        return true;
                    }
                }
                return false;
            }
            void AddSource(TreeViewItem parent, SourceDocument.Node node, int depth)
            {
                if (++visited > 10000)
                {
                    return;
                }
                string name = node.Attribute("id");
                if (!NodePresentation.Visible(node.Tag, name, Full))
                {
                    if (node.Tag == "html")
                    {
                        foreach (var child in node.Children)
                        {
                            AddSource(parent, child, depth);
                        }
                        return;
                    }
                    if (!HasNamedDescendant(node))
                    {
                        return;
                    }
                }
                int id = Id(AuthoringEnvironment.Author.Id + "/" + AuthoringEnvironment.Author.NodeIdentity(node.Key));
                if (autoExpand && (node.Tag == "html" || node.Tag == "body") && !state.expandedIDs.Contains(id))
                {
                    state.expandedIDs.Add(id);
                }
                sourceKeys[id] = node.Key;
                var item = new NodeItem(id, depth, node.Tag, name, node.Attribute("type"));
                parent.AddChild(item);
                if (node.Children.Count == 0)
                {
                    return;
                }
                if (IsExpanded(id) || !string.IsNullOrEmpty(searchString))
                {
                    foreach (var child in node.Children)
                    {
                        AddSource(item, child, depth + 1);
                    }
                }
                else
                {
                    item.children = CreateChildListForCollapsedParent();
                }
            }
            static bool HasNamedDescendant(InspectedNode node, int depth = 0)
            {
                if (depth > 128)
                {
                    return false;
                }
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var child = node.Child(i);
                    if (!string.IsNullOrEmpty(child.Name) || HasNamedDescendant(child, depth + 1))
                    {
                        return true;
                    }
                }
                return false;
            }
            void AddLive(TreeViewItem parent, InspectedNode node, int depth)
            {
                if (node == null || !node.IsValid || ++visited > 10000)
                {
                    return;
                }
                if (!NodePresentation.Visible(node.Tag, node.Name, Full))
                {
                    if (node.Tag == "html")
                    {
                        for (int i = 0; i < node.ChildCount; i++)
                        {
                            AddLive(parent, node.Child(i), depth);
                        }
                        return;
                    }
                    if (!HasNamedDescendant(node))
                    {
                        return;
                    }
                }
                string key = AuthoringEnvironment.Live.Id + "/" + node.Id;
                bool expandBody = runtimeWindow && node.Tag == "body" && !ids.ContainsKey(key);
                int id = Id(key);
                if (expandBody)
                {
                    state.expandedIDs.Add(id);
                }
                liveNodes[id] = node;
                var item = runtimeWindow
                    ? new NodeItem(id, depth, node.Tag, node.Name, node.Read("attr.type"), node.Read("attr.class"),
                        string.IsNullOrEmpty(node.Name) ? node.Read("text") : null, runtime: true)
                    : new NodeItem(id, depth, node.Tag, node.Name);
                parent.AddChild(item);
                if (node.ChildCount == 0)
                {
                    return;
                }
                if (IsExpanded(id) || !string.IsNullOrEmpty(searchString))
                {
                    for (int i = 0; i < node.ChildCount; i++)
                    {
                        AddLive(item, node.Child(i), depth + 1);
                    }
                }
                else
                {
                    item.children = CreateChildListForCollapsedParent();
                }
            }
            protected override void ExpandedStateChanged() => Reload();

            protected override void SingleClickedItem(int id)
            {
                var selection = GetSelection();
                if (Selection.count != 0 && selection.Contains(id))
                {
                    SelectionChanged(selection);
                }
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                if (syncing)
                {
                    return;
                }
                var proxies = new List<ElementSelectionProxy>();
                foreach (int id in selectedIds)
                {
                    if (sourceKeys.TryGetValue(id, out var key))
                    {
                        proxies.Add(ElementSelectionProxy.ForAuthor(key));
                    }
                    else if (liveNodes.TryGetValue(id, out var node) && node.IsValid)
                    {
                        proxies.Add(ElementSelectionProxy.ForLive(node, AuthoringEnvironment.Live));
                    }
                }
                if (runtimeWindow || proxies.Count != 0)
                {
                    AuthoringEnvironment.SetNodeSelection(proxies);
                }
            }
            internal void Reveal(ElementSelectionProxy proxy)
            {
                if (!proxy.IsTargetValid || proxy.IsLive != EditorApplication.isPlaying)
                {
                    return;
                }
                string tag = proxy.IsLive ? proxy.Node.Tag : proxy.SourceNode.Tag;
                string name = proxy.IsLive ? proxy.Node.Name : proxy.SourceNode.Attribute("id");
                if (!NodePresentation.Visible(tag, name, false))
                {
                    ShowFullStructure = true;
                }
                syncing = true;
                try
                {
                    int selected;
                    if (proxy.IsLive)
                    {
                        AuthoringEnvironment.SetDocument(proxy.Node.Document);
                        for (var node = proxy.Node.Parent; node != null; node = node.Parent)
                        {
                            int id = Id(AuthoringEnvironment.Live.Id + "/" + node.Id);
                            if (!state.expandedIDs.Contains(id))
                            {
                                state.expandedIDs.Add(id);
                            }
                        }
                        selected = Id(AuthoringEnvironment.Live.Id + "/" + proxy.Node.Id);
                    }
                    else
                    {
                        string prefix = AuthoringEnvironment.Author.Id + "/";
                        for (var node = proxy.SourceNode.Parent; node != null; node = node.Parent)
                        {
                            int id = Id(prefix + AuthoringEnvironment.Author.NodeIdentity(node.Key));
                            if (!state.expandedIDs.Contains(id))
                            {
                                state.expandedIDs.Add(id);
                            }
                        }
                        selected = Id(prefix + AuthoringEnvironment.Author.NodeIdentity(proxy.SourceKey));
                    }
                    if (!string.IsNullOrEmpty(searchString))
                    {
                        bool visible = false;
                        foreach (var row in GetRows())
                        {
                            if (row.id == selected)
                            {
                                visible = true;
                            }
                        }
                        if (!visible)
                        {
                            searchString = "";
                        }
                    }
                    Reload();
                    SetSelection(new[] { selected }, TreeViewSelectionOptions.RevealAndFrame);
                }
                finally
                {
                    syncing = false;
                }
            }
            protected override void ContextClickedItem(int id)
            {
                SetSelection(new[] { id }, TreeViewSelectionOptions.FireSelectionChanged);
                var menu = new GenericMenu();
                if (sourceKeys.TryGetValue(id, out var key))
                {
                    string name = AuthoringEnvironment.Author.Model.Find(key).Attribute("id");
                    menu.AddItem(new GUIContent(L.Tr("复制绑定 id")), false, () => EditorGUIUtility.systemCopyBuffer = name ?? "");
                    menu.AddItem(new GUIContent(L.Tr("重命名绑定 id")), false, () => BeginRename(FindItem(id, rootItem)));
                    menu.AddItem(new GUIContent(L.Tr("创建子节点…")), false, ShowCreateMenu);
                }
                else if (liveNodes.TryGetValue(id, out var live))
                {
                    menu.AddItem(new GUIContent(L.Tr("复制绑定 id")), false, () => EditorGUIUtility.systemCopyBuffer = live.Name ?? "");
                }
                menu.AddItem(new GUIContent(L.Tr("显示完整结构")), ShowFullStructure, () =>
                {
                    ShowFullStructure = !ShowFullStructure;
                    Reload();
                });
                menu.ShowAsContext();
            }
            protected override bool CanRename(TreeViewItem item) => !EditorApplication.isPlaying && sourceKeys.ContainsKey(item.id);
            protected override void RenameEnded(RenameEndedArgs args)
            {
                if (args.acceptedRename)
                {
                    AuthoringEnvironment.Try(() =>
                                    {
                                        RenameNode(sourceKeys[args.itemID], args.newName);
                                    });
                }
            }
            protected override bool CanStartDrag(CanStartDragArgs args) => !EditorApplication.isPlaying && args.draggedItemIDs.Count == 1;
            protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData("URUI.SourceKey", sourceKeys[args.draggedItemIDs[0]]);
                DragAndDrop.SetGenericData("URUI.SourceRevision", AuthoringEnvironment.Author.Revision);
                DragAndDrop.StartDrag("Move URUI node");
            }
            protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
            {
                string key = DragAndDrop.GetGenericData("URUI.SourceKey") as string;
                if (!(DragAndDrop.GetGenericData("URUI.SourceRevision") is long revision) || revision != AuthoringEnvironment.Author.Revision)
                {
                    return DragAndDropVisualMode.Rejected;
                }
                if (key == null || args.parentItem == null || !sourceKeys.TryGetValue(args.parentItem.id, out var parent))
                {
                    return DragAndDropVisualMode.Rejected;
                }
                if (args.performDrop)
                {
                    AuthoringEnvironment.Try(() =>
                                    {
                                        int index = args.insertAtIndex;
                                        if (index >= 0 && args.parentItem.children != null)
                                        {
                                            // A filtered tree index is not a DOM index. Insert before the real visible sibling.
                                            if (index < args.parentItem.children.Count && sourceKeys.TryGetValue(args.parentItem.children[index].id, out var sibling))
                                            {
                                                var children = AuthoringEnvironment.Author.Model.Find(parent).Children;
                                                for (int i = 0; i < children.Count; i++)
                                                {
                                                    if (children[i].Key == sibling)
                                                    {
                                                        index = i;
                                                        break;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                index = -1;
                                            }
                                        }
                                        MoveNode(key, parent, index);
                                    });
                }
                return DragAndDropVisualMode.Move;
            }
        }
    }

    [CustomEditor(typeof(Document))]
    sealed class DocumentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                DrawDefaultInspector();
            }
            if (GUILayout.Button(L.Tr("View URUI Nodes")))
            {
                AuthoringEnvironment.SetDocument((Document)target);
                HierarchyWindow.Open();
            }
        }
    }
    [CustomEditor(typeof(Surface))]
    sealed class SurfaceInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button(L.Tr("View URUI Documents")))
            {
                HierarchyWindow.Open();
            }
        }
    }
}
