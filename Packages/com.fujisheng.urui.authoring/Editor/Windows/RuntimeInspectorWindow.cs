using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using URUI.Authoring.Sessions;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "URUI.Authoring.Windows", sourceClassName: "UruiRuntimeInspectorWindow")]
    internal sealed class RuntimeInspectorWindow : EditorWindow
    {
        ElementInspector inspector;
        ElementSelectionProxy[] inspected = Array.Empty<ElementSelectionProxy>();
        Vector2 scroll;
        double nextRefresh;
        VisualElement authorView;
        VisualElement inspectorHeader;
        VisualElement propertiesTabs;
        VisualElement emptyInspector;
        VisualElement ruleInspector;
        VisualElement extraction;
        IMGUIContainer inspectorPanel;
        IMGUIContainer liveView;
        ToolbarToggle basicProperties;
        ToolbarToggle advancedProperties;
        bool showAdvanced;
        long inspectorRevision = -1;

        void OnEnable()
        {
            AuthoringEnvironment.Observe(true);
            AuthoringEnvironment.Changed += RefreshView;
            AuthoringEnvironment.NodeSelectionChanged += SelectionChanged;
            RuntimeHierarchyWindow.StyleSelectionChanged += StyleSelectionChanged;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.update += Tick;
            L.LanguageChanged += CreateGUI;
            L.Title(this, "URUI Inspector");
        }

        void OnDisable()
        {
            AuthoringEnvironment.Observe(false);
            AuthoringEnvironment.Changed -= RefreshView;
            AuthoringEnvironment.NodeSelectionChanged -= SelectionChanged;
            RuntimeHierarchyWindow.StyleSelectionChanged -= StyleSelectionChanged;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            EditorApplication.update -= Tick;
            L.LanguageChanged -= CreateGUI;
            DestroyInspector();
        }

        void OnFocus() => WorkbenchUI.FocusWhenEmpty(this);

        public void CreateGUI()
        {
            var root = rootVisualElement;
            WorkbenchUI.BuildRoot(root);
            L.Title(this, "URUI Inspector");
            var right = new VisualElement { name = "urui-inspector" };
            right.AddToClassList("urui-inspector");
            inspectorHeader = new VisualElement();
            inspectorHeader.AddToClassList("urui-selection-header");
            right.Add(inspectorHeader);
            propertiesTabs = new VisualElement();
            propertiesTabs.AddToClassList("urui-tabs");
            basicProperties = PropertyTab("常用属性", false);
            advancedProperties = PropertyTab("全部属性", true);
            propertiesTabs.Add(basicProperties);
            propertiesTabs.Add(advancedProperties);
            right.Add(propertiesTabs);
            var inspectorScroll = new ScrollView();
            inspectorScroll.style.flexGrow = 1;
            emptyInspector = new Label(L.Tr("在画布或图层中选中控件，即可在这里编辑。"));
            emptyInspector.AddToClassList("urui-empty");
            inspectorScroll.Add(emptyInspector);
            inspectorPanel = new IMGUIContainer(DrawInspector);
            inspectorPanel.AddToClassList("urui-inspector-fields");
            inspectorScroll.Add(inspectorPanel);
            ruleInspector = new VisualElement();
            extraction = new VisualElement();
            inspectorRevision = -1;
            inspectorScroll.Add(ruleInspector);
            inspectorScroll.Add(extraction);
            right.Add(inspectorScroll);
            right.style.flexGrow = 1;
            authorView = right;
            root.Add(authorView);
            liveView = new IMGUIContainer(DrawLive) { name = "urui-live-inspector" };
            liveView.style.flexGrow = 1;
            root.Add(liveView);
            root.UnregisterCallback<KeyDownEvent>(AuthoringWindow.HandleKeys, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(AuthoringWindow.HandleKeys, TrickleDown.TrickleDown);
            RefreshView();
        }

        void StyleSelectionChanged()
        {
            inspectorRevision = -1;
            RefreshView();
        }

        void SelectionChanged()
        {
            RefreshView();
            if (EditorApplication.isPlaying)
            {
                liveView?.MarkDirtyRepaint();
            }
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
            if (!playing)
            {
                RefreshInspector();
            }
        }

        ToolbarToggle PropertyTab(string title, bool advanced)
        {
            var tab = new ToolbarToggle
            {
                text = L.Tr(title),
                name = advanced ? "urui-all-properties" : "urui-basic-properties"
            };
            tab.RegisterValueChangedCallback(_ =>
            {
                showAdvanced = advanced;
                RefreshInspector();
            });
            return tab;
        }

        void RefreshInspector()
        {
            if (inspectorPanel == null)
            {
                return;
            }
            var styles = RuntimeHierarchyWindow.Current?.StyleSheets;
            var targets = AuthoringEnvironment.SelectedNodes.Where(node => node && !node.IsLive && node.IsTargetValid).ToArray();
            bool selectionChanged = !inspected.SequenceEqual(targets);
            bool hasNode = targets.Length > 0;
            bool layout = AuthoringEnvironment.Mode == WorkspaceMode.Layout && !EditorApplication.isPlaying;
            inspectorPanel.style.display = hasNode ? DisplayStyle.Flex : DisplayStyle.None;
            emptyInspector.style.display = !hasNode && styles?.HasSelection != true ? DisplayStyle.Flex : DisplayStyle.None;
            propertiesTabs.style.display = hasNode && layout ? DisplayStyle.Flex : DisplayStyle.None;
            extraction.style.display = showAdvanced ? DisplayStyle.Flex : DisplayStyle.None;
            basicProperties.SetValueWithoutNotify(!showAdvanced);
            advancedProperties.SetValueWithoutNotify(showAdvanced);
            if (inspectorRevision != AuthoringEnvironment.Author.Revision || selectionChanged)
            {
                inspectorRevision = AuthoringEnvironment.Author.Revision;
                ruleInspector.Clear();
                extraction.Clear();
                if (styles?.HasSelection == true && targets.Length == 0)
                {
                    AuthoringEnvironment.Try(() => styles.DrawInspector(ruleInspector));
                }
                if (targets.Length == 1)
                {
                    styles?.AddExtraction(extraction, targets[0]);
                }
                RefreshInspectorHeader(targets, styles?.HasSelection == true);
            }
            SetTargets(targets, live: false);
            inspectorPanel.MarkDirtyRepaint();
        }

        void RefreshInspectorHeader(ElementSelectionProxy[] targets, bool styleSelected)
        {
            inspectorHeader.Clear();
            if (targets.Length > 1)
            {
                inspectorHeader.Add(WorkbenchUI.Heading(string.Format(L.Tr("已选中 {0} 个图层"), targets.Length)));
                return;
            }
            var target = targets.FirstOrDefault();
            string title = target
                ? WorkbenchUI.NodeTitle(target.SourceNode.Tag, target.Read("attr.id"), target.Read("attr.class"), target.Read("text"), target.Read("attr.type"))
                : L.Tr(styleSelected ? "样式表" : "属性");
            inspectorHeader.Add(WorkbenchUI.Heading(title));
            if (target && target.SourceNode?.Parent is { Tag: not "html" } parent)
            {
                var ancestor = ElementSelectionProxy.ForAuthor(parent.Key);
                var parentButton = new Button(() => AuthoringEnvironment.Select(ancestor))
                {
                    text = "↑  " + WorkbenchUI.NodeTitle(parent.Tag, parent.Attribute("id"), parent.Attribute("class"), null, parent.Attribute("type")),
                    tooltip = L.Tr("选择父级")
                };
                parentButton.AddToClassList("urui-parent");
                inspectorHeader.Add(parentButton);
            }
        }

        void DrawInspector()
        {
            if (inspector is ElementInspector element && !showAdvanced && AuthoringEnvironment.Mode == WorkspaceMode.Layout && !EditorApplication.isPlaying)
            {
                element.DrawWorkbenchProperties();
            }
            else if (inspector)
            {
                inspector.OnInspectorGUI();
            }
        }

        void PlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                DestroyInspector();
            }
            RefreshView();
            Repaint();
        }

        void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextRefresh)
            {
                return;
            }
            nextRefresh = EditorApplication.timeSinceStartup + .25;
            var hasLive = false;
            var selected = AuthoringEnvironment.SelectedNodes;
            for (var i = 0; i < selected.Count; i++)
            {
                var node = selected[i];
                if (node && node.IsLive && node.IsTargetValid)
                {
                    hasLive = true;
                    break;
                }
            }
            if (!hasLive && !inspector)
            {
                return;
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
            var targets = AuthoringEnvironment.SelectedNodes.Where(node => node && node.IsLive && node.IsTargetValid).ToArray();
            SetTargets(targets, live: true);
            if (!inspector)
            {
                EditorGUILayout.HelpBox(L.Tr("在运行时层级中选择一个节点。"), MessageType.Info);
                return;
            }
            DrawHistory();
            using (var area = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = area.scrollPosition;
                inspector.OnInspectorGUI();
            }
        }

        static void DrawHistory()
        {
            var session = AuthoringEnvironment.Live;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(L.Tr("LIVE · temporary"), EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!session.CanUndo))
                {
                    if (GUILayout.Button(new GUIContent("↶", L.Tr("撤销")), EditorStyles.toolbarButton))
                    {
                        AuthoringEnvironment.Try(Undo.PerformUndo);
                    }
                }
                using (new EditorGUI.DisabledScope(!session.CanRedo))
                {
                    if (GUILayout.Button(new GUIContent("↷", L.Tr("重做")), EditorStyles.toolbarButton))
                    {
                        AuthoringEnvironment.Try(Undo.PerformRedo);
                    }
                }
            }
        }

        void SetTargets(ElementSelectionProxy[] targets, bool live)
        {
            if (inspected.SequenceEqual(targets))
            {
                return;
            }
            DestroyInspector();
            inspected = targets;
            scroll = Vector2.zero;
            if (targets.Length == 0)
            {
                return;
            }
            inspector = (ElementInspector)UnityEditor.Editor.CreateEditor(targets, typeof(ElementInspector));
            inspector.AllowSharedRules = live;
        }

        void DestroyInspector()
        {
            if (inspector)
            {
                DestroyImmediate(inspector);
            }
            inspector = null;
            inspected = Array.Empty<ElementSelectionProxy>();
        }
    }
}
