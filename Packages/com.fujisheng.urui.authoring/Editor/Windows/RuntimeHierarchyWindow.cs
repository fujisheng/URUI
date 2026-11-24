using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine.UIElements;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Documents;
using URUI.Editor.Inspection;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "URUI.Authoring.Windows", sourceClassName: "UruiRuntimeHierarchyWindow")]
    internal sealed class RuntimeHierarchyWindow : EditorWindow
    {
        SearchField search;
        [SerializeField]
        TreeViewState treeState;
        [SerializeField]
        string filter = "";
        [SerializeField]
        bool fullStructure;
        HierarchyWindow.NodeTree tree;
        Document[] documents = Array.Empty<Document>();
        GUIContent[] documentNames = Array.Empty<GUIContent>();
        double nextRefresh;
        Document currentDocument;
        string currentRoot;
        ulong currentStamp;
        WorkbenchHierarchy hierarchy;
        WorkbenchStyleSheets styles;
        VisualElement authorView;
        IMGUIContainer liveView;
        Button layersTab;
        Button stylesTab;
        bool showStyles;
        internal static RuntimeHierarchyWindow Current
        {
            get;
            private set;
        }
        internal static event Action StyleSelectionChanged;
        internal WorkbenchStyleSheets StyleSheets => styles;

        void OnEnable()
        {
            search = new SearchField();
            treeState ??= new TreeViewState();
            tree = new HierarchyWindow.NodeTree(treeState, runtimeWindow: true)
            {
                ShowFullStructure = fullStructure,
                searchString = filter
            };
            Current = this;
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += RefreshView;
            AuthoringEnvironment.NodeSelectionChanged += FollowSelection;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.update += Tick;
            L.LanguageChanged += CreateGUI;
            L.Title(this, "URUI Hierarchy");
        }

        void OnDisable()
        {
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= RefreshView;
            AuthoringEnvironment.NodeSelectionChanged -= FollowSelection;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            EditorApplication.update -= Tick;
            L.LanguageChanged -= CreateGUI;
            if (Current == this)
            {
                Current = null;
                StyleSelectionChanged?.Invoke();
            }
        }

        void OnFocus() => WorkbenchUI.FocusWhenEmpty(this);

        public void CreateGUI()
        {
            var root = rootVisualElement;
            WorkbenchUI.BuildRoot(root);
            L.Title(this, "URUI Hierarchy");
            hierarchy = new WorkbenchHierarchy();
            styles = new WorkbenchStyleSheets(() => StyleSelectionChanged?.Invoke());
            var left = new VisualElement { name = "urui-navigation" };
            left.AddToClassList("urui-navigation");
            var navigation = new VisualElement();
            navigation.AddToClassList("urui-navigation-body");
            var navTabs = new VisualElement();
            navTabs.AddToClassList("urui-tabs");
            layersTab = NavigationTab("图层", false);
            stylesTab = NavigationTab("样式表", true);
            navTabs.Add(layersTab);
            navTabs.Add(stylesTab);
            navigation.Add(navTabs);
            navigation.Add(hierarchy);
            navigation.Add(styles);
            left.Add(navigation);

            authorView = left;
            authorView.style.flexGrow = 1;
            root.Add(authorView);
            liveView = new IMGUIContainer(DrawLive);
            liveView.style.flexGrow = 1;
            root.Add(liveView);
            root.UnregisterCallback<KeyDownEvent>(AuthoringWindow.HandleKeys, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(AuthoringWindow.HandleKeys, TrickleDown.TrickleDown);
            RefreshView();
            FollowSelection();
            StyleSelectionChanged?.Invoke();
        }

        void PlayStateChanged(PlayModeStateChange state)
        {
            nextRefresh = 0;
            RefreshView();
            FollowSelection();
        }

        void RefreshView()
        {
            if (authorView == null)
            {
                return;
            }
            bool playing = EditorApplication.isPlaying;
            authorView.style.display = playing ? DisplayStyle.None : DisplayStyle.Flex;
            liveView.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            if (playing)
            {
                return;
            }
            hierarchy.Refresh();
            styles.Refresh();
            styles.SetEnabled(NodePlacement.Editing);
            RefreshNavigation();
        }

        Button NavigationTab(string title, bool stylesTab)
        {
            var button = new Button(() =>
            {
                showStyles = stylesTab;
                RefreshNavigation();
                if (!showStyles)
                {
                    hierarchy.Reveal();
                }
            })
            {
                text = L.Tr(title)
            };
            button.AddToClassList("urui-tab");
            return button;
        }

        void RefreshNavigation()
        {
            hierarchy.style.display = showStyles ? DisplayStyle.None : DisplayStyle.Flex;
            styles.style.display = showStyles ? DisplayStyle.Flex : DisplayStyle.None;
            layersTab.EnableInClassList("urui-active-tab", !showStyles);
            stylesTab.EnableInClassList("urui-active-tab", showStyles);
        }

        static bool SameDocuments(Document[] a, Document[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        void Tick()
        {
            if (!EditorApplication.isPlaying || AuthoringEnvironment.Live == null || EditorApplication.timeSinceStartup < nextRefresh)
            {
                return;
            }
            nextRefresh = EditorApplication.timeSinceStartup + .5;
            var latest = AuthoringEnvironment.Documents();
            bool docsChanged = !SameDocuments(documents, latest);
            if (docsChanged)
            {
                documents = latest;
                documentNames = Array.ConvertAll(documents, doc =>
                    new GUIContent(string.IsNullOrEmpty(doc.SourceUrl) ? doc.name : doc.name + " · " + doc.SourceUrl, doc.SourceUrl));
            }
            var active = AuthoringEnvironment.LiveDocument;
            if (Array.IndexOf(documents, active) < 0)
            {
                AuthoringEnvironment.SetDocument(documents.Length == 0 ? null : documents[0]);
            }
            active = AuthoringEnvironment.LiveDocument;
            string root = active && active.IsLoaded ? RuntimeInspectionBridge.Root(active)?.Id : null;
            ulong stamp = RuntimeInspectionBridge.InspectionStamp(active);
            if (currentDocument != active || currentRoot != root)
            {
                currentDocument = active;
                currentRoot = root;
                currentStamp = stamp;
                tree.ResetRuntimeItems();
                AuthoringEnvironment.SetNodeSelection(AuthoringEnvironment.SelectedNodes, clearUnitySelection: false);
                FollowSelection();
                liveView?.MarkDirtyRepaint();
                Repaint();
            }
            else if (docsChanged || stamp != currentStamp || AuthoringEnvironment.ActiveNode is { IsLive: true, IsTargetValid: false })
            {
                currentStamp = stamp;
                if (AuthoringEnvironment.ActiveNode is { IsLive: true, IsTargetValid: false })
                {
                    AuthoringEnvironment.SetNodeSelection(AuthoringEnvironment.SelectedNodes, clearUnitySelection: false);
                }
                tree.Reload();
                liveView?.MarkDirtyRepaint();
                Repaint();
            }
        }

        void FollowSelection()
        {
            if (!EditorApplication.isPlaying)
            {
                hierarchy?.Reveal();
                if (AuthoringEnvironment.ActiveNode && styles?.HasSelection == true)
                {
                    styles.ClearSelection();
                    StyleSelectionChanged?.Invoke();
                }
                return;
            }
            if (AuthoringEnvironment.ActiveNode is { IsLive: true, IsTargetValid: true } proxy)
            {
                tree.Reveal(proxy);
                filter = tree.searchString;
            }
            else
            {
                tree.SetSelection(Array.Empty<int>());
            }
            liveView?.MarkDirtyRepaint();
            Repaint();
        }

        void DrawLive()
        {
            if (!EditorApplication.isPlaying || AuthoringEnvironment.Live == null)
            {
                EditorGUILayout.HelpBox(L.Tr("运行调试需要进入 Unity Play Mode。"), MessageType.Info);
                return;
            }
            int selected = Array.IndexOf(documents, AuthoringEnvironment.LiveDocument);
            if (selected < 0)
            {
                EditorGUILayout.HelpBox(L.Tr("当前没有已加载的 URUI 页面。"), MessageType.Info);
                return;
            }
            int next;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(L.Tr("Document"), GUILayout.ExpandWidth(false));
                next = EditorGUILayout.Popup(selected, documentNames);
            }
            if (next != selected && documents[next] && documents[next].IsLoaded)
            {
                AuthoringEnvironment.SetDocument(documents[next]);
                nextRefresh = 0;
            }
            tree.DrawToolbar();
            fullStructure = tree.ShowFullStructure;
            var evt = Event.current;
            bool selectMatch = search.HasFocus() && evt.type == EventType.KeyDown &&
                evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter;
            filter = search.OnToolbarGUI(filter);
            if (filter != tree.searchString)
            {
                tree.searchString = filter;
            }
            if (selectMatch && tree.SelectFirstMatch() && evt.type == EventType.KeyDown)
            {
                evt.Use();
            }
            tree.OnGUI(GUILayoutUtility.GetRect(0, 10000, 0, 10000, GUILayout.ExpandHeight(true)));
        }
    }
}
