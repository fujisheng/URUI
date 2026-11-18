using L = URUI.Editor.Localization.EditorText;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Animation;

namespace URUI.Authoring.Windows
{
    /// <summary>布局、动画、运行调试的统一入口；节点复用独立树，选择同步原生 Inspector。</summary>
    public sealed class UruiEditorWindow : EditorWindow
    {
        CanvasView canvas;
        TimelineView timeline;
        UruiHierarchyWindow.NodeTree tree;
        TreeViewState treeState;
        [SerializeField] float treeWidth = 230;
        bool timelineExpanded = true;
        double lastTreeRefresh;
        [InitializeOnLoadMethod]
        static void RegisterLocalizedMenus()
        {
            EditorApplication.delayCall += () =>
            {
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Editor", 0, Open);
                URUI.Editor.Localization.EditorMenus.Register("Assets/URUI/Open Editor", 0, OpenAsset, CanOpenAsset);
            };
        }
        [MenuItem("Window/URUI/Editor", false, 0)]
        public static void Open()
        {
            var window = GetWindow<UruiEditorWindow>(L.Tr("URUI 编辑器"), typeof(SceneView));
            window.minSize = new Vector2(900, 600);
            window.Show();
        }
        public static void OpenAnimation()
        {
            Open();
            AuthoringEnvironment.SetMode(WorkspaceMode.Animation);
            GetWindow<UruiEditorWindow>().Repaint();
        }
        [MenuItem("Assets/URUI/Open Editor", false, 0)]
        static void OpenAsset()
        {
            AuthoringEnvironment.Try(() => { AuthoringEnvironment.Open(AssetDatabase.GetAssetPath(Selection.activeObject)); Open(); });
        }
        [MenuItem("Assets/URUI/Open Editor", true)]
        static bool CanOpenAsset() => AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".html", StringComparison.OrdinalIgnoreCase);
        void OnEnable()
        {
            canvas = new CanvasView(Repaint);
            timeline = new TimelineView(Repaint);
            treeState ??= new TreeViewState();
            tree = new UruiHierarchyWindow.NodeTree(treeState);
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += Refresh;
            Selection.selectionChanged += FollowSelection;
            L.LanguageChanged += RefreshLanguage;
            EditorApplication.update += Tick;
            FollowSelection();
        }
        void OnDisable()
        {
            AnimationEnvironment.Stop();
            AuthoringEnvironment.SetMode(WorkspaceMode.Layout);
            canvas?.Dispose();
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= Refresh;
            Selection.selectionChanged -= FollowSelection;
            L.LanguageChanged -= RefreshLanguage;
            EditorApplication.update -= Tick;
        }
        void OnLostFocus() => canvas?.CancelInput();
        void RefreshLanguage()
        {
            tree?.Reload();
            Repaint();
        }
        void FollowSelection()
        {
            if (Selection.activeObject is ElementSelectionProxy proxy)
                tree?.Reveal(proxy);
            Repaint();
        }
        void Refresh()
        {
            if (EditorApplication.timeSinceStartup - lastTreeRefresh > .2)
            {
                tree?.Reload();
                lastTreeRefresh = EditorApplication.timeSinceStartup;
            }
            Repaint();
        }
        void Tick()
        {
            if (AnimationEnvironment.Playing)
                Repaint();
        }
        void OnGUI()
        {
            L.Title(this, "URUI 编辑器");
            int mode = (int)AuthoringEnvironment.Mode;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(L.Tr("打开页面…"), EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    string path = EditorUtility.OpenFilePanel(L.Tr("打开 URUI 页面"), Application.dataPath, "html");
                    if (path.Length > 0)
                        AuthoringEnvironment.Try(() => AuthoringEnvironment.Open(path));
                }
                if (GUILayout.Button(L.Tr("保存"), EditorStyles.toolbarButton, GUILayout.Width(45)))
                    AuthoringEnvironment.Try(AuthoringEnvironment.Save);
                int next = GUILayout.Toolbar(mode, new[] { L.Tr("布局"), L.Tr("动画"), L.Tr("运行调试") }, EditorStyles.toolbarButton, GUILayout.Width(360));
                if (next != mode)
                {
                    canvas.CancelInput();
                    AuthoringEnvironment.SetMode((WorkspaceMode)next);
                    mode = next;
                }
                GUILayout.FlexibleSpace();
                int dirty = 0;
                foreach (var file in AuthoringEnvironment.Author.Workspace.Files)
                    if (file.Dirty)
                        dirty++;
                int assets = AuthoringEnvironment.PendingAssets().Count;
                GUILayout.Label(dirty == 0 && assets == 0 ? L.Tr("已保存") : dirty + L.Tr(" 个文件未保存 · ") + assets + L.Tr(" 个资产未应用"), EditorStyles.miniLabel);
                if (GUILayout.Button(L.Tr("Inspector"), EditorStyles.toolbarButton, GUILayout.Width(80)))
                    EditorApplication.ExecuteMenuItem("Window/General/Inspector");
            }
            if (AuthoringEnvironment.Author.File != null)
                EditorGUILayout.LabelField(Path.GetFileName(AuthoringEnvironment.Author.File.Path), EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(treeWidth)))
                {
                    if (EditorApplication.isPlaying)
                        DrawDocuments();
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || AuthoringEnvironment.Author.File == null))
                        if (GUILayout.Button(L.Tr("＋ 创建节点")))
                            UruiHierarchyWindow.ShowCreateMenu();
                    tree.DrawToolbar();
                    string next = EditorGUILayout.TextField(tree.searchString, EditorStyles.toolbarSearchField);
                    if (next != tree.searchString)
                        tree.searchString = next;
                    tree.OnGUI(GUILayoutUtility.GetRect(200, 10000, 100, 10000, GUILayout.ExpandHeight(true)));
                    UruiHierarchyWindow.HandleKeys();
                }
                DrawTreeSplitter();
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    if (mode == 2 || EditorApplication.isPlaying)
                    {
                        EditorGUILayout.HelpBox(L.Tr("运行 Unity 后，在此选择 Document 与节点，原生 Inspector 会展示属性。修改即时生效；退出 Play 后丢弃。画面在 Game View 查看。"), MessageType.Info);
                        if (!EditorApplication.isPlaying)
                            EditorGUILayout.HelpBox(L.Tr("运行调试需要进入 Unity Play Mode。"), MessageType.None);
                        GUILayout.FlexibleSpace();
                    }
                    else
                        canvas.Draw();
                }
            }
            if (mode == 1)
            {
                timelineExpanded = EditorGUILayout.Foldout(timelineExpanded, L.Tr("动画时间轴"), true);
                if (timelineExpanded)
                    timeline.Draw();
            }
            EditorGUILayout.LabelField(L.Message(AuthoringEnvironment.Status), EditorStyles.wordWrappedMiniLabel);
        }
        void DrawTreeSplitter()
        {
            Rect rect = GUILayoutUtility.GetRect(5, 5, 0, 10000, GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            int control = GUIUtility.GetControlID(FocusType.Passive);
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && rect.Contains(evt.mousePosition))
            {
                GUIUtility.hotControl = control;
                evt.Use();
            }
            if (GUIUtility.hotControl != control)
                return;
            if (evt.type == EventType.MouseDrag)
            {
                treeWidth = Mathf.Clamp(treeWidth + evt.delta.x, 170, Mathf.Min(420, position.width * .45f));
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                evt.Use();
            }
        }
        internal static void DrawDocuments()
        {
            var documents = AuthoringEnvironment.Documents();
            if (documents.Length == 0)
            {
                EditorGUILayout.HelpBox(L.Tr("当前没有已加载的 URUI 页面。"), MessageType.Info);
                return;
            }
            int selected = Array.IndexOf(documents, AuthoringEnvironment.LiveDocument);
            string[] names = Array.ConvertAll(documents, d => d.name + " · " + d.SourceUrl);
            int next = EditorGUILayout.Popup(Math.Max(0, selected), names);
            if (selected != next)
                AuthoringEnvironment.SetDocument(documents[next]);
        }
    }
}
