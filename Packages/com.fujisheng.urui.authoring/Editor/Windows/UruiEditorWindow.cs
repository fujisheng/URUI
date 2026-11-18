using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using URUI.Authoring.Animation;
using URUI.Authoring.Sessions;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    /// <summary>The authoring workbench and the standalone tools share the same authoring session.</summary>
    public sealed class UruiEditorWindow : EditorWindow
    {
        CanvasView canvas;
        TimelineView timeline;
        IMGUIContainer canvasPanel, timelinePanel;
        Label documentLabel, status;
        Button saveButton;
        WorkbenchCompileStatus compileStatus;
        ToolbarToggle libraryToggle, layoutToggle, motionToggle, sourceToggle;
        ToolbarButton undoButton, redoButton;
        TextField code;
        WorkbenchLibrary library;
        [SerializeField] bool showLibrary = true;
        bool cssPreview;
        long codeRevision = -1;
        bool observing;
        static UruiEditorWindow Current => UnityEngine.Resources.FindObjectsOfTypeAll<UruiEditorWindow>().FirstOrDefault();

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
            RuntimeDebugWindows.Open();
            var window = GetWindow<UruiEditorWindow>(L.Tr("URUI 编辑器"), typeof(SceneView));
            window.minSize = new Vector2(900, 550);
            window.Show();
        }

        public static void OpenAnimation()
        {
            Open();
            AuthoringEnvironment.SetMode(WorkspaceMode.Animation);
        }

        [MenuItem("Assets/URUI/Open Editor", false, 0)]
        static void OpenAsset()
        {
            AuthoringEnvironment.Try(() => { if (OpenPageWithConfirmation(AssetDatabase.GetAssetPath(Selection.activeObject))) Open(); });
        }

        [MenuItem("Assets/URUI/Open Editor", true)]
        static bool CanOpenAsset() => AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".html", StringComparison.OrdinalIgnoreCase);

        void OnEnable()
        {
            wantsMouseMove = true;
            canvas = new CanvasView(() => { canvasPanel?.MarkDirtyRepaint(); Repaint(); }, rememberView: true);
            timeline = new TimelineView(() => { timelinePanel?.MarkDirtyRepaint(); Repaint(); });
            AuthoringEnvironment.Observe(true);
            observing = true;
            AuthoringEnvironment.Changed += Refresh;
            AuthoringEnvironment.NodeSelectionChanged += SelectionChanged;
            UruiRuntimeHierarchyWindow.StyleSelectionChanged += RuleSelectionChanged;
            L.LanguageChanged += CreateGUI;
            EditorApplication.update += Tick;
            EditorApplication.projectChanged += ProjectChanged;
        }

        void OnDisable()
        {
            if (observing)
                AuthoringEnvironment.Observe(false);
            observing = false;
            AuthoringEnvironment.Changed -= Refresh;
            AuthoringEnvironment.NodeSelectionChanged -= SelectionChanged;
            UruiRuntimeHierarchyWindow.StyleSelectionChanged -= RuleSelectionChanged;
            L.LanguageChanged -= CreateGUI;
            EditorApplication.update -= Tick;
            EditorApplication.projectChanged -= ProjectChanged;
            AnimationEnvironment.Stop();
            AuthoringEnvironment.SetMode(WorkspaceMode.Layout);
            canvas?.Dispose();
        }

        void OnFocus() => WorkbenchUi.FocusWhenEmpty(this);

        void OnLostFocus() => canvas?.CancelInput();

        public void CreateGUI()
        {
            L.Title(this, "URUI 编辑器");
            var root = rootVisualElement;
            WorkbenchUi.BuildRoot(root);
            var toolbar = new Toolbar();
            toolbar.AddToClassList("urui-topbar");
            libraryToggle = new ToolbarToggle { name = "urui-toggle-library", text = L.Tr("控件库") };
            libraryToggle.RegisterValueChangedCallback(evt => SetLibraryOpen(evt.newValue));
            toolbar.Add(libraryToggle);
            toolbar.Add(new ToolbarButton(OpenPage) { text = L.Tr("打开页面…") });
            undoButton = new ToolbarButton(() => History(false)) { text = "↶", tooltip = L.Tr("撤销") };
            redoButton = new ToolbarButton(() => History(true)) { text = "↷", tooltip = L.Tr("重做") };
            toolbar.Add(undoButton);
            toolbar.Add(redoButton);
            documentLabel = new Label();
            documentLabel.AddToClassList("urui-document-title");
            toolbar.Add(documentLabel);
            layoutToggle = ModeTab(WorkspaceMode.Layout, "布局");
            motionToggle = ModeTab(WorkspaceMode.Animation, "动画");
            toolbar.Add(layoutToggle);
            toolbar.Add(motionToggle);
            toolbar.Add(new ToolbarButton(RuntimeDebugWindows.Open) { text = L.Tr("层级和属性") });
            sourceToggle = new ToolbarToggle { text = L.Tr("源码"), viewDataKey = "urui-show-source" };
            sourceToggle.RegisterValueChangedCallback(_ => Refresh());
            toolbar.Add(sourceToggle);
            saveButton = new Button(SavePage) { text = L.Tr("保存"), name = "urui-save" };
            saveButton.AddToClassList("urui-save-button");
            toolbar.Add(saveButton);
            root.Add(toolbar);

            canvasPanel = new IMGUIContainer(() => canvas.Draw()) { name = "urui-viewport" };
            canvasPanel.style.flexGrow = 1;
            canvasPanel.style.minHeight = 0;
            canvasPanel.style.minWidth = 0;
            var body = new VisualElement();
            body.AddToClassList("urui-workbench-body");
            library = new WorkbenchLibrary(() => SetLibraryOpen(false), HandleLibraryDrag) { name = "urui-library-drawer" };
            body.Add(library);
            body.Add(canvasPanel);
            root.Add(body);

            code = new TextField { multiline = true, isReadOnly = true, name = "urui-source-preview" };
            code.style.flexGrow = 1;
            code.style.minHeight = 0;
            code.style.height = 140;
            codeRevision = -1;
            var source = new VisualElement { name = "urui-source-pane" };
            var sourceBar = new Toolbar();
            sourceBar.Add(new ToolbarButton(() => { cssPreview = false; codeRevision = -1; RefreshCode(); }) { text = "HTML" });
            sourceBar.Add(new ToolbarButton(() => { cssPreview = true; codeRevision = -1; RefreshCode(); }) { text = "CSS" });
            sourceBar.Add(new ToolbarButton(OpenSourceAsset) { text = L.Tr("打开源文件") });
            source.Add(sourceBar);
            source.Add(code);
            source.style.display = DisplayStyle.None;
            timelinePanel = new IMGUIContainer(() => timeline.Draw());
            timelinePanel.style.maxHeight = 345;
            root.Add(source);
            root.Add(timelinePanel);
            status = new Label { name = "urui-status" };
            var footer = new VisualElement();
            footer.AddToClassList("urui-footer");
            footer.Add(status);
            compileStatus = new WorkbenchCompileStatus();
            footer.Add(compileStatus);
            root.Add(footer);
            root.UnregisterCallback<KeyDownEvent>(HandleKeys, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(HandleKeys, TrickleDown.TrickleDown);
            WorkbenchUi.FocusWhenEmpty(this);
            Refresh();
            SelectionChanged();
        }

        void SetLibraryOpen(bool open)
        {
            showLibrary = open;
            RefreshLibrary();
            if (open)
                library.FocusSearch();
            else
                libraryToggle.Focus();
        }

        bool HandleLibraryDrag(WorkbenchDrag drag, Vector2 panelPosition, bool drop)
        {
            bool inside = drag != null && rootVisualElement.worldBound.Contains(panelPosition);
            canvas.TrackNodeDrag(inside ? drag : null, canvasPanel.WorldToLocal(panelPosition), drop);
            return inside;
        }

        void RefreshLibrary()
        {
            bool layout = AuthoringEnvironment.Mode == WorkspaceMode.Layout && !EditorApplication.isPlayingOrWillChangePlaymode;
            bool expanded = layout && showLibrary;
            libraryToggle.style.display = layout ? DisplayStyle.Flex : DisplayStyle.None;
            libraryToggle.SetValueWithoutNotify(expanded);
            libraryToggle.tooltip = L.Tr(expanded ? "关闭控件库" : "打开控件库");
            library.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            if (expanded)
                library.Refresh();
        }

        ToolbarToggle ModeTab(WorkspaceMode mode, string text)
        {
            var toggle = new ToolbarToggle { text = L.Tr(text) };
            toggle.RegisterValueChangedCallback(_ =>
            {
                canvas.CancelInput();
                AuthoringEnvironment.SetMode(mode);
                Refresh();
            });
            return toggle;
        }

        internal static void HandleKeys(KeyDownEvent evt)
        {
            if (EditorApplication.isPlaying || evt.currentTarget is not VisualElement root)
                return;
            if (!evt.commandKey && !evt.ctrlKey)
            {
                if (!evt.altKey && !evt.shiftKey && evt.keyCode == KeyCode.F &&
                    !EditorGUIUtility.editingTextField && !FocusInTextField(root) && Current?.canvas?.FrameSelection() == true)
                    evt.StopImmediatePropagation();
                return;
            }
            if (evt.keyCode == KeyCode.S)
            {
                SavePage(root);
                evt.StopImmediatePropagation();
            }
            else if (evt.keyCode == KeyCode.Z && !EditorGUIUtility.editingTextField && !FocusInTextField(root))
            {
                History(evt.shiftKey);
                evt.StopImmediatePropagation();
            }
        }

        static bool FocusInTextField(VisualElement root)
        {
            for (var element = root.focusController?.focusedElement as VisualElement; element != null; element = element.parent)
                if (element is TextField || element.ClassListContains("unity-base-text-field"))
                    return true;
            return false;
        }

        static void History(bool redo)
        {
            Current?.canvas?.CancelInput();
            if (EditorApplication.isPlaying || (redo ? !AuthoringEnvironment.Author.Workspace.CanRedo : !AuthoringEnvironment.Author.Workspace.CanUndo))
                return;
            AuthoringEnvironment.Try(() =>
            {
                if (redo)
                    Undo.PerformRedo();
                else
                    Undo.PerformUndo();
            });
            Current?.Refresh();
        }

        void OpenPage()
        {
            string path = EditorUtility.OpenFilePanel(L.Tr("打开 URUI 页面"), Application.dataPath, "html");
            if (!string.IsNullOrEmpty(path))
                AuthoringEnvironment.Try(() => OpenPageWithConfirmation(path));
        }

        internal static bool OpenPageWithConfirmation(string path)
        {
            var author = AuthoringEnvironment.Author;
            path = author.Workspace.CheckPath(path);
            if (path == author.File?.Path)
                return true;
            bool discard = false;
            if (AuthoringEnvironment.HasUnsavedSources)
            {
                int choice = EditorUtility.DisplayDialogComplex(L.Tr("当前页面有未保存的修改"),
                    string.Format(L.Tr("打开 {0} 前，选择如何处理当前页面及关联文件。"), Path.GetFileName(path)),
                    L.Tr("保存并打开"), L.Tr("取消"), L.Tr("放弃修改并打开"));
                if (choice == 1)
                    return false;
                if (choice == 0)
                    AuthoringEnvironment.SaveSources();
                else
                    discard = true;
            }
            AuthoringEnvironment.Open(path, discard);
            return true;
        }

        void SavePage() => SavePage(rootVisualElement);

        static void SavePage(VisualElement root)
        {
            root.Focus();
            AuthoringEnvironment.Try(AuthoringEnvironment.Save);
            Current?.compileStatus?.Invalidate();
        }

        void ProjectChanged() => compileStatus?.Invalidate();

        void OpenSourceAsset()
        {
            var author = AuthoringEnvironment.Author;
            string path = cssPreview ? UruiRuntimeHierarchyWindow.Current?.StyleSheets?.SelectedPath ?? AuthoringEnvironment.Preview?.StylePaths.FirstOrDefault() : author.File?.Path;
            if (path == null)
                return;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(FileUtil.GetProjectRelativePath(path));
            if (asset)
                EditorUtility.OpenWithDefaultApp(AssetDatabase.GetAssetPath(asset));
        }

        void RefreshCode()
        {
            var author = AuthoringEnvironment.Author;
            if (code == null || codeRevision == author.Revision || !sourceToggle.value)
                return;
            codeRevision = author.Revision;
            code.SetValueWithoutNotify(cssPreview
                ? UruiRuntimeHierarchyWindow.Current?.StyleSheets?.PreviewText ?? string.Join("\n\n", AuthoringEnvironment.Preview?.StylePaths.Select(path => "/* " + Path.GetFileName(path) + " */\n" + author.Workspace.Open(path).Text) ?? Array.Empty<string>())
                : author.File?.Text ?? "");
        }

        void Refresh()
        {
            if (status == null)
                return;
            bool playing = EditorApplication.isPlaying;
            var author = AuthoringEnvironment.Author;
            var mode = AuthoringEnvironment.Mode;
            layoutToggle.SetValueWithoutNotify(!playing && mode == WorkspaceMode.Layout);
            motionToggle.SetValueWithoutNotify(mode == WorkspaceMode.Animation);
            RefreshLibrary();
            bool dirty = AuthoringEnvironment.HasUnsavedSources;
            saveButton.SetEnabled((!playing && (author.File != null || dirty)) || AuthoringEnvironment.PendingAssets().Count != 0);
            documentLabel.text = author.File == null ? L.Tr("未打开页面") : Path.GetFileName(author.File.Path) + (dirty ? "  ●" : "");
            documentLabel.tooltip = author.File?.Path;
            string sessionStatus = AuthoringEnvironment.Status;
            status.tooltip = L.Message(sessionStatus);
            status.text = sessionStatus.StartsWith("Authoring · /", StringComparison.Ordinal) ? L.Tr("作者模式") : status.tooltip;
            compileStatus?.Refresh();
            undoButton.SetEnabled(!playing && author.Workspace.CanUndo);
            redoButton.SetEnabled(!playing && author.Workspace.CanRedo);
            rootVisualElement.Q("urui-source-pane").style.display = sourceToggle.value ? DisplayStyle.Flex : DisplayStyle.None;
            timelinePanel.style.display = mode == WorkspaceMode.Animation ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshCode();
            canvasPanel.MarkDirtyRepaint();
            timelinePanel.MarkDirtyRepaint();
            Repaint();
        }

        void SelectionChanged()
        {
            library?.RefreshDestination();
            canvasPanel?.MarkDirtyRepaint();
            timelinePanel?.MarkDirtyRepaint();
            Repaint();
        }

        void RuleSelectionChanged()
        {
            codeRevision = -1;
            RefreshCode();
        }

        void Tick()
        {
            if (canvasPanel == null)
                return;
            if (AnimationEnvironment.Playing || AuthoringEnvironment.Preview?.Playing == true)
            {
                canvasPanel.MarkDirtyRepaint();
                timelinePanel.MarkDirtyRepaint();
                Repaint();
            }
        }

    }
}
